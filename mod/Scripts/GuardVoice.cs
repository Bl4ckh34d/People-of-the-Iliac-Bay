using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardActor
    {
        public static event Action<Vector3, Vector3, float> PlayerHalted;
        public static bool PursuitHaltDue(float now, float nextHalt) { return now >= nextHalt; }
        public static void PublishPlayerHalt(Vector3 guardPosition, Vector3 playerPosition, float audibleRange)
        {
            var callback = PlayerHalted;
            if (callback != null) callback(guardPosition, playerPosition, audibleRange);
        }
        public int HaltWarnings { get; private set; }
        private float nextPursuitHalt;
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
            if(populationOwner)populationOwner.HearGuardHalt(this,target);
            if (target == GameManager.Instance.PlayerEntityBehaviour)
            {
                nextPursuitHalt = Time.time + 20f;
                PublishPlayerHalt(transform.position, target.transform.position, 20f);
            }
        }
        private void Update()
        {
            if (!CombatReady || DefenseTarget || GameManager.IsGamePaused || !PursuitHaltDue(Time.time, nextPursuitHalt)) return;
            var player = GameManager.Instance.PlayerEntityBehaviour;
            if (!player || player.Entity == null || player.Entity.CurrentHealth <= 0 ||
                GameManager.Instance.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None ||
                !CivilianResponses.Sees(transform.position, player.transform.position, 20)) return;
            nextPursuitHalt = Time.time + 20f;
            GetComponent<DaggerfallAudioSource>().PlayOneShot(SoundClips.Halt);
            HaltWarnings++;
            if(populationOwner)populationOwner.HearGuardHalt(this,player);
            PublishPlayerHalt(transform.position, player.transform.position, 20f);
        }
    }
}
