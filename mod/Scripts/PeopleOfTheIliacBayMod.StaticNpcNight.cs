using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        // Runtime visibility only. The native object, position, inventory, name
        // seed and saved NativePerson remain authoritative across night and dawn.
        private readonly Dictionary<StaticNPC, string> exteriorStaticNightHidden = new Dictionary<StaticNPC, string>();
        private readonly List<StaticNPC> exteriorStaticNightReleased = new List<StaticNPC>();
        private BuildingDirectory exteriorStaticNightDirectory;
        private float nextExteriorStaticNightCheck;

        private void TickExteriorStaticNpcNight()
        {
            var game = GameManager.Instance;
            if (saveFault || store == null || !options.Enabled || game == null || !game.IsReady ||
                !game.IsPlayingGame() || game.PlayerEnterExit.IsPlayerInside || !game.PlayerGPS.HasCurrentLocation)
            { RestoreExteriorStaticNpcNight(); return; }

            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            if (!directory || directory.MapID != game.PlayerGPS.CurrentMapID || current == null ||
                current.SettlementId != directory.MapID)
            { RestoreExteriorStaticNpcNight(); return; }
            if (exteriorStaticNightDirectory != directory)
            {
                RestoreExteriorStaticNpcNight();
                exteriorStaticNightDirectory = directory;
            }
            if (Time.unscaledTime < nextExteriorStaticNightCheck) return;
            nextExteriorStaticNightCheck = Time.unscaledTime + 1;
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            bool absent = ExteriorStaticNpcAbsent(directory.MapID, now);

            exteriorStaticNightReleased.Clear();
            foreach (var entry in exteriorStaticNightHidden)
            {
                string personId;
                if (absent && entry.Key && IsExteriorStaticNpcNightEligible(entry.Key, directory, out personId) &&
                    !IsExteriorStaticNpcTalking(entry.Key)) continue;
                RestoreExteriorStaticNpcNightBody(entry.Key, entry.Value, directory.MapID);
                exteriorStaticNightReleased.Add(entry.Key);
            }
            foreach (var npc in exteriorStaticNightReleased) exteriorStaticNightHidden.Remove(npc);
            if (!absent) return;

            // Include inactive children so an already hidden native body is never
            // replaced or duplicated. Initially inactive bodies are never claimed.
            foreach (var npc in directory.GetComponentsInChildren<StaticNPC>(true))
            {
                string personId;
                if (!npc || !npc.gameObject.activeInHierarchy || exteriorStaticNightHidden.ContainsKey(npc) ||
                    !IsExteriorStaticNpcNightEligible(npc, directory, out personId) || IsExteriorStaticNpcTalking(npc)) continue;
                exteriorStaticNightHidden.Add(npc, personId);
                npc.gameObject.SetActive(false);
            }
        }

        private bool ExteriorStaticNpcAbsent(int settlement, double now)
        {
            // Use the same configured waking window as household routines. The
            // dummy key is only needed by DailySchedule's existing validation;
            // no home, bed, resident, itinerary or save field is created here.
            return DailySchedule.HomeDay(0, options.WakeHour * 60, options.SleepHour * 60)
                .Evaluate((long)(now / 60)).Activity == Activity.Sleep ||
                SettlementHaunting.StreetsDangerous(settlement,
                    GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0), now);
        }

        private bool IsExteriorStaticNpcNightEligible(StaticNPC npc, BuildingDirectory directory, out string personId)
        {
            personId = null;
            if (!npc || !directory || !npc.transform.IsChildOf(directory.transform) ||
                npc.Data.context != StaticNPC.Context.Custom || !RDBLayout.IsNPCFlat(npc.Data.billboardArchiveIndex) ||
                npc.GetComponent<ResidentActor>() || npc.GetComponent<GuardActor>() ||
                npc.GetComponent<QuestResourceBehaviour>() || npc.GetComponent<DaggerfallAction>() ||
                IsAuthoredNativeIndividual(npc.Data.factionID) ||
                QuestMachine.Instance.HasFactionListener(npc.Data.factionID) ||
                QuestMachine.Instance.ActiveQuestor(npc.Data) != null) return false;

            personId = NativeExteriorNpcIdentity.PersonId(directory.MapID, NativeExteriorNpcIdentity.Hash(npc, directory.transform));
            var native = store.NativePersonInSettlement(directory.MapID, personId);
            if (native != null && (!native.IsAlive || native.RetiredBedroomFlat || native.HomeKey >= 0)) return false;
            if (current != null && current.SettlementId == directory.MapID)
                foreach (var home in current.Households)
                    foreach (var resident in home.Residents)
                        if (resident.Id == personId) return false;
            var role = store.ReadNpcRole(personId);
            return role == null || role.CombatRole != NpcCombatRole.Guard && role.CombatRole != NpcCombatRole.PalaceGuard;
        }

        private static bool IsExteriorStaticNpcTalking(StaticNPC npc)
        {
            var game = GameManager.Instance;
            return game != null && game.TalkManager != null && game.TalkManager.CurrentNPCType == TalkManager.NPCType.Static &&
                game.TalkManager.StaticNPC == npc && DaggerfallUI.UIManager != null &&
                DaggerfallUI.UIManager.ContainsWindow(DaggerfallUI.Instance.TalkWindow);
        }

        private void RestoreExteriorStaticNpcNightBody(StaticNPC npc, string personId, int settlement)
        {
            if (!npc) return;
            var native = store == null ? null : store.NativePersonInSettlement(settlement, personId);
            if (native != null && (!native.IsAlive || native.RetiredBedroomFlat)) return;
            var resource = npc.GetComponent<QuestResourceBehaviour>();
            var questPerson = resource ? resource.TargetResource as Person : null;
            if (questPerson == null) questPerson = QuestMachine.Instance.ActiveQuestor(npc.Data);
            // A quest can claim or remove a person while its ordinary night body
            // is inactive. Releasing our visibility must not undo that removal.
            if (questPerson != null && (questPerson.IsHidden || questPerson.IsDestroyed)) return;
            npc.gameObject.SetActive(true);
        }

        private void RestoreExteriorStaticNpcNight()
        {
            if (exteriorStaticNightDirectory)
                foreach (var entry in exteriorStaticNightHidden)
                    RestoreExteriorStaticNpcNightBody(entry.Key, entry.Value, exteriorStaticNightDirectory.MapID);
            exteriorStaticNightHidden.Clear(); exteriorStaticNightReleased.Clear();
            exteriorStaticNightDirectory = null; nextExteriorStaticNightCheck = 0;
        }

        private string ExteriorStaticNpcNightContext(StaticNPC npc, double now)
        {
            var game = GameManager.Instance;
            if (saveFault || store == null || !options.Enabled || game == null || game.PlayerEnterExit.IsPlayerInside) return null;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            string personId;
            if (!directory || current == null || current.SettlementId != directory.MapID ||
                !IsExteriorStaticNpcNightEligible(npc, directory, out personId)) return null;
            string wake = options.WakeHour.ToString("00", CultureInfo.InvariantCulture) + ":00";
            string sleep = options.SleepHour.ToString("00", CultureInfo.InvariantCulture) + ":00";
            string context = "Your ordinary routine is this outdoor post during waking hours " + wake + "–" + sleep +
                ". You are absent from this post at night and return in the morning. No household, home address or trip to a home is verified.";
            if (directory.MapID == 1291010263 && !game.PlayerEntity.GlobalVars.GetGlobalVar(0))
                context += " While the haunting remains unresolved, outdoor posts are also absent from 19:00 until 05:00; the normal waking window still applies.";
            if (ExteriorStaticNpcAbsent(directory.MapID, now) && IsExteriorStaticNpcTalking(npc))
                context += " You are still physically present for this current conversation; ordinary night absence waits until the conversation closes. Do not claim to have already left.";
            else if (npc && npc.gameObject.activeInHierarchy)
                context += " You are physically present at this outdoor post now. This does not prove a specific work task or performance.";
            return context;
        }
    }
}
