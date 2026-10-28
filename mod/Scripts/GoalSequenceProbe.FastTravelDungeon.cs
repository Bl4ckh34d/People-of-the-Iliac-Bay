using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class GoalSequenceProbe
    {
        private static DFLocation SelectNativeDungeonDestination(double consentEnds)
        {
            var game=GameManager.Instance;var reader=DaggerfallUnity.Instance.ContentReader;
            var region=reader.MapFileReader.GetRegion(game.PlayerGPS.CurrentRegionIndex);
            bool horse=game.PlayerEntity.Items.Contains(ItemGroups.Transportation,(int)Transportation.Horse);
            bool cart=game.PlayerEntity.Items.Contains(ItemGroups.Transportation,(int)Transportation.Small_cart);
            int bestMinutes=int.MaxValue;DFLocation best=default(DFLocation);bool found=false;
            for(int index=0;index<region.LocationCount;index++)
            {
                var entry=region.MapTable[index];
                if(entry.MapId==game.PlayerGPS.CurrentLocation.MapTableData.MapId)continue;
                // Cities can also contain a castle dungeon. This fixture needs
                // a native dungeon site whose arrival is near its exterior entrance.
                if(entry.LocationType!=DFRegion.LocationTypes.DungeonLabyrinth &&
                    entry.LocationType!=DFRegion.LocationTypes.DungeonKeep &&
                    entry.LocationType!=DFRegion.LocationTypes.DungeonRuin)continue;
                var target=MapsFile.LongitudeLatitudeToMapPixel(entry.Longitude,entry.Latitude);
                var calculator=new TravelTimeCalculator();
                int minutes=game.GuildManager.FastTravel(calculator.CalculateTravelTime(target,false,true,false,horse,cart));
                if(minutes<=0 || minutes>=bestMinutes || FastTravelNow()+minutes*60+1200>=consentEnds)continue;
                DFLocation location;
                if(!reader.GetLocation(region.Name,region.MapNames[index],out location) || !location.Loaded || !location.HasDungeon)continue;
                best=location;bestMinutes=minutes;found=true;
            }
            RequireFastTravel(found,"No actual native dungeon journey fits the existing consent with twenty game minutes remaining.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_DUNGEON_DESTINATION: name="+best.Name+"; map="+best.MapTableData.MapId+
                "; nativeMinutes="+bestMinutes+"; originalConsentEnd="+consentEnds+"; newConsent=false");
            return best;
        }

        internal static IEnumerator VerifyNativeFastTravelDungeon(PeopleOfTheIliacBayMod host,StreetPopulation streets,bool resumeEntrance=false)
        {
            var game=GameManager.Instance;var roads=host.GetComponent<RoadTravelActors>();
            RequireFastTravel(NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) && streets && (streets.Ready || resumeEntrance) && roads &&
                !game.IsPlayerInside && game.PlayerGPS.HasCurrentLocation,
                "Dungeon fast travel requires an actual owned exterior companion checkpoint.");
            var store=(WorldStateStore)Field(host.GetType(),"store").GetValue(host);
            CompanionFastTravelQuote party=null;float ready=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<ready)
            {party=roads.CaptureFastTravelQuote();if(party.Members.Count>0)break;yield return null;}
            RequireFastTravel(party!=null && party.Members.Count==1,"Dungeon fast travel requires exactly one actual eligible companion.");
            var actor=party.Members[0].Actor;var job=store.ReadNpcAction(party.Members[0].ActionId);
            RequireFastTravel(job.FastTravelFollowOwner && game.PlayerEffectManager.DiseaseCount==0 && game.PlayerEffectManager.PoisonCount==0,
                "The real fast-travel checkpoint must retain its owner and be free of native travel illness.");
            if(resumeEntrance)
            {
                RequireFastTravel(game.PlayerGPS.CurrentLocation.HasDungeon,"Entrance resume requires the actual saved dungeon arrival.");
                yield return FinishNativeFastTravelDungeon(host,streets,actor,game.PlayerGPS.CurrentLocation.MapTableData.MapId,false);
                yield break;
            }
            string id=job.ResidentId,action=job.Id;double finish=job.FinishAt,consentDeadline=job.DeadlineAt;
            actor.CaptureState();string inventory=FastTravelInventory(store,id),relation=FastTravelRelationship(store,id);
            int body=actor.GetInstanceID(),person=actor.Person.GetInstanceID();
            var inventoryOwner=actor.GetComponent<NativeNpcInventoryOwner>();int inventoryOwnerId=inventoryOwner.GetInstanceID();
            var destination=SelectNativeDungeonDestination(Math.Min(finish,consentDeadline));
            int mapId=destination.MapTableData.MapId;
            var manager=DaggerfallUI.UIManager;var map=DaggerfallUI.Instance.DfTravelMapWindow;
            manager.PushWindow(map);yield return null;
            var popup=(NativeCompanionTravelPopUp)UIWindowFactory.GetInstanceWithArgs(UIWindowType.TravelPopUp,new object[]{manager,map,map});
            typeof(DaggerfallTravelPopUp).GetProperty("EndPos").SetValue(popup,
                MapsFile.LongitudeLatitudeToMapPixel(destination.MapTableData.Longitude,destination.MapTableData.Latitude),null);
            popup.SpeedCautious=false;popup.TravelShip=false;popup.SleepModeInn=true;
            manager.PushWindow(popup);yield return null;
            int total,pieces,minutes;ReadNativeFastTravelPrice(popup,1,out total,out pieces,out minutes);
            int purse=game.PlayerEntity.GetGoldAmount();
            RequireFastTravel(purse>=total && game.PlayerEntity.GoldPieces>=pieces,"Actual dungeon journey lacks the required native fare.");
            double before=FastTravelNow();int departures=0,arrivals=0;
            Action<DaggerfallTravelPopUp> pre=window=>departures++;
            DaggerfallTravelPopUp.OnOnPostFastTravelEventHandler post=()=>arrivals++;
            DaggerfallTravelPopUp.OnPreFastTravel+=pre;DaggerfallTravelPopUp.OnPostFastTravel+=post;
            try
            {
                popup.BeginButtonOnClickHandler(null,Vector2.zero);ready=Time.realtimeSinceStartup+30;
                while(Time.realtimeSinceStartup<ready && (arrivals==0 || !actor || !actor.gameObject.activeInHierarchy ||
                    game.StreamingWorld.IsInit || !game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer ||
                    !game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.MapTableData.MapId!=mapId))yield return null;
                RequireFastTravel(departures==1 && arrivals==1 && actor && actor.gameObject.activeInHierarchy &&
                    game.PlayerGPS.HasCurrentLocation && game.PlayerGPS.CurrentLocation.MapTableData.MapId==mapId &&
                    game.PlayerEntity.GetGoldAmount()==purse-total && FastTravelNow()>=before+minutes*60 && FastTravelNow()<before+minutes*60+120,
                    "Native dungeon journey failed its arrival, fare or duration contract.");
                job=store.ReadNpcAction(action);
                RequireFastTravel(NpcCompanionFastTravel.CanFollow(store,job,FastTravelNow()) && job.FastTravelFollowOwner &&
                    job.FinishAt==finish && job.DeadlineAt==consentDeadline && actor.GetInstanceID()==body &&
                    actor.Person.GetInstanceID()==person && actor.GetComponent<NativeNpcInventoryOwner>().GetInstanceID()==inventoryOwnerId,
                    "Native dungeon journey changed the body, owner or original consent.");
                actor.CaptureState();RequireFastTravel(FastTravelInventory(store,id)==inventory && FastTravelRelationship(store,id)==relation,
                    "Native dungeon journey changed the actual inventory, money or relationship.");
            }
            finally{DaggerfallTravelPopUp.OnPreFastTravel-=pre;DaggerfallTravelPopUp.OnPostFastTravel-=post;}
            yield return SaveFastTravelCheckpoint(game,"People - fast travel dungeon entry");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_DUNGEON_CHECKPOINT: person="+id+"; map="+mapId+
                "; actualNativeJourney=true; fare="+total+"; minutes="+minutes+"; originalBodyAndConsent=true; checkpointSaved=true; approachPending=true");
            yield return FinishNativeFastTravelDungeon(host,streets,actor,mapId,true);
        }

        private static IEnumerator FinishNativeFastTravelDungeon(PeopleOfTheIliacBayMod host,StreetPopulation streets,
            ResidentActor actor,int mapId,bool nativeJourneyThisRun)
        {
            var game=GameManager.Instance;string id=actor.Resident.Id;
            yield return NativeDungeonFollowProbe.StageActualFastTravelEntrance(actor,mapId);
            yield return SaveFastTravelCheckpoint(game,"People - fast travel dungeon entry");
            yield return NativeDungeonFollowProbe.RunExistingFastTravelFollower(host,streets,mapId,id);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_DUNGEON_PASS: person="+id+"; map="+mapId+
                "; nativeFastTravelThisRun="+nativeJourneyThisRun+"; nativeDungeonRoundtrip=true; originalConsent=true; setupNpcPlacement=false");
        }
    }
}
