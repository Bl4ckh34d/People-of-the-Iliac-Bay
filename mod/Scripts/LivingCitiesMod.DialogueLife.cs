using System;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;

namespace LivingCities.DFU
{
    public sealed partial class LivingCitiesMod
    {
        private string CurrentConversationLifeContext(string npcName, int disposition)
        {
            var game = GameManager.Instance;
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            int day = (int)Math.Floor(now / 86400);
            int settlement = game.PlayerGPS.HasCurrentLocation ? game.PlayerGPS.CurrentLocation.MapTableData.MapId : -1;
            Resident resident = conversationActor && conversationActor.Resident != null && conversationActor.Resident.IsAlive
                ? conversationActor.Resident : null;
            NativePerson native = resident == null && store != null && settlement >= 0
                ? FindConversationNative(settlement) : null;
            var mobile = game.TalkManager.MobileNPC;
            string fallbackIdentity = !string.IsNullOrEmpty(conversationPersonId) ? conversationPersonId :
                npcName + (mobile ? ":" + mobile.GetInstanceID() : string.Empty);
            uint seed = resident != null ? resident.AppearanceSeed : native != null
                ? StableSeed.Combine(0, (uint)native.SettlementId, (uint)native.WorkKey, unchecked((uint)native.NativeHash))
                : ConversationNameSeed(fallbackIdentity, settlement);
            bool grieving = resident != null && store != null && store.BereavementFor(resident, now) != null;
            bool alarmed = resident != null && streets && streets.IsAlarmed(resident.Id);
            var weather = game.WeatherManager.PlayerWeather;
            string condition = weather == null ? string.Empty : weather.WeatherType.ToString();
            bool outdoors = !game.PlayerEnterExit.IsPlayerInside;
            string activity = resident != null && streets
                ? streets.GetState(resident.Id, now).Activity.ToString().ToLowerInvariant()
                : native != null && game.PlayerEnterExit.IsPlayerInsideBuilding &&
                    game.PlayerEnterExit.BuildingDiscoveryData.buildingKey == native.WorkKey ? "work" : string.Empty;
            string plan = "No tracked daily itinerary is available.";
            if (resident != null && store != null)
            {
                ResidentTrip trip = store.GetResidentDialogueTrip(resident.Id);
                string travel = trip != null ? ConversationLifeContext.TripStatus(trip, now,
                    store.ResidentTripAt(resident.Id, now), ConversationSettlementName,
                    key => trip.DestinationSettlement == settlement ? ConversationBuildingName(key) : string.Empty)
                    : null;
                if (travel != null) plan = travel;
                else
                {
                    int homeSettlement = trip != null ? trip.HomeSettlement : settlement;
                    ResidentRoutine routine = store.GetResidentDialogueRoutine(homeSettlement, resident.Id);
                    if (homeSettlement == settlement && streets && streets.Ready)
                    {
                        double shift = 0;
                        int observedTown, observedBuilding;
                        double observedAt, endsAt, plannedEndAt, resumeUntil;
                        if (store.TryObservedVisitMetadata(resident.Id, out observedTown, out observedBuilding,
                            out observedAt, out endsAt, out plannedEndAt, out resumeUntil) &&
                            observedTown == settlement && now >= observedAt && now < resumeUntil + endsAt - plannedEndAt)
                            shift = endsAt - plannedEndAt;
                        plan = ConversationLifeContext.RoutineStatus(routine, now,
                            streets.GetState(resident.Id, now), shift, ConversationBuildingName);
                    }
                    else plan = ConversationLifeContext.Routine(routine,
                        key => homeSettlement == settlement ? ConversationBuildingName(key) : string.Empty);
                }
            }
            else if (native != null)
                plan = ConversationLifeContext.NativeDuty(ConversationBuildingName(native.WorkKey),
                    ConversationBuildingName(native.HomeKey),
                    game.PlayerEnterExit.IsPlayerInsideBuilding &&
                    game.PlayerEnterExit.BuildingDiscoveryData.buildingKey == native.WorkKey);
            var mood = new ConversationMoodInputs { Disposition = disposition, Hour = (int)(now % 86400 / 3600),
                Grieving = grieving, Alarmed = alarmed, Outdoors = outdoors, Weather = condition,
                Familiar = conversationPersonId != null && store != null && store.ConversationEncounters(conversationPersonId) > 1,
                Activity = activity };
            return " " + ConversationLifeContext.Personality(seed) + " " +
                ConversationLifeContext.Mood(seed, day, mood) + " " + plan;
        }

        private static uint ConversationNameSeed(string name, int settlement)
        {
            uint value = 2166136261u;
            foreach (char letter in name ?? string.Empty)
                unchecked { value = (value ^ letter) * 16777619u; }
            return StableSeed.Combine(value, unchecked((uint)settlement), 0x6469616cu, 1);
        }

        private static string ConversationSettlementName(int mapId)
        {
            if (mapId < 0) return string.Empty;
            int pixel = mapId & 0x000fffff;
            var reader = DaggerfallUnity.Instance.ContentReader;
            DaggerfallWorkshop.Utility.ContentReader.MapSummary summary;
            if (!reader.HasLocation(pixel % 1000, pixel / 1000, out summary) || summary.MapID != mapId)
                return string.Empty;
            DFLocation location;
            return reader.GetLocation(summary.RegionIndex, summary.MapIndex, out location) ? location.Name : string.Empty;
        }

        private static string ConversationBuildingName(int key)
        {
            if (key < 0) return string.Empty;
            var game = GameManager.Instance;
            BuildingSummary summary;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            if (!directory || !directory.GetBuildingSummary(key, out summary)) return string.Empty;
            try { return game.TalkManager.GetBuildingNameForBuildingKey(key); }
            catch (Exception) { return string.Empty; }
        }
    }
}
