using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class RoadTravelActors : MonoBehaviour
    {
        private Func<WorldStateStore> source;
        private Func<bool> enabledForWorld;
        private WorldStateStore current;
        private float nextScan;
        private CityNavigation navigation;
        private StreetPopulation streets;
        private readonly Dictionary<string,ResidentActor> actors=new Dictionary<string,ResidentActor>();
        private readonly List<string> remove=new List<string>();
        private readonly List<string> transferred=new List<string>();
        public void Install(Func<WorldStateStore> store,Func<bool> enabled)
        {
            source=store; enabledForWorld=enabled;
            streets=GetComponent<StreetPopulation>();
            // Vanilla ClearMotor unconditionally clears its navigation flags.
            // An inactive zero-sized grid makes that cleanup safe outside towns.
            var owner=new GameObject("Road actor navigation lifetime");
            owner.SetActive(false); owner.transform.SetParent(transform,false);
            navigation=owner.AddComponent<CityNavigation>();
            PlayerEnterExit.OnTransitionDungeonInterior+=CompleteNativeDungeonEntry;
            PlayerEnterExit.OnTransitionDungeonExterior+=CompleteNativeDungeonExit;
            PlayerEnterExit.OnFailedTransition+=AbortNativeDungeonFollow;
        }
        public ResidentActor Find(string id) { ResidentActor actor; actors.TryGetValue(id,out actor); return actor?actor:FindDungeonCompanion(id); }
        public bool TryAdopt(ResidentActor actor,WorldStateStore store,double now)
        {
            if(!actor || !actor.Resident.IsAlive || source==null || !ReferenceEquals(source(),store) ||
                !enabledForWorld() || GameManager.Instance.IsPlayerInside || !store.IsResidentAway(actor.Resident.Id,now)) return false;
            var state=store.ResidentTripAt(actor.Resident.Id,now);
            if(state.Phase!=ResidentTripPhase.Outbound && state.Phase!=ResidentTripPhase.Returning) return false;
            Vector3 ground; Transform parent;
            if(!Wilderness(state.Position) || !Ground(state.Position,out ground,out parent) ||
                (ground-GameManager.Instance.PlayerMotor.transform.position).sqrMagnitude>165*165) return false;
            if(!ReferenceEquals(current,store)) { Clear(); current=store; }
            ResidentActor existing;
            if(actors.TryGetValue(actor.Resident.Id,out existing)) return existing==actor;
            actor.TransferOwner(parent,navigation);
            actors.Add(actor.Resident.Id,actor);
            actor.Place(ground,actor.transform.forward);
            return true;
        }
        public bool TryReturn(string id,WorldStateStore store,double now)
        {
            ResidentActor actor;
            if(!ReferenceEquals(current,store) || !actors.TryGetValue(id,out actor) || !streets ||
                !streets.TryReceiveRoadActor(actor,store,now)) return false;
            actors.Remove(id); return true;
        }
        public void Clear()
        {
            ClearRoadActors(); ClearPaidCompanions(); ClearDungeonFollowers();
        }
        private void ClearRoadActors()
        {
            foreach(var actor in actors.Values) if(actor) {actor.CaptureState(); actor.gameObject.SetActive(false); Destroy(actor.gameObject);}
            actors.Clear(); nextScan=0;
        }
        public void CaptureNpcInventories()
        {
            foreach(var actor in actors.Values) if(actor) actor.CaptureState();
            CapturePaidCompanionStates();
            CaptureDungeonFollowers();
        }
        private void OnDestroy() { PlayerEnterExit.OnTransitionDungeonInterior-=CompleteNativeDungeonEntry;
            PlayerEnterExit.OnTransitionDungeonExterior-=CompleteNativeDungeonExit;
            PlayerEnterExit.OnFailedTransition-=AbortNativeDungeonFollow;Clear(); }
        public static bool Ground(RoutePoint position,out Vector3 ground,out Transform terrainParent)
        {
            ground=Vector3.zero; terrainParent=null;
            var point=new RoadTerrainPoint(position);
            var terrainObject=GameManager.Instance.StreamingWorld.GetTerrainFromPixel(point.PixelX,point.PixelY);
            if(!terrainObject || !terrainObject.activeInHierarchy) return false;
            var terrain=terrainObject.GetComponent<Terrain>();
            if(!terrain || !terrain.terrainData) return false;
            var size=terrain.terrainData.size;
            ground=terrain.transform.TransformPoint(new Vector3((float)point.U*size.x,0,(float)point.V*size.z));
            ground.y=terrain.SampleHeight(ground)+terrain.transform.position.y;
            terrainParent=terrain.transform; return true;
        }
        public static bool Wilderness(RoutePoint position)
        {
            var point=new RoadTerrainPoint(position);
            var body=GameManager.Instance.StreamingWorld.GetTerrainFromPixel(point.PixelX,point.PixelY);
            var terrain=body ? body.GetComponent<DaggerfallTerrain>() : null;
            if(!terrain || !body.activeInHierarchy) return false;
            return !terrain.MapData.hasLocation || !terrain.MapData.locationRect.Contains(new Vector2(
                (float)point.U*DaggerfallConnect.Arena2.MapsFile.WorldMapTileDim,
                (float)point.V*DaggerfallConnect.Arena2.MapsFile.WorldMapTileDim));
        }
        private void Update()
        {
            if(source==null || !enabledForWorld())
            { if(actors.Count>0 || HasPaidCompanions || dungeonFollowers.Count>0) Clear(); return; }
            var store=source();
            if(!ReferenceEquals(current,store)) { Clear(); current=store; }
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            double now=(double)clock.ToSeconds()+clock.Second-Math.Floor(clock.Second);
            if(GameManager.IsGamePaused) return;
            UpdateDungeonFollowers(store,now);
            if(GameManager.Instance.StreamingWorld.IsInit)
            { CapturePaidForUnobservedReload(store,now); return; }
            if(GameManager.Instance.IsPlayerInside)
            { if(actors.Count>0) ClearRoadActors(); return; }
            Vector3 player=GameManager.Instance.PlayerMotor.transform.position;
            if(Time.unscaledTime>=nextScan)
            {
                nextScan=Time.unscaledTime+0.5f;
                foreach(var resident in store.RoadResidents(now))
                {
                    if(actors.ContainsKey(resident.Id) || actors.Count>=16) continue;
                    var state=store.ResidentTripAt(resident.Id,now);
                    Vector3 ground; Transform parent;
                    if(!Wilderness(state.Position) || !Ground(state.Position,out ground,out parent) || (ground-player).sqrMagnitude>140*140) continue;
                    var streetActor=streets ? streets.FindActor(resident.Id) : null;
                    if(streetActor)
                    {
                        if(TryAdopt(streetActor,store,now)) streets.ReleaseTransferredActor(streetActor);
                        continue;
                    }
                    var body=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,parent);
                    body.name="People of the Iliac Bay road "+resident.Id;
                    var actor=body.AddComponent<ResidentActor>();
                    actor.Initialize(resident,0,navigation,inventoryStore:store);
                    actor.Place(ground,Vector3.forward);
                    actors.Add(resident.Id,actor);
                }
            }
            remove.Clear(); transferred.Clear();
            foreach(var pair in actors)
            {
                var state=store.ResidentTripAt(pair.Key,now);
                if(pair.Value && (store.IsResidentOnHomeRoute(pair.Key,now) || state.Phase==ResidentTripPhase.Visiting) && streets && streets.TryReceiveRoadActor(pair.Value,store,now))
                { transferred.Add(pair.Key); continue; }
                Vector3 ground; Transform parent;
                if(!pair.Value || state.Phase!=ResidentTripPhase.Outbound && state.Phase!=ResidentTripPhase.Returning ||
                    !Wilderness(state.Position) || !Ground(state.Position,out ground,out parent) || (ground-player).sqrMagnitude>165*165)
                { remove.Add(pair.Key); continue; }
                var next=store.ResidentTripAt(pair.Key,now+0.1);
                Vector3 direction=new Vector3((float)(next.Position.X-state.Position.X),0,(float)(state.Position.Z-next.Position.Z));
                pair.Value.transform.SetParent(parent,true);
                pair.Value.Place(ground,direction.normalized);
            }
            foreach(string id in remove)
            {
                var actor=actors[id]; if(actor) {actor.CaptureState(); actor.gameObject.SetActive(false); Destroy(actor.gameObject);}
                actors.Remove(id);
            }
            foreach(string id in transferred) actors.Remove(id);
            UpdatePaidCompanions(store,now);
        }
    }
}
