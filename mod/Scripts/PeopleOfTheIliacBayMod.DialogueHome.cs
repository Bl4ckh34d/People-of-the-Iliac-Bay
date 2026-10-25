using System;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private bool conversationHomeTopicAvailable;

        private bool TryConversationHome(out int homeSettlement, out int homeKey, out string homeName)
        {
            homeSettlement = -1;
            homeKey = -1;
            homeName = string.Empty;
            if (GameManager.Instance == null || !GameManager.Instance.PlayerGPS.HasCurrentLocation || store == null)
                return false;

            int currentSettlement = GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId;
            ResidentActor actor = conversationActor;
            if (actor && actor.Resident != null && actor.Resident.IsAlive && actor.Resident.Id == conversationPersonId)
            {
                ResidentTrip trip = store.GetResidentDialogueTrip(actor.Resident.Id);
                homeSettlement = trip == null ? currentSettlement : trip.HomeSettlement;
                homeKey = actor.Resident.HomeKey;
            }
            else
            {
                NativePerson native = FindConversationNative(currentSettlement);
                if (native == null || !native.IsAlive || native.Id != conversationPersonId) return false;
                var reservation=store.InnLodgerHome(native.Id);
                homeSettlement = reservation!=null ? reservation.HomeSettlement : native.SettlementId;
                homeKey = reservation!=null ? reservation.HomeKey : native.HomeKey;
            }

            if (homeSettlement < 0 || homeKey < 0) return false;
            if (homeSettlement != currentSettlement) return true;
            var directory = GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
            BuildingSummary summary;
            if (!directory || !directory.GetBuildingSummary(homeKey, out summary)) return false;
            homeName = ConversationBuildingName(homeKey);
            // Residence summaries are authoritative even when the native TalkManager
            // has no printable name for the house. The key still identifies an exact
            // building for directional hints and PlayerGPS discovery.
            return true;
        }

        private string ConversationHomeContext(int disposition)
        {
            string traveler=CurrentTravelerIdentityContext();
            if(!string.IsNullOrEmpty(traveler))return traveler;
            int settlement, building;
            string name;
            if (!TryConversationHome(out settlement, out building, out name))
                return "The NPC cannot point to a home location. Do not invent one.";
            string town = ConversationSettlementName(settlement);
            if (settlement == GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId && !string.IsNullOrWhiteSpace(name))
                return "Private verified home knowledge: " + town + ", " + name + ". Decide whether to disclose based on your personality, relationship and current request; disposition is not a fixed permission threshold. This is the exact building. Do not add road, gate, or river details.";
            if (settlement == GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId)
                return "Your actual home is an unnamed house in " + town + ". Verified direction from here: " +
                    ConversationHomeDirection(building) + ". If no direction is supplied, do not guess. Do not invent a name or landmark.";
            return string.IsNullOrWhiteSpace(town) ?
                "The NPC has a tracked home outside this settlement, but its location is not available here. Do not invent an address." :
                "The NPC's tracked home is in " + town + ", outside the current settlement. Do not invent a building or route.";
        }

        private bool TrySemanticHomeConversation(DaggerfallTalkWindow window,string text,string history,
            ConversationContextSnapshot snapshot=null)
        {
            if(snapshot!=null && !IsCurrentConversationContext(snapshot,window))
            {HandleRejectedConversationStage(snapshot,window,0,text,"No home disclosure or map action was accepted.");return true;}
            int settlement,building;string name;
            bool available=TryConversationHome(out settlement,out building,out name);
            if(!available){settlement=-1;building=-1;}
            var gps=GameManager.Instance.PlayerGPS;
            ConversationLocationSnapshot submittedLocation=snapshot==null?null:snapshot.PlayerLocationAtSubmission;
            int submittedSettlement=-1;
            bool hasSubmittedSettlement=false;
            if(submittedLocation!=null)
            {submittedSettlement=submittedLocation.PlaceCopy().SettlementId;hasSubmittedSettlement=submittedSettlement>=0;}
            bool mapLocationAvailable=snapshot==null?gps.HasCurrentLocation:hasSubmittedSettlement;
            int mapSettlement=snapshot==null?(gps.HasCurrentLocation?gps.CurrentLocation.MapTableData.MapId:-1):submittedSettlement;
            bool canMap=available && !saveFault && conversationMapRequestsEnabled && mapLocationAvailable &&
                settlement==mapSettlement;
            int session=memorySession;string person=conversationPersonId;
            int expectedSettlement=settlement,expectedBuilding=building;
            string speaker=conversationActor?conversationActor.Person.NameNPC:person;
            int effectiveDisposition=snapshot==null?CurrentDialogueDisposition():snapshot.EffectiveDisposition;
            string here=snapshot==null?string.Empty:submittedLocation==null?
                " Player HERE at submission is unknown. Do not infer a current location or use a later position.":
                " "+submittedLocation.Context();
            string homeContext;
            if(snapshot==null) homeContext=ConversationHomeContext(effectiveDisposition);
            else if(!available) homeContext="The NPC cannot point to a verified home location. Do not invent one.";
            else
            {
                string homeTown=ConversationSettlementName(settlement);
                if(!hasSubmittedSettlement)
                    homeContext="Private verified home location: "+(string.IsNullOrWhiteSpace(homeTown)?"settlement "+settlement:homeTown)+
                        ". Player location at submission is unknown; do not infer a direction, current location, or map eligibility.";
                else if(settlement==submittedSettlement && !string.IsNullOrWhiteSpace(name))
                    homeContext="Private verified home knowledge: "+(string.IsNullOrWhiteSpace(homeTown)?"settlement "+settlement:homeTown)+
                        ", "+name+". Exact native building key="+building+". Do not add road, gate, or river details.";
                else if(settlement==submittedSettlement)
                    homeContext="Your actual home is an unnamed house in "+(string.IsNullOrWhiteSpace(homeTown)?"the captured settlement":""+homeTown)+
                        ". Do not invent a building name or direction from HERE.";
                else
                    homeContext="The NPC has a tracked private home in "+(string.IsNullOrWhiteSpace(homeTown)?"settlement "+settlement:homeTown)+
                        ", outside the settlement captured at submission. Do not invent an address or route.";
            }
            string homeMarkerLabel=snapshot==null?null:ConversationHomeMarkerLabel();
            string context=snapshot==null?
                CurrentConversationClock()+" "+ConversationPlaceContext()+" "+
                    CurrentConversationLifeContext(speaker,effectiveDisposition)+" "+homeContext:
                ConversationContextFormatter.ForSemanticClassifier(snapshot,2048)+here+" "+homeContext;
            context+=" Effective disposition="+effectiveDisposition+". Decide your own willingness to disclose; there is no fixed approval threshold. "+
                "Home was previously discussed in this session="+conversationHomeTopicAvailable+". No key or home-entry permission is being offered.";
            long stageToken=snapshot==null?0:BeginConversationContextStage(snapshot,window);
            if(snapshot!=null && stageToken<=0)
            {HandleRejectedConversationStage(snapshot,window,stageToken,text,"No home disclosure or map action was accepted.");return true;}
            if(!localDialogue.Classify(NewDialogueConnection(),SemanticHomeChat.Prompt(text,context,settlement,building,canMap),history,output=>
            {
                if(!CurrentMemorySession(window,session) || conversationPersonId!=person ||
                    snapshot!=null && !TryConsumeConversationContextStage(snapshot,window,stageToken))
                {if(snapshot!=null)HandleRejectedConversationStage(snapshot,window,stageToken,text,"No home disclosure or map action was accepted.");return;}
                if(output==LocalDialogueClient.Unavailable || output==LocalDialogueClient.ContextLimit)
                {conversationBusy=false;AppendRememberedDialoguePair(window,text,DialogueReplyFallback.UnavailableReply,"unavailable");DaggerfallUI.AddHUDText("Local dialogue unavailable. No home location was disclosed or marked.");return;}
                var decision=SemanticHomeChat.Parse(output,expectedSettlement,expectedBuilding,canMap);
                string result="No home disclosure or map action was accepted. Do not disclose the private home location or claim a map marker.";
                string fallback=null;
                if(decision!=null && decision.Kind=="DECLINE")
                    result="You chose to keep your home location private. Explain naturally without disclosing the address, building, direction or town. No map action occurred.";
                else if(decision!=null && (decision.Kind=="DISCLOSE" || decision.Kind=="MAP"))
                {
                    int currentSettlement,currentBuilding;string currentName;
                    if(TryConversationHome(out currentSettlement,out currentBuilding,out currentName) &&
                        currentSettlement==expectedSettlement && currentBuilding==expectedBuilding)
                    {
                        conversationHomeTopicAvailable=true;
                        result="You willingly chose to share your verified home location. "+homeContext+
                            " No key, invitation or home-entry permission was granted.";
                        fallback="I can tell you where I live.";
                        if(decision.Kind=="MAP")
                        {
                            var currentGps=GameManager.Instance.PlayerGPS;
                            bool permitted=!saveFault && conversationMapRequestsEnabled && currentGps.HasCurrentLocation &&
                                currentGps.CurrentLocation.MapTableData.MapId==mapSettlement && expectedSettlement==mapSettlement;
                            if(permitted)
                            {
                                currentGps.DiscoverBuilding(expectedBuilding);
                                PlayerGPS.DiscoveredBuilding discovery;
                                    if(string.IsNullOrWhiteSpace(snapshot==null?currentName:name) && currentGps.GetDiscoveredBuilding(expectedBuilding,out discovery) &&
                                    !discovery.isOverrideName && string.IsNullOrWhiteSpace(discovery.customUserDisplayName))
                                    currentGps.SetDiscoveredBuildingCustomName(expectedBuilding,
                                        snapshot==null?ConversationHomeMarkerLabel():homeMarkerLabel);
                            }
                            bool marked=permitted && currentGps.HasDiscoveredBuilding(expectedBuilding);
                            result+=marked?" The exact home is now verified as discovered on the player's map. You may report that result.":
                                " The map request could not be completed. Do not claim a marker was placed.";
                            fallback=marked?"I've marked my home on your map.":"I couldn't mark my home on your map.";
                        }
                    }
                }
                conversationResponseCue+=" "+result;
                SubmitSocialText(window,text,history,knowledgeResult:result,verifiedFallback:fallback,snapshot:snapshot);
            },SemanticHomeChat.Grammar(settlement,building,canMap)))
            {
                if(snapshot!=null && !TryConsumeConversationContextStage(snapshot,window,stageToken))
                {HandleRejectedConversationStage(snapshot,window,stageToken,text,"No home disclosure or map action was accepted.");return true;}
                conversationBusy=false;AppendRememberedDialoguePair(window,text,DialogueReplyFallback.UnavailableReply,"unavailable");DaggerfallUI.AddHUDText("Waiting for the current reply. No home location was disclosed or marked.");
            }
            return true;
        }

        private string ConversationHomeDirection(int buildingKey)
        {
            try { return GameManager.Instance.TalkManager.GetBuildingCompassDirection(buildingKey); }
            catch (Exception) { return string.Empty; }
        }

        private string ConversationHomeMarkerLabel()
        {
            string name = null;
            ResidentActor actor = conversationActor;
            if (actor && actor.Resident != null)
                name = !string.IsNullOrWhiteSpace(actor.Resident.FullName) ? actor.Resident.FullName : actor.Person.NameNPC;
            if (string.IsNullOrWhiteSpace(name))
            {
                NativePerson native = FindConversationNative(GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId);
                if (native != null) name = native.Name;
            }
            return string.IsNullOrWhiteSpace(name) ? "NPC home" : "Home of " + name;
        }
    }
}
