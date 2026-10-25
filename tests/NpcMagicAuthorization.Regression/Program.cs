using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var cases=new List<(string,Action)>
        {
            ("exact known spell and effect admit",KnownSpellAdmits),
            ("unknown or mismatched spell and effect reject",UnknownSpellRejects),
            ("magicka and native adapter are required",ResourcesAndAdapter),
            ("legal live target and duty/quest permission are required",WorldConstraints),
            ("hostile casts require hostility; helpful magic requires consent",HostilityAndConsent),
            ("hostile cast cannot target self or use supportive effect",HostileTargetAndEffect),
            ("intent must match exact native effect kind",IntentMatchesEffect),
            ("observed matching heal spends exact magicka",MatchingHeal),
            ("generic success and mismatched observation reject",UnobservedRejects),
            ("cure and buff require observed condition/effect changes",CureAndBuff),
            ("cast requires observed effect transition",CastTransition)
        };
        int failed=0;
        foreach(var test in cases)
        {
            try {test.Item2();Console.WriteLine("PASS "+test.Item1);}
            catch(Exception ex){failed++;Console.WriteLine("FAIL "+test.Item1+": "+ex.Message);}
        }
        Console.WriteLine((cases.Count-failed)+"/"+cases.Count+" passed");
        return failed==0?0:1;
    }

    private static NpcMagicRequest Request(NpcMagicIntent intent=NpcMagicIntent.Heal,
        NpcMagicEffectKind kind=NpcMagicEffectKind.Health,bool hostile=false)
    {return new NpcMagicRequest {ActorId="npc:mage",TargetId="npc:patient",Intent=intent,
        SpellId="native:spell:17",EffectId="native:effect:3",EffectKind=kind,MagickaCost=12,Hostile=hostile};}
    private static NpcMagicAdmissionFacts Facts(NpcMagicRequest request)
    {return new NpcMagicAdmissionFacts {KnownEffects=new [] {new NpcMagicKnownEffect
        {SpellId=request.SpellId,EffectId=request.EffectId,EffectKind=request.EffectKind,
         MagickaCost=request.MagickaCost,Hostile=request.Hostile}},CurrentMagicka=20,
        TargetIsLegal=true,TargetIsAlive=true,TargetConsents=true,TargetIsHostile=true,
        NativeAdapterAvailable=true,DutyAvailable=true};}
    private static NpcMagicObservedOutcome Outcome(NpcMagicRequest request,int before=5,int after=8)
    {return new NpcMagicObservedOutcome {ActorId=request.ActorId,TargetId=request.TargetId,
        SpellId=request.SpellId,EffectId=request.EffectId,EffectKind=request.EffectKind,
        NativeAdapterExecuted=true,MagickaBefore=20,MagickaAfter=8,
        EffectValueBefore=before,EffectValueAfter=after};}
    private static NpcMagicAuthorization Admit(NpcMagicRequest request,NpcMagicAdmissionFacts facts)
    {NpcMagicAuthorization authorization;True(NpcMagicActionContract.TryAuthorize(request,facts,out authorization),"admission failed");return authorization;}
    private static void KnownSpellAdmits()
    {var request=Request();Admit(request,Facts(request));}
    private static void UnknownSpellRejects()
    {
        var request=Request();var facts=Facts(request);facts.KnownEffects=new NpcMagicKnownEffect[0];
        NpcMagicAuthorization authorization;
        False(NpcMagicActionContract.TryAuthorize(request,facts,out authorization),"unknown spell admitted");
        facts=Facts(request);facts.KnownEffects[0].EffectId="native:other";
        False(NpcMagicActionContract.TryAuthorize(request,facts,out authorization),"wrong effect admitted");
        facts=Facts(request);facts.KnownEffects[0].MagickaCost=3;
        False(NpcMagicActionContract.TryAuthorize(request,facts,out authorization),"wrong cost admitted");
    }
    private static void ResourcesAndAdapter()
    {
        var request=Request();var facts=Facts(request);facts.CurrentMagicka=11;
        NpcMagicAuthorization authorization;
        False(NpcMagicActionContract.TryAuthorize(request,facts,out authorization),"insufficient magicka admitted");
        facts=Facts(request);facts.NativeAdapterAvailable=false;
        False(NpcMagicActionContract.TryAuthorize(request,facts,out authorization),"missing adapter admitted");
    }
    private static void WorldConstraints()
    {
        var request=Request();NpcMagicAuthorization authorization;
        var facts=Facts(request);facts.TargetIsLegal=false;
        False(NpcMagicActionContract.TryAuthorize(request,facts,out authorization),"illegal target admitted");
        facts=Facts(request);facts.TargetIsAlive=false;
        False(NpcMagicActionContract.TryAuthorize(request,facts,out authorization),"dead target admitted");
        facts=Facts(request);facts.DutyAvailable=false;
        False(NpcMagicActionContract.TryAuthorize(request,facts,out authorization),"duty conflict admitted");
        facts=Facts(request);facts.QuestProtected=true;
        False(NpcMagicActionContract.TryAuthorize(request,facts,out authorization),"quest protection ignored");
    }
    private static void HostilityAndConsent()
    {
        var hostile=Request(NpcMagicIntent.Cast,NpcMagicEffectKind.Damage,true);
        var facts=Facts(hostile);facts.TargetIsHostile=false;NpcMagicAuthorization authorization;
        False(NpcMagicActionContract.TryAuthorize(hostile,facts,out authorization),"peaceful target attacked");
        var helpful=Request();facts=Facts(helpful);facts.TargetConsents=false;
        False(NpcMagicActionContract.TryAuthorize(helpful,facts,out authorization),"unconsenting target healed");
    }
    private static void HostileTargetAndEffect()
    {
        var request=Request(NpcMagicIntent.Cast,NpcMagicEffectKind.Damage,true);
        request.TargetId=request.ActorId;
        NpcMagicAuthorization authorization;
        False(NpcMagicActionContract.TryAuthorize(request,Facts(request),out authorization),"hostile self-cast admitted");
        request=Request(NpcMagicIntent.Cast,NpcMagicEffectKind.Health,true);
        False(NpcMagicActionContract.TryAuthorize(request,Facts(request),out authorization),"hostile supportive effect admitted");
    }
    private static void IntentMatchesEffect()
    {
        var request=Request(NpcMagicIntent.Cure,NpcMagicEffectKind.Health);NpcMagicAuthorization authorization;
        False(NpcMagicActionContract.TryAuthorize(request,Facts(request),out authorization),"cure with health effect admitted");
        request=Request(NpcMagicIntent.Buff,NpcMagicEffectKind.Condition);
        False(NpcMagicActionContract.TryAuthorize(request,Facts(request),out authorization),"buff with cure effect admitted");
    }
    private static void MatchingHeal()
    {var request=Request();True(NpcMagicActionContract.MatchesObservedOutcome(Admit(request,Facts(request)),Outcome(request)),"matching heal rejected");}
    private static void UnobservedRejects()
    {
        var request=Request();var authorization=Admit(request,Facts(request));var outcome=Outcome(request);
        outcome.NativeAdapterExecuted=false;
        False(NpcMagicActionContract.MatchesObservedOutcome(authorization,outcome),"generic success admitted");
        outcome=Outcome(request);outcome.EffectValueAfter=outcome.EffectValueBefore;
        False(NpcMagicActionContract.MatchesObservedOutcome(authorization,outcome),"no effect admitted");
        outcome=Outcome(request);outcome.SpellId="native:other";
        False(NpcMagicActionContract.MatchesObservedOutcome(authorization,outcome),"wrong spell admitted");
        outcome=Outcome(request);outcome.TargetId="npc:other";
        False(NpcMagicActionContract.MatchesObservedOutcome(authorization,outcome),"wrong target admitted");
        outcome=Outcome(request);outcome.MagickaAfter=9;
        False(NpcMagicActionContract.MatchesObservedOutcome(authorization,outcome),"wrong magicka debit admitted");
    }
    private static void CureAndBuff()
    {
        var cure=Request(NpcMagicIntent.Cure,NpcMagicEffectKind.Condition);
        var authorization=Admit(cure,Facts(cure));
        True(NpcMagicActionContract.MatchesObservedOutcome(authorization,Outcome(cure,1,0)),"observed cure rejected");
        False(NpcMagicActionContract.MatchesObservedOutcome(authorization,Outcome(cure,0,0)),"absent condition called cured");
        var buff=Request(NpcMagicIntent.Buff,NpcMagicEffectKind.TimedEffect);
        authorization=Admit(buff,Facts(buff));
        True(NpcMagicActionContract.MatchesObservedOutcome(authorization,Outcome(buff,0,30)),"observed buff rejected");
        False(NpcMagicActionContract.MatchesObservedOutcome(authorization,Outcome(buff,30,30)),"unchanged buff called applied");
    }
    private static void CastTransition()
    {
        var request=Request(NpcMagicIntent.Cast,NpcMagicEffectKind.Damage,true);
        var authorization=Admit(request,Facts(request));
        True(NpcMagicActionContract.MatchesObservedOutcome(authorization,Outcome(request,10,6)),"observed damage rejected");
        False(NpcMagicActionContract.MatchesObservedOutcome(authorization,Outcome(request,10,10)),"unobserved damage admitted");
    }
    private static void True(bool value,string message){if(!value)throw new Exception(message);}
    private static void False(bool value,string message){if(value)throw new Exception(message);}
}
