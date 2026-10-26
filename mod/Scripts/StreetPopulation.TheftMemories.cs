using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private Action<string, int> CapturePrivateTheftMemory(DaggerfallInventoryWindow window)
        {
            if (window == null || !window.LootTarget ||
                CanTakeHouseholdLoot(GetComponent<PeopleOfTheIliacBayMod>(), window)) return null;
            return CaptureTheftMemory(window.LootTarget.transform, "private property");
        }

        // Called immediately before the native transfer. Only this captured set can
        // remember its confirmed result; waking, turning or rebinding cannot add people.
        private Action<string, int> CaptureTheftMemory(Transform source, string property)
        {
            var game = GameManager.Instance;
            if (!Ready || worldStore == null || population == null || game == null || !game.IsReady ||
                game.PlayerEntity == null || game.PlayerMotor == null || game.PlayerEntity.IsInvisible ||
                !game.PlayerEnterExit.IsPlayerInsideBuilding || !game.PlayerGPS.HasCurrentLocation ||
                game.PlayerGPS.CurrentLocation.MapTableData.MapId != population.SettlementId) return null;
            var witnesses = new HashSet<string>();
            Vector3 player = game.PlayerMotor.transform.position;
            foreach (var actor in actors.Values)
            {
                if (!actor || actor.Resident == null || !actor.Resident.IsAlive) continue;
                var observer = actor.GetComponent<NativeNpcSceneObserver>();
                if (observer && observer.Sees(player, 20) && (!source || observer.Sees(source.position, 20, source)))
                    witnesses.Add(actor.Resident.Id);
            }
            foreach (var witness in stationaryParticipants.Values)
                if (witness.Person != null && witness.Observer && witness.Observer.Sees(player, 20) &&
                    (!source || witness.Observer.Sees(source.position, 20, source))) witnesses.Add(witness.Person.Id);
            if (witnesses.Count == 0) return null;
            var capturedStore = worldStore;
            object epoch = capturedStore.CaptureNpcVitalsWriteEpoch();
            int settlement = population.SettlementId, building = game.PlayerEnterExit.BuildingDiscoveryData.buildingKey;
            double at = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            string place = game.TalkManager.GetBuildingNameForBuildingKey(building);
            if (string.IsNullOrWhiteSpace(place)) place = "this building";
            place = CompactTheftFact(place, 120);
            string occurrence = "native-theft:" + Guid.NewGuid().ToString("N");
            int itemIndex = 0;
            return (name, quantity) =>
            {
                if (worldStore != capturedStore || !capturedStore.IsNpcVitalsWriteEpochCurrent(epoch) || quantity < 1) return;
                string item = string.IsNullOrWhiteSpace(name) ? "item(s) whose exact identity I do not know" : CompactTheftFact(name, 160);
                string text = "I saw the player steal " + quantity + " " + item + " from " + property +
                    ". I do not know the motive. Location: " + place + ".";
                string eventId = occurrence + ":" + itemIndex++;
                foreach (string witness in witnesses)
                    capturedStore.RememberSceneEvent(witness, eventId, NpcSceneEventKind.Theft, at,
                        settlement, building, text, responsible: NpcDirectedRelationship.PlayerTargetId);
            };
        }

        private static string CompactTheftFact(string text, int maximum)
        {
            text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            return text.Length <= maximum ? text : text.Substring(0, maximum);
        }
    }
}
