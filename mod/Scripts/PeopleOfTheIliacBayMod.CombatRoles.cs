using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private float nextCombatRoleScan;
        private WorldStateStore combatRoleStore;
        private long combatRoleSaveEpoch = -1;
        internal uint NativeCombatWorldSeed { get { return store.WorldSeed; } }
        private readonly List<NativeNpcCombatRoleBinding> combatRoleBindings = new List<NativeNpcCombatRoleBinding>();

        private void TickNativeCombatRoles()
        {
            if (!ReferenceEquals(combatRoleStore, store) || combatRoleSaveEpoch != conversationSaveEpoch)
            {
                foreach (var binding in combatRoleBindings)
                    if (binding) { binding.enabled = false; Object.Destroy(binding); }
                combatRoleBindings.Clear();
                combatRoleStore = store;
                combatRoleSaveEpoch = conversationSaveEpoch;
                nextCombatRoleScan = 0;
            }
            var game = GameManager.Instance;
            bool active = options.Enabled && !saveFault && store != null && game != null &&
                game.IsReady && game.IsPlayingGame();
            for (int i = combatRoleBindings.Count - 1; i >= 0; i--)
            {
                var binding = combatRoleBindings[i];
                if (!binding) { combatRoleBindings.RemoveAt(i); continue; }
                binding.enabled = active;
            }
            if (!active || Time.unscaledTime < nextCombatRoleScan) return;
            nextCombatRoleScan = Time.unscaledTime + 1f;
            foreach (var actor in Object.FindObjectsOfType<DaggerfallEntityBehaviour>())
            {
                // Managed guards own their roster/refuge lifecycle and bind through GuardPopulation.
                if (!actor || actor.GetComponent<GuardActor>() || actor.GetComponent<NativeNpcCombatRoleBinding>() ||
                    actor.EntityType != EntityTypes.EnemyClass || actor.Entity == null ||
                    actor.Entity.CurrentHealth <= 0 || actor.Entity.Team == MobileTeams.PlayerAlly ||
                    actor.GetComponent<QuestResourceBehaviour>()) continue;
                var enemy = actor.Entity as EnemyEntity;
                var native = actor.GetComponent<DaggerfallEnemy>();
                if (enemy == null || !native || native.QuestSpawn || native.LoadID == 0) continue;
                NpcCombatRole career;
                bool cityWatch = enemy.MobileEnemy.ID == (int)MobileTypes.Knight_CityWatch;
                if (!cityWatch && !NativeNpcCombatRoleDecision.TryMapCareer(enemy.MobileEnemy.ID - 128, out career)) continue;
                career = cityWatch ? NpcCombatRole.Guard : MapCombatCareer(enemy.MobileEnemy.ID - 128);
                var stationary = actor.GetComponent<StaticNPC>();
                bool palace = stationary && IsPalaceStationed(stationary);
                if (palace && cityWatch) career = NpcCombatRole.PalaceGuard;
                string id = "native-enemy:" + native.LoadID.ToString(CultureInfo.InvariantCulture);
                var role = new NpcRoleState
                {
                    PersonId = id,
                    CombatRole = career,
                    CombatSourceId = palace && cityWatch ? "native:palace-watch:" + id : "native:career:" + enemy.MobileEnemy.ID,
                    ServiceBinding = palace || cityWatch ? NpcServiceBinding.NobleKingdom : NpcServiceBinding.Independent,
                    ServiceSourceId = palace ? "native:palace-layout:" + id : cityWatch ? "native:city-watch:" + id : "native:unbound-class:" + id,
                    Revision = 1
                };
                var binding = NativeNpcCombatRoleBinding.Bind(actor, role, store.WorldSeed, null, StableCombatTargetId);
                if (binding) combatRoleBindings.Add(binding);
            }
        }

        private static NpcCombatRole MapCombatCareer(int career)
        {
            NpcCombatRole result;
            return NativeNpcCombatRoleDecision.TryMapCareer(career, out result) ? result : NpcCombatRole.Unknown;
        }

        internal static string StableCombatTargetId(DaggerfallEntityBehaviour actor)
        {
            if (!actor) return null;
            var guard = actor.GetComponent<GuardActor>();
            if (guard) return guard.GuardId;
            var resident = actor.GetComponent<ResidentActor>();
            return resident && resident.Resident != null ? resident.Resident.Id : null;
        }

    }
}
