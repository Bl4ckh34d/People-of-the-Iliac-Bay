using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class InteriorStairsProbe
    {
        public static IEnumerator Run(string keeperId)
        {
            var game = GameManager.Instance;
            var room = game.PlayerEnterExit.Interior;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var graph = new InteriorGeometry(room);
            timer.Stop();
            Debug.Log("LIVING_CITIES_INTERIOR_SURVEY_TIME: fixture=131343; milliseconds=" + timer.ElapsedMilliseconds);
            if (!graph.EntryGround.HasValue) throw new InvalidOperationException("Stair fixture lacks entry ground.");
            int upper = 0, routed = 0; string samples = ""; Vector3 chosen = Vector3.zero;
            foreach (var bed in BedGeometry.Read(room))
            {
                Vector3 center = room.transform.TransformPoint(bed.LocalCenter);
                for (int z = -5; z <= 5; z++) for (int x = -5; x <= 5; x++)
                {
                    Vector3 candidate = center + new Vector3(x * 0.4f, 0, z * 0.4f);
                    RaycastHit hit;
                    if (!Physics.Raycast(candidate + Vector3.up * 0.3f, Vector3.down, out hit, 1.6f, ~0, QueryTriggerInteraction.Ignore) ||
                        !hit.collider.transform.IsChildOf(room.transform) || hit.normal.y < 0.9f ||
                        hit.point.y < graph.EntryGround.Value.y + 1.5f) continue;
                    bool blocked = false;
                    foreach (var body in Physics.OverlapCapsule(hit.point + Vector3.up * 0.4f, hit.point + Vector3.up * 1.65f,
                        0.24f, ~0, QueryTriggerInteraction.Ignore))
                        if (!body.GetComponentInParent<StaticNPC>() && !body.GetComponentInParent<ResidentActor>() &&
                            !body.GetComponentInParent<PlayerMotor>()) blocked = true;
                    if (blocked) continue;
                    upper++;
                    var route = graph.RouteToExit(hit.point);
                    if (route.Count >= 2)
                    {
                        for (int i = 1; i < route.Count; i++)
                        {
                            var a = route[i-1]; var b = route[i];
                            Vector3 from = room.transform.TransformPoint(new Vector3((float)a.X,(float)a.Y,(float)a.Z));
                            Vector3 to = room.transform.TransformPoint(new Vector3((float)b.X,(float)b.Y,(float)b.Z));
                            for (float t = 0; t <= 1; t += 0.1f)
                            {
                                Vector3 p = Vector3.Lerp(from,to,t); RaycastHit support;
                                if (Physics.Raycast(p+Vector3.up*0.55f,Vector3.down,out support,1.1f,~0,QueryTriggerInteraction.Ignore) &&
                                    support.collider.transform.IsChildOf(room.transform) && support.normal.y > 0.65f && support.point.y > p.y + 0.04f)
                                    throw new InvalidOperationException("Escape feet cut through a stair tread; point=" + room.transform.InverseTransformPoint(p));
                            }
                        }
                        var end = route[route.Count - 1];
                        if (Vector3.Distance(room.transform.TransformPoint(new Vector3((float)end.X,(float)end.Y,(float)end.Z)), graph.EntryGround.Value) > 0.02f)
                            throw new InvalidOperationException("Upper-floor route does not reach actual exit ground.");
                        routed++;
                        chosen = hit.point;
                    }
                    if (samples.Length < 300) samples += room.transform.InverseTransformPoint(hit.point) + ":" + route.Count + ";";
                }
            }
            if (upper == 0 || routed == 0)
                throw new InvalidOperationException("Upper-floor escape unavailable; clear samples=" + upper + "; routed=" + routed + "; " + samples);
            Debug.Log("LIVING_CITIES_INTERIOR_STAIRS_PASS: actual upper bedroom floor routes to exit; clear=" + upper + "; routed=" + routed);
            var adapter = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var streets = adapter.GetComponent<StreetPopulation>(); streets.ReactionsEnabled = true;
            var store = (WorldStateStore)typeof(LivingCitiesMod).GetField("store",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(adapter);
            var world = store.Snapshot(); var keeper = world.NativePeople.Find(p=>p.Id==keeperId);
            var npc = Array.Find(room.GetComponentsInChildren<StaticNPC>(true),n=>n.Data.hash==keeper.NativeHash);
            if (!npc || streets.KeeperServiceBlocked(npc)) throw new InvalidOperationException("Stair movement fixture keeper unavailable.");
            var victim = world.Settlements.Find(t=>t.SettlementId==keeper.SettlementId).Households.Find(h=>h.Residents.Exists(r=>r.IsAlive && r.Id!=keeperId)).Residents.Find(r=>r.IsAlive && r.Id!=keeperId);
            Vector3 offset = Vector3.up * npc.GetComponent<DaggerfallBillboard>().Summary.Size.y * 0.5f;
            npc.transform.position = chosen+offset; Physics.SyncTransforms();
            double now = DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var alarm = store.RecordViolence(keeper.SettlementId,victim.Id,now);
            Vector3 local = room.transform.InverseTransformPoint(chosen);
            store.LocateViolence(alarm.Id,keeper.WorkKey,local.x,local.y,local.z);
            // Controlled awake upstairs origin isolates physical stair traversal.
            var person = store.HearViolence(alarm.Id,keeperId,keeper.WorkKey,now,false,0,3,0);
            person.HasPosition=true; person.BuildingKey=keeper.WorkKey; person.X=local.x; person.Y=local.y; person.Z=local.z;
            yield return new WaitForSecondsRealtime(0.25f);
            if (person.Escape==null) throw new InvalidOperationException("Live upstairs keeper did not plan an exit.");
            double started = person.Escape.StartedAt, duration = person.Escape.ExitAt-started;
            float lowY = chosen.y;
            for (int i=1;i<=20;i++)
            {
                var clock = DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now;
                clock.RaiseTime((float)Math.Max(0,started+duration*i/21-clock.ToSeconds()));
                yield return new WaitForSecondsRealtime(0.12f);
                if (npc.gameObject.activeSelf)
                {
                    lowY=Mathf.Min(lowY,npc.transform.position.y-offset.y);
                    Vector3 expected=room.transform.TransformPoint(new Vector3((float)person.X,(float)person.Y,(float)person.Z));
                    if (Vector3.Distance(npc.transform.position-offset,expected)>0.4f)
                        throw new InvalidOperationException("Live keeper left its saved stair path.");
                }
            }
            var endClock=DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now;
            endClock.RaiseTime((float)Math.Max(0,person.Escape.ExitAt+1-endClock.ToSeconds()));
            yield return new WaitForSecondsRealtime(0.25f);
            if (lowY>chosen.y-2 || npc.gameObject.activeSelf)
                throw new InvalidOperationException("Live keeper failed to descend stairs and exit.");
            Debug.Log("LIVING_CITIES_INTERIOR_STAIRS_MOVEMENT_PASS: controlled upstairs native keeper follows saved descending path and physically reaches exit.");
        }
    }
}
