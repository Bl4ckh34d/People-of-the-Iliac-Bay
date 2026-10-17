using System;
using System.Reflection;
using DaggerfallWorkshop;

namespace LivingCities.DFU
{
    // Native idle art has only a frontal record. Use a stationary frame of the
    // directional walking records, retaining native camera/orientation updates.
    public sealed class DirectionalStandingPose
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo Speed = typeof(MobilePersonBillboard).GetField("animSpeed", Flags);
        private static readonly FieldInfo Timer = typeof(MobilePersonBillboard).GetField("animTimer", Flags);
        private static readonly FieldInfo Frame = typeof(MobilePersonBillboard).GetField("currentFrame", Flags);
        private MobilePersonBillboard held;
        public bool Active { get { return held; } }

        public bool Set(MobilePersonAsset asset, bool standing)
        {
            var native = asset as MobilePersonBillboard;
            if (held && (!standing || held != native))
            {
                held.IsIdle = false; // Restore native frame rate and animation timer.
                held = null;
            }
            if (!standing || !native) return false;
            if (held == native && !native.IsIdle) return true;
            if (Speed == null || Timer == null || Frame == null)
                throw new MissingFieldException("DFU directional standing animation fields changed.");
            native.IsIdle = false;
            // Native Update compares elapsed time with 1 / animSpeed. Zero makes
            // that threshold infinite without disabling orientation or the asset.
            Speed.SetValue(native, 0f);
            Timer.SetValue(native, 0f);
            // The original civilian cycles alternate wide strides (0/2) and
            // narrow passing poses (1/3). Source sheets were inspected across
            // all 24 archives. Frame 1 avoids freezing with legs spread wide;
            // dedicated directional idle art remains a separate asset task.
            Frame.SetValue(native, 1);
            held = native;
            return true;
        }
    }
}
