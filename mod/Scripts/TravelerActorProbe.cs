using System;
using System.Collections;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public static class TravelerActorProbe
    {
        public static IEnumerator Run(BuildingDirectory directory,TownGeometry geometry,int inn,TownGeometry.TravelerAccess access)
        {
            var game=GameManager.Instance;
            var adapter=UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var streets=adapter.GetComponent<StreetPopulation>();
            float deadline=Time.realtimeSinceStartup+90;
            while(!streets.Ready && Time.realtimeSinceStartup<deadline) yield return null;
            if(!streets.Ready) throw new InvalidOperationException("Traveler actor settlement not ready: "+adapter.TestStatus());
            var field=typeof(LivingCitiesMod).GetField("store",BindingFlags.NonPublic|BindingFlags.Instance);
            var store=(WorldStateStore)field.GetValue(adapter);
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            var proposal=TravelerIdentityAdapter.Create(42,directory.MapID,inn,(int)(clock.ToSeconds()/86400d),777,access);
            proposal.Visit.DesiredArrival=clock.ToSeconds();
            store.RegisterTraveler(proposal.Person); store.RegisterTravelerVisit(proposal.Visit);
            var journey=store.TravelerJourneyFor(proposal.Visit.Id);
            clock.RaiseTime((float)(journey.EntryAt+journey.CheckInAt-2*clock.ToSeconds())*0.5f);
            var ground=geometry.ScenePoint(journey.Evaluate(clock.ToSeconds()).Position);
            game.PlayerMotor.transform.position=ground+Vector3.up+Vector3.right*2;
            Physics.SyncTransforms();
            yield return new WaitForSecondsRealtime(2);
            var actor=streets.FindActor(proposal.Person.Id);
            if(!actor || !actor.gameObject.activeInHierarchy || actor.Person.NameNPC!=proposal.Person.Name)
                throw new InvalidOperationException("Registered traveler has no visible correctly named arrival actor.");
            var initial=actor.transform.position;
            yield return new WaitForSecondsRealtime(2);
            if(Vector3.Distance(initial,actor.transform.position)<0.1f)
                throw new InvalidOperationException("Traveler arrival actor does not move along the supplied route.");
            yield return SaveArrival(game.PlayerEntity.Name,streets);
            store=(WorldStateStore)field.GetValue(adapter);
            actor=streets.FindActor(proposal.Person.Id);
            if(!actor || !actor.gameObject.activeInHierarchy || actor.Person.NameNPC!=proposal.Person.Name ||
                store.TravelerVisitsFor(directory.MapID).FindAll(v=>v.Id==proposal.Visit.Id).Count!=1)
                throw new InvalidOperationException("Arrival reload lost or duplicated the traveler.");
            clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)(journey.CheckInAt+10-clock.ToSeconds()));
            DaggerfallUI.Instance.PopToHUD();
            deadline=Time.realtimeSinceStartup+60;
            while(store.Snapshot().TravelerVisits.Find(v=>v.Id==proposal.Visit.Id).Admission==TravelerAdmission.Pending &&
                Time.realtimeSinceStartup<deadline) yield return null;
            var admitted=store.Snapshot().TravelerVisits.Find(v=>v.Id==proposal.Visit.Id);
            if(admitted.Admission!=TravelerAdmission.Admitted ||
                !store.Snapshot().Lodgings.Exists(l=>l.BuildingKey==inn && l.Bookings.Exists(b=>b.Id==admitted.BookingId)))
                throw new InvalidOperationException("Arriving traveler did not automatically obtain a paid room.");
            actor=streets.FindActor(proposal.Person.Id);
            if(!actor || !actor.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Admitted arrival actor vanished before doorway continuity check.");
            var plans=(System.Collections.IDictionary)typeof(StreetPopulation).GetField("travelerPlans",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(streets);
            var plan=plans[proposal.Person.Id];
            var continuationTime=journey.CheckInAt+601;
            var state=journey.Evaluate(continuationTime);
            var last=proposal.Visit.Route[proposal.Visit.Route.Count-1];
            var doorway=geometry.ScenePoint(new RoutePoint(last.X,last.Z));
            var originalPosition=actor.transform.position;
            bool continues,continuesPastExit,arrived,freshLeavingRetained;
            try
            {
                actor.transform.position=doorway+Vector3.up+Vector3.right*2;
                // A doorway latch exists only after this existing body was observed
                // on the outdoor arrival route and crossed scheduled check-in.
                plan.GetType().GetField("ObservedOutdoorArrival").SetValue(plan,true);
                plan.GetType().GetField("PendingDoorwayArrival").SetValue(plan,false);
                typeof(StreetPopulation).GetMethod("ObserveTravelerArrivalBoundary",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(streets,new object[] {actor,plan,continuationTime});
                var continuationArgs=new object[] {actor,plan,state,continuationTime,Vector3.zero};
                continues=(bool)typeof(StreetPopulation).GetMethod("ContinueTravelerArrival",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(streets,continuationArgs);
                double afterExit=journey.ExitAt+1;
                var departed=journey.Evaluate(afterExit);
                plan.GetType().GetField("ObservedOutdoorArrival").SetValue(plan,false);
                plan.GetType().GetField("PendingDoorwayArrival").SetValue(plan,false);
                freshLeavingRetained=(bool)typeof(StreetPopulation).GetMethod("IsPendingTravelerArrival",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(streets,new object[] {actor,plan,afterExit});
                plan.GetType().GetField("PendingDoorwayArrival").SetValue(plan,true);
                continuesPastExit=(bool)typeof(StreetPopulation).GetMethod("IsPendingTravelerArrival",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(streets,new object[] {actor,plan,afterExit});
                var departedArgs=new object[] {actor,plan,departed,afterExit,Vector3.zero};
                continuesPastExit=continuesPastExit && (bool)typeof(StreetPopulation).GetMethod("ContinueTravelerArrival",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(streets,departedArgs);
                actor.transform.position=doorway+Vector3.up+Vector3.right*0.2f;
                plan.GetType().GetField("PendingDoorwayArrival").SetValue(plan,true);
                var arrivedArgs=new object[] {actor,plan,state,continuationTime,Vector3.zero};
                arrived=(bool)typeof(StreetPopulation).GetMethod("ContinueTravelerArrival",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(streets,arrivedArgs);
            }
            finally
            {
                actor.transform.position=originalPosition;
                plan.GetType().GetField("ObservedOutdoorArrival").SetValue(plan,false);
                plan.GetType().GetField("PendingDoorwayArrival").SetValue(plan,false);
            }
            if(!actor || !actor.gameObject.activeInHierarchy || !continues || !continuesPastExit || freshLeavingRetained || arrived)
                throw new InvalidOperationException("Traveler doorway continuation ignored long delay or physical arrival.");
            yield return GuestRoom(proposal.Visit.Id,proposal.Person.Id,proposal.Person.Name,inn,streets,adapter,field);
            store=(WorldStateStore)field.GetValue(adapter);
            clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)(journey.ExitAt+60-clock.ToSeconds()));
            var later=new TravelerVisit { Id=proposal.Visit.Id+":return",PersonId=proposal.Person.Id,SettlementId=proposal.Visit.SettlementId,
                TavernKey=inn,Nights=1,Walled=access.Walled,DesiredArrival=clock.ToSeconds(),CommuteSeconds=proposal.Visit.CommuteSeconds,
                Route=proposal.Visit.Route };
            store.RegisterTravelerVisit(later);
            var nextJourney=store.TravelerJourneyFor(later.Id);
            clock.RaiseTime((float)((nextJourney.EntryAt+nextJourney.CheckInAt)*0.5-clock.ToSeconds()));
            directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            geometry=new TownGeometry(directory,game.StreamingWorld.GetCurrentCityNavigation());
            game.PlayerMotor.transform.position=geometry.ScenePoint(nextJourney.Evaluate(clock.ToSeconds()).Position)+Vector3.up+Vector3.right*2;
            Physics.SyncTransforms(); yield return new WaitForSecondsRealtime(2);
            // Advancing several days can open vanilla timed-event dialogue.
            // This fixture tests traveler lifecycle, not that unrelated dialogue.
            DaggerfallUI.Instance.PopToHUD();
            yield return null;
            actor=streets.FindActor(proposal.Person.Id);
            if(!actor || !actor.gameObject.activeInHierarchy || actor.Person.NameNPC!=proposal.Person.Name)
                throw new InvalidOperationException("Later visit did not replace completed traveler journey.");
            streets.ReactionsEnabled=true;
            game.WeaponManager.WeaponDamage(null,false,false,actor.transform,actor.transform.position,Vector3.forward);
            if(store.Traveler(proposal.Person.Id).IsAlive || actor.gameObject.activeSelf)
                throw new InvalidOperationException("Native weapon hit did not persist traveler death.");
            yield return new WaitForSecondsRealtime(0.5f);
            DaggerfallUI.Instance.PopToHUD();
            yield return new WaitForSecondsRealtime(0.5f);
            var alarm=store.Snapshot().AlarmIncidents.Find(a=>a.VictimId==proposal.Person.Id);
            if(streets.TravelerPlanCount!=0) throw new InvalidOperationException("Dead traveler retained an active runtime plan.");
            if(alarm==null || alarm.Corpse==null || streets.CorpseCount==0)
                throw new InvalidOperationException("Traveler native hit lost crime or physical corpse: alarm="+(alarm!=null)+
                    "; corpse="+(alarm!=null && alarm.Corpse!=null)+"; views="+streets.CorpseCount+
                    "; cleared="+(alarm!=null && alarm.Corpse!=null && alarm.Corpse.Cleared)+"; paused="+GameManager.IsGamePaused);
            bool saved=false,loaded=false;
            SaveLoadManager.OnSaveEventHandler onSave=ignored=>saved=true;
            SaveLoadManager.OnLoadEventHandler onLoad=ignored=>loaded=true;
            SaveLoadManager.OnSave+=onSave; SaveLoadManager.OnLoad+=onLoad;
            try
            {
                string character=game.PlayerEntity.Name;
                game.SaveLoadManager.Save(character,"Living Cities - traveler death");
                deadline=Time.realtimeSinceStartup+30;
                while(!saved && Time.realtimeSinceStartup<deadline) yield return null;
                if(!saved) throw new InvalidOperationException("Traveler death save timed out.");
                int slot=game.SaveLoadManager.FindSaveFolderByNames(character,"Living Cities - traveler death");
                game.SaveLoadManager.Load(slot); DaggerfallUI.Instance.PopToHUD();
                deadline=Time.realtimeSinceStartup+90;
                while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<deadline) yield return null;
                if(!loaded || !streets.Ready) throw new InvalidOperationException("Traveler death reload timed out.");
                yield return new WaitForSecondsRealtime(1);
                store=(WorldStateStore)field.GetValue(adapter);
                if(store.Traveler(proposal.Person.Id).IsAlive || streets.FindActor(proposal.Person.Id) || streets.CorpseCount==0)
                    throw new InvalidOperationException("Traveler revived or corpse vanished on actual save reload.");
                Debug.Log("LIVING_CITIES_TRAVELER_ACTOR_PASS: Daggerfall arrival movement/name/reload, later visit, native weapon death, corpse and real save/load.");
                yield return RefusedGuest(streets,adapter,field,inn,access);
            }
            finally { SaveLoadManager.OnSave-=onSave; SaveLoadManager.OnLoad-=onLoad; }
        }
        private static IEnumerator RefusedGuest(StreetPopulation streets,LivingCitiesMod adapter,FieldInfo field,int inn,TownGeometry.TravelerAccess access)
        {
            var game=GameManager.Instance;
            var store=(WorldStateStore)field.GetValue(adapter);
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            var proposal=TravelerIdentityAdapter.Create(42,directory.MapID,inn,(int)(clock.ToSeconds()/86400),778,access);
            proposal.Visit.DesiredArrival=clock.ToSeconds();
            store.RegisterTraveler(proposal.Person); store.RegisterTravelerVisit(proposal.Visit);
            var planned=store.TravelerJourneyFor(proposal.Visit.Id);
            var lodging=store.Snapshot().Lodgings.Find(l=>l.SettlementId==directory.MapID && l.BuildingKey==inn);
            foreach(var room in lodging.Rooms)
                store.TryBookLodging(directory.MapID,inn,"full:"+proposal.Visit.Id+":"+room.Id,"full-guest:"+room.Id,
                    proposal.Person.HomeSettlement,false,1,planned.CheckInAt,planned.CheckOutAt,room.Id);
            clock.RaiseTime((float)(planned.CheckInAt+10-clock.ToSeconds()));
            DaggerfallUI.Instance.PopToHUD();
            float deadline=Time.realtimeSinceStartup+60;
            while(store.TravelerVisitsFor(directory.MapID).Find(v=>v.Id==proposal.Visit.Id).Admission==TravelerAdmission.Pending &&
                Time.realtimeSinceStartup<deadline) yield return null;
            if(store.TravelerVisitsFor(directory.MapID).Find(v=>v.Id==proposal.Visit.Id).Admission!=TravelerAdmission.Refused ||
                store.TravelerBed(proposal.Visit.Id,clock.ToSeconds())!=null)
                throw new InvalidOperationException("Full inn did not refuse traveler without assigning bed.");
            var departure=store.TravelerJourneyFor(proposal.Visit.Id);
            clock.RaiseTime((float)((departure.LeaveInnAt+departure.ExitAt)*0.5-clock.ToSeconds()));
            var geometry=new TownGeometry(directory,game.StreamingWorld.GetCurrentCityNavigation());
            game.PlayerMotor.transform.position=geometry.ScenePoint(departure.Evaluate(clock.ToSeconds()).Position)+Vector3.up+Vector3.right*2;
            yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
            yield return new WaitForSecondsRealtime(1);
            var actor=streets.FindActor(proposal.Person.Id);
            if(!actor || actor.Person.NameNPC!=proposal.Person.Name) throw new InvalidOperationException("Refused guest has no named departure actor.");
            var before=actor.transform.position;
            yield return new WaitForSecondsRealtime(1);
            if(Vector3.Distance(before,actor.transform.position)<0.1f) throw new InvalidOperationException("Refused guest is not walking away.");
            yield return SaveArrival(game.PlayerEntity.Name,streets,"Living Cities - traveler refused");
            store=(WorldStateStore)field.GetValue(adapter);
            if(!streets.FindActor(proposal.Person.Id) || store.TravelerVisitsFor(proposal.Visit.SettlementId).Find(v=>v.Id==proposal.Visit.Id).Admission!=TravelerAdmission.Refused)
                throw new InvalidOperationException("Refused guest departure lost on reload.");
            Debug.Log("LIVING_CITIES_TRAVELER_REFUSAL_PASS: full inn, no bed, named walking departure and disk reload.");
        }
        private static IEnumerator GuestRoom(string visit,string person,string name,int inn,StreetPopulation streets,LivingCitiesMod adapter,FieldInfo field)
        {
            var game=GameManager.Instance;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            DaggerfallStaticDoors owner=null; StaticDoor entry=default(StaticDoor);
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var door in doors.Doors)
                    if(door.doorType==DoorTypes.Building && door.buildingKey==inn) {owner=doors; entry=door; break;}
            if(!owner) throw new InvalidOperationException("Traveler inn entrance missing.");
            TownSmokeHarness.EnterInterior(owner.transform,entry);
            yield return null;
            DaggerfallUI.Instance.PopToHUD();
            var actor=streets.FindActor(person);
            if(!actor || !actor.gameObject.activeInHierarchy || actor.Person.NameNPC!=name || actor.IsSleeping)
                throw new InvalidOperationException("Paid traveler missing from inn during waking hours.");
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            var store=(WorldStateStore)field.GetValue(adapter);
            string bed=store.TravelerBed(visit,clock.ToSeconds());
            var roomGeometry=new InteriorGeometry(game.PlayerEnterExit.Interior);
            var anchor=BedGeometry.Read(game.PlayerEnterExit.Interior).Find(b=>"bed:"+b.RecordIndex==bed);
            Vector3 standing;
            if(anchor==null || !roomGeometry.StandBesideBed(anchor,out standing))
                throw new InvalidOperationException("Traveler room has no reachable bed-side floor.");
            Vector3 roomTarget=standing+Vector3.up;
            Vector3 initial=actor.transform.position;
            float arrivalDeadline=Time.realtimeSinceStartup+35;
            float totalMovement=0;
            bool arrived=Vector3.Distance(initial,roomTarget)<=0.45f;
            while(!arrived && Time.realtimeSinceStartup<arrivalDeadline)
            {
                yield return null;
                actor=streets.FindActor(person);
                if(!actor || !actor.gameObject.activeInHierarchy || actor.Person.NameNPC!=name)
                    throw new InvalidOperationException("Traveler disappeared before physical bed arrival.");
                float frameDistance=Vector3.Distance(initial,actor.transform.position);
                if(frameDistance>0.9f)
                    throw new InvalidOperationException("Traveler room follower teleported between rendered frames.");
                totalMovement+=frameDistance;
                initial=actor.transform.position;
                arrived=Vector3.Distance(initial,roomTarget)<=0.45f;
            }
            if(!arrived || totalMovement<0.1f)
                throw new InvalidOperationException("Paid traveler did not physically reach assigned room bed-side floor.");
            Debug.Log("LIVING_CITIES_TRAVELER_ROOM_ARRIVAL_PASS: guest moved by rendered frames to assigned bed-side floor.");
            clock.RaiseTime((float)(Math.Floor(clock.ToSeconds()/86400d)*86400+86340-clock.ToSeconds()));
            yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
            yield return new WaitForSecondsRealtime(1);
            actor=streets.FindActor(person);
            if(!actor || !actor.IsSleeping) throw new InvalidOperationException("Paid traveler did not sleep in inn.");
            anchor=BedGeometry.Read(game.PlayerEnterExit.Interior).Find(b=>"bed:"+b.RecordIndex==bed);
            if(anchor==null || Vector3.Distance(actor.transform.position,game.PlayerEnterExit.Interior.transform.TransformPoint(anchor.LocalCenter))>2)
                throw new InvalidOperationException("Traveler sleeps outside assigned bed.");
            if(new InteriorGeometry(game.PlayerEnterExit.Interior).StandBesideBed(anchor,out standing))
                game.PlayerMotor.transform.position=standing+Vector3.up;
            yield return null;
            SleepProbe.AimCamera(actor.transform.position);
            yield return new WaitForEndOfFrame();
            yield return SaveArrival(game.PlayerEntity.Name,streets,"Living Cities - traveler sleeping");
            actor=streets.FindActor(person);
            if(!actor || !actor.IsSleeping || actor.Person.NameNPC!=name)
                throw new InvalidOperationException("Sleeping guest identity or pose lost on reload.");
            clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((Math.Floor(clock.ToSeconds()/86400d)+1)*86400+9*3600-clock.ToSeconds()));
            yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
            yield return new WaitForSecondsRealtime(1);
            actor=streets.FindActor(person);
            if(!actor || actor.IsSleeping) throw new InvalidOperationException("Guest did not wake after sleeping.");
            store=(WorldStateStore)field.GetValue(adapter);
            var itinerary=store.TravelerJourneyFor(visit);
            roomGeometry=new InteriorGeometry(game.PlayerEnterExit.Interior);
            anchor=BedGeometry.Read(game.PlayerEnterExit.Interior).Find(b=>"bed:"+b.RecordIndex==bed);
            if(anchor==null || !roomGeometry.StandBesideBed(anchor,out standing))
                throw new InvalidOperationException("Guest checkout bed has no floor access.");
            var exitRoute=roomGeometry.RouteToExit(standing);
            if(exitRoute.Count<2) throw new InvalidOperationException("Guest checkout has no surveyed exit route.");
            exitRoute.Reverse();
            var walk=TravelerRoomWalk.Create(exitRoute,itinerary.CheckInAt,itinerary.CheckOutAt,12/1.3);
            clock.RaiseTime((float)(itinerary.CheckOutAt-walk.Duration*0.5-clock.ToSeconds()));
            yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
            yield return new WaitForSecondsRealtime(1);
            actor=streets.FindActor(person);
            if(!actor || actor.IsSleeping) throw new InvalidOperationException("Guest missing during checkout walk.");
            var before=actor.transform.position;
            yield return new WaitForSecondsRealtime(1);
            if(Vector3.Distance(before,actor.transform.position)<0.1f)
                throw new InvalidOperationException("Guest does not walk toward exit before checkout.");
            game.PlayerEnterExit.TransitionExterior();
            yield return new WaitForSecondsRealtime(2);
            Debug.Log("LIVING_CITIES_TRAVELER_INN_PASS: named paid guest, assigned bed, sleeping reload and morning wake.");
        }
        private static IEnumerator SaveArrival(string character,StreetPopulation streets,string saveName="Living Cities - traveler arrival")
        {
            var game=GameManager.Instance;
            bool saved=false,loaded=false;
            SaveLoadManager.OnSaveEventHandler onSave=ignored=>saved=true;
            SaveLoadManager.OnLoadEventHandler onLoad=ignored=>loaded=true;
            SaveLoadManager.OnSave+=onSave; SaveLoadManager.OnLoad+=onLoad;
            try
            {
                game.SaveLoadManager.Save(character,saveName);
                float deadline=Time.realtimeSinceStartup+30;
                while(!saved && Time.realtimeSinceStartup<deadline) yield return null;
                if(!saved) throw new InvalidOperationException("Traveler arrival save timed out.");
                int slot=game.SaveLoadManager.FindSaveFolderByNames(character,saveName);
                game.SaveLoadManager.Load(slot); DaggerfallUI.Instance.PopToHUD();
                deadline=Time.realtimeSinceStartup+90;
                while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<deadline) yield return null;
                if(!loaded || !streets.Ready) throw new InvalidOperationException("Traveler arrival reload timed out.");
                yield return new WaitForSecondsRealtime(1);
            }
            finally {SaveLoadManager.OnSave-=onSave; SaveLoadManager.OnLoad-=onLoad;}
        }
    }
}
