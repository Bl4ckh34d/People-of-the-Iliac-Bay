using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;

namespace LivingCities.DFU
{
    public sealed partial class GuardActor
    {
        public int HaltWarnings { get; private set; }
        private void SayHaltOnce(DaggerfallEntityBehaviour target)
        {
            if (!target || target.Entity.CurrentHealth<=0 ||
                target.EntityType != EntityTypes.Player && target.EntityType != EntityTypes.EnemyClass ||
                !CivilianResponses.Sees(transform.position,target.transform.position,20)) return;
            if (target == GameManager.Instance.PlayerEntityBehaviour &&
                GameManager.Instance.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None) return;
            if (!roster.TryGuardHalt(GuardId)) return;
            GetComponent<DaggerfallAudioSource>().PlayOneShot(SoundClips.Halt);
            HaltWarnings++;
        }
    }
}
