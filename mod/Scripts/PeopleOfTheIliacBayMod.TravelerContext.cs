using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private string ManagedTravelerPlanContext(WorldStateStore activeStore,string personId,double now,int settlement)
        {
            string delayed=streets?streets.PendingTravelerDepartureContext(activeStore,personId,now):null;
            return delayed??TravelerConversationContext.PlannedActivity(activeStore,personId,settlement,now,ConversationBuildingName);
        }

        private string CurrentTravelerIdentityContext()
        {
            var game=GameManager.Instance;
            int settlement=game!=null && game.PlayerGPS.HasCurrentLocation?game.PlayerGPS.CurrentLocation.MapTableData.MapId:-1;
            return TravelerConversationContext.IdentityAndLodging(store,conversationPersonId,settlement,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),ConversationSettlementName,ConversationBuildingName);
        }

        private ConversationContextFact<string> CaptureTravelerIdentityContext(WorldStateStore activeStore,string personId,double now)
        {
            var game=GameManager.Instance;
            int settlement=game!=null && game.PlayerGPS.HasCurrentLocation?game.PlayerGPS.CurrentLocation.MapTableData.MapId:-1;
            return VerifiedString(TravelerConversationContext.IdentityAndLodging(activeStore,personId,settlement,now,
                ConversationSettlementName,ConversationBuildingName),ConversationContextFactSource.Save,now);
        }
    }
}
