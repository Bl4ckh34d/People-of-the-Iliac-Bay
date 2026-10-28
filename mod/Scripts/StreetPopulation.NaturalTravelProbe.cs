using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Acceptance of existing SAVE9 state only. No person, trip, visit, bed
        // booking or itinerary is created or replaced by these observers.
        internal IEnumerator VerifyNaturalTravel(string mode)
        {
            var game=GameManager.Instance;
            NaturalNeed(game && NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) && !game.IsPlayerInside,
                "Natural travel acceptance requires the owned outdoor checkpoint.");
            float ready=Time.realtimeSinceStartup+30;
            while((!Ready || !EntranceSurveyReady || geometry==null) && Time.realtimeSinceStartup<ready)yield return null;
            NaturalNeed(Ready && EntranceSurveyReady && geometry!=null && worldStore!=null && directory &&
                (mode=="shelter-census" || directory.MapID==1291010263),
                "Natural travel checkpoint geometry did not become ready within30seconds.");
            bool motor=game.PlayerMotor.enabled;game.PlayerMotor.enabled=false;
            try
            {
                if(mode=="fallback")yield return ObserveNaturalFallback();
                else if(mode=="checkout")yield return ObserveNaturalCheckout();
                else if(mode=="home")yield return ObserveNaturalHome();
                else if(mode=="shelter-census")yield return ObserveNaturalShelterCensus();
                else if(mode=="controlled-guild" || mode=="controlled-camp")yield return ObserveControlledShelter(mode=="controlled-camp");
                else throw new InvalidOperationException("Unknown natural travel observation.");
            }
            finally{game.PlayerMotor.enabled=motor;}
        }

        private IEnumerator ObserveNaturalFallback()
        {
            const string id="s:1276329198:h:262:r:0",visitId="visit:1291010263:262912:145805:1";
            var visit=NaturalVisit(visitId,id);var person=worldStore.Traveler(id);
            NaturalNeed(person!=null && person.IsAlive && worldStore.ActiveNpcAction(id)==null &&
                (visit.Admission==TravelerAdmission.Refused || visit.Diversion!=null),"Existing Alison visit is not an ordinary refused/fallback stay.");
            var original=visit.Diversion==null?visit.Route:visit.Diversion.OriginalRoute;
            var route=new List<RoutePoint>();foreach(var point in original)route.Add(new RoutePoint(point.X,point.Z));
            var intended=TravelerJourney.Create(person.AppearanceSeed,visit.DesiredArrival,visit.Nights,
                visit.Diversion==null?visit.CommuteSeconds:visit.Diversion.OriginalCommuteSeconds,visit.Walled,route);
            NaturalNeed(ActionNow()<intended.CheckOutAt && worldStore.AvailableLodgingBeds(1291010263,262912,1,ActionNow(),intended.CheckOutAt)==null,
                "Actual refused Inn no longer requires a lodging alternative for this stay.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATURAL_FALLBACK_SOURCE_FULL: person="+id+"; visit="+visitId+
                "; sourceInn=262912; originalCheckout="+intended.CheckOutAt+"; availableOffer=false; productionBookingUnchanged=true");
            NaturalObserveAt(geometry.ScenePoint(new RoutePoint(original[original.Count-1].X,original[original.Count-1].Z)));
            float until=Time.realtimeSinceStartup+30;
            while(visit.Diversion==null && Time.realtimeSinceStartup<until)
            {yield return null;visit=NaturalVisit(visitId,id);}
            NaturalNeed(visit.Diversion!=null && !visit.Diversion.Camping && visit.Diversion.PreferredTavernKey==262912 &&
                visit.TavernKey!=262912 && visit.Admission==TravelerAdmission.Admitted && worldStore.LodgingGuildFaction(1291010263,visit.TavernKey)==0,
                "Ordinary production did not choose an actual alternative Inn within30seconds; no Guild/Camp result is inferred.");
            var journey=worldStore.TravelerJourneyFor(visitId);float observed=0;bool observedDoor=false;ResidentActor tracked=null;Vector3 previous=Vector3.zero;
            until=Time.realtimeSinceStartup+180;
            while(Time.realtimeSinceStartup<until)
            {
                var actor=FindActor(id);
                if(actor && actor.gameObject.activeInHierarchy)
                {
                    var ground=actor.transform.position-Vector3.up;
                    if(tracked==actor)observed+=NaturalStep(previous,ground);
                    tracked=actor;previous=ground;NaturalObserveAt(ground);
                    var last=visit.Route[visit.Route.Count-1];var door=geometry.ScenePoint(new RoutePoint(last.X,last.Z));
                    if(Vector3.Distance(ground,door)<=0.6f)observedDoor=true;
                    if(ActionNow()>=journey.CheckInAt && observedDoor)break;
                }
                else
                {
                    var expected=journey.Evaluate(ActionNow()).Position;
                    NaturalObserveAt(geometry.ScenePoint(expected));tracked=null;
                    // A completed unobserved exterior leg can only establish
                    // admission. Its physical walking distance remains zero.
                    if(ActionNow()>=journey.CheckInAt)break;
                }
                yield return null;
            }
            NaturalNeed(ActionNow()>=journey.CheckInAt && worldStore.TravelerBed(visitId,ActionNow())!=null,
                "Existing fallback did not obtain its actual admitted bed before the observation deadline.");
            int inn=visit.TavernKey;yield return NaturalEnter(inn);
            var body=FindActor(id);float bodyReady=Time.realtimeSinceStartup+30;
            while(!body && Time.realtimeSinceStartup<bodyReady){yield return null;body=FindActor(id);}
            NaturalNeed(body && body.Person.NameNPC==person.Name,"Actual alternative Inn contains no living named fallback guest.");
            string bed=worldStore.TravelerBed(visitId,ActionNow());
            var anchor=BedGeometry.Read(interior).Find(value=>"bed:"+value.RecordIndex==bed);Vector3 standing=Vector3.zero;
            NaturalNeed(anchor!=null && indoorGeometry.StandBesideBed(anchor,out standing),"Actual fallback bed has no reachable standing floor.");
            // Native entry leaves the observer in the doorway. Move only the
            // observer to supported clear floor near the actual assigned bed,
            // so its capsule does not prevent the guest's ordinary room walk.
            NaturalObserveAt(standing);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATURAL_FALLBACK_ROOM_OBSERVER: actualBed="+bed+
                "; supportedClearFloor=true; npcMovedByProbe=false");
            float roomMetres=0;previous=body.transform.position-Vector3.up;until=Time.realtimeSinceStartup+60;
            while(Vector3.Distance(body.transform.position-Vector3.up,standing)>0.55f && Time.realtimeSinceStartup<until)
            {
                yield return null;NaturalNeed(body && FindActor(id)==body,"Fallback room body vanished before reaching its actual bed.");
                var current=body.transform.position-Vector3.up;roomMetres+=NaturalStep(previous,current);previous=current;
            }
            NaturalNeed(Vector3.Distance(body.transform.position-Vector3.up,standing)<=0.55f &&
                worldStore.TravelerBed(visitId,ActionNow())==bed,"Existing fallback did not reach its exact allocated bed floor.");
            NaturalNeed(observedDoor && observed>1 || roomMetres>1,
                "Fallback admission/bed is real, but no physical arrival leg was observed; reconstructed presence is not walking evidence.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATURAL_FALLBACK_PASS: person="+id+"; visit="+visitId+"; refusedInn=262912; actualInn="+inn+
                "; bed="+bed+"; observedExteriorMetres="+observed+"; observedDoor="+observedDoor+"; observedRoomMetres="+roomMetres+
                "; existingVisit=true; newBookingsByProbe=false; guildOrCamp=false; unobservedExteriorWalkClaimed=false");
        }

        private IEnumerator ObserveNaturalCheckout()
        {
            const string id="s:1356029978:h:65792:r:0",visitId="visit:1291010263:66318:145804:0";
            var visit=NaturalVisit(visitId,id);var journey=worldStore.TravelerJourneyFor(visitId);
            NaturalNeed(visit.Admission==TravelerAdmission.Admitted && visit.TavernKey==66318 && worldStore.ActiveNpcAction(id)==null &&
                !FindActor(id),"Natural checkout needs the existing admitted, currently unobserved Ludovic guest.");
            double measuredDuration=0;
            yield return NaturalEnter(66318,loaded=>{
                // Native entry is synchronous. Measure the actual existing bed's
                // exit route before yielding a frame that could create its body.
                NaturalNeed(!FindActor(id),"Checkout preparation refuses to advance an already observed guest.");
                string reserved=worldStore.TravelerBed(visitId,journey.CheckOutAt-0.01);
                var anchor=BedGeometry.Read(loaded).Find(value=>"bed:"+value.RecordIndex==reserved);
                var measured=new InteriorGeometry(loaded,CrowdMotion.BodyRadius);Vector3 standing=Vector3.zero;
                NaturalNeed(anchor!=null && measured.StandBesideBed(anchor,out standing),"Existing checkout bed has no actual supported standing floor.");
                var route=measured.RouteToExit(standing);
                NaturalNeed(route.Count>=2,"Existing checkout bed has no actual route to its native exit.");
                double scale=DaggerfallUnity.Instance.WorldTime.TimeScale;
                double pace=ResidentMovement.WalkSpeedFor(worldStore.Traveler(id).AppearanceSeed);
                measuredDuration=new InteriorRoomFollower(route,scale/pace).Duration;
                double target=journey.CheckOutAt-measuredDuration-5*scale;
                NaturalSkipUnobserved(target,id,"checkout-five-real-seconds-before-measured-return");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATURAL_CHECKOUT_PREPARED: person="+id+"; actualReturnDuration="+measuredDuration+
                    "; scale="+scale+"; leaveAt="+(journey.CheckOutAt-measuredDuration)+"; now="+ActionNow()+
                    "; npcObserved=false; actualBedRoute=true; probeBookings=false");
            });
            float until=Time.realtimeSinceStartup+30;TravelerPlan plan=null;ResidentActor actor=null;
            while(Time.realtimeSinceStartup<until)
            {
                travelerPlans.TryGetValue(id,out plan);actor=FindActor(id);
                if(actor && plan!=null && plan.RoomFollower!=null && plan.RoomFollower.Arrived && !plan.RoomFollower.Leaving)break;
                yield return null;
            }
            NaturalNeed(actor && plan!=null && plan.RoomFollower!=null && plan.RoomFollower.Arrived && !plan.RoomFollower.Leaving && indoorGeometry.EntryGround.HasValue,
                "Existing guest was not observed at its actual room before natural checkout within30seconds.");
            NaturalNeed(Math.Abs(plan.RoomFollower.Duration-measuredDuration)<0.01,
                "Actual ordinary room route differs from the measured unobserved checkout preparation.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATURAL_CHECKOUT_ROOM_READY: now="+ActionNow()+"; leaveAt="+
                (journey.CheckOutAt-plan.RoomFollower.Duration)+"; checkout="+journey.CheckOutAt+"; actualReturnDuration="+plan.RoomFollower.Duration);
            string bed=worldStore.TravelerBed(visitId,ActionNow());NaturalNeed(bed!=null,"Actual guest lacks its current booking before checkout.");
            var entry=indoorGeometry.EntryGround.Value;var previous=actor.transform.position-Vector3.up;float walked=0;bool reached=false;
            until=Time.realtimeSinceStartup+(float)(measuredDuration/DaggerfallUnity.Instance.WorldTime.TimeScale)+25;
            bool leavingLogged=false;
            while(Time.realtimeSinceStartup<until)
            {
                yield return null;
                if(!actor || FindActor(id)!=actor)break;
                var current=actor.transform.position-Vector3.up;
                walked+=NaturalStep(previous,current);previous=current;
                if(plan.RoomFollower.Leaving && !leavingLogged)
                {leavingLogged=true;Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATURAL_CHECKOUT_RETURN_STARTED: now="+ActionNow()+"; checkout="+journey.CheckOutAt+"; measuredMetres="+walked);}
                if(plan.RoomFollower.Leaving && Vector3.Distance(current,entry)<=0.6f){reached=true;break;}
            }
            NaturalNeed(reached && walked>1,"Existing checkout guest did not physically walk from its room to the native exit.");
            until=Time.realtimeSinceStartup+30;
            while(ActionNow()<journey.CheckOutAt && Time.realtimeSinceStartup<until)yield return null;
            NaturalNeed(ActionNow()>=journey.CheckOutAt && worldStore.TravelerBed(visitId,ActionNow())==null,
                "Actual checkout did not release the guest's bed at its original deadline.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATURAL_CHECKOUT_PASS: person="+id+"; visit="+visitId+"; inn=66318; originalBed="+bed+
                "; walkedMetres="+walked+"; actualDoorReached=true; bookingExpired=true; originalCheckout="+journey.CheckOutAt+
                "; regionalReturnObserved=false; itineraryChanged=false");
        }

        private IEnumerator ObserveNaturalHome()
        {
            const string id="s:1291010263:h:198154:r:0";
            var trip=worldStore.GetResidentDialogueTrip(id);var resident=worldStore.ResolveResident(id);
            NaturalNeed(trip!=null && resident!=null && resident.IsAlive && trip.HomeSettlement==1291010263 && resident.HomeKey==198154 &&
                trip.DestinationSettlement==1112742339 && trip.HomeRoute.Count>=2 && worldStore.ActiveNpcAction(id)==null && !FindActor(id),
                "Existing Basil journey is unavailable or its body is already observed; no replacement trip is created.");
            var first=trip.HomeRoute[0];var savedHome=geometry.ScenePoint(new RoutePoint(first.X,first.Z));
            var home=NaturalHomeDoor(198154,savedHome);
            NaturalSkipUnobserved(trip.HomeEntryAt-2,id,"regional-time-before-home-connector");
            var join=trip.HomeRoute[trip.HomeRoute.Count-1];NaturalObserveAt(geometry.ScenePoint(new RoutePoint(join.X,join.Z)));
            yield return null;
            float until=Time.realtimeSinceStartup+30;ResidentActor actor=null;
            while(Time.realtimeSinceStartup<until)
            {actor=FindActor(id);if(!actor && roadTravelActors)actor=roadTravelActors.Find(id);if(actor && actor.gameObject.activeInHierarchy)break;yield return null;}
            NaturalNeed(actor && worldStore.IsResidentAway(id,ActionNow()),"Existing returning body did not appear before actual home arrival within30seconds.");
            var previous=actor.transform.position-Vector3.up;float walked=0;bool reached=false;
            double remaining=NaturalRemainingHomeMetres(trip.HomeRoute,directory.transform.InverseTransformPoint(previous));
            double walkingBudget=remaining/actor.WalkingSpeed+20;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATURAL_HOME_WALK_BUDGET: person="+id+"; remainingMetres="+remaining+
                "; personalMetresPerSecond="+actor.WalkingSpeed+"; seconds="+walkingBudget+"; reserveSeconds=20; readinessLimit=30; routeChanged=false");
            until=Time.realtimeSinceStartup+(float)walkingBudget;
            while(Time.realtimeSinceStartup<until)
            {
                yield return null;NaturalNeed(actor && actor.gameObject.activeInHierarchy,"Observed natural home body vanished before its real home doorway.");
                var current=actor.transform.position-Vector3.up;walked+=NaturalStep(previous,current);previous=current;NaturalObserveAt(current);
                if(Vector3.Distance(current,home)<=0.6f){reached=true;break;}
            }
            NaturalNeed(reached && walked>1,"Existing natural resident did not physically complete its real home connector.");
            until=Time.realtimeSinceStartup+30;
            while(worldStore.IsResidentAway(id,ActionNow()) && Time.realtimeSinceStartup<until)yield return null;
            var retained=worldStore.GetResidentDialogueTrip(id);
            NaturalNeed(!worldStore.IsResidentAway(id,ActionNow()) && retained!=null && retained.DepartsAt==trip.DepartsAt && retained.HomeAt==trip.HomeAt &&
                worldStore.ResolveResident(id).HomeKey==198154,"Actual home arrival changed its itinerary or failed to release the original household absence.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATURAL_HOME_PASS: person="+id+"; home=198154; walkedMetres="+walked+
                "; actualHomeDoorReached=true; currentNativeDoorMatched=true; originalHomeAt="+trip.HomeAt+"; regionalTravelObserved=false; itineraryChanged=false; newTrip=false");
        }

        private static double NaturalRemainingHomeMetres(IList<SavedRoutePoint> route,Vector3 local)
        {
            double prefix=0,remaining=0,nearest=double.MaxValue;
            for(int index=1;index<route.Count;index++)
            {
                var from=route[index-1];var to=route[index];double dx=to.X-from.X,dz=to.Z-from.Z,length=Math.Sqrt(dx*dx+dz*dz);
                if(length==0)continue;
                double fraction=Math.Max(0,Math.Min(1,((local.x-from.X)*dx+(local.z-from.Z)*dz)/(length*length)));
                double x=local.x-from.X-fraction*dx,z=local.z-from.Z-fraction*dz,distance=x*x+z*z;
                double candidate=prefix+fraction*length+Math.Sqrt(distance);
                if(distance<nearest-0.000001 || Math.Abs(distance-nearest)<=0.000001 && candidate>remaining)
                {nearest=distance;remaining=candidate;}
                prefix+=length;
            }
            NaturalNeed(nearest<double.MaxValue,"Actual existing home return route has no measurable remaining segment.");
            return remaining;
        }

        private Vector3 NaturalHomeDoor(int building,Vector3 savedHome)
        {
            float nearest=float.MaxValue;Vector3 actual=Vector3.zero;
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())foreach(var raw in owner.Doors??new StaticDoor[0])
            {
                if(raw.doorType!=DoorTypes.Building || raw.buildingKey!=building)continue;
                var door=raw;door.ownerPosition=owner.transform.position;door.ownerRotation=owner.transform.rotation;
                var expected=FollowExteriorDoorGround(door);RaycastHit hit;
                if(!Physics.Raycast(expected+Vector3.up*0.6f,Vector3.down,out hit,1.2f,~0,QueryTriggerInteraction.Ignore) ||
                    !hit.collider || hit.normal.y<0.7f || NativeNavigationGeometry.IsDynamicNavigationBody(hit.collider))continue;
                float distance=Vector3.Distance(savedHome,hit.point);
                if(distance<nearest){nearest=distance;actual=hit.point;}
            }
            NaturalNeed(nearest<=0.6f,"Existing HomeRoute endpoint does not match an actual supported current native home door: "+building+"; distance="+nearest);
            return actual;
        }

        private TravelerVisit NaturalVisit(string visitId,string personId)
        {
            var value=worldStore.TravelerVisitsFor(1291010263).Find(visit=>visit.Id==visitId && visit.PersonId==personId);
            NaturalNeed(value!=null,"Actual prepared SAVE9 visit is unavailable: "+visitId);return value;
        }
        private IEnumerator NaturalEnter(int building,Action<DaggerfallInterior> beforeObservation=null)
        {
            DaggerfallStaticDoors owner=null;StaticDoor entry=default(StaticDoor);
            foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())foreach(var door in candidate.Doors??new StaticDoor[0])
                if(door.doorType==DoorTypes.Building && door.buildingKey==building){owner=candidate;entry=door;break;}
            NaturalNeed(owner,"Actual saved lodging entrance is not loaded: "+building);
            TownSmokeHarness.EnterInterior(owner.transform,entry);
            if(beforeObservation!=null)
            {
                var loaded=GameManager.Instance.PlayerEnterExit.Interior;
                NaturalNeed(loaded && loaded.EntryDoor.buildingKey==building,"Native checkout entry did not synchronously load its actual Inn.");
                Physics.SyncTransforms();beforeObservation(loaded);
            }
            yield return null;DaggerfallUI.Instance.PopToHUD();GameManager.Instance.PauseGame(false);
            float until=Time.realtimeSinceStartup+30;
            while((!interior || interior.EntryDoor.buildingKey!=building || indoorGeometry==null) && Time.realtimeSinceStartup<until)yield return null;
            NaturalNeed(interior && interior.EntryDoor.buildingKey==building && indoorGeometry!=null,"Actual saved lodging interior was not ready within30seconds.");
        }
        private void NaturalSkipUnobserved(double target,string id,string reason)
        {
            NaturalNeed(!FindActor(id) && (!roadTravelActors || !roadTravelActors.Find(id)) && target>ActionNow(),
                "Natural observation refuses to skip an observed body or rewind its existing itinerary.");
            double before=ActionNow();DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(target-before));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATURAL_TRAVEL_SETUP: person="+id+"; from="+before+"; until="+ActionNow()+
                "; reason="+reason+"; skippedIntervalObserved=false; npcMovedByProbe=false");
        }
        private static float NaturalStep(Vector3 previous,Vector3 current)
        {float step=Vector3.Distance(previous,current);NaturalNeed(step<=0.55f,"Observed natural travel body snapped: "+step);return step;}
        private static void NaturalObserveAt(Vector3 target)
        {
            var game=GameManager.Instance;if(Vector3.Distance(game.PlayerMotor.transform.position-Vector3.up,target)<6)return;
            foreach(var direction in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
            {
                RaycastHit hit;var point=target+direction*3;
                if(!Physics.Raycast(point+Vector3.up*0.6f,Vector3.down,out hit,1.2f,~0,QueryTriggerInteraction.Ignore) ||
                    !hit.collider || hit.normal.y<0.7f || NativeNavigationGeometry.IsDynamicNavigationBody(hit.collider) ||
                    Physics.CheckCapsule(hit.point+Vector3.up*0.78f,hit.point+Vector3.up*1.6f,CrowdMotion.BodyRadius,~0,QueryTriggerInteraction.Ignore))continue;
                game.PlayerMotor.transform.position=hit.point+Vector3.up;Physics.SyncTransforms();return;
            }
            throw new InvalidOperationException("Natural observer has no actual clear nearby floor; NPC placement is unchanged.");
        }
        private static void NaturalNeed(bool value,string reason)
        {if(!value)throw new InvalidOperationException(reason);}
    }
}
