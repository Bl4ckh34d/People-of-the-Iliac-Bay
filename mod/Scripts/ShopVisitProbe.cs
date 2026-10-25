using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public static class ShopVisitProbe
    {
        public static IEnumerator Run()
        {
            yield return RunVisit(false);
            yield return RunVisit(true);
            yield return RunForeignVisit();
        }
        private static string ShopDepartureDiagnostics(StreetPopulation streets,string residentId,ResidentActor actor,
            DaggerfallInterior room,Vector3 doorway,double now)
        {
            var departures=typeof(StreetPopulation).GetField("shopDepartures",BindingFlags.Instance|BindingFlags.NonPublic)
                .GetValue(streets) as System.Collections.IDictionary;
            var value=departures!=null && departures.Contains(residentId) ? departures[residentId] : null;
            if(value==null) return "departure=missing; actor="+(actor ? actor.transform.position.ToString() : "missing")+
                "; distanceToDoor="+(actor ? Vector3.Distance(actor.transform.position,doorway).ToString("F2") : "n/a")+
                "; now="+now.ToString("F2");
            var type=value.GetType();
            var route=type.GetField("Route",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(value) as System.Collections.IList;
            int next=(int)type.GetField("Next",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(value);
            double startedAt=(double)type.GetField("StartedAt",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(value);
            double retryAt=(double)type.GetField("RetryAt",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(value);
            string target="missing";
            string bypass="missing";
            Vector3 targetGround=Vector3.zero;
            if(route!=null && route.Count>0)
            {
                int index=Mathf.Clamp(next,0,route.Count-1);
                var point=(GuardPoint3)route[index];
                targetGround=room.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z));
                target=targetGround.ToString();
                if(actor && index+1<route.Count)
                {
                    var after=(GuardPoint3)route[index+1];
                    var afterGround=room.transform.TransformPoint(new Vector3((float)after.X,(float)after.Y,(float)after.Z));
                    var method=typeof(StreetPopulation).GetMethod("ShopDepartureSegmentClear",BindingFlags.Instance|BindingFlags.NonPublic);
                    bool clear=(bool)method.Invoke(streets,new object[]{actor.transform.position-Vector3.up,afterGround,actor});
                    bypass="next="+afterGround.ToString("F4")+"; clear="+clear+
                        "; distance="+Vector3.Distance(actor.transform.position-Vector3.up,afterGround).ToString("F4")+
                        "; deltaY="+(afterGround.y-(actor.transform.position.y-1)).ToString("F4")+
                        "; sweep="+DepartureSweepBlockers(room,actor.transform.position-Vector3.up,afterGround,actor)+
                        "; support="+DepartureSupport(room,actor.transform.position-Vector3.up,afterGround);
                }
            }
            string actorBlockers=actor ? DepartureBlockers(room,actor.transform.position-Vector3.up,actor) : "missing";
            string targetBlockers=route!=null && route.Count>0 ? DepartureBlockers(room,targetGround,null) : "missing";
            string sweepBlockers="missing";
            if(actor && route!=null && route.Count>0)
                sweepBlockers=DepartureSweepBlockers(room,actor.transform.position-Vector3.up,targetGround,actor);
            var neighbors=new List<string>();
            if(actor)
                foreach(var body in UnityEngine.Object.FindObjectsOfType<CrowdMotion>())
                {
                    if(!body || body.gameObject==actor.gameObject || !body.gameObject.activeInHierarchy) continue;
                    var position=body.transform.position-Vector3.up;
                    float actorDistance=Vector3.Distance(position,actor.transform.position-Vector3.up);
                    float targetDistance=route!=null && route.Count>0 ? Vector3.Distance(position,targetGround) : float.MaxValue;
                    if(actorDistance>2.0f && targetDistance>2.0f) continue;
                    var other=body.GetComponent<ResidentActor>();
                    neighbors.Add((other && other.Resident!=null ? other.Resident.Id : body.name)+"@"+position+
                        "/actor="+actorDistance.ToString("F2")+"/target="+targetDistance.ToString("F2"));
                    if(neighbors.Count>=8) break;
                }
            return "actor="+(actor ? actor.transform.position.ToString() : "missing")+
                "; target="+target+"; routeNext="+next+"; routeCount="+(route==null?0:route.Count)+
                "; elapsed="+(now-startedAt).ToString("F2")+"; retryAt="+retryAt.ToString("F2")+
                "; distanceToDoor="+(actor ? Vector3.Distance(actor.transform.position,doorway).ToString("F2") : "n/a")+
                "; bypass="+bypass+"; actorBlockers="+actorBlockers+"; targetBlockers="+targetBlockers+"; sweepBlockers="+sweepBlockers+
                "; crowdNeighbors="+string.Join(",",neighbors.ToArray());
        }
        private static string DepartureBlockers(DaggerfallInterior room,Vector3 ground,ResidentActor actor)
        {
            var blockers=new List<string>();
            Vector3 bottom=ground+Vector3.up*(0.51f+CrowdMotion.BodyRadius),top=ground+Vector3.up*1.65f;
            foreach(var collider in Physics.OverlapCapsule(bottom,top,CrowdMotion.BodyRadius,~0,QueryTriggerInteraction.Ignore))
            {
                if(IsDepartureIgnored(collider,actor)) continue;
                blockers.Add(collider.name+"/"+(collider.GetComponentInParent<StaticNPC>() ? "StaticNPC" : collider.GetComponentInParent<MobilePersonNPC>() ? "MobilePersonNPC" : collider.GetComponentInParent<CrowdMotion>() ? "CrowdMotion" : "Collider"));
                if(blockers.Count>=8) break;
            }
            return blockers.Count==0 ? "none" : string.Join(",",blockers.ToArray());
        }
        private static string DepartureSupport(DaggerfallInterior room,Vector3 from,Vector3 to)
        {
            for(float t=0;t<=1.001f;t+=0.2f)
            {
                var sample=Vector3.Lerp(from,to,t); bool supported=false;var hits=new List<string>();
                foreach(var hit in Physics.RaycastAll(sample+Vector3.up*0.55f,Vector3.down,1.1f,~0,QueryTriggerInteraction.Ignore))
                {
                    bool child=hit.collider.transform.IsChildOf(room.transform);
                    hits.Add(hit.collider.name+"/normal="+hit.normal.y+"/height="+(hit.point.y-sample.y)+"/child="+child);
                    if(hit.normal.y>=0.65f && Mathf.Abs(hit.point.y-sample.y)<=0.5f && child) supported=true;
                }
                if(!supported) return "unsupported@"+t+":"+string.Join(",",hits.ToArray());
            }
            return "supported";
        }
        private static string DepartureSweepBlockers(DaggerfallInterior room,Vector3 from,Vector3 to,ResidentActor actor)
        {
            Vector3 delta=to-from; if(delta.sqrMagnitude<0.000001f) return "none";
            var blockers=new List<string>();
            foreach(var hit in Physics.CapsuleCastAll(from+Vector3.up*(0.51f+CrowdMotion.BodyRadius),from+Vector3.up*1.65f,
                CrowdMotion.BodyRadius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
            {
                if(IsDepartureIgnored(hit.collider,actor)) continue;
                blockers.Add(hit.collider.name+"@"+hit.point+"/"+(hit.collider.GetComponentInParent<StaticNPC>() ? "StaticNPC" : hit.collider.GetComponentInParent<MobilePersonNPC>() ? "MobilePersonNPC" : "Collider"));
                if(blockers.Count>=8) break;
            }
            return blockers.Count==0 ? "none" : string.Join(",",blockers.ToArray());
        }
        private static bool IsDepartureIgnored(Collider collider,ResidentActor actor)
        {
            if(actor && collider.transform.IsChildOf(actor.transform) || collider.GetComponentInParent<CrowdMotion>()) return true;
            return collider.GetComponentInParent<DaggerfallActionDoor>() || collider.GetComponentInParent<DaggerfallAction>();
        }
        private static IEnumerator RunForeignVisit()
        {
            var game=GameManager.Instance; var room=game.PlayerEnterExit.Interior;
            var mod=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>(); var streets=mod.GetComponent<StreetPopulation>();
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var store=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); store.Restore((WorldState)mod.GetSaveData());
            var person=store.GetOrCreate(100,new[]{30},1).Households[0].Residents[0];
            var clock=DaggerfallUnity.Instance.WorldTime.Now; double now=clock.ToSeconds();
            var trip=ResidentTrip.Create(person.Id,100,directory.MapID,now-100,3600,100,
                new[]{new RoutePoint(207,212),new RoutePoint(207,213)});
            var plan=new ResidentDestination {ExitWalkSeconds=10};
            plan.Stops.Add(new ResidentVisitStop {BuildingKey=room.EntryDoor.buildingKey,Purpose=ResidentVisitPurpose.Shop,
                WalkSeconds=10,DwellSeconds=1440,Route=new List<SavedRoutePoint> {new SavedRoutePoint {X=0,Z=0},new SavedRoutePoint {X=1,Z=0}}});
            plan.ExitRoute.Add(new SavedRoutePoint {X=1,Z=0}); plan.ExitRoute.Add(new SavedRoutePoint {X=0,Z=0});
            trip.Destination=plan; store.ScheduleResidentTrip(trip); mod.RestoreSaveData(store.Snapshot());
            clock.RaiseTime(12);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var journeys=(Dictionary<string,ShopVisitJourney>)typeof(StreetPopulation).GetField("shopJourneys",flags).GetValue(streets);
            float deadline=Time.realtimeSinceStartup+15;
            while((!streets.Ready || !journeys.ContainsKey(person.Id) || !streets.FindActor(person.Id)) && Time.realtimeSinceStartup<deadline) yield return null;
            ShopVisitJourney journey;
            if(!journeys.TryGetValue(person.Id,out journey) || journey==null || !streets.FindActor(person.Id))
                throw new InvalidOperationException("Foreign resident has no real shop browsing journey.");
            var accepted=ResidentTrip.CopyValidated(trip); double browse=-1;
            for(double t=clock.ToSeconds()+1;t<accepted.ReturnsFromVisitAt-10;t++)
                if(journey.Evaluate(t).Phase==ShopVisitPhase.Browsing) {browse=t;break;}
            if(browse<0) throw new InvalidOperationException("Foreign visitor has no browsing phase.");
            clock.RaiseTime((float)(browse-clock.ToSeconds())); yield return null; yield return null;
            var actor=streets.FindActor(person.Id); var sample=journey.Evaluate(clock.ToSeconds());
            var target=room.transform.TransformPoint(new Vector3((float)sample.Position.X,(float)sample.Position.Y,(float)sample.Position.Z));
            deadline=Time.realtimeSinceStartup+12;
            while(actor && Vector3.Distance(actor.transform.position-Vector3.up,target)>0.6f && Time.realtimeSinceStartup<deadline) yield return null;
            if(!actor || Vector3.Distance(actor.transform.position-Vector3.up,target)>0.6f)
                throw new InvalidOperationException("Foreign shopper did not follow browsing journey.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOREIGN_SHOP_PASS: original foreign resident uses surveyed shop browsing within visit window.");

            // A destination visitor must finish the same physical doorway route as a
            // local shopper when its trip window ends. The old destination path removed
            // its actor as soon as the itinerary switched to the exterior road phase.
            var indoor=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",flags).GetValue(streets);
            if(!indoor.EntryGround.HasValue) throw new InvalidOperationException("Foreign departure probe lacks surveyed entry ground.");
            var doorway=indoor.EntryGround.Value+Vector3.up;
            var departureStart=actor.transform.position;
            float departureStartDistance=Vector3.Distance(departureStart,doorway);
            if(departureStartDistance<0.75f) throw new InvalidOperationException("Foreign visitor reached doorway before departure continuation could be tested.");
            double returnAt=accepted.ReturnsFromVisitAt;
            // Observe the remaining visit continuously. Advancing the clock used
            // to conceal missed corners and bodies left behind their schedules.
            yield return null;
            actor=streets.FindActor(person.Id);
            if(!actor) throw new InvalidOperationException("Foreign shopper disappeared before physical exit continuation began.");
            Vector3 previous=actor.transform.position,lastPosition=previous;
            float maximumStep=0; bool movedTowardDoor=false,removed=false; int departureFrames=0;
            deadline=Time.realtimeSinceStartup+180;
            while(departureFrames<36000 && Time.realtimeSinceStartup<deadline)
            {
                yield return new WaitForEndOfFrame();
                departureFrames++;
                actor=streets.FindActor(person.Id);
                if(!actor) { removed=true; break; }
                var current=actor.transform.position;
                float step=Vector3.Distance(previous,current);
                maximumStep=Mathf.Max(maximumStep,step);
                if(step>=0.01f && Vector3.Distance(current,doorway)<Vector3.Distance(previous,doorway)) movedTowardDoor=true;
                if(step>1.0f) throw new InvalidOperationException("Foreign departure made discontinuous rendered step: "+step);
                previous=current; lastPosition=current;
            }
            if(!removed) throw new InvalidOperationException("Foreign shopper did not finish continuous visit and physical exit within 180 seconds; "+
                ShopDepartureDiagnostics(streets,person.Id,actor,room,doorway,clock.ToSeconds()));
            if(Vector3.Distance(lastPosition,doorway)>0.7f) throw new InvalidOperationException("Foreign shopper disappeared away from doorway: last="+lastPosition+"; doorway="+doorway);
            if(!movedTowardDoor || departureFrames<2) throw new InvalidOperationException("Foreign shopper did not visibly walk out from actual interior position.");
            yield return null; yield return null;
            foreach(var duplicate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if(duplicate && duplicate.Resident!=null && duplicate.Resident.Id==person.Id)
                    throw new InvalidOperationException("Foreign shopper left a duplicate body after physical departure.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOREIGN_SHOP_DEPARTURE_PASS: actual interior position continued to doorway without teleport or duplicate; resident="+person.Id+
                "; startDistance="+departureStartDistance+"; maxFrameStep="+maximumStep+"; frames="+departureFrames);

            // Continue the corpse-report test with a second foreign resident after the
            // departure fixture completes, so it still tests a visitor inside the shop.
            var witness=store.GetOrCreate(102,new[]{30},1).Households[0].Residents[0];
            now=clock.ToSeconds();
            trip=ResidentTrip.Create(witness.Id,102,directory.MapID,now-100,3600,100,
                new[]{new RoutePoint(207,212),new RoutePoint(207,213)});
            trip.Destination=plan; store.ScheduleResidentTrip(trip); mod.RestoreSaveData(store.Snapshot());
            clock.RaiseTime(12);
            deadline=Time.realtimeSinceStartup+15;
            while((!streets.Ready || !journeys.ContainsKey(witness.Id) || !streets.FindActor(witness.Id)) && Time.realtimeSinceStartup<deadline) yield return null;
            if(!journeys.TryGetValue(witness.Id,out journey) || journey==null || !streets.FindActor(witness.Id))
                throw new InvalidOperationException("Second foreign resident has no real shop browsing journey for corpse-report fixture.");
            person=witness;
            actor=streets.FindActor(person.Id);
            var crimeStore=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); crimeStore.Restore((WorldState)mod.GetSaveData());
            var localTown=crimeStore.GetOrCreate(directory.MapID,new[]{30},1); Resident victim=null;
            foreach(var household in localTown.Households)
                foreach(var resident in household.Residents)
                    if(victim==null && resident.IsAlive && !resident.IsQuestProtected && resident.NativeIdentity==null) victim=resident;
            if(victim==null) throw new InvalidOperationException("Foreign shopper corpse fixture lacks victim.");
            now=clock.ToSeconds(); var alarm=crimeStore.RecordViolence(directory.MapID,victim.Id,now);
            crimeStore.LocateViolence(alarm.Id,room.EntryDoor.buildingKey,0,0,0);
            victim.IsAlive=false; crimeStore.ConfirmCorpse(alarm.Id,now);
            var isolated=crimeStore.Snapshot();
            // Disposable fixture: only the foreign visitor can deliver this report.
            foreach(var town in isolated.Settlements) if(town.SettlementId==directory.MapID)
                foreach(var household in town.Households) foreach(var resident in household.Residents) resident.IsAlive=false;
            foreach(var native in isolated.NativePeople) if(native.SettlementId==directory.MapID) native.IsAlive=false;
            mod.RestoreSaveData(isolated);
            AlarmPerson observer=null; deadline=Time.realtimeSinceStartup+20;
            while(Time.realtimeSinceStartup<deadline)
            {
                var saved=(WorldState)mod.GetSaveData();
                var recorded=saved.AlarmIncidents.Find(a=>a.Id==alarm.Id);
                observer=recorded==null ? null : recorded.People.Find(p=>p.ResidentId==person.Id);
                if(observer!=null && observer.CorpseObserver && observer.Escape!=null) break;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            if(observer==null || !observer.CorpseObserver || observer.Escape==null || observer.Reaction!=CivilianReaction.Flee)
                throw new InvalidOperationException("Foreign shopper ignored corpse or failed to prepare guard-report escape.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOREIGN_CORPSE_PASS: visitor stops shopping, discovers corpse and prepares physical reporting route.");
            string reportId="inn-alarm:"+alarm.Id;
            var guards=mod.GetComponent<GuardPopulation>();
            if(guards.Roster.Reports.Exists(r=>r.Id==reportId && r.GuardIds.Count>0))
                throw new InvalidOperationException("Foreign corpse report arrived before witness left the shop.");
            deadline=Time.realtimeSinceStartup+45;
            bool delivered=false;
            while(Time.realtimeSinceStartup<deadline)
            {
                DaggerfallUI.Instance.PopToHUD(); game.PauseGame(false);
                clock.RaiseTime(12); yield return new WaitForSecondsRealtime(0.1f);
                if(guards.Roster.Reports.Exists(r=>r.Id==reportId && r.GuardIds.Count>0)) {delivered=true;break;}
            }
            if(!delivered) throw new InvalidOperationException("Isolated foreign witness never reached guards to report corpse.");
            var finalState=(WorldState)mod.GetSaveData();
            var finalAlarm=finalState.AlarmIncidents.Find(a=>a.Id==alarm.Id);
            var finalObserver=finalAlarm.People.Find(p=>p.ResidentId==person.Id);
            if(finalObserver==null || finalObserver.BuildingKey!=-1 || finalObserver.DirectWitness || finalAlarm.People.Exists(p=>p.ResidentId!=person.Id))
                throw new InvalidOperationException("Foreign report lacks isolated neutral exterior witness evidence.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOREIGN_REPORT_PASS: sole foreign witness exits and physically informs guards while player remains indoors.");
            deadline=Time.realtimeSinceStartup+45;
            while(!guards.OccupiesCrimeScene(room.EntryDoor.buildingKey,clock.ToSeconds()) && Time.realtimeSinceStartup<deadline)
            {
                DaggerfallUI.Instance.PopToHUD(); game.PauseGame(false);
                clock.RaiseTime(12); yield return new WaitForSecondsRealtime(0.1f);
            }
            if(!guards.OccupiesCrimeScene(room.EntryDoor.buildingKey,clock.ToSeconds()))
                throw new InvalidOperationException("Responding guards never occupied shopper crime scene.");
            var arrivalStore=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); arrivalStore.Restore((WorldState)mod.GetSaveData());
            var newcomer=arrivalStore.GetOrCreate(101,new[]{30},1).Households[0].Residents[0];
            var arrival=ResidentTrip.Create(newcomer.Id,101,directory.MapID,clock.ToSeconds(),3600,100,
                new[]{new RoutePoint(207,212),new RoutePoint(207,213)});
            arrival.DepartsAt=clock.ToSeconds()-arrival.TravelSeconds;
            arrival.Destination=plan; arrivalStore.ScheduleResidentTrip(arrival); mod.RestoreSaveData(arrivalStore.Snapshot());
            double expectedInsideAt=clock.ToSeconds()+12;
            var expectedArrival=arrivalStore.ResidentTripAt(newcomer.Id,expectedInsideAt);
            if(expectedArrival.Phase!=ResidentTripPhase.Visiting || expectedArrival.VisitPhase!=ResidentVisitPhase.Inside ||
                expectedArrival.BuildingKey!=room.EntryDoor.buildingKey)
                throw new InvalidOperationException("Guarded-scene shopper fixture does not place newcomer inside the occupied shop at probe time.");
            clock.RaiseTime(12); ResidentServiceExit cancellation=null;
            deadline=Time.realtimeSinceStartup+10;
            while(Time.realtimeSinceStartup<deadline)
            {
                var saved=(WorldState)mod.GetSaveData();
                var activeTrip=saved.ResidentTrips.Find(t=>t.ResidentId==newcomer.Id);
                cancellation=activeTrip.ServiceExits.Count>0 ? activeTrip.ServiceExits[0] : null;
                if(cancellation!=null) break;
                yield return null;
            }
            if(cancellation==null) throw new InvalidOperationException("Arriving shopper failed to leave guarded crime scene.");
            var occupiedSave=(WorldState)mod.GetSaveData();
            if(occupiedSave.AlarmIncidents.Exists(a=>a.People.Exists(p=>p.ResidentId==newcomer.Id)))
                throw new InvalidOperationException("Shopper redundantly reported a scene already occupied by guards.");
            double exitAt=ResidentServiceExit.Copy(cancellation).ExitAt;
            clock.RaiseTime((float)Math.Max(0,exitAt+1-clock.ToSeconds())); yield return new WaitForSecondsRealtime(1);
            if(streets.FindActor(newcomer.Id)) throw new InvalidOperationException("Guarded-shop visitor remained in interior after exit.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOREIGN_GUARDED_EXIT_PASS: new visitor automatically leaves occupied scene without another report.");
        }
        private static IEnumerator RunVisit(bool purchase)
        {
            var game=GameManager.Instance; var room=game.PlayerEnterExit.Interior;
            if(!room) throw new InvalidOperationException("Shop fixture is not inside.");
            int buildingKey=room.EntryDoor.buildingKey;
            var mod=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>(); var streets=mod.GetComponent<StreetPopulation>();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var baselineJson=SaveLoadManager.Serialize(typeof(WorldState),mod.GetSaveData());
            var baseline=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),baselineJson);
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var fixtureGeometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",flags).GetValue(streets);
            if(fixtureGeometry==null || !fixtureGeometry.EntryGround.HasValue)
                throw new InvalidOperationException("Shop fixture needs surveyed doorway.");
            Vector3 observation=fixtureGeometry.EntryGround.Value;
            float observationDistance=0;
            foreach(var anchor in fixtureGeometry.LocalAnchors)
            {
                var point=room.transform.TransformPoint(anchor);
                float distance=(point-fixtureGeometry.EntryGround.Value).sqrMagnitude;
                if(distance>observationDistance) {observationDistance=distance;observation=point;}
            }
            if(observationDistance<16) throw new InvalidOperationException("No observation position clear of shopper entrance.");
            game.PlayerMotor.transform.position=observation+Vector3.up*1.5f;
            Physics.SyncTransforms();
            int day=(int)Math.Floor(now/86400);
            int minute=(int)(now%86400/60);
            int opens=minute+1, closes=minute+30;
            if(closes>=1440) throw new InvalidOperationException("Shop traversal fixture needs eighteen game minutes remaining in this day.");
            var plans=(IEnumerable)typeof(StreetPopulation).GetField("plans",flags).GetValue(streets);
            object selected=null;
            foreach(var plan in plans)
            {
                var resident=(Resident)plan.GetType().GetField("Resident").GetValue(plan);
                if(!resident.IsAlive || resident.IsQuestProtected || resident.NativeIdentity!=null || resident.AgeGroup==ResidentAgeGroup.Child || resident.HomeKey==buildingKey) continue;
                uint visitSeed=StableSeed.Combine(resident.AppearanceSeed,(uint)buildingKey,(uint)day,0x53484f50);
                if(((visitSeed&1)==0)!=purchase) continue;
                selected=plan; break;
            }
            if(selected==null) throw new InvalidOperationException("Shop visitor fixture absent for requested stable purchase outcome today.");
            var type=selected.GetType(); var person=(Resident)type.GetField("Resident").GetValue(selected);
            string residentId=person.Id;
            int settlementId=-1;
            foreach(var settlement in baseline.Settlements)
                if(settlement.Households.Exists(h=>h.Residents.Exists(r=>r.Id==residentId)))
                { settlementId=settlement.SettlementId; break; }
            if(settlementId<0) throw new InvalidOperationException("Shop fixture resident has no saved settlement.");
            var liveStore=typeof(PeopleOfTheIliacBayMod).GetField("store",flags).GetValue(mod) as WorldStateStore;
            if(liveStore==null) throw new InvalidOperationException("Shop fixture has no persistent world store.");
            double walkSeconds=60;
            double startOffset=(opens+1)*60d;
            var options=typeof(PeopleOfTheIliacBayMod).GetField("options",flags).GetValue(mod);
            int wakeMinute=(int)options.GetType().GetField("WakeHour").GetValue(options)*60;
            int sleepMinute=(int)options.GetType().GetField("SleepHour").GetValue(options)*60;
            if(startOffset-walkSeconds<wakeMinute*60d || startOffset+24*60d+walkSeconds>sleepMinute*60d)
                throw new InvalidOperationException("Shop checkpoint fixture window does not fit resident waking hours.");
            var destination=new ResidentDestination {ExitRoute=new List<SavedRoutePoint>
                {new SavedRoutePoint {X=1,Z=0},new SavedRoutePoint {X=0,Z=0}},ExitWalkSeconds=walkSeconds};
            destination.Stops.Add(new ResidentVisitStop {BuildingKey=buildingKey,Purpose=ResidentVisitPurpose.Shop,
                OpenMinute=opens,CloseMinute=closes,WalkSeconds=walkSeconds,DwellSeconds=24*60d,
                Route=new List<SavedRoutePoint> {new SavedRoutePoint {X=0,Z=0},new SavedRoutePoint {X=1,Z=0}}});
            PersistFixtureRoutine(liveStore,settlementId,residentId,destination,startOffset-walkSeconds);
            mod.RestoreSaveData(liveStore.Snapshot());
            float buildDeadline=Time.realtimeSinceStartup+20;
            object rebuilt=null;
            while(Time.realtimeSinceStartup<buildDeadline &&
                (!streets.Ready || (rebuilt=FindPlan(streets,residentId,flags))==null)) yield return null;
            if(!streets.Ready || rebuilt==null) throw new InvalidOperationException("Persisted shop fixture routine did not rebuild.");
            selected=rebuilt; type=selected.GetType(); person=(Resident)type.GetField("Resident").GetValue(selected);
            if(person.Id!=residentId) throw new InvalidOperationException("Persisted shop fixture changed resident identity.");
            var schedule=(DailySchedule)type.GetField("Schedule").GetValue(selected);
            double start=0,end=0;
            if(!schedule.TryVisitWindowForBuilding(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),buildingKey,out start,out end) || end<=start)
                throw new InvalidOperationException("Persisted shop itinerary lacks exact visit window.");
            if(start<=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()) throw new InvalidOperationException("Persisted shop window must begin naturally in the future.");
            typeof(StreetPopulation).GetField("shopVisitDay",flags).SetValue(streets,-1d);
            var journeys=(Dictionary<string,ShopVisitJourney>)typeof(StreetPopulation).GetField("shopJourneys",flags).GetValue(streets);
            journeys.Remove(person.Id);
            var physical=(Dictionary<string,PhysicalShopVisit>)typeof(StreetPopulation).GetField("physicalShopVisits",flags).GetValue(streets);
            physical.Remove(person.Id);
            try
            {
                float deadline=Time.realtimeSinceStartup+120f;
                while(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()<start && Time.realtimeSinceStartup<deadline) yield return null;
                if(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()<start) throw new InvalidOperationException("Shop visit did not reach its scheduled start through rendered time.");
                while((!journeys.ContainsKey(person.Id) || !streets.FindActor(person.Id)) && Time.realtimeSinceStartup<deadline) yield return null;
                ShopVisitJourney journey;
                if(!journeys.TryGetValue(person.Id,out journey) || journey==null) throw new InvalidOperationException("Real shop lacks usable activity routes: "+
                    ((ShopVisitGeometry)typeof(StreetPopulation).GetField("shopGeometry",flags).GetValue(streets)).RejectionFor(person.Id));
                if(journey.Purchases!=purchase) throw new InvalidOperationException("Shop purchase choice did not match its stable visit seed.");
                var shopGeometry=(ShopVisitGeometry)typeof(StreetPopulation).GetField("shopGeometry",flags).GetValue(streets);
                var browsingStops=new HashSet<string>();
                var actor=streets.FindActor(person.Id);
                var indoor=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",flags).GetValue(streets);
                if(!indoor.EntryGround.HasValue) throw new InvalidOperationException("Shop traversal probe lacks surveyed entry ground.");
                Vector3 doorway=indoor.EntryGround.Value+Vector3.up;
                if(Vector3.Distance(doorway,actor.transform.position)>1.25f)
                    throw new InvalidOperationException("Shop visitor did not begin physical traversal at the surveyed doorway.");
                var stableFacing=new HashSet<string>();
                var facingFrames=new Dictionary<string,int>();
                bool counterSpacingChecked=false,talked=false,sawLeaving=false,removed=false;
                bool checkpointed=false;
                double counterStarted=-1,counterDuration=-1;
                float maximumStep=0,departureStartDistance=-1,lastDoorDistance=Vector3.Distance(actor.transform.position,doorway);
                Vector3 previous=actor.transform.position,lastPosition=previous;
                float traversalDeadline=Time.realtimeSinceStartup+180f;
                ShopVisitPhase previousPhase=ShopVisitPhase.Walking;
                while(Time.realtimeSinceStartup<traversalDeadline)
                {
                    yield return new WaitForEndOfFrame();
                    actor=streets.FindActor(person.Id);
                    if(!actor) { removed=true; break; }
                    ShopVisitSnapshot state;
                    if(!streets.TryShopActivity(person.Id,out state))
                        throw new InvalidOperationException("Shop visitor has no physical activity snapshot.");
                    if(!checkpointed && (state.Phase==ShopVisitPhase.Walking || state.Phase==ShopVisitPhase.Browsing))
                    {
                        // Saved routine already names this target before capture validates it.
                        var saved=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),
                            SaveLoadManager.Serialize(typeof(WorldState),mod.GetSaveData()));
                        var observed=saved.ObservedVisits.Find(item=>item.ResidentId==person.Id);
                        double checkpointTime=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                        if(observed==null || observed.Legs.Count<2)
                            throw new InvalidOperationException("Shop checkpoint did not capture remaining route legs.");
                        var expected=observed.Evaluate(checkpointTime);
                        var expectedPosition=room.transform.TransformPoint(new Vector3((float)expected.Position.X,
                            (float)expected.Position.Y,(float)expected.Position.Z))+Vector3.up;
                        float captureError=Vector3.Distance(actor.transform.position,expectedPosition);
                        if(captureError>0.15f || (expected.Phase!=ShopVisitPhase.Walking && expected.Phase!=ShopVisitPhase.Browsing))
                            throw new InvalidOperationException("Shop checkpoint did not preserve current walking/browsing body position: phase="+
                                expected.Phase+"; positionError="+captureError.ToString("F3")+" m.");
                        mod.RestoreSaveData(saved);
                        checkpointed=true;
                        float reloadDeadline=Time.realtimeSinceStartup+12;
                        actor=null;
                        while((!streets.Ready || !(actor=streets.FindActor(person.Id))) &&
                            Time.realtimeSinceStartup<reloadDeadline) yield return null;
                        if(!streets.Ready || !actor)
                            throw new InvalidOperationException("Shopper did not rebuild after serialized checkpoint reload.");
                        room=game.PlayerEnterExit.Interior;
                        indoor=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",flags).GetValue(streets);
                        shopGeometry=(ShopVisitGeometry)typeof(StreetPopulation).GetField("shopGeometry",flags).GetValue(streets);
                        journeys=(Dictionary<string,ShopVisitJourney>)typeof(StreetPopulation).GetField("shopJourneys",flags).GetValue(streets);
                        physical=(Dictionary<string,PhysicalShopVisit>)typeof(StreetPopulation).GetField("physicalShopVisits",flags).GetValue(streets);
                        if(!room || indoor==null || shopGeometry==null || !indoor.EntryGround.HasValue)
                            throw new InvalidOperationException("Shop geometry did not rebuild after serialized checkpoint reload.");
                        doorway=indoor.EntryGround.Value+Vector3.up;
                        double advanced=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()-checkpointTime;
                        float reloadError=Vector3.Distance(actor.transform.position,expectedPosition);
                        if(advanced<0 || advanced>5 || actor.Resident==null || actor.Resident.Id!=person.Id)
                            throw new InvalidOperationException("Shop checkpoint reload changed identity, position, or clock bound: resident="+
                                (actor.Resident==null?"missing":actor.Resident.Id)+"; advanced="+advanced.ToString("F2")+
                                "; positionError="+reloadError.ToString("F3")+" m.");
                        double restoredTime=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                        var resumedExpected=observed.Evaluate(restoredTime);
                        expectedPosition=room.transform.TransformPoint(new Vector3((float)resumedExpected.Position.X,
                            (float)resumedExpected.Position.Y,(float)resumedExpected.Position.Z))+Vector3.up;
                        reloadError=Vector3.Distance(actor.transform.position,expectedPosition);
                        ShopVisitSnapshot resumed;
                        if(!streets.TryShopActivity(person.Id,out resumed) || resumed.Phase!=resumedExpected.Phase || reloadError>0.35f)
                            throw new InvalidOperationException("Shop checkpoint reload did not resume saved route at current bounded time: phase="+
                                resumed.Phase+"; expected="+resumedExpected.Phase+"; positionError="+reloadError.ToString("F3")+" m.");
                        previous=actor.transform.position; lastPosition=previous; previousPhase=resumed.Phase;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_CHECKPOINT_PASS: serialized remaining route restored same resident and position; resident="+
                            person.Id+"; phase="+resumed.Phase+"; clockAdvance="+advanced.ToString("F2")+
                            "; positionError="+reloadError.ToString("F3")+" m; remainingLegs="+observed.Legs.Count);

                        game.PlayerEnterExit.TransitionExterior();
                        // DFU restores its scene cache on the next frame. Give the
                        // population controller the exterior frame before reentry.
                        yield return null; yield return new WaitForEndOfFrame();
                        reloadDeadline=Time.realtimeSinceStartup+15;
                        while(game.PlayerEnterExit.IsPlayerInside && Time.realtimeSinceStartup<reloadDeadline) yield return null;
                        if(game.PlayerEnterExit.IsPlayerInside)
                            throw new InvalidOperationException("Shop checkpoint scene exit did not reach exterior.");
                        var afterExit=(WorldState)mod.GetSaveData();
                        var exitCheckpoint=afterExit.ObservedVisits.Find(item=>item.ResidentId==person.Id);
                        if(exitCheckpoint==null)
                            throw new InvalidOperationException("Shop checkpoint was lost during native scene exit.");
                        observed=exitCheckpoint;
                        var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                        DaggerfallStaticDoors doorOwner=null; StaticDoor shopDoor=default(StaticDoor); bool foundDoor=false;
                        foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                            foreach(var door in owner.Doors ?? new StaticDoor[0])
                                if(door.doorType==DoorTypes.Building && door.buildingKey==buildingKey)
                                { doorOwner=owner; shopDoor=door; foundDoor=true; break; }
                        if(!foundDoor) throw new InvalidOperationException("Shop checkpoint reentry could not resolve same native building door.");
                        TownSmokeHarness.EnterInterior(doorOwner.transform,shopDoor);
                        yield return null; yield return new WaitForEndOfFrame();
                        reloadDeadline=Time.realtimeSinceStartup+20;
                        while((!game.PlayerEnterExit.IsPlayerInsideBuilding || !game.PlayerEnterExit.Interior ||
                            game.PlayerEnterExit.Interior.EntryDoor.buildingKey!=shopDoor.buildingKey || !streets.Ready) &&
                            Time.realtimeSinceStartup<reloadDeadline) yield return null;
                        if(!game.PlayerEnterExit.IsPlayerInsideBuilding || !streets.Ready)
                            throw new InvalidOperationException("Shop checkpoint native scene reentry did not rebuild interior population.");
                        room=game.PlayerEnterExit.Interior;
                        fixtureGeometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",flags).GetValue(streets);
                        indoor=fixtureGeometry;
                        shopGeometry=(ShopVisitGeometry)typeof(StreetPopulation).GetField("shopGeometry",flags).GetValue(streets);
                        journeys=(Dictionary<string,ShopVisitJourney>)typeof(StreetPopulation).GetField("shopJourneys",flags).GetValue(streets);
                        physical=(Dictionary<string,PhysicalShopVisit>)typeof(StreetPopulation).GetField("physicalShopVisits",flags).GetValue(streets);
                        if(!room || room.EntryDoor.buildingKey!=buildingKey || fixtureGeometry==null || !fixtureGeometry.EntryGround.HasValue || shopGeometry==null)
                            throw new InvalidOperationException("Shop geometry did not rebuild after native scene reentry.");
                        observation=fixtureGeometry.EntryGround.Value;
                        doorway=fixtureGeometry.EntryGround.Value+Vector3.up;
                        double reentryTime=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                        if(reentryTime>=observed.EndsAt)
                            throw new InvalidOperationException("Shop checkpoint expired during native scene exit/reentry.");
                        expected=observed.Evaluate(reentryTime);
                        expectedPosition=game.PlayerEnterExit.Interior.transform.TransformPoint(new Vector3((float)expected.Position.X,
                            (float)expected.Position.Y,(float)expected.Position.Z))+Vector3.up;
                        actor=null; reloadDeadline=Time.realtimeSinceStartup+12;
                        while(!(actor=streets.FindActor(person.Id)) && Time.realtimeSinceStartup<reloadDeadline) yield return null;
                        float reentryError=actor ? Vector3.Distance(actor.transform.position,expectedPosition) : float.MaxValue;
                        if(!actor || actor.Resident==null || actor.Resident.Id!=person.Id || reentryError>0.5f)
                            throw new InvalidOperationException("Shop checkpoint did not resume same visitor position after native scene reentry: resident="+
                                (actor&&actor.Resident!=null?actor.Resident.Id:"missing")+"; error="+reentryError.ToString("F3")+" m.");
                        previous=actor.transform.position; lastPosition=previous;
                        ShopVisitSnapshot reentered;
                        if(!streets.TryShopActivity(person.Id,out reentered) || reentered.Phase!=expected.Phase)
                            throw new InvalidOperationException("Shop checkpoint activity phase changed across native scene reentry.");
                        previousPhase=reentered.Phase;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_SCENE_RESUME_PASS: same resident resumed saved route after native exit/reentry; resident="+
                            person.Id+"; phase="+reentered.Phase+"; positionError="+reentryError.ToString("F3")+" m.");
                    }
                    Vector3 current=actor.transform.position;
                    float step=Vector3.Distance(previous,current);
                    maximumStep=Mathf.Max(maximumStep,step);
                    if(step>0.75f) throw new InvalidOperationException("Shop traversal made discontinuous rendered step: "+step.ToString("F3")+" m.");
                    Vector3 target=room.transform.TransformPoint(new Vector3((float)state.Position.X,(float)state.Position.Y,(float)state.Position.Z))+Vector3.up;
                    float targetDistance=Vector3.Distance(current,target);
                    string targetKey=state.Phase+":"+Math.Round(state.Position.X,2)+":"+Math.Round(state.Position.Y,2)+":"+Math.Round(state.Position.Z,2);
                    if(state.Phase==ShopVisitPhase.Browsing || state.Phase==ShopVisitPhase.Haggling)
                    {
                        if(targetDistance<=0.5f)
                        {
                            if(state.Phase==ShopVisitPhase.Browsing) browsingStops.Add(targetKey);
                            if(state.Phase==ShopVisitPhase.Haggling && !counterSpacingChecked)
                            {
                                if(!shopGeometry.CounterPosition.HasValue) throw new InvalidOperationException("Purchase has no keeper position.");
                                var keeperPoint=shopGeometry.CounterPosition.Value;
                                double distance=Math.Sqrt(Math.Pow(state.Position.X-keeperPoint.x,2)+Math.Pow(state.Position.Z-keeperPoint.z,2));
                                if(!SocialInteractionSpacing.Contains(distance))
                                    throw new InvalidOperationException("Customer is outside horizontal conversational spacing: "+distance.ToString("F2")+" m.");
                                counterSpacingChecked=true;
                            }
                            Vector3 local=new Vector3((float)state.Position.X,(float)state.Position.Y,(float)state.Position.Z);
                            Vector3 facing=room.transform.TransformDirection(shopGeometry.Face(local,state.Phase)); facing.y=0;
                            var native=actor.Person.Asset as MobilePersonBillboard;
                            if(!native) throw new InvalidOperationException("Directional activity probe requires a native adult billboard.");
                            var frame=typeof(MobilePersonBillboard).GetField("currentFrame",flags);
                            if(Vector3.Dot(actor.transform.forward,facing.normalized)<0.99f || native.IsIdle || (int)frame.GetValue(native)!=1)
                                throw new InvalidOperationException("Busy visitor lost task-facing or animated walking in place: "+state.Phase);
                            int count;
                            facingFrames.TryGetValue(targetKey,out count);
                            count++; facingFrames[targetKey]=count;
                            if(count>=60 && stableFacing.Add(targetKey))
                            {
                                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_FACING_PASS: "+state.Phase+"; stable direction and stationary directional frame over 60 rendered frames.");
                                if(state.Phase==ShopVisitPhase.Browsing && !talked)
                                {
                                    talked=true;
                                    game.TalkManager.TalkToMobileNPC(actor.Person);
                                    DaggerfallUI.Instance.PopToHUD();
                                    DaggerfallUI.UIManager.PushWindow(DaggerfallUI.Instance.TalkWindow);
                                    if(!native.IsIdle) throw new InvalidOperationException("Opening conversation must immediately face the player.");
                                    yield return null; yield return new WaitForEndOfFrame();
                                    if(!native.IsIdle) throw new InvalidOperationException("Activity placement overwrote conversation facing.");
                                    DaggerfallUI.Instance.PopToHUD();
                                    yield return null; yield return new WaitForEndOfFrame();
                                    if(native.IsIdle || Vector3.Dot(actor.transform.forward,facing.normalized)<0.99f)
                                        throw new InvalidOperationException("Closing conversation did not restore shelf-facing.");
                                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_TALK_FACING_PASS");
                                }
                            }
                        }
                    }
                    if(state.Phase==ShopVisitPhase.Haggling && previousPhase!=ShopVisitPhase.Haggling)
                        counterStarted=Time.realtimeSinceStartup;
                    if(previousPhase==ShopVisitPhase.Haggling && state.Phase!=ShopVisitPhase.Haggling)
                    {
                        counterDuration=Time.realtimeSinceStartup-counterStarted;
                        if(counterDuration<3 || counterDuration>7)
                            throw new InvalidOperationException("Rendered purchase duration outside three to seven seconds: "+counterDuration.ToString("F2"));
                    }
                    if(state.Phase==ShopVisitPhase.Leaving)
                    {
                        if(!sawLeaving) departureStartDistance=Vector3.Distance(current,doorway);
                        sawLeaving=true; lastDoorDistance=Vector3.Distance(current,doorway);
                    }
                    previous=current; lastPosition=current; previousPhase=state.Phase;
                }
                if(!removed) throw new InvalidOperationException("Shop visitor did not finish continuous physical traversal in 180 rendered seconds; "+
                    ShopDepartureDiagnostics(streets,person.Id,actor,room,doorway,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())+
                    "; physicalFailure="+(physical.ContainsKey(person.Id)?physical[person.Id].LastFailure:"missing")+
                    "; player="+game.PlayerMotor.transform.position);
                if(!checkpointed) throw new InvalidOperationException("Shop visitor completed without a stable walking/browsing checkpoint.");
                if(browsingStops.Count<3) throw new InvalidOperationException("Shop visitor did not physically inspect at least three distinct shelf sections: "+browsingStops.Count);
                if(purchase && (!counterSpacingChecked || counterDuration<3 || counterDuration>7))
                    throw new InvalidOperationException("Purchase counter was not physically reached for three to seven seconds.");
                if(!purchase && counterDuration>=0) throw new InvalidOperationException("Non-purchase visit unexpectedly used the keeper counter.");
                if(!sawLeaving || departureStartDistance<0.75f || Vector3.Distance(lastPosition,doorway)>0.7f)
                    throw new InvalidOperationException("Shop visitor did not finish at the physical doorway: startDistance="+departureStartDistance+"; last="+lastPosition+"; doorway="+doorway);
                if(maximumStep>0.75f) throw new InvalidOperationException("Shop visitor exceeded rendered movement step cap: "+maximumStep);
                if(stableFacing.Count<3+(purchase?1:0)) throw new InvalidOperationException("Shop tasks did not retain facing for 60 rendered frames at each activity target: "+stableFacing.Count);
                yield return null; yield return null;
                foreach(var duplicate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if(duplicate && duplicate.Resident!=null && duplicate.Resident.Id==person.Id)
                        throw new InvalidOperationException("Shop visitor left a duplicate body after physical departure.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_DEPARTURE_PASS: continuous physical traversal reached doorway without teleport or duplicate; resident="+person.Id+
                    "; startDistance="+departureStartDistance+"; maxFrameStep="+maximumStep+"; doorwayDistance="+lastDoorDistance);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_VISIT_PASS: actual physical phases and targets; resident="+person.Id+
                    "; purchase="+purchase+"; shelfStops="+browsingStops.Count+"; counterSeconds="+counterDuration.ToString("F2"));
            }
            finally
            {
                var savedStore=typeof(PeopleOfTheIliacBayMod).GetField("store",flags).GetValue(mod) as WorldStateStore;
                if(savedStore!=null) savedStore.RemoveObservedVisit(person.Id);
                // Restore durable source before stale Plan references are discarded.
                mod.RestoreSaveData(baseline);
                journeys.Remove(person.Id); physical.Remove(person.Id);
                typeof(StreetPopulation).GetField("shopVisitDay",flags).SetValue(streets,-1d);
            }
        }
        private static object FindPlan(StreetPopulation streets,string residentId,BindingFlags flags)
        {
            var plans=(IEnumerable)typeof(StreetPopulation).GetField("plans",flags).GetValue(streets);
            foreach(var plan in plans)
                if(((Resident)plan.GetType().GetField("Resident").GetValue(plan)).Id==residentId) return plan;
            return null;
        }
        private static void PersistFixtureRoutine(WorldStateStore store,int settlementId,string residentId,
            ResidentDestination destination,double departureSeconds)
        {
            TownRoutineState town=store.GetRoutines(settlementId) ?? new TownRoutineState {SettlementId=settlementId};
            town.ItineraryVersion=1;
            var routine=town.Residents.Find(item=>item.ResidentId==residentId);
            if(routine==null) { routine=new ResidentRoutine {ResidentId=residentId}; town.Residents.Add(routine); }
            routine.DestinationKey=-1; routine.OpenMinute=0; routine.CloseMinute=0;
            routine.Route=new List<SavedRoutePoint>(); routine.Itinerary=destination;
            routine.ItineraryDepartureSeconds=departureSeconds;
            store.SetRoutines(town);
        }
    }
}
