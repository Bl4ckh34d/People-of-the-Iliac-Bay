using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcMagicCareNeed { Heal, CurePoison, CureDisease }

    /// <summary>Read-only native facts. The runtime must recheck them before a cast.</summary>
    public sealed class NpcMagicCareFacts
    {
        public string ActorId;
        public NpcMagicKnownEffect[] KnownEffects;
        public int CurrentHealth, MaximumHealth, CurrentMagicka, PoisonCount, DiseaseCount;
        public bool NativeAdapterAvailable, DutyAvailable, QuestProtected;
    }

    /// <summary>Eligible spell, not a reserved action or durable treatment receipt.</summary>
    public sealed class NpcMagicCareOffer
    {
        public NpcMagicCareNeed Need { get; private set; }
        public NpcMagicAuthorization Authorization { get; private set; }
        public int ObservedConditionBefore { get; private set; }

        internal NpcMagicCareOffer(NpcMagicCareNeed need, NpcMagicAuthorization authorization,int observedConditionBefore)
        { Need = need; Authorization = authorization; ObservedConditionBefore = observedConditionBefore; }
    }

    /// <summary>
    /// Produces exact self-treatment offers from the native spellbook and status.
    /// The existing care task owner does not yet reserve or commit spell treatment.
    /// </summary>
    public static class NpcMagicCarePort
    {
        public static NpcMagicCareOffer[] Discover(NpcMagicCareFacts facts)
        {
            if (facts == null || string.IsNullOrWhiteSpace(facts.ActorId) || facts.ActorId.Length > 240 ||
                facts.KnownEffects == null || facts.KnownEffects.Length > 128 ||
                facts.CurrentHealth < 1 || facts.MaximumHealth < facts.CurrentHealth ||
                facts.CurrentMagicka < 0 || facts.PoisonCount < 0 || facts.DiseaseCount < 0 ||
                !facts.NativeAdapterAvailable || !facts.DutyAvailable || facts.QuestProtected)
                return new NpcMagicCareOffer[0];

            var offers = new List<NpcMagicCareOffer>();
            foreach (NpcMagicKnownEffect effect in facts.KnownEffects)
            {
                if (effect == null || effect.Hostile) continue;
                NpcMagicCareNeed need;
                NpcMagicIntent intent;
                NpcMagicEffectKind kind;
                int observedConditionBefore;
                if (effect.EffectId == "Heal-Health" &&
                    effect.EffectKind == NpcMagicEffectKind.Health &&
                    facts.CurrentHealth < facts.MaximumHealth)
                { need = NpcMagicCareNeed.Heal; intent = NpcMagicIntent.Heal; kind = NpcMagicEffectKind.Health;
                  observedConditionBefore = facts.CurrentHealth; }
                else if (effect.EffectId == "Cure-Poison" &&
                    effect.EffectKind == NpcMagicEffectKind.Condition && facts.PoisonCount > 0)
                { need = NpcMagicCareNeed.CurePoison; intent = NpcMagicIntent.Cure; kind = NpcMagicEffectKind.Condition;
                  observedConditionBefore = facts.PoisonCount; }
                else if (effect.EffectId == "Cure-Disease" &&
                    effect.EffectKind == NpcMagicEffectKind.Condition && facts.DiseaseCount > 0)
                { need = NpcMagicCareNeed.CureDisease; intent = NpcMagicIntent.Cure; kind = NpcMagicEffectKind.Condition;
                  observedConditionBefore = facts.DiseaseCount; }
                else continue;

                var request = new NpcMagicRequest {
                    ActorId = facts.ActorId, TargetId = facts.ActorId,
                    SpellId = effect.SpellId, EffectId = effect.EffectId,
                    Intent = intent, EffectKind = kind,
                    MagickaCost = effect.MagickaCost, Hostile = false
                };
                var admission = new NpcMagicAdmissionFacts {
                    KnownEffects = new[] { effect }, CurrentMagicka = facts.CurrentMagicka,
                    TargetIsLegal = true, TargetIsAlive = true, TargetConsents = true,
                    NativeAdapterAvailable = true, DutyAvailable = true, QuestProtected = false
                };
                NpcMagicAuthorization authorization;
                if (NpcMagicActionContract.TryAuthorize(request, admission, out authorization))
                    offers.Add(new NpcMagicCareOffer(need, authorization, observedConditionBefore));
            }
            return offers.ToArray();
        }
    }
}
