using System;

namespace PeopleOfTheIliacBay.Core
{
    // Instant native CasterOnly or verified native ByTouch contact with direct bundle delivery.
    public enum NpcCompanionCastDelivery { Self=0, PlayerTouch=1 }

    [Serializable]
    public sealed class NpcCompanionCastSpec
    {
        public string SpellId,EffectId,TargetId;
        public NpcMagicEffectKind EffectKind;
        public NpcCompanionCastDelivery Delivery;
        public int MagickaCost;

        public static NpcCompanionCastSpec CopyValidated(NpcCompanionCastSpec value)
        {
            if(value==null || !Id(value.SpellId) || !Id(value.EffectId) || !Id(value.TargetId) ||
                (int)value.EffectKind<(int)NpcMagicEffectKind.Health ||
                (int)value.EffectKind>(int)NpcMagicEffectKind.TimedEffect ||
                (int)value.Delivery<0 || (int)value.Delivery>1 || value.MagickaCost<=0 ||
                value.MagickaCost>1000000 || value.Delivery==NpcCompanionCastDelivery.PlayerTouch &&
                value.TargetId!="player")
                throw new ArgumentException("Invalid companion cast specification.");
            return (NpcCompanionCastSpec)value.MemberwiseClone();
        }

        public static bool Same(NpcCompanionCastSpec a,NpcCompanionCastSpec b)
        {
            return a!=null && b!=null && a.SpellId==b.SpellId && a.EffectId==b.EffectId &&
                a.TargetId==b.TargetId && a.EffectKind==b.EffectKind && a.Delivery==b.Delivery &&
                a.MagickaCost==b.MagickaCost;
        }

        private static bool Id(string value)
        {return !string.IsNullOrWhiteSpace(value) && value.Length<=240;}
    }
}
