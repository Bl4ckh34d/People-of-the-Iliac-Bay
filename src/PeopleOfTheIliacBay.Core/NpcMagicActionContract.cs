using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcMagicIntent { Cast, Heal, Cure, Buff }
    public enum NpcMagicEffectKind { Damage, Health, Condition, TimedEffect }

    /// <summary>One exact spell/effect pair discovered from the native actor and spell catalog.</summary>
    public sealed class NpcMagicKnownEffect
    {
        public string SpellId, EffectId;
        public NpcMagicEffectKind EffectKind;
        public int MagickaCost;
        public bool Hostile;
    }

    public sealed class NpcMagicRequest
    {
        public string ActorId, TargetId, SpellId, EffectId;
        public NpcMagicIntent Intent;
        public NpcMagicEffectKind EffectKind;
        public int MagickaCost;
        public bool Hostile;
    }

    /// <summary>Current native facts; absent knowledge, adapter, consent, or authority denies admission.</summary>
    public sealed class NpcMagicAdmissionFacts
    {
        public NpcMagicKnownEffect[] KnownEffects;
        public int CurrentMagicka;
        public bool TargetIsLegal, TargetIsAlive, TargetIsHostile, TargetConsents;
        public bool NativeAdapterAvailable, DutyAvailable, QuestProtected;
    }

    /// <summary>Immutable admission snapshot. Recheck current world facts before native execution.</summary>
    public sealed class NpcMagicAuthorization
    {
        public string ActorId {get;private set;}
        public string TargetId {get;private set;}
        public string SpellId {get;private set;}
        public string EffectId {get;private set;}
        public NpcMagicIntent Intent {get;private set;}
        public NpcMagicEffectKind EffectKind {get;private set;}
        public int MagickaBefore {get;private set;}
        public int MagickaCost {get;private set;}
        public bool Hostile {get;private set;}

        internal NpcMagicAuthorization(NpcMagicRequest request,int magickaBefore)
        {
            ActorId=request.ActorId;TargetId=request.TargetId;SpellId=request.SpellId;
            EffectId=request.EffectId;Intent=request.Intent;EffectKind=request.EffectKind;
            MagickaBefore=magickaBefore;MagickaCost=request.MagickaCost;Hostile=request.Hostile;
        }
    }

    /// <summary>Native adapter observation of exact target effect state before and after execution.</summary>
    public sealed class NpcMagicObservedOutcome
    {
        public string ActorId, TargetId, SpellId, EffectId;
        public NpcMagicEffectKind EffectKind;
        public bool NativeAdapterExecuted;
        public int MagickaBefore, MagickaAfter;
        // Damage and condition counts decrease. Health and timed-effect strength/duration increase.
        public int EffectValueBefore, EffectValueAfter;
    }

    public static class NpcMagicActionContract
    {
        public static bool TryAuthorize(NpcMagicRequest request,NpcMagicAdmissionFacts facts,
            out NpcMagicAuthorization authorization)
        {
            authorization=null;
            if(request==null || facts==null || !Id(request.ActorId) || !Id(request.TargetId) ||
                !Id(request.SpellId) || !Id(request.EffectId) ||
                (int)request.Intent<0 || (int)request.Intent>3 ||
                (int)request.EffectKind<0 || (int)request.EffectKind>3 ||
                request.MagickaCost<=0 || facts.CurrentMagicka<request.MagickaCost ||
                facts.KnownEffects==null || facts.KnownEffects.Length==0 || facts.KnownEffects.Length>128 ||
                !facts.NativeAdapterAvailable || !facts.DutyAvailable || facts.QuestProtected ||
                !facts.TargetIsLegal || !facts.TargetIsAlive)return false;

            if(request.Intent==NpcMagicIntent.Heal && request.EffectKind!=NpcMagicEffectKind.Health ||
                request.Intent==NpcMagicIntent.Cure && request.EffectKind!=NpcMagicEffectKind.Condition ||
                request.Intent==NpcMagicIntent.Buff && request.EffectKind!=NpcMagicEffectKind.TimedEffect ||
                request.Intent!=NpcMagicIntent.Cast && request.Hostile ||
                request.Hostile && (request.EffectKind!=NpcMagicEffectKind.Damage ||
                    request.ActorId==request.TargetId) ||
                request.EffectKind==NpcMagicEffectKind.Damage && !request.Hostile)return false;
            if(request.Hostile ? !facts.TargetIsHostile :
                request.ActorId!=request.TargetId && !facts.TargetConsents)return false;

            bool known=false;
            foreach(var effect in facts.KnownEffects)
            {
                if(effect!=null && effect.SpellId==request.SpellId && effect.EffectId==request.EffectId &&
                    effect.EffectKind==request.EffectKind && effect.MagickaCost==request.MagickaCost &&
                    effect.Hostile==request.Hostile)
                {known=true;break;}
            }
            if(!known)return false;
            authorization=new NpcMagicAuthorization(request,facts.CurrentMagicka);
            return true;
        }

        public static bool MatchesObservedOutcome(NpcMagicAuthorization authorization,NpcMagicObservedOutcome outcome)
        {
            if(authorization==null || outcome==null || !outcome.NativeAdapterExecuted ||
                outcome.ActorId!=authorization.ActorId || outcome.TargetId!=authorization.TargetId ||
                outcome.SpellId!=authorization.SpellId || outcome.EffectId!=authorization.EffectId ||
                outcome.EffectKind!=authorization.EffectKind ||
                outcome.MagickaBefore!=authorization.MagickaBefore ||
                outcome.MagickaAfter!=authorization.MagickaBefore-authorization.MagickaCost ||
                outcome.MagickaAfter<0 || outcome.EffectValueBefore<0 || outcome.EffectValueAfter<0)
                return false;
            switch(authorization.EffectKind)
            {
                case NpcMagicEffectKind.Damage:
                case NpcMagicEffectKind.Condition:
                    return outcome.EffectValueBefore>outcome.EffectValueAfter;
                case NpcMagicEffectKind.Health:
                case NpcMagicEffectKind.TimedEffect:
                    return outcome.EffectValueAfter>outcome.EffectValueBefore;
                default:return false;
            }
        }

        private static bool Id(string value)
        {return !string.IsNullOrWhiteSpace(value) && value.Length<=240;}
    }
}
