using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private readonly Dictionary<string,Resident> destinationResidents=new Dictionary<string,Resident>();
        private float nextDestinationRefresh;
        private readonly Dictionary<string,double> destinationShopWindows=new Dictionary<string,double>();
        private readonly ResidentRoutine destinationFallbackRoutine=new ResidentRoutine();
        private void UpdateDestinationActors(double seconds)
        {
            if(interior) PrepareShopVisits(seconds);
            var sceneGuards=interior && ReactionsEnabled ? GetComponent<GuardPopulation>() : null;
            bool occupied=sceneGuards && sceneGuards.OccupiesCrimeScene(interior.EntryDoor.buildingKey,seconds);
            var visibleCorpses=interior && ReactionsEnabled ? worldStore.ObserveCorpses(population.SettlementId,interior.EntryDoor.buildingKey,seconds) : null;
            if(Time.unscaledTime>=nextDestinationRefresh)
            {
                nextDestinationRefresh=Time.unscaledTime+0.5f;
                var current=worldStore.DestinationResidents(population.SettlementId,seconds);
                var ids=new HashSet<string>();
                foreach(var resident in current) { ids.Add(resident.Id); destinationResidents[resident.Id]=resident; }
                foreach(var id in new List<string>(destinationResidents.Keys))
                {
                    if(ids.Contains(id) || Reaction(id,seconds)!=null) continue;
                    var old=FindActor(id);
                    if(CanContinueDestinationDeparture(old,seconds)) continue;
                    if(old && roadTravelActors && roadTravelActors.TryAdopt(old,worldStore,seconds)) actors.Remove(id);
                    else RemoveDestinationActor(id);
                    destinationResidents.Remove(id);
                }
            }
            foreach(var resident in destinationResidents.Values)
            {
                var actor=FindActor(resident.Id);
                ActivitySnapshot state;
                bool visiting=worldStore.TryDestinationResidentState(resident.Id,population.SettlementId,seconds,out state);
                var reaction=Reaction(resident.Id,seconds);
                if(reaction!=null || !resident.IsAlive)
                {worldStore.RemoveObservedVisit(resident.Id);RemovePhysicalShopVisit(resident.Id);}
                bool reacting=reaction!=null && reaction.HasPosition;
                int building=reacting ? reaction.BuildingKey : state.BuildingKey;
                bool inside=interior && building==interior.EntryDoor.buildingKey;
                bool outside=!GameManager.Instance.IsPlayerInside && building==-1;
                Vector3 ground=Vector3.zero;
                if(reacting)
                    ground=(inside ? interior.transform : directory.transform).TransformPoint(new Vector3((float)reaction.X,(float)reaction.Y,(float)reaction.Z));
                else if(outside) ground=geometry.ScenePoint(state.Position);
                else if(inside && indoorGeometry!=null)
                {
                    int slot;
                    if(!indoorSlots.TryGetValue(resident.Id,out slot))
                    {
                        slot=0; while(slot<indoorGeometry.LocalAnchors.Count && indoorSlots.ContainsValue(slot)) slot++;
                        if(slot==indoorGeometry.LocalAnchors.Count) continue;
                        indoorSlots.Add(resident.Id,slot);
                    }
                    ground=interior.transform.TransformPoint(indoorGeometry.LocalAnchors[slot]);
                }
                bool visible=resident.IsAlive && (visiting || reacting) && (inside || outside) &&
                    (ground-GameManager.Instance.PlayerMotor.transform.position).sqrMagnitude<=(actor ? 165*165 : 140*140);
                if(!reacting && actor && ContinueResidentLodgingDoorwayExit(actor,seconds))continue;
                if(!visible)
                {
                    if(!reacting && actor && visiting && ContinueDestinationOutdoorArrival(actor,state)) continue;
                    if(!reacting && ContinueDestinationDeparture(actor,seconds)) continue;
                    RemoveDestinationActor(resident.Id); continue;
                }
                if(!actor && outside && roadTravelActors && roadTravelActors.TryReturn(resident.Id,worldStore,seconds)) actor=FindActor(resident.Id);
                if(!actor)
                {
                    if(actors.Count>=80) continue;
                    NativeNpcStartingBelongingsSceneProbe.BeforeNaturalSpawn(this,resident,worldStore);
                    var body=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,inside ? interior.transform : directory.transform);
                    body.name="People of the Iliac Bay visitor "+resident.Id;
                    actor=body.AddComponent<ResidentActor>(); actor.Initialize(resident,0,navigation,inventoryStore:worldStore);
                    NativeNpcStartingBelongingsSceneProbe.AfterNaturalInitialize(actor,worldStore);
                    actor.SetRunningPilotAtlas(runningPilotAtlas); actors.Add(resident.Id,actor);
                    actor.transform.position=ground+Vector3.up;
                    if(inside && !reacting && visiting && indoorGeometry!=null && indoorGeometry.EntryGround.HasValue)
                    {
                        var arrival=worldStore.ResidentTripAt(resident.Id,seconds);
                        if(arrival.VisitPurpose==ResidentVisitPurpose.Shop || arrival.VisitPurpose==ResidentVisitPurpose.Social)
                            actor.transform.position=indoorGeometry.EntryGround.Value+Vector3.up;
                    }
                }
                if(inside && !reacting && ReactionsEnabled && !occupied)
                {
                    var corpse=visibleCorpses.Find(a=>a.ThreatId==null && a.BuildingKey==interior.EntryDoor.buildingKey);
                    if(corpse!=null)
                    {
                        var observer=worldStore.NoticeInteriorCorpse(corpse.Id,resident.Id,interior.EntryDoor.buildingKey,seconds);
                        if(observer!=null && CivilianAlarms.PersonActive(corpse,observer,seconds))
                        {
                            if(!observer.HasPosition)
                            {
                                var local=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
                                observer.HasPosition=true; observer.BuildingKey=interior.EntryDoor.buildingKey;
                                observer.X=local.x; observer.Y=local.y; observer.Z=local.z;
                            }
                            reaction=observer; reacting=true;
                            PrepareIndoorEscapes(seconds);
                        }
                    }
                }
                if(reacting && !reaction.ReachedRefuge)
                {
                    if(!WakeResidentForAlarm(actor,reaction)) continue;
                    var parent=inside ? interior.transform : directory.transform;
                    if(reaction.Escape!=null) PlaceEscape(actor,reaction,seconds,parent,!inside);
                    else CivilianResponses.Move(actor,reaction,destinationFallbackRoutine,parent,geometry,inside ? indoorGeometry : null,allowHome:false);
                    continue;
                }
                ActivitySnapshot next;
                if(inside && !reacting && visiting)
                {
                    var visit=worldStore.ResidentTripAt(resident.Id,seconds);
                    if(visit.VisitPhase==ResidentVisitPhase.ExitingService)
                    {
                        RemovePhysicalShopVisit(resident.Id);
                        worldStore.RemoveObservedVisit(resident.Id);
                        var point=interior.transform.TransformPoint(new Vector3((float)visit.Position.X,(float)visit.InteriorY,(float)visit.Position.Z));
                        var following=worldStore.ResidentTripAt(resident.Id,seconds+0.1);
                        var facing=following.VisitPhase==ResidentVisitPhase.ExitingService ? interior.transform.TransformVector(
                            new Vector3((float)(following.Position.X-visit.Position.X),(float)(following.InteriorY-visit.InteriorY),(float)(following.Position.Z-visit.Position.Z))) : actor.transform.forward;
                        InteriorGeometry.OpenOrdinaryDoors(actor.transform.position-Vector3.up,point);
                        actor.Place(point,facing); continue;
                    }
                    bool refusedInn=visit.VisitPurpose==ResidentVisitPurpose.Inn &&
                        worldStore.ResidentInnAdmission(resident.Id,seconds)==TravelerAdmission.Refused;
                    if(occupied || refusedInn)
                    {
                        var route=indoorGeometry.RouteToExit(actor.transform.position-Vector3.up);
                        if(route.Count>=2)
                        {
                            try
                            {
                                worldStore.SaveResidentServiceExit(resident.Id,new ResidentServiceExit {VisitId=visit.VisitId,SettlementId=visit.SettlementId,BuildingKey=visit.BuildingKey,
                                    VisitEnterAt=visit.VisitEnterAt,VisitLeaveAt=visit.VisitLeaveAt,StartedAt=seconds,
                                    SecondsPerMetre=DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed,Route=route});
                                continue;
                            }
                            catch(System.ArgumentException) { /* Unsupported exit geometry retains the existing schedule. */ }
                        }
                    }
                }
                if(inside && !reacting && visiting && !occupied && PlaceResidentReception(actor,seconds)) continue;
                if(inside && !reacting && visiting && !occupied &&
                    worldStore.ResidentTripAt(resident.Id,seconds).VisitPurpose==ResidentVisitPurpose.Inn && PlaceResidentInn(actor,seconds)) continue;
                if(inside && !reacting && visiting && shopGeometry!=null)
                {
                    var visit=worldStore.ResidentTripAt(resident.Id,seconds);
                    if(visit.VisitPurpose==ResidentVisitPurpose.Shop || shopGeometry.IsTavern && visit.VisitPurpose==ResidentVisitPurpose.Social)
                    {
                        double window;
                        if(!shopJourneys.ContainsKey(resident.Id) || !destinationShopWindows.TryGetValue(resident.Id,out window) || window!=visit.VisitEnterAt)
                        {
                            destinationShopWindows[resident.Id]=visit.VisitEnterAt;
                            shopJourneys[resident.Id]=shopGeometry.Create(resident.Id,resident.AppearanceSeed,visit.VisitEnterAt,visit.VisitLeaveAt);
                        }
                        if(PlaceShopVisit(actor,seconds)) continue;
                        ShopVisitJourney journey;
                        if(shopGeometry.IsTavern && visit.VisitPurpose==ResidentVisitPurpose.Social &&
                            shopJourneys.TryGetValue(resident.Id,out journey) && journey==null)
                        {
                            var route=indoorGeometry.RouteToExit(actor.transform.position-Vector3.up);
                            if(route.Count<2)
                            {
                                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_VISITOR_EXIT_ROUTE_MISSING: "+resident.Id+
                                    "; building="+visit.BuildingKey);
                                continue;
                            }
                            try
                            {
                                worldStore.SaveResidentServiceExit(resident.Id,new ResidentServiceExit {VisitId=visit.VisitId,
                                    SettlementId=visit.SettlementId,BuildingKey=visit.BuildingKey,VisitEnterAt=visit.VisitEnterAt,
                                    VisitLeaveAt=visit.VisitLeaveAt,StartedAt=seconds,
                                    SecondsPerMetre=DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed,Route=route});
                                continue;
                            }
                            catch(System.ArgumentException)
                            {
                                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_VISITOR_EXIT_REJECTED: "+resident.Id+
                                    "; building="+visit.BuildingKey);
                                continue;
                            }
                        }
                    }
                }
                Vector3 direction=outside && worldStore.TryDestinationResidentState(resident.Id,population.SettlementId,seconds+0.1,out next) && next.BuildingKey==-1 ?
                    geometry.ScenePoint(next.Position)-ground : Vector3.zero;
                Vector3? destination=!reacting && outside && state.TravelDestination.HasValue ?
                    (Vector3?)geometry.ScenePoint(state.TravelDestination.Value) : null;
                actor.Place(ground,direction,inside,freeStanding:inside,navigationDestination:destination);
            }
        }
        private bool CanContinueDestinationDeparture(ResidentActor actor,double seconds)
        {
            if(!actor || !actor.Resident.IsAlive || !interior || indoorGeometry==null ||
                Reaction(actor.Resident.Id,seconds)!=null) return false;
            if(ResidentInnExitPending(actor,seconds)) return true;
            if(actor.transform.parent!=interior.transform) return false;
            ShopVisitJourney journey;
            return shopJourneys.TryGetValue(actor.Resident.Id,out journey) && journey!=null;
        }
        private bool ContinueDestinationDeparture(ResidentActor actor,double seconds)
        {
            if(ContinueResidentInnExit(actor,seconds)) return true;
            PhysicalShopVisit unfinished;
            if(CanContinueDestinationDeparture(actor,seconds) && physicalShopVisits.TryGetValue(actor.Resident.Id,out unfinished) &&
                unfinished.Snapshot.Phase!=ShopVisitPhase.Complete && PlaceShopVisit(actor,seconds)) return true;
            return CanContinueDestinationDeparture(actor,seconds) && ContinueShopDeparture(actor,seconds);
        }
        private void RemoveDestinationActor(string id)
        {
            RemovePhysicalShopVisit(id);
            var actor=FindActor(id);
            if(actor) { actor.CaptureState(); actor.gameObject.SetActive(false); Destroy(actor.gameObject); }
            actors.Remove(id); indoorSlots.Remove(id);
            residentRooms.Remove(id);
            residentReceptions.Remove(id);
        }
    }
}
