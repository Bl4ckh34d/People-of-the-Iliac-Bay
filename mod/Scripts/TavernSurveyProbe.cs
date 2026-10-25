using System;
using System.Collections;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class TavernSurveyProbe
    {
        private static void VerifyVisitSettlement(WorldState saved,string resident,int town)
        {
            var trip=saved.ResidentTrips.Find(t=>t.ResidentId==resident);
            if(trip==null || trip.InnStays.Count==0 || trip.InnReceptions.Count==0)
                throw new InvalidOperationException("Visit settlement fixture lacks lodging records.");
            if(trip.InnStays.Exists(s=>s.SettlementId!=town) || trip.InnReceptions.Exists(r=>r.SettlementId!=town) ||
                trip.ServiceExits.Exists(e=>e.SettlementId!=town))
                throw new InvalidOperationException("Native serializer lost visit settlement identity.");
            if(trip.InnStays.Exists(s=>string.IsNullOrEmpty(s.VisitId)) || trip.InnReceptions.Exists(r=>string.IsNullOrEmpty(r.VisitId)) ||
                trip.ServiceExits.Exists(e=>string.IsNullOrEmpty(e.VisitId)))
                throw new InvalidOperationException("Native serializer lost stable visit ID.");
            var legacy=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),saved));
            var old=legacy.ResidentTrips.Find(t=>t.ResidentId==resident);
            foreach(var stay in old.InnStays) {stay.SettlementId=null;stay.VisitId=null;}
            foreach(var reception in old.InnReceptions) {reception.SettlementId=null;reception.VisitId=null;}
            foreach(var exit in old.ServiceExits) {exit.SettlementId=null;exit.VisitId=null;}
            var migrated=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot());migrated.Restore(legacy);
            var copy=migrated.Snapshot().ResidentTrips.Find(t=>t.ResidentId==resident);
            if(copy.InnStays.Exists(s=>s.SettlementId!=town) || copy.InnReceptions.Exists(r=>r.SettlementId!=town) ||
                copy.ServiceExits.Exists(e=>e.SettlementId!=town) || copy.InnStays[0].BookingId!=trip.InnStays[0].BookingId)
                throw new InvalidOperationException("Legacy visit migration changed settlement or payment identity.");
            for(int i=0;i<trip.InnStays.Count;i++)
                if(copy.InnStays[i].VisitId!=trip.InnStays[i].VisitId) throw new InvalidOperationException("Legacy stay inferred wrong visit ID.");
            for(int i=0;i<trip.InnReceptions.Count;i++)
                if(copy.InnReceptions[i].VisitId!=trip.InnReceptions[i].VisitId) throw new InvalidOperationException("Legacy reception inferred wrong visit ID.");
            for(int i=0;i<trip.ServiceExits.Count;i++)
                if(copy.ServiceExits[i].VisitId!=trip.ServiceExits[i].VisitId) throw new InvalidOperationException("Legacy exit inferred wrong visit ID.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_VISIT_SETTLEMENT_SAVE_PASS: settlement="+town+"; exits="+copy.ServiceExits.Count+"; native fields and legacy inference.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_VISIT_ID_SAVE_PASS: settlement="+town+"; exits="+copy.ServiceExits.Count+"; stable IDs and legacy inference.");
        }
        private static void VerifyRemoteClaims(BuildingDirectory directory,StaticDoor entry,TavernRoomSurvey.Result inventory)
        {
            var local=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); local.GetOrCreate(directory.MapID,new int[0]);
            local.RegisterLodging(directory.MapID,entry.buildingKey,inventory.Rooms);
            NativeInnOccupants.Claim(local,directory,entry,inventory.Rooms,inventory.Occupants,100);
            var remote=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); remote.GetOrCreate(directory.MapID,new int[0]);
            remote.RegisterLodging(directory.MapID,entry.buildingKey,inventory.Rooms);
            uint seed=DFRandom.Seed;
            NativeInnOccupants.Claim(remote,directory.MapID,directory.LocationData,entry,inventory.Rooms,inventory.Occupants,100);
            if(DFRandom.Seed!=seed) throw new InvalidOperationException("Remote claims changed global random state.");
            var a=local.NativePeopleFor(directory.MapID); var b=remote.NativePeopleFor(directory.MapID);
            if(a.Count!=b.Count) throw new InvalidOperationException("Remote native identity count differs.");
            foreach(var person in a)
            {
                var other=b.Find(p=>p.Id==person.Id);
                if(other==null || other.Name!=person.Name || other.Race!=person.Race ||
                    remote.NativeLodgingBed(person.Id)!=local.NativeLodgingBed(person.Id))
                    throw new InvalidOperationException("Remote native room claim differs.");
            }
            var foreign=directory.LocationData; foreign.LocationIndex++;
            for(int r=0;r<MapsFile.RegionRaces.Length;r++)
                if(MapsFile.RegionRaces[r]!=MapsFile.RegionRaces[foreign.RegionIndex]) {foreign.RegionIndex=r;break;}
            var alternate=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); alternate.GetOrCreate(directory.MapID,new int[0]);
            alternate.RegisterLodging(directory.MapID,entry.buildingKey,inventory.Rooms);
            NativeInnOccupants.Claim(alternate,directory.MapID,foreign,entry,inventory.Rooms,inventory.Occupants,100);
            foreach(var person in alternate.NativePeopleFor(directory.MapID))
                if(person.FactionId==0 && person.Race!=MapsFile.RegionRaces[foreign.RegionIndex]+1)
                    throw new InvalidOperationException("Remote occupant inherited player region.");
            if(DFRandom.Seed!=seed) throw new InvalidOperationException("Foreign claims changed global random state.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_REMOTE_CLAIMS_PASS: building="+entry.buildingKey+"; people="+a.Count);
        }
        private static void VerifyRemoteReceipt(BuildingDirectory directory,StaticDoor entry,TavernRoomSurvey.Result inventory)
        {
            int marker=-1; string room=null;
            foreach(var pair in inventory.Markers) {marker=pair.Key;room=pair.Value;break;}
            if(marker<0) throw new InvalidOperationException("No rental marker for receipt fixture.");
            var store=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); store.GetOrCreate(directory.MapID,new int[0]);
            var receipt=new RoomRental_v1 {mapID=directory.MapID,buildingKey=entry.buildingKey,allocatedBedIndex=marker,expiryTime=1000};
            NativeTavernRentals.RegisterInventory(store,directory.MapID,directory.LocationData,entry,inventory,receipt,100);
            NativeTavernRentals.RegisterInventory(store,directory.MapID,directory.LocationData,entry,inventory,receipt,200);
            var ledger=store.Snapshot().Lodgings[0];
            if(ledger.Bookings.Count!=1 || !ledger.Bookings[0].Player || ledger.Bookings[0].RoomId!=room || ledger.Bookings[0].EndsAt!=1000)
                throw new InvalidOperationException("Remote registration lost or duplicated paid receipt.");
            var paidRoom=ledger.Rooms.Find(r=>r.Id==room);
            if(ledger.Occupants.Exists(o=>paidRoom.Beds.Contains(o.BedId)))
                throw new InvalidOperationException("Permanent occupant displaced paid player room.");
            if(store.TryBookLodging(directory.MapID,entry.buildingKey,"conflicting-remote-guest","visitor",777,false,1,200,500,room))
                throw new InvalidOperationException("Remote guest double-booked player room.");
            var invalid=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); invalid.GetOrCreate(directory.MapID,new int[0]);
            receipt.allocatedBedIndex=int.MaxValue; bool rejected=false;
            try {NativeTavernRentals.RegisterInventory(invalid,directory.MapID,directory.LocationData,entry,inventory,receipt,100);}
            catch(InvalidOperationException) {rejected=true;}
            if(!rejected || invalid.Snapshot().Lodgings.Count!=0 || invalid.NativePeopleFor(directory.MapID).Count!=0)
                throw new InvalidOperationException("Invalid receipt partially registered destination.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_REMOTE_RECEIPT_PASS: building="+entry.buildingKey);
        }
        private static void VerifyLocalPatronFlow(DaggerfallInterior room,InteriorGeometry geometry,WorldState saved,int town,double now)
        {
            var store=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); store.Restore(saved);
            var people=store.NativePeopleFor(town);
            Func<StaticNPC,bool> isLivingKeeper=npc=>
            {
                var person=people.Find(p=>p.WorkKey==room.EntryDoor.buildingKey && p.NativeHash==npc.Data.hash);
                return person!=null && person.IsAlive;
            };
            var absent=new ShopVisitGeometry(room,geometry,null);
            var absentVisit=absent.Create("inn-probe-absent",1,now+10,now+7210);
            if(absent.TavernKeeper || absentVisit!=null)
                throw new InvalidOperationException("Tavern patron flow used a keeper without a living-keeper resolver.");
            var rejected=new ShopVisitGeometry(room,geometry,npc=>false);
            if(rejected.Create("inn-probe-dead",2,now+10,now+7210)!=null)
                throw new InvalidOperationException("Tavern patron flow fell back after all keepers were rejected.");

            StaticNPC expected=null;
            foreach(var npc in room.GetComponentsInChildren<StaticNPC>())
            {
                FactionFile.FactionData faction;
                if(isLivingKeeper(npc) && GameManager.Instance.PlayerEntity.FactionData.GetFactionData(npc.Data.factionID,out faction) &&
                    faction.sgroup==(int)FactionFile.SocialGroups.Merchants) {expected=npc;break;}
            }
            bool keeperAlive=true;
            var rechecked=new ShopVisitGeometry(room,geometry,npc=>keeperAlive && isLivingKeeper(npc));
            if(expected && rechecked.TavernKeeper!=expected) throw new InvalidOperationException("Tavern geometry missed living keeper at survey time.");
            keeperAlive=false;
            if(rechecked.Create("inn-probe-became-dead",4,now+10,now+7210)!=null)
                throw new InvalidOperationException("Tavern patron flow used a keeper who died after survey.");
            var visits=new ShopVisitGeometry(room,geometry,isLivingKeeper);
            if(!expected)
            {
                if(visits.Create("inn-probe-no-living",3,now+10,now+7210)!=null || visits.TavernKeeper)
                    throw new InvalidOperationException("Tavern without living keeper created local patron visit.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_PATRON_PASS: building="+room.EntryDoor.buildingKey+"; no living keeper; no local fallback.");
                return;
            }
            double start=now+10,end=now+7210;
            var journey=visits.Create("inn-probe-local",3,start,end);
            if(journey==null || visits.TavernKeeper!=expected)
                throw new InvalidOperationException("Tavern local patron did not select living keeper.");
            var stops=new List<ShopOccupancy>(journey.Occupancies());
            if(stops.Count<3 || stops[0].Start<start || stops[0].End<=stops[0].Start)
                throw new InvalidOperationException("Tavern local patron lacks keeper and common-room stops.");
            var keeperLocal=room.transform.InverseTransformPoint(expected.transform.position);
            double keeperDistance=Math.Sqrt(Math.Pow(stops[0].Position.X-keeperLocal.x,2)+Math.Pow(stops[0].Position.Y-keeperLocal.y,2)+
                Math.Pow(stops[0].Position.Z-keeperLocal.z,2));
            if(keeperDistance>2.5) throw new InvalidOperationException("Tavern local patron stopped away from living keeper.");
            int browsing=0;
            var bedGrounds=new List<Vector3>();
            foreach(var bed in BedGeometry.Read(room)) {Vector3 ground;if(geometry.StandBesideBed(bed,out ground)) bedGrounds.Add(ground);}
            for(int i=1;i<stops.Count;i++)
            {
                browsing++; double nearestBed=double.MaxValue;
                foreach(var bed in bedGrounds)
                {
                    double dx=stops[i].Position.X-bed.x,dy=stops[i].Position.Y-bed.y,dz=stops[i].Position.Z-bed.z;
                    nearestBed=Math.Min(nearestBed,dx*dx+dy*dy+dz*dz);
                }
                if(nearestBed<6.25) throw new InvalidOperationException("Tavern local patron browsed beside bedroom bed.");
            }
            if(browsing<2 || journey.Evaluate(start).Phase!=ShopVisitPhase.Walking ||
                journey.Evaluate(stops[stops.Count-1].End+0.1).Phase!=ShopVisitPhase.Leaving ||
                journey.Evaluate(end).Phase!=ShopVisitPhase.Complete)
                throw new InvalidOperationException("Tavern local patron did not browse then leave through entrance route.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_PATRON_PASS: building="+room.EntryDoor.buildingKey+"; keeper="+expected.Data.hash+
                "; keeper-first, common-room stops="+browsing+", exit route, missing/dead keeper fallback rejected.");
        }
        public static IEnumerator Run()
        {
            var game=GameManager.Instance;
            if(game.IsPlayerInside) game.PlayerEnterExit.TransitionExterior();
            yield return new WaitForSecondsRealtime(1);
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var taverns=directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern);
            if(taverns.Count==0) throw new InvalidOperationException("No taverns to survey.");
            yield return VerifyExteriorInventory(directory,taverns.Count,taverns[0].buildingKey);
            var streetGeometry=new TownGeometry(directory,game.StreamingWorld.GetCurrentCityNavigation());
            var metadata=new UnloadedSettlementSurvey.Result();
            yield return UnloadedSettlementSurvey.ReadSteps(directory.LocationData,metadata);
            int totalBeds=0,totalOccupants=0,totalSleepVerified=0,missingSleepArt=0;
            bool socialMovementChecked=false;
            foreach(var tavern in taverns)
            {
                DaggerfallStaticDoors owner=null; StaticDoor entry=default(StaticDoor);
                foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach(var door in candidate.Doors)
                        if(door.doorType==DoorTypes.Building && door.buildingKey==tavern.buildingKey)
                        { owner=candidate; entry=door; break; }
                if(!owner) throw new InvalidOperationException("Tavern entrance missing.");
                var travelerRoutes=streetGeometry.SurveyTravelerRoutes(tavern.buildingKey);
                if(travelerRoutes.Count==0) throw new InvalidOperationException("No physical road-edge route to tavern "+tavern.buildingKey);
                bool walled=directory.GetComponentsInChildren<DaggerfallCityGate>().Length>0;
                foreach(var route in travelerRoutes)
                {
                    if(route.Walled!=walled || route.Points.Count<2) throw new InvalidOperationException("Invalid traveler route or wall classification.");
                    var end=route.Points[route.Points.Count-1]; var target=streetGeometry.Entrances[tavern.buildingKey].LocalDoor;
                    if(Math.Abs(end.X-target.x)>.01 || Math.Abs(end.Z-target.z)>.01) throw new InvalidOperationException("Traveler route misses inn entrance.");
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_ROUTE_PASS: building="+tavern.buildingKey+"; routes="+travelerRoutes.Count+"; walled="+walled);
                var playerPosition=game.PlayerMotor.transform.position;
                int people=UnityEngine.Object.FindObjectsOfType<StaticNPC>().Length;
                Dictionary<int,string> unloadedMarkers;
                Dictionary<int,string> unloadedOccupants;
                var unloadedRooms=TavernRoomSurvey.ReadUnloaded(owner.transform,entry,out unloadedMarkers,out unloadedOccupants);
                UnloadedSettlementSurvey.Entrance remoteDoor=null;
                foreach(var candidate in metadata.Grid.Entrances)
                    if(candidate.BuildingKey==tavern.buildingKey) {remoteDoor=candidate;break;}
                if(remoteDoor==null) throw new InvalidOperationException("Metadata inn entrance missing.");
                var remote=new TavernRoomSurvey.Result();
                yield return TavernRoomSurvey.ReadRemoteSteps(remoteDoor,directory.LocationData.Climate.ClimateType,remote,()=>true);
                if(!remote.KeeperHash.HasValue || remote.ReceptionRoute==null || remote.ReceptionRoute.Count<2)
                    throw new InvalidOperationException("Remote inn lacks keeper approach geometry.");
                if(remote.Rooms==null || remote.Rooms.Count!=unloadedRooms.Count || remote.Markers.Count!=unloadedMarkers.Count ||
                    remote.Occupants.Count!=unloadedOccupants.Count) throw new InvalidOperationException("Remote inn inventory differs from native entrance survey.");
                for(int r=0;r<unloadedRooms.Count;r++)
                    if(remote.Rooms[r].Id!=unloadedRooms[r].Id || string.Join(",",remote.Rooms[r].Beds.ToArray())!=string.Join(",",unloadedRooms[r].Beds.ToArray()))
                        throw new InvalidOperationException("Remote inn room identity mismatch.");
                foreach(var marker in unloadedMarkers) if(remote.Markers[marker.Key]!=marker.Value)
                    throw new InvalidOperationException("Remote rental marker mismatch.");
                foreach(var occupant in unloadedOccupants) if(remote.Occupants[occupant.Key]!=occupant.Value)
                    throw new InvalidOperationException("Remote permanent occupant mismatch.");
                var cancelled=new TavernRoomSurvey.Result();
                yield return TavernRoomSurvey.ReadRemoteSteps(remoteDoor,directory.LocationData.Climate.ClimateType,cancelled,()=>false);
                if(cancelled.Rooms!=null) throw new InvalidOperationException("Cancelled remote inventory was published.");
                VerifyRemoteClaims(directory,remoteDoor.NativeDoor,remote);
                VerifyRemoteReceipt(directory,remoteDoor.NativeDoor,remote);
                if(tavern.buildingKey==taverns[0].buildingKey)
                {
                    bool valid=true; var interrupted=new TavernRoomSurvey.Result();
                    var first=TavernRoomSurvey.ReadRemoteSteps(remoteDoor,directory.LocationData.Climate.ClimateType,interrupted,()=>valid);
                    var secondResult=new TavernRoomSurvey.Result();
                    var second=TavernRoomSurvey.ReadRemoteSteps(remoteDoor,directory.LocationData.Climate.ClimateType,secondResult,()=>valid);
                    try
                    {
                        while(first.MoveNext() && !GameObject.Find("People of the Iliac Bay remote inn owner")) yield return null;
                        if(!GameObject.Find("People of the Iliac Bay remote inn owner")) throw new InvalidOperationException("Remote cancellation fixture never started.");
                        if(!second.MoveNext() || secondResult.Rooms!=null) throw new InvalidOperationException("Concurrent survey bypassed queue.");
                        valid=false;
                        if(first.MoveNext() || second.MoveNext() || interrupted.Rooms!=null || secondResult.Rooms!=null ||
                            GameObject.Find("People of the Iliac Bay remote inn owner")) throw new InvalidOperationException("Cancelled remote survey retained geometry or results.");
                    }
                    finally {(first as IDisposable)?.Dispose();(second as IDisposable)?.Dispose();}
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_REMOTE_CANCEL_PASS: active and queued surveys cleaned up.");
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_REMOTE_INN_PASS: building="+tavern.buildingKey);
                var sliced=new TavernRoomSurvey.Result();
                var operation=TavernRoomSurvey.ReadUnloadedSteps(owner.transform,entry,sliced);
                int slices=0;
                try { while(operation.MoveNext()) { slices++; yield return null; } }
                finally { (operation as IDisposable)?.Dispose(); }
                if(slices<1 || sliced.Rooms.Count!=unloadedRooms.Count || sliced.Markers.Count!=unloadedMarkers.Count ||
                    sliced.Occupants.Count!=unloadedOccupants.Count) throw new InvalidOperationException("Sliced tavern survey differs from synchronous survey.");
                for(int r=0;r<unloadedRooms.Count;r++)
                    if(sliced.Rooms[r].Id!=unloadedRooms[r].Id || string.Join(",",sliced.Rooms[r].Beds.ToArray())!=string.Join(",",unloadedRooms[r].Beds.ToArray()))
                        throw new InvalidOperationException("Sliced survey changed bedroom partition.");
                foreach(var marker in unloadedMarkers) if(sliced.Markers[marker.Key]!=marker.Value)
                    throw new InvalidOperationException("Sliced survey changed rental marker.");
                foreach(var occupant in unloadedOccupants) if(sliced.Occupants[occupant.Key]!=occupant.Value)
                    throw new InvalidOperationException("Sliced survey changed native bed ownership.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SLICED_TAVERN_PASS: building="+tavern.buildingKey+"; slices="+slices);
                if(game.IsPlayerInside || game.PlayerMotor.transform.position!=playerPosition ||
                    UnityEngine.Object.FindObjectsOfType<StaticNPC>().Length!=people)
                    throw new InvalidOperationException("Unloaded tavern survey changed player scene or spawned people.");
                TownSmokeHarness.EnterInterior(owner.transform,entry);
                yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
                var room=game.PlayerEnterExit.Interior;
                if(!room || room.EntryDoor.buildingKey!=tavern.buildingKey) throw new InvalidOperationException("Wrong tavern loaded.");
                // PopToHUD above resumes gameplay. Give the live controller an Update
                // before reading its keeper bindings and interior navigation; the
                // entrance description window previously kept IsPlayingGame false.
                var liveStreets=UnityEngine.Object.FindObjectOfType<StreetPopulation>();
                var boundInterior=typeof(StreetPopulation).GetField("interior",
                    System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                float bindingDeadline=Time.realtimeSinceStartup+30;
                do { yield return null; }
                while(liveStreets && (!liveStreets.Ready || (DaggerfallInterior)boundInterior.GetValue(liveStreets)!=room) &&
                    Time.realtimeSinceStartup<bindingDeadline);
                if(!liveStreets || !liveStreets.Ready || (DaggerfallInterior)boundInterior.GetValue(liveStreets)!=room)
                    throw new InvalidOperationException("Live inn controller did not bind after entrance UI closed; " +
                        "controller="+(liveStreets!=null)+"; enabled="+(liveStreets && liveStreets.isActiveAndEnabled)+
                        "; streetsReady="+(liveStreets && liveStreets.Ready)+"; bound="+
                        (liveStreets ? Convert.ToString(boundInterior.GetValue(liveStreets)) : "absent")+"; "+
                        UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>().TestStatus());
                var beds=BedGeometry.Read(room); totalBeds+=beds.Count;
                var rest=room.FindMarkers(DaggerfallInterior.InteriorMarkerTypes.Rest);
                for(int index=0;index<rest.Length;index++)
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_REST: building="+tavern.buildingKey+"; index="+index+
                        "; position="+room.transform.InverseTransformPoint(rest[index]).ToString("F3"));
                var navigation=new InteriorGeometry(room); int reachable=0;
                StaticNPC surveyedKeeper=null;
                foreach(var npc in room.GetComponentsInChildren<StaticNPC>())
                    if(npc.Data.hash==remote.KeeperHash.Value) {surveyedKeeper=npc;break;}
                if(!surveyedKeeper || !navigation.EntryGround.HasValue) throw new InvalidOperationException("Remote keeper not found in loaded inn.");
                var currentState=(WorldState)UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>().GetSaveData();
                VerifyLocalPatronFlow(room,navigation,currentState,directory.MapID,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                if(!socialMovementChecked)
                {
                    var adapter=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
                    yield return InnSocialMovementProbe.Run(adapter,adapter.GetComponent<StreetPopulation>(),room);
                    socialMovementChecked=true;
                }
                var start=remote.ReceptionRoute[0];var counterEnd=remote.ReceptionRoute[remote.ReceptionRoute.Count-1];
                var startWorld=room.transform.TransformPoint(new Vector3((float)start.X,(float)start.Y,(float)start.Z));
                var endWorld=room.transform.TransformPoint(new Vector3((float)counterEnd.X,(float)counterEnd.Y,(float)counterEnd.Z));
                if(Vector3.Distance(startWorld,navigation.EntryGround.Value)>0.3f ||
                    Vector3.Distance(endWorld+Vector3.up,surveyedKeeper.transform.position)>4)
                    throw new InvalidOperationException("Remote reception endpoints disagree with actual entry or keeper.");
                for(int step=1;step<remote.ReceptionRoute.Count;step++)
                {
                    var a=remote.ReceptionRoute[step-1];var b=remote.ReceptionRoute[step];
                    if(!navigation.HasClearLine(room.transform.TransformPoint(new Vector3((float)a.X,(float)a.Y+1,(float)a.Z)),
                        room.transform.TransformPoint(new Vector3((float)b.X,(float)b.Y+1,(float)b.Z))))
                        throw new InvalidOperationException("Remote reception route crosses loaded inn geometry.");
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_REMOTE_RECEPTION_PASS: building="+tavern.buildingKey+"; keeper="+remote.KeeperHash+"; points="+remote.ReceptionRoute.Count);
                var bedIds=new HashSet<int>();
                foreach(var bed in beds)
                {
                    if(!bedIds.Add(bed.RecordIndex)) throw new InvalidOperationException("Duplicate tavern bed identity.");
                    Vector3 ground; bool access=navigation.StandBesideBed(bed,out ground);
                    bool route=access && navigation.RouteToExit(ground).Count>=2; if(route) reachable++;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_BED: building="+tavern.buildingKey+"; bed="+bed.RecordIndex+
                        "; position="+bed.LocalCenter.ToString("F3")+"; accessible="+route);
                }
                var doors=room.GetComponentsInChildren<DaggerfallActionDoor>();
                VerifyRooms(room,beds,doors);
                Dictionary<int,string> loadedMarkers;
                Dictionary<int,string> loadedOccupants;
                var loadedRooms=TavernRoomSurvey.Read(room,out loadedMarkers,out loadedOccupants);
                totalOccupants+=loadedOccupants.Count;
                if(loadedOccupants.Count!=unloadedOccupants.Count) throw new InvalidOperationException("Loaded/unloaded inn occupants differ.");
                foreach(var occupant in loadedOccupants)
                {
                    string otherBed;
                    if(!unloadedOccupants.TryGetValue(occupant.Key,out otherBed) || otherBed!=occupant.Value)
                        throw new InvalidOperationException("Loaded/unloaded native bed assignment differs.");
                    if(!Array.Exists(room.GetComponentsInChildren<StaticNPC>(true),n=>n.Data.hash==occupant.Key))
                        throw new InvalidOperationException("Assigned inn occupant does not exist in native layout.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_OCCUPANT_SURVEY: building="+tavern.buildingKey+"; hash="+occupant.Key+"; bed="+occupant.Value);
                }
                if(unloadedRooms.Count!=loadedRooms.Count || unloadedMarkers.Count!=loadedMarkers.Count)
                    throw new InvalidOperationException("Unloaded tavern room or marker count differs from actual interior.");
                foreach(var group in loadedRooms)
                {
                    var other=unloadedRooms.Find(r=>r.Id==group.Id);
                    if(other==null || other.Beds.Count!=group.Beds.Count || !other.Beds.TrueForAll(b=>group.Beds.Contains(b)))
                        throw new InvalidOperationException("Unloaded tavern room partition differs from actual interior.");
                }
                foreach(var marker in loadedMarkers)
                    if(!unloadedMarkers.ContainsKey(marker.Key) || unloadedMarkers[marker.Key]!=marker.Value)
                        throw new InvalidOperationException("Unloaded tavern marker allocation differs from actual interior.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_UNLOADED_TAVERN_PASS: building="+tavern.buildingKey+"; rooms/markers match loaded interior; player scene and native people unchanged.");
                yield return VerifyUnrentedInventory(room,directory.MapID);
                yield return new WaitForSecondsRealtime(2);
                var live=(WorldState)UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>().GetSaveData();
                var ledger=live.Lodgings.Find(t=>t.SettlementId==directory.MapID && t.BuildingKey==tavern.buildingKey);
                foreach(var occupant in loadedOccupants)
                {
                    if(!ledger.Rooms.Exists(r=>r.Beds.Contains(occupant.Value))) continue;
                    var person=live.NativePeople.Find(p=>p.SettlementId==directory.MapID && p.WorkKey==tavern.buildingKey && p.NativeHash==occupant.Key);
                    if(person==null || !ledger.Occupants.Exists(o=>o.PersonId==person.Id && o.BedId==occupant.Value))
                        throw new InvalidOperationException("Native bedroom occupant lacks automatic lodging claim.");
                }
                if(ledger.Occupants.Count>0)
                {
                    var clock=DaggerfallUnity.Instance.WorldTime.Now;
                    double night=(Math.Floor(clock.ToSeconds()/86400d)+1)*86400+7200;
                    clock.RaiseTime((float)(night-clock.ToSeconds())); DaggerfallUI.Instance.PopToHUD();
                    yield return new WaitForSecondsRealtime(3);
                    foreach(var claim in ledger.Occupants)
                    {
                        var person=live.NativePeople.Find(p=>p.Id==claim.PersonId);
                        // These eight appearances are an explicitly documented art gap,
                        // not evidence that survey or sleeping behavior has passed.
                        if(person.TextureArchive==184 && person.TextureRecord>=5 && person.TextureRecord<=12 &&
                            SleepArtCatalog.Find(person.TextureArchive,person.TextureRecord)==null)
                        {
                            missingSleepArt++;
                            Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_INN_SLEEP_ART_MISSING: "+person.Id+"; texture=184:"+person.TextureRecord);
                            continue;
                        }
                        var npc=Array.Find(room.GetComponentsInChildren<StaticNPC>(true),n=>n.Data.hash==person.NativeHash);
                        var pose=npc ? npc.GetComponent<SleepingPose>() : null;
                        if(!pose || !pose.Active) throw new InvalidOperationException("Claimed native inn occupant does not sleep at night: id="+person.Id+
                            "; alive="+person.IsAlive+"; texture="+person.TextureArchive+":"+person.TextureRecord+
                            "; npc="+(npc!=null)+"; pose="+(pose!=null)+"; paused="+GameManager.IsGamePaused+
                            "; playing="+game.IsPlayingGame()+"; time="+clock.ToSeconds()+"; bed="+claim.BedId);
                        totalSleepVerified++;
                    }
                    clock.RaiseTime(10*3600); DaggerfallUI.Instance.PopToHUD();
                    yield return new WaitForSecondsRealtime(3);
                    foreach(var claim in ledger.Occupants)
                    {
                        var person=live.NativePeople.Find(p=>p.Id==claim.PersonId);
                        var npc=Array.Find(room.GetComponentsInChildren<StaticNPC>(true),n=>n.Data.hash==person.NativeHash);
                        var pose=npc.GetComponent<SleepingPose>();
                        if(pose && pose.Active) throw new InvalidOperationException("Native inn occupant remains asleep at midday.");
                    }
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_NATIVE_SLEEP_PASS: building="+tavern.buildingKey+"; occupants="+ledger.Occupants.Count);
                }
                yield return VerifyNativeRental(room,directory.MapID);
                TavernPaymentProbe.Run(room,directory.MapID);
                TavernRestProbe.Run(room,directory.MapID);
                yield return VerifyBookedResident(room,directory.MapID);
                yield return VerifyRefusedResident(room,directory.MapID);
                foreach(var door in doors)
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_DOOR: building="+tavern.buildingKey+"; id="+door.LoadID+
                        "; position="+room.transform.InverseTransformPoint(door.transform.position).ToString("F3")+
                        "; closedRotation="+door.ClosedRotation.eulerAngles.ToString("F3"));
                foreach(var npc in room.GetComponentsInChildren<StaticNPC>())
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_NPC: building="+tavern.buildingKey+"; hash="+npc.Data.hash+
                        "; faction="+npc.Data.factionID+"; position="+room.transform.InverseTransformPoint(npc.transform.position).ToString("F3"));
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_SURVEY: building="+tavern.buildingKey+"; beds="+beds.Count+
                    "; reachableBeds="+reachable+"; actionDoors="+doors.Length);
                game.PlayerEnterExit.TransitionExterior(); yield return new WaitForSecondsRealtime(1);
            }
            if(totalOccupants==0 || totalSleepVerified==0) throw new InvalidOperationException("Inn occupant fixture did not exercise bedroom occupants with sleeping art.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_SURVEY_PASS: taverns="+taverns.Count+"; beds="+totalBeds+"; assignedOccupants="+totalOccupants+
                "; sleepingVerified="+totalSleepVerified+"; missingSleepArt="+missingSleepArt+"; native interior census complete.");
        }
        private static IEnumerator VerifyBookedResident(DaggerfallInterior room,int town)
        {
            var mod=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>(); var streets=mod.GetComponent<StreetPopulation>();
            var baseline=(WorldState)mod.GetSaveData(); var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)(86400-clock.ToSeconds()%86400));
            double now=clock.ToSeconds(); var store=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); store.Restore(baseline);
            var person=store.GetOrCreate(102,new[]{30},1).Households[0].Residents[0];
            if(SleepArtCatalog.ForResident(person)==null) throw new InvalidOperationException("Resident inn fixture lacks sleeping art.");
            var trip=ResidentTrip.Create(person.Id,102,town,now-100,3600,100,
                new[]{new RoutePoint(207,212),new RoutePoint(207,213)});
            trip.Destination=new ResidentDestination {ExitWalkSeconds=10,
                Stops=new List<ResidentVisitStop> {new ResidentVisitStop {BuildingKey=room.EntryDoor.buildingKey,Purpose=ResidentVisitPurpose.Inn,
                    WalkSeconds=10,DwellSeconds=14*3600,Route=new List<SavedRoutePoint> {new SavedRoutePoint {X=0,Z=0},new SavedRoutePoint {X=1,Z=0}}}},
                ExitRoute=new List<SavedRoutePoint> {new SavedRoutePoint {X=1,Z=0},new SavedRoutePoint {X=0,Z=0}}};
            double firstLeave=now+10+14*3600,returnEnter=firstLeave+10+3600+10,finalCheckout=returnEnter+14*3600;
            trip.Destination.Stops[0].KeepRoomUntil=finalCheckout;
            trip.Destination.Stops.Add(new ResidentVisitStop {BuildingKey=999999,Purpose=ResidentVisitPurpose.Guild,
                WalkSeconds=10,DwellSeconds=3600,Route=new List<SavedRoutePoint>{new SavedRoutePoint {X=1},new SavedRoutePoint {X=2}}});
            trip.Destination.Stops.Add(new ResidentVisitStop {BuildingKey=room.EntryDoor.buildingKey,Purpose=ResidentVisitPurpose.Inn,
                WalkSeconds=10,DwellSeconds=14*3600,Route=new List<SavedRoutePoint>{new SavedRoutePoint {X=2},new SavedRoutePoint {X=1}}});
            store.ScheduleResidentTrip(trip);
            try
            {
                mod.RestoreSaveData(store.Snapshot()); clock.RaiseTime(12);
                ResidentInnReception reception=null; float receptionDeadline=Time.realtimeSinceStartup+10;
                while(Time.realtimeSinceStartup<receptionDeadline)
                {
                    var pending=(WorldState)mod.GetSaveData(); var pendingTrip=pending.ResidentTrips.Find(t=>t.ResidentId==person.Id);
                    if(pendingTrip.InnStays.Count>0) throw new InvalidOperationException("Resident booked before approaching keeper.");
                    if(pendingTrip.InnReceptions.Count>0) {reception=ResidentInnReception.Copy(pendingTrip.InnReceptions[0]); break;}
                    yield return null;
                }
                if(reception==null) throw new InvalidOperationException("Resident has no native keeper approach.");
                clock.RaiseTime((float)Math.Max(0,reception.CounterAt+1-clock.ToSeconds())); yield return null;
                var counterActor=streets.FindActor(person.Id);
                if(!counterActor) throw new InvalidOperationException("Resident missing at keeper.");
                clock.RaiseTime((float)Math.Max(0,reception.ServiceEndsAt+1-clock.ToSeconds()));
                yield return new WaitForSecondsRealtime(2);
                var saved=(WorldState)mod.GetSaveData(); var active=saved.ResidentTrips.Find(t=>t.ResidentId==person.Id);
                if(active.InnStays.Count!=1 || active.InnStays[0].Admission!=TravelerAdmission.Admitted)
                    throw new InvalidOperationException("Resident inn fixture did not acquire a room.");
                clock.RaiseTime(600); float deadline=Time.realtimeSinceStartup+10;
                while((!streets.FindActor(person.Id) || !streets.FindActor(person.Id).IsSleeping) && Time.realtimeSinceStartup<deadline) yield return null;
                var actor=streets.FindActor(person.Id);
                if(!actor || !actor.IsSleeping) throw new InvalidOperationException("Admitted resident did not sleep in booked inn bed.");
                var evidence=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); evidence.Restore((WorldState)mod.GetSaveData());
                string bedId=evidence.ResidentInnBed(person.Id,clock.ToSeconds());
                var bed=BedGeometry.Read(room).Find(b=>"bed:"+b.RecordIndex==bedId);
                if(bed==null || Vector3.Distance(actor.transform.position,room.transform.TransformPoint(bed.LocalCenter)+Vector3.up*0.18f)>0.05f)
                    throw new InvalidOperationException("Resident sleeps away from the actually booked bed.");
                mod.RestoreSaveData((WorldState)mod.GetSaveData()); yield return new WaitForSecondsRealtime(2);
                actor=streets.FindActor(person.Id);
                if(!actor || !actor.IsSleeping) throw new InvalidOperationException("Resident inn sleep lost on restore.");
                clock.RaiseTime((float)(now+12*3600-clock.ToSeconds())); yield return new WaitForSecondsRealtime(1);
                actor=streets.FindActor(person.Id);
                if(!actor || actor.IsSleeping || streets.GetState(person.Id,clock.ToSeconds()).Activity==Activity.Sleep)
                    throw new InvalidOperationException("Resident inn guest did not wake at midday.");
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var plans=(System.Collections.IDictionary)typeof(StreetPopulation).GetField("residentRooms",flags).GetValue(streets);
                var roomPlan=plans[person.Id];
                var walk=(TravelerRoomWalk)roomPlan.GetType().GetField("Walk").GetValue(roomPlan);
                double checkout=firstLeave;
                clock.RaiseTime((float)(checkout-walk.Duration+1-clock.ToSeconds())); yield return null;
                var before=actor.transform.position;
                yield return new WaitForSecondsRealtime(1);
                if(actor.IsSleeping || Vector3.Distance(before,actor.transform.position)<0.1f)
                    throw new InvalidOperationException("Resident inn guest did not physically walk out at checkout.");
                clock.RaiseTime((float)(checkout+1-clock.ToSeconds())); yield return new WaitForSecondsRealtime(1);
                if(streets.FindActor(person.Id)) throw new InvalidOperationException("Resident inn guest remained indoors after checkout.");
                var persisted=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),mod.GetSaveData()));
                VerifyVisitSettlement(persisted,person.Id,town);
                mod.RestoreSaveData(persisted);
                evidence.Restore(persisted);
                if(evidence.ResidentInnBed(person.Id,checkout+100)!=bedId)
                    throw new InvalidOperationException("Daytime excursion released paid room.");
                clock.RaiseTime((float)(returnEnter+600-clock.ToSeconds())); yield return new WaitForSecondsRealtime(2);
                actor=streets.FindActor(person.Id);
                var returned=(WorldState)mod.GetSaveData(); evidence.Restore(returned);
                if(!actor || evidence.ResidentInnBed(person.Id,clock.ToSeconds())!=bedId ||
                    returned.ResidentTrips.Find(t=>t.ResidentId==person.Id).InnStays.Count!=1)
                    throw new InvalidOperationException("Returning guest lost room or paid twice.");
                clock.RaiseTime((float)(now+86400+3600-clock.ToSeconds())); yield return new WaitForSecondsRealtime(2);
                actor=streets.FindActor(person.Id);
                if(!actor || !actor.IsSleeping) throw new InvalidOperationException("Returning guest did not sleep on second night.");
                clock.RaiseTime((float)(finalCheckout+1-clock.ToSeconds())); yield return new WaitForSecondsRealtime(1);
                evidence.Restore((WorldState)mod.GetSaveData());
                if(streets.FindActor(person.Id) || evidence.ResidentInnBed(person.Id,clock.ToSeconds())!=null)
                    throw new InvalidOperationException("Final checkout retained guest or room claim.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_RESIDENT_ROOM_HOLD_PASS: building="+room.EntryDoor.buildingKey+"; excursion, serialized return, second night and checkout.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_RESIDENT_INN_ROOM_PASS: building="+room.EntryDoor.buildingKey+"; booked bed, sleep restore, daytime waking and physical checkout.");
            }
            finally {mod.RestoreSaveData(baseline);}
        }
        private static IEnumerator VerifyRefusedResident(DaggerfallInterior room,int town)
        {
            var mod=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var streets=mod.GetComponent<StreetPopulation>();
            var baseline=(WorldState)mod.GetSaveData();
            var clock=DaggerfallUnity.Instance.WorldTime.Now; double now=clock.ToSeconds();
            var store=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); store.Restore(baseline);
            var person=store.GetOrCreate(100,new[]{30},1).Households[0].Residents[0];
            int key=room.EntryDoor.buildingKey;
            // Fill every remaining rentable room in this disposable test population.
            for(int i=0;i<100;i++)
                if(!store.TryBookLodging(town,key,"probe-full:"+i,"probe-guest:"+i,101,false,1,now,now+7200)) break;
            var trip=ResidentTrip.Create(person.Id,100,town,now-100,3600,100,
                new[]{new RoutePoint(207,212),new RoutePoint(207,213)});
            trip.Destination=new ResidentDestination {ExitWalkSeconds=20,
                Stops=new List<ResidentVisitStop> {new ResidentVisitStop {BuildingKey=key,Purpose=ResidentVisitPurpose.Inn,
                    WalkSeconds=10,DwellSeconds=3600,Route=new List<SavedRoutePoint> {new SavedRoutePoint {X=0,Z=0},new SavedRoutePoint {X=1,Z=0}}}},
                ExitRoute=new List<SavedRoutePoint> {new SavedRoutePoint {X=2,Z=0},new SavedRoutePoint {X=0,Z=0}}};
            trip.Destination.Stops.Add(new ResidentVisitStop {BuildingKey=999999,Purpose=ResidentVisitPurpose.Guild,
                WalkSeconds=10,DwellSeconds=3600,Route=new List<SavedRoutePoint> {new SavedRoutePoint {X=1,Z=0},new SavedRoutePoint {X=2,Z=0}}});
            store.ScheduleResidentTrip(trip);
            try
            {
                mod.RestoreSaveData(store.Snapshot()); clock.RaiseTime(12);
                ResidentServiceExit exit=null; float deadline=Time.realtimeSinceStartup+12;
                while(Time.realtimeSinceStartup<deadline)
                {
                    var saved=(WorldState)mod.GetSaveData(); var active=saved.ResidentTrips.Find(t=>t.ResidentId==person.Id);
                    if(active.InnReceptions.Count>0)
                    {
                        double serviceEnds=ResidentInnReception.Copy(active.InnReceptions[0]).ServiceEndsAt;
                        if(clock.ToSeconds()<serviceEnds) clock.RaiseTime((float)(serviceEnds+1-clock.ToSeconds()));
                    }
                    if(active.InnStays.Count>0 && active.InnStays[0].Admission!=TravelerAdmission.Refused)
                        throw new InvalidOperationException("Full inn admitted resident visitor.");
                    if(active.ServiceExits.Count>0) {exit=active.ServiceExits[0]; break;}
                    yield return null;
                }
                if(exit==null) throw new InvalidOperationException("Refused resident did not prepare physical inn exit.");
                var persisted=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),mod.GetSaveData()));
                VerifyVisitSettlement(persisted,person.Id,town);
                var returning=persisted.ResidentTrips.Find(t=>t.ResidentId==person.Id);
                if(returning.Destination.Stops.Count!=1 || returning.Destination.ExitRoute[0].X!=1 ||
                    returning.Destination.ExitRoute[returning.Destination.ExitRoute.Count-1].X!=0)
                    throw new InvalidOperationException("Refused resident retained later appointments or wrong return route.");
                mod.RestoreSaveData(persisted);
                double ends=ResidentServiceExit.Copy(exit).ExitAt;
                clock.RaiseTime((float)Math.Max(0,ends+1-clock.ToSeconds()));
                yield return new WaitForSecondsRealtime(1);
                if(streets.FindActor(person.Id)) throw new InvalidOperationException("Refused resident remained inside after exit.");
                var restored=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); restored.Restore(persisted);
                if(restored.IsResidentAway(person.Id,returning.HomeAt+1))
                    throw new InvalidOperationException("Refused resident did not return home after serialized exit.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_RESIDENT_INN_REFUSAL_PASS: building="+key+"; native admission, physical exit, cancelled appointments and serialized return home.");
            }
            finally {mod.RestoreSaveData(baseline);}
        }
        private static IEnumerator VerifyExteriorInventory(BuildingDirectory directory,int expected,int paidBuilding)
        {
            var game=GameManager.Instance; var player=game.PlayerEntity;
            var host=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var priorLodgings=((WorldState)host.GetSaveData()).Lodgings;
            var receipts=new List<DaggerfallWorkshop.Game.Serialization.RoomRental_v1>(player.RentedRooms);
            int gold=player.GetGoldAmount(); var position=game.PlayerMotor.transform.position;
            ulong expiry=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()+86400;
            try
            {
                player.RentedRooms.RemoveAll(r=>r.mapID==directory.MapID);
                player.RentedRooms.Add(new DaggerfallWorkshop.Game.Serialization.RoomRental_v1 {
                    name="Exterior inventory probe",mapID=directory.MapID,buildingKey=paidBuilding,allocatedBedIndex=0,expiryTime=expiry });
                var clean=(WorldState)host.GetSaveData(); clean.Lodgings.RemoveAll(t=>t.SettlementId==directory.MapID);
                host.RestoreSaveData(clean);
                // Every native Inn now also surveys its counter and public
                // tables. Budget per building and avoid copying the entire
                // save every render frame while the worker is progressing.
                List<TavernLodging> found=null;float deadline=Time.realtimeSinceStartup+Math.Max(30,expected*15);
                while(Time.realtimeSinceStartup<deadline)
                {
                    found=((WorldState)host.GetSaveData()).Lodgings.FindAll(t=>t.SettlementId==directory.MapID);
                    if(found.Count==expected && found.Exists(t=>t.BuildingKey==paidBuilding && t.Bookings.Count==1)) break;
                    yield return new WaitForSecondsRealtime(0.1f);
                }
                if(found==null || found.Count!=expected || game.IsPlayerInside || game.PlayerMotor.transform.position!=position || player.GetGoldAmount()!=gold)
                    throw new InvalidOperationException("Exterior tavern inventory missing or survey moved/charged player: inns="+
                        (found==null?0:found.Count)+"/"+expected+"; playerDelta="+Vector3.Distance(game.PlayerMotor.transform.position,position)+
                        "; gold="+gold+"/"+player.GetGoldAmount()+"; "+ExteriorSurveyState(host));
                foreach(var inn in found)
                    if(inn.Rooms.Count==0 || (inn.BuildingKey==paidBuilding ?
                        inn.Bookings.Count!=1 || !inn.Bookings[0].Player || inn.Bookings[0].EndsAt!=expiry : inn.Bookings.Count!=0))
                        throw new InvalidOperationException("Exterior inventory lost paid room or invented reservations.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_EXTERIOR_INVENTORY_PASS: inns="+found.Count+"; paid room imported; no entry, movement or charge.");
            }
            finally
            {
                player.RentedRooms.Clear(); player.RentedRooms.AddRange(receipts);
                var restored=(WorldState)host.GetSaveData(); restored.Lodgings=priorLodgings; host.RestoreSaveData(restored);
            }
        }
        private static string ExteriorSurveyState(PeopleOfTheIliacBayMod host)
        {
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var rental=typeof(PeopleOfTheIliacBayMod).GetField("nativeRentals",flags).GetValue(host);
            var type=typeof(NativeTavernRentals);var operation=type.GetField("surveyOperation",flags).GetValue(rental);
            object state=operation==null?null:operation.GetType().GetField("<>1__state",flags)?.GetValue(operation);
            return "surveyKey="+type.GetField("surveyKey",flags).GetValue(rental)+"; active="+(operation!=null)+"; iteratorState="+state+
                "; sharedSurveyActive="+typeof(TavernRoomSurvey).GetField("remoteSurveyActive",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);
        }
        private static IEnumerator VerifyUnrentedInventory(DaggerfallInterior interior,int town)
        {
            var player=GameManager.Instance.PlayerEntity;
            var host=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var priorLodgings=((WorldState)host.GetSaveData()).Lodgings;
            var receipts=new List<DaggerfallWorkshop.Game.Serialization.RoomRental_v1>(player.RentedRooms);
            int building=interior.EntryDoor.buildingKey,gold=player.GetGoldAmount();
            try
            {
                player.RentedRooms.RemoveAll(r=>r.mapID==town && r.buildingKey==building);
                var clean=(WorldState)host.GetSaveData();
                clean.Lodgings.RemoveAll(t=>t.SettlementId==town && t.BuildingKey==building);
                host.RestoreSaveData(clean);
                TavernLodging lodging=null; float deadline=Time.realtimeSinceStartup+15;
                while(Time.realtimeSinceStartup<deadline)
                {
                    lodging=((WorldState)host.GetSaveData()).Lodgings.Find(t=>t.SettlementId==town && t.BuildingKey==building);
                    if(lodging!=null) break;
                    yield return null;
                }
                Dictionary<int,string> mapping; TavernRoomSurvey.Read(interior,out mapping);
                if(lodging==null || lodging.Rooms.FindAll(r=>!r.NonRentable).Count!=new HashSet<string>(mapping.Values).Count || lodging.Bookings.Count!=0 ||
                    player.GetGoldAmount()!=gold || player.GetRentedRoom(town,building)!=null)
                    throw new InvalidOperationException("Unrented tavern did not register guest inventory without payment or bookings.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_UNRENTED_INVENTORY_PASS: building="+building+"; guestRooms="+lodging.Rooms.Count+"; no receipt, booking or charge.");
            }
            finally
            {
                player.RentedRooms.Clear(); player.RentedRooms.AddRange(receipts);
                var restored=(WorldState)host.GetSaveData(); restored.Lodgings=priorLodgings; host.RestoreSaveData(restored);
            }
        }
        private static IEnumerator VerifyNativeRental(DaggerfallInterior interior,int town)
        {
            var player=GameManager.Instance.PlayerEntity;
            var host=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var priorLodgings=((WorldState)host.GetSaveData()).Lodgings;
            var prior=new List<DaggerfallWorkshop.Game.Serialization.RoomRental_v1>(player.RentedRooms);
            var receipt=new DaggerfallWorkshop.Game.Serialization.RoomRental_v1 {
                name="People of the Iliac Bay rental probe",mapID=town,buildingKey=interior.EntryDoor.buildingKey,
                allocatedBedIndex=1,expiryTime=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()+86400 };
            int gold=player.GetGoldAmount(); ulong expiry=receipt.expiryTime;
            try
            {
                player.RentedRooms.RemoveAll(r=>r.mapID==town && r.buildingKey==receipt.buildingKey);
                player.RentedRooms.Add(receipt);
                TavernLodging lodging=null; float deadline=Time.realtimeSinceStartup+15;
                while(Time.realtimeSinceStartup<deadline)
                {
                    lodging=((WorldState)host.GetSaveData()).Lodgings.Find(t=>t.SettlementId==town && t.BuildingKey==receipt.buildingKey);
                    if(lodging!=null && lodging.Bookings.Exists(b=>b.Player && b.EndsAt==expiry)) break;
                    yield return null;
                }
                if(lodging==null || lodging.Bookings.Count!=1 || lodging.Bookings[0].EndsAt!=expiry)
                    throw new InvalidOperationException("Native paid rental was not imported by running controller.");
                Dictionary<int,string> mapping; TavernRoomSurvey.Read(interior,out mapping);
                var booking=lodging.Bookings[0];
                if(booking.RoomId!=mapping[1] || receipt.allocatedBedIndex!=1 || receipt.expiryTime!=expiry || player.GetGoldAmount()!=gold)
                    throw new InvalidOperationException("Native rental import changed allocation or payment.");
                receipt.expiryTime+=86400; deadline=Time.realtimeSinceStartup+15;
                while(Time.realtimeSinceStartup<deadline)
                {
                    lodging=((WorldState)host.GetSaveData()).Lodgings.Find(t=>t.SettlementId==town && t.BuildingKey==receipt.buildingKey);
                    if(lodging.Bookings[0].EndsAt==receipt.expiryTime) break;
                    yield return null;
                }
                if(lodging.Bookings.Count!=1 || lodging.Bookings[0].Id!=booking.Id || lodging.Bookings[0].StartsAt!=booking.StartsAt ||
                    lodging.Bookings[0].EndsAt!=receipt.expiryTime || player.GetGoldAmount()!=gold)
                    throw new InvalidOperationException("Native rental extension duplicated or charged booking.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_RENTAL_PASS: building="+receipt.buildingKey+"; room="+booking.RoomId+"; paid marker preserved, repeat/extension no charge.");
            }
            finally
            {
                player.RentedRooms.Clear(); player.RentedRooms.AddRange(prior);
                var current=(WorldState)host.GetSaveData(); current.Lodgings=priorLodgings;
                host.RestoreSaveData(current);
            }
        }
        private static void VerifyRooms(DaggerfallInterior interior,List<BedAnchor> beds,DaggerfallActionDoor[] doors)
        {
            var original=new List<bool>(); foreach(var door in doors) original.Add(door.IsOpen);
            try
            {
                foreach(var door in doors) door.SetOpen(false,true,true); Physics.SyncTransforms();
                Dictionary<int,string> closedMarkers,openMarkers;
                var closed=TavernRoomSurvey.Read(interior,out closedMarkers);
                foreach(var door in doors) door.SetOpen(true,true,true); Physics.SyncTransforms();
                var opened=TavernRoomSurvey.Read(interior,out openMarkers);
                Func<List<LodgingRoom>,string> signature=rooms=> {
                    var parts=new List<string>(); foreach(var room in rooms) parts.Add(room.Id+"="+string.Join(",",room.Beds.ToArray()));
                    return string.Join(";",parts.ToArray()); };
                if(signature(closed)!=signature(opened)) throw new InvalidOperationException("Opening tavern doors changed private room partition.");
                var assigned=new HashSet<string>();
                foreach(var room in closed) foreach(var bed in room.Beds)
                    if(!assigned.Add(bed)) throw new InvalidOperationException("Bed belongs to multiple tavern rooms.");
                foreach(var bed in beds) if(!assigned.Contains("bed:"+bed.RecordIndex)) throw new InvalidOperationException("Tavern bed missing from room partition: "+bed.RecordIndex);
                if(closed.Count<2) throw new InvalidOperationException("Known multi-room tavern collapsed into one room.");
                var markers=interior.FindMarkers(DaggerfallInterior.InteriorMarkerTypes.Rest);
                if(closedMarkers.Count!=markers.Length || openMarkers.Count!=markers.Length)
                    throw new InvalidOperationException("Native rental markers are missing room mappings.");
                for(int index=0;index<markers.Length;index++)
                {
                    if(!closedMarkers.ContainsKey(index) || !openMarkers.ContainsKey(index) || closedMarkers[index]!=openMarkers[index] ||
                        !closed.Exists(r=>r.Id==closedMarkers[index])) throw new InvalidOperationException("Rental marker mapping changed with open doors.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_MARKER_PASS: building="+interior.EntryDoor.buildingKey+"; marker="+index+"; room="+closedMarkers[index]);
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_ROOMS_PASS: building="+interior.EntryDoor.buildingKey+"; "+signature(closed)+"; open/closed identical.");
            }
            finally { for(int i=0;i<doors.Length;i++) doors[i].SetOpen(original[i],true,true); Physics.SyncTransforms(); }
        }
    }
}
