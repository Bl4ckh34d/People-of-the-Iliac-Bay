using System;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private bool TryCaptureNativeKeeperWorkDuty(ConversationSpeakerCapture speaker,
            ConversationContextSnapshot snapshot, out NpcMotivationDuty duty, out string siteId)
        {
            duty = null;
            siteId = null;
            if (speaker == null || snapshot == null || store == null ||
                speaker.Category != ConversationActorCategory.Static ||
                !(speaker.NativeObject is StaticNPC)) return false;

            StaticNPC npc = (StaticNPC)speaker.NativeObject;
            var data = npc.Data;
            int settlement, building;
            BuildingSummary summary;
            if (!TryConversationBuilding(out settlement, out building, out summary) ||
                data.mapID != settlement || data.buildingKey != building ||
                (summary.BuildingType != DFLocation.BuildingTypes.Tavern && !RMBLayout.IsShop(summary.BuildingType)) ||
                !NativeBusinessServices.IsKeeper(npc, summary.BuildingType)) return false;

            ConversationDialogueRole expectedRole = summary.BuildingType == DFLocation.BuildingTypes.Tavern
                ? ConversationDialogueRole.Innkeeper : ConversationDialogueRole.Shopkeeper;
            if (!speaker.Role.HasValue || speaker.Role.Value != expectedRole ||
                !string.Equals(speaker.PersonId, NativePersonId(data.mapID, data.buildingKey, data.hash), StringComparison.Ordinal))
                return false;

            NativePerson native = store.NativePeopleFor(settlement).Find(person => person != null &&
                string.Equals(person.Id, speaker.PersonId, StringComparison.Ordinal));
            if (native == null || !native.IsAlive || native.SettlementId != settlement || native.WorkKey != building ||
                native.NativeHash != data.hash || native.FactionId != data.factionID) return false;

            int type = (int)summary.BuildingType;
            if (type < 0 || type >= PlayerActivate.openHours.Length || type >= PlayerActivate.closeHours.Length)
                return false;
            int openHour = PlayerActivate.openHours[type];
            int closeHour = Math.Min(24, (int)PlayerActivate.closeHours[type]);
            if (openHour < 0 || openHour >= closeHour || closeHour > 24) return false;

            double now = snapshot.CapturedGameTime;
            double dayStart = Math.Floor(now / 86400d) * 86400d;
            double startsAt = dayStart + openHour * 3600d;
            double endsAt = dayStart + closeHour * 3600d;
            if (now < startsAt || now >= endsAt) return false;

            siteId = "building:" + settlement.ToString(CultureInfo.InvariantCulture) + ":" +
                building.ToString(CultureInfo.InvariantCulture);
            long revision = snapshot.Binding.RequestRevision;
            var evidence = new NpcMotivationEvidenceRef(snapshot.MotivationBranchId, native.Id,
                "native-work:" + siteId, NpcMotivationEvidenceKind.NativeObservation, revision, now);
            duty = new NpcMotivationDuty("work:" + native.Id + ":" + siteId, NpcMotivationDutyKind.Work,
                NpcMotivationDutyScope.RoutineWork | NpcMotivationDutyScope.Schedule,
                NpcMotivationDutyStatus.Active, startsAt, endsAt, revision, evidence);
            return true;
        }

        private static string NativePersonId(int settlementId, int buildingKey, int nativeHash)
        {
            return "npc:" + settlementId.ToString(CultureInfo.InvariantCulture) + ":" +
                buildingKey.ToString(CultureInfo.InvariantCulture) + ":" + nativeHash.ToString(CultureInfo.InvariantCulture);
        }
    }
}
