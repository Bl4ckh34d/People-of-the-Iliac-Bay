using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        private IDisposable theftHook;
        public bool TheftAwarenessEnabled { get; set; } = true;
        public int TheftWitnessRange { get; set; } = 20;
        private void InstallTheftHook()
        {
            var type = Type.GetType("LivingCities.QuestHooks.NativeTheftHooks, LivingCities.QuestHooks", true);
            theftHook = (IDisposable)Activator.CreateInstance(type, new object[] { new Func<bool?>(HasTheftWitness) });
        }
        public bool? HasTheftWitness()
        {
            var game = GameManager.Instance;
            if (!TheftAwarenessEnabled || !Ready || population == null || game == null || !game.IsReady || !game.PlayerEnterExit.IsPlayerInsideBuilding ||
                !game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.MapTableData.MapId != population.SettlementId)
                return null;
            // Query at the actual theft roll, even while inventory UI pauses the clock.
            var room = game.PlayerEnterExit.Interior;
            if (!room) return null;
            Vector3 target = game.PlayerMotor.transform.position;
            foreach (var npc in room.GetComponentsInChildren<StaticNPC>())
                if (TheftCandidate(npc.transform, target)) return true;
            foreach (var npc in room.GetComponentsInChildren<MobilePersonNPC>())
                if (TheftCandidate(npc.transform, target)) return true;
            foreach (var guard in room.GetComponentsInChildren<GuardActor>())
                if (TheftCandidate(guard.transform, target)) return true;
            return false;
        }
        private bool TheftCandidate(Transform candidate, Vector3 target)
        {
            var entity = candidate.GetComponent<DaggerfallEntityBehaviour>();
            bool alive = entity == null || entity.Entity == null || entity.Entity.CurrentHealth > 0;
            bool awake = true;
            var keeper = KeeperPerson(candidate.GetComponent<StaticNPC>());
            if (keeper != null)
            {
                alive &= keeper.IsAlive;
                awake = !KeeperSleeping(keeper, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            }
            var resident = candidate.GetComponent<ResidentActor>();
            if (resident)
            {
                if (resident.Resident == null) return false;
                alive &= resident.Resident.IsAlive;
                double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                awake = Reaction(resident.Resident.Id, now) != null || GetState(resident.Resident.Id, now).Activity != Activity.Sleep;
            }
            return TheftWitnessSight.CanSee(candidate, target, alive, awake, Mathf.Clamp(TheftWitnessRange, 5, 40));
        }
    }
}
