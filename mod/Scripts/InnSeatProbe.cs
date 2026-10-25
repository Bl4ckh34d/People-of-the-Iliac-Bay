using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class InnSeatProbe
    {
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,DaggerfallInterior room,InteriorGeometry geometry,ShopVisitGeometry activities,InnActivityLayout layout)
        {
            VerifyPreferredOutdoorSeatOrdering();
            VerifyCombinedMesh();
            VerifyOversizedClearanceFailsClosed();
            VerifyMetadataAndSceneShift();
            var candidates=new List<FurnitureSeatSlot>();
            foreach(var spot in layout.Spots)if(spot.Slot!=null)candidates.Add(spot.Slot);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SEAT_SURVEY: furniture="+layout.Seating.Furniture.Count+"; slots="+layout.Seating.Slots.Count+"; public="+candidates.Count);
            if(candidates.Count==0)throw new InvalidOperationException("No real public seat slot with a safe approach was discovered.");
            bool hostEnabled=host.enabled,streetEnabled=streets.enabled;host.enabled=false;streets.enabled=false;
            var hidden=new List<GameObject>();var actors=new List<ResidentActor>();var claims=new List<SelectedSeatAccess>();
            foreach(var existing in UnityEngine.Object.FindObjectsOfType<ResidentActor>())if(existing.gameObject.activeSelf){hidden.Add(existing.gameObject);existing.gameObject.SetActive(false);}
            // Keep authored patrons from obscuring the eight visual checks.
            // This fixture never saves and restores every hidden object below.
            foreach(var existing in room.GetComponentsInChildren<StaticNPC>())if(existing.gameObject.activeSelf){hidden.Add(existing.gameObject);existing.gameObject.SetActive(false);}
            try
            {
                yield return new WaitForSecondsRealtime(2.1f);
                foreach(var slot in candidates)
                {
                    if(actors.Count>=3)break;
                    if(!FurnitureSeatCatalog.Blocks(slot.Approach,slot.Ground,CrowdMotion.BodyRadius))throw new InvalidOperationException("Ordinary walking is allowed through selected furniture.");
                    var body=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,room.transform);
                    var actor=body.AddComponent<ResidentActor>();actors.Add(actor);
                    actor.Initialize(new Resident {Id="seat-probe-"+actors.Count,FullName="Seat Patron",AppearanceSeed=(uint)(52+actors.Count),Sex=actors.Count==2?ResidentSex.Female:ResidentSex.Male},1,GameManager.Instance.StreamingWorld.GetCurrentCityNavigation());
                    actor.Place(slot.Approach,slot.Facing,true);
                    if(Vector3.Distance(actor.transform.position-Vector3.up,slot.Approach)>0.2f)throw new InvalidOperationException("Seat apron could not place patron safely.");
                    if(actors.Count==1)yield return StandingPoseProbe.Run(actor);
                    SelectedSeatAccess access,duplicate;
                    if(!layout.Seating.TryAcquire(actor.Resident.Id,slot.Id,out access))throw new InvalidOperationException("Seat claim unavailable.");
                    claims.Add(access);
                    if(layout.Seating.TryAcquire("duplicate-seat-owner",slot.Id,out duplicate))throw new InvalidOperationException("One slot admitted two owners.");
                    float deadline=Time.realtimeSinceStartup+8;bool arrived=false;
                    while(Time.realtimeSinceStartup<deadline)
                    {
                        var before=actor.transform.position;arrived=access.Advance(actor);
                        if(Vector3.Distance(before,actor.transform.position)>0.51f)throw new InvalidOperationException("Seat approach teleported.");
                        foreach(var active in claims)active.Refresh();
                        if(arrived)break;yield return null;
                    }
                    if(!arrived||!slot.Furniture.Contains(actor.transform.position-Vector3.up,0.02f))throw new InvalidOperationException("Patron did not enter the real seat footprint.");
                    if(Vector3.Dot(actor.transform.forward,slot.Facing)<0.95f)throw new InvalidOperationException("Seated patron does not face its activity.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SEAT_OCCUPIED: id="+slot.Id+"; ground="+slot.Ground.ToString("F3")+"; actual="+(actor.transform.position-Vector3.up).ToString("F3"));
                    if(!actor.IsSeated)throw new InvalidOperationException("Supported native patron reached a chair but still uses its standing sprite.");
                }
                foreach(var access in claims)access.Refresh();
                if(SittingArt.Current==null||SittingArt.Current.ForActor(actors[0])==null)
                    throw new InvalidOperationException("Native sitting acceptance requires a loaded atlas.");
                yield return SittingPoseProbe.Run(actors[0],claims[0],()=>{foreach(var claim in claims)claim.Refresh();});
                var center=Vector3.zero;foreach(var actor in actors)center+=actor.transform.position;center/=actors.Count;
                SleepProbe.AimCamera(center);yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,"People-exact-seats.png"));
                for(int i=0;i<actors.Count;i++)
                {
                    float deadline=Time.realtimeSinceStartup+8;bool left=false;
                    while(Time.realtimeSinceStartup<deadline){foreach(var active in claims)active.Refresh();left=claims[i].Advance(actors[i],true);if(left)break;yield return null;}
                    if(!left)throw new InvalidOperationException("Seat departure did not reach the public apron.");
                    var departed=actors[i].transform.position-Vector3.up;
                    if(FurnitureSeatCatalog.Blocks(departed,departed,CrowdMotion.BodyRadius)||geometry.RouteToExit(departed).Count<2)
                        throw new InvalidOperationException("Seat departure released an actor before ordinary pathfinding could resume: body="+departed.ToString("F3")+"; approach="+claims[i].Slot.Approach.ToString("F3"));
                    var standing=actors[i].GetComponent<StandingPose>();
                    bool restored=standing&&standing.Active||actors[i].Person.Asset.GetComponent<MeshRenderer>().enabled;
                    if(actors[i].IsSeated||!restored)throw new InvalidOperationException("Seat departure did not restore a standing or walking renderer.");
                    claims[i].Release();
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_SEATS_PASS: count="+actors.Count+"; exact furniture interior; continuous approach/departure; exclusive claims; activity facing; combined wall preserved.");
            }
            finally
            {
                foreach(var access in claims)access.Release();
                foreach(var actor in actors)if(actor){actor.gameObject.SetActive(false);UnityEngine.Object.Destroy(actor.gameObject);}
                foreach(var body in hidden)if(body)body.SetActive(true);host.enabled=hostEnabled;streets.enabled=streetEnabled;
            }
        }
        private static void VerifyCombinedMesh()
        {
            var go=new GameObject("Seat combined collision fixture");var mesh=new Mesh();
            try
            {
                var vertices=new[]{new Vector3(-0.5f,0.9f,-0.5f),new Vector3(0.5f,0.9f,-0.5f),new Vector3(0,0.9f,0.5f),
                    new Vector3(0.8f,0,-1),new Vector3(0.8f,2,-1),new Vector3(0.8f,1,1)};
                mesh.vertices=vertices;mesh.triangles=new[]{0,1,2,3,4,5};mesh.RecalculateBounds();var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;
                var furniture=new SeatFurniture {Matrix=Matrix4x4.identity,Inverse=Matrix4x4.identity,Bounds=new Bounds(new Vector3(0,0.5f,0),new Vector3(1,1,1))};
                for(int i=0;i<3;i++)furniture.Vertices.Add(SeatFurniture.Key(vertices[i]));
                var clearance=new SeatMeshClearance(collider,furniture);
                if(!clearance.HasSeatTriangles||!clearance.Clear(Vector3.zero,Vector3.zero,0.25f))throw new InvalidOperationException("Selected combined seat triangles remain blocking.");
                if(clearance.Clear(new Vector3(0.7f,0,0),new Vector3(0.7f,0,0),0.25f))throw new InvalidOperationException("Selected seat authorization removed a wall in the same mesh.");
            }
            finally{UnityEngine.Object.Destroy(go);UnityEngine.Object.Destroy(mesh);}
        }
        private static void VerifyPreferredOutdoorSeatOrdering()
        {
            var slots=new List<FurnitureSeatSlot>();
            for(int i=0;i<14;i++)slots.Add(new FurnitureSeatSlot {Id="outdoor/"+i,Ground=new Vector3(i*0.1f,0,0)});
            var prepared=OutdoorSeatActivity.PreparationCandidates(slots,Vector3.zero,20,"outdoor/13");
            if(prepared.Count!=13||prepared[0].Id!="outdoor/13")
                throw new InvalidOperationException("Requested outdoor seat did not receive first bounded preparation attempt.");
            var outside=OutdoorSeatActivity.PreparationCandidates(slots,new Vector3(100,0,0),0,"outdoor/13");
            if(outside.Count!=1||outside[0].Id!="outdoor/13")
                throw new InvalidOperationException("Explicit outdoor seat was lost outside nearby scan radius.");
        }
        private static void VerifyOversizedClearanceFailsClosed()
        {
            var go=new GameObject("Seat oversized collision fixture");var mesh=new Mesh();
            try
            {
                var vertices=new[]{new Vector3(-0.5f,0.9f,-0.5f),new Vector3(0.5f,0.9f,-0.5f),new Vector3(0,0.9f,0.5f)};
                var indices=new int[(SeatMeshClearance.MaxSourceTriangles+1)*3];
                for(int i=0;i<indices.Length;i++)indices[i]=i%3;
                mesh.vertices=vertices;mesh.triangles=indices;mesh.RecalculateBounds();
                var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;
                var furniture=new SeatFurniture {Matrix=Matrix4x4.identity,Inverse=Matrix4x4.identity,Bounds=new Bounds(new Vector3(0,0.5f,0),new Vector3(1,1,1))};
                foreach(var vertex in vertices)furniture.Vertices.Add(SeatFurniture.Key(vertex));
                var clearance=new SeatMeshClearance(collider,furniture);
                if(clearance.Supported||clearance.HasSeatTriangles||clearance.Clear(Vector3.zero,Vector3.zero,0.25f))
                    throw new InvalidOperationException("Oversized combined mesh did not fail closed.");
            }
            finally{UnityEngine.Object.Destroy(go);UnityEngine.Object.Destroy(mesh);}
        }
        private static void VerifyMetadataAndSceneShift()
        {
            var root=new GameObject("Seat metadata fixture");var model=new GameObject("Provider bench");model.transform.SetParent(root.transform,false);var mesh=new Mesh();
            try
            {
                mesh.vertices=new[]{new Vector3(-1.3f,0,-0.25f),new Vector3(1.3f,0,-0.25f),new Vector3(-1.3f,0.5f,0.25f),new Vector3(1.3f,0.5f,0.25f),
                    new Vector3(-1.3f,1.2f,-0.25f),new Vector3(1.3f,1.2f,-0.25f)};
                mesh.triangles=new[]{0,1,2,2,1,3,0,4,5,0,5,1};mesh.RecalculateNormals();mesh.RecalculateBounds();model.AddComponent<MeshFilter>().sharedMesh=mesh;
                var metadata=model.AddComponent<SeatFurnitureMetadata>();metadata.StableId="test-bench";metadata.Bench=true;metadata.UsableSurface=new Bounds(new Vector3(0,0.5f,0),new Vector3(2.6f,0,0.5f));
                var catalog=FurnitureSeatCatalog.ForLoadedScene(root.transform,"test-scene");
                if(catalog.Slots.Count!=3)throw new InvalidOperationException("Provider bench did not produce three spaced slots.");
                var slot=catalog.Slots[1];string id=slot.Id;var before=slot.Ground;
                if(!slot.HasBackrest||Vector3.Dot(slot.Facing,Vector3.forward)<0.99f)throw new InvalidOperationException("Seat front does not point away from its real backrest.");
                root.transform.position=new Vector3(100,0,200);
                var rebound=catalog.Find(id);
                if(rebound==null||Vector3.Distance(rebound.Ground,before+root.transform.position)>0.001f)throw new InvalidOperationException("Scene shift changed seat identity or lost world position.");
                root.SetActive(false);
            }
            finally{root.SetActive(false);UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(mesh);}
        }
    }
}
