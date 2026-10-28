using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeNpcShieldState
    {
        internal static int ProtectionRounds(EntityEffectManager manager)
        {
            int rounds=0;
            foreach(var bundle in manager.EffectBundles)
            {
                if(bundle==null || bundle.liveEffects==null)continue;
                foreach(var effect in bundle.liveEffects)
                    if(effect!=null && effect.GetType()==typeof(Shield) && !effect.HasEnded &&
                        effect.RoundsRemaining>rounds &&
                        ((Shield.SaveData_v1)effect.GetSaveData()).shieldRemaining>0)
                        rounds=effect.RoundsRemaining;
            }
            return rounds;
        }
    }
}
