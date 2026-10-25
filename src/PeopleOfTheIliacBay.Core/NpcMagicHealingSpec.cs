using System;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Exact native self-healing spell selected for one health-care task step.</summary>
    [Serializable]
    public sealed class NpcMagicHealingSpec
    {
        public string SpellId, EffectId;
        public int MagickaCost, MagickaBefore, HealthBefore;

        public static NpcMagicHealingSpec FromOffer(NpcMagicCareOffer offer)
        {
            if (offer == null || offer.Need != NpcMagicCareNeed.Heal || offer.Authorization == null ||
                offer.Authorization.ActorId != offer.Authorization.TargetId || offer.Authorization.Hostile ||
                offer.Authorization.Intent != NpcMagicIntent.Heal ||
                offer.Authorization.EffectKind != NpcMagicEffectKind.Health ||
                offer.ObservedConditionBefore < 1) throw new ArgumentException("Invalid self-healing offer.");
            var auth = offer.Authorization;
            return CopyValidated(new NpcMagicHealingSpec { SpellId = auth.SpellId, EffectId = auth.EffectId,
                MagickaCost = auth.MagickaCost, MagickaBefore = auth.MagickaBefore,
                HealthBefore = offer.ObservedConditionBefore });
        }

        public static NpcMagicHealingSpec CopyValidated(NpcMagicHealingSpec value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.SpellId) || value.SpellId.Length > 240 ||
                value.EffectId != "Heal-Health" || value.MagickaCost <= 0 ||
                value.MagickaBefore < value.MagickaCost || value.HealthBefore < 1 || value.HealthBefore > 10000)
                throw new ArgumentException("Invalid exact self-healing spell.");
            return new NpcMagicHealingSpec { SpellId = value.SpellId, EffectId = value.EffectId,
                MagickaCost = value.MagickaCost, MagickaBefore = value.MagickaBefore,
                HealthBefore = value.HealthBefore };
        }

        public static NpcMagicHealingSpec Copy(NpcMagicHealingSpec value)
        { return value == null ? null : CopyValidated(value); }

        internal static bool Same(NpcMagicHealingSpec left, NpcMagicHealingSpec right)
        {
            return left != null && right != null && left.SpellId == right.SpellId &&
                left.EffectId == right.EffectId && left.MagickaCost == right.MagickaCost &&
                left.MagickaBefore == right.MagickaBefore && left.HealthBefore == right.HealthBefore;
        }
    }
}
