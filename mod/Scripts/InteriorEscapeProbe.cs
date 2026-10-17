using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class InteriorEscapeProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var streets = adapter.GetComponent<StreetPopulation>(); var guards = adapter.GetComponent<GuardPopulation>();
            var flag = typeof(LivingCitiesMod).GetField("exteriorGuardsEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
            bool previous = (bool)flag.GetValue(adapter); flag.SetValue(adapter, true); streets.ReactionsEnabled = true;
            bool savedEvent = false, loadedEvent = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => savedEvent = true;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loadedEvent = true;
            SaveLoadManager.OnSave += onSave; SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.PlayerEntity.GodMode = true;
                var clock = DaggerfallUnity.Instance.WorldTime.Now;
                clock.RaiseTime((86400 + 20*3600 - (int)(clock.ToSeconds()%86400))%86400);
                var population = (SettlementPopulation)typeof(LivingCitiesMod).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                int building = -1; float deadline = Time.realtimeSinceStartup + 60;
                while (building < 0 && Time.realtimeSinceStartup < deadline)
                {
                    if (guards.Roster != null) foreach (var approach in guards.Roster.BuildingApproaches)
                    {
                        var home = population.Households.Find(h => h.BuildingKey == approach.BuildingKey);
                        if (home != null && home.Residents.Count >= 2 && home.Residents.Exists(r => r.IsAlive &&
                            CivilianAlarms.ChooseReaction(r) == CivilianReaction.Flee &&
                            streets.GetState(r.Id,clock.ToSeconds()).Activity != Activity.Sleep))
                        { building = home.BuildingKey; break; }
                    }
                    yield return null;
                }
                if (building < 0) throw new InvalidOperationException("No surveyed family home for escape test.");
                DaggerfallStaticDoors owner = null; StaticDoor door = default(StaticDoor);
                foreach (var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach (var item in candidate.Doors) if (item.doorType == DoorTypes.Building && item.buildingKey == building)
                    { owner = candidate; door = item; break; }
                if (!owner) throw new InvalidOperationException("No family entrance.");
                TownSmokeHarness.EnterInterior(owner.transform, door);
                yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
                ResidentActor witness = null, victim = null;
                foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if (CivilianAlarms.ChooseReaction(actor.Resident) == CivilianReaction.Flee &&
                        streets.GetState(actor.Resident.Id,clock.ToSeconds()).Activity != Activity.Sleep) { witness = actor; break; }
                foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if (actor != witness) { victim = actor; break; }
                if (!victim || !witness) throw new InvalidOperationException("Need two present family residents.");
                var geometry = new InteriorGeometry(game.PlayerEnterExit.Interior);
                var origin = game.PlayerEnterExit.Interior.transform;
                Vector3 chosen = witness.transform.position - Vector3.up; float longest = 0;
                foreach (var anchor in geometry.LocalAnchors)
                {
                    Vector3 p = origin.TransformPoint(anchor); float length = Vector3.Distance(p, geometry.EntryGround.Value);
                    if (length > longest) { chosen = p; longest = length; }
                }
                var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.transform.position = chosen + Vector3.up;
                Physics.SyncTransforms();
                bool blocked = geometry.RouteToExit(chosen).Count == 0;
                UnityEngine.Object.Destroy(obstacle); yield return null;
                if (!blocked) throw new InvalidOperationException("Escape route accepts an overlapping obstacle.");
                // Controlled placement isolates actual path traversal from the daily anchor assignment.
                bool placed=false;
                foreach(var witnessAnchor in geometry.LocalAnchors)
                {
                    chosen=origin.TransformPoint(witnessAnchor);
                    if(Vector3.Distance(chosen,geometry.EntryGround.Value)<4 || geometry.RouteToExit(chosen).Count<2) continue;
                    witness.Place(chosen,Vector3.forward,true);
                    if(!witness.gameObject.activeInHierarchy) continue;
                    foreach(var anchor in geometry.LocalAnchors)
                    {
                        var other=origin.TransformPoint(anchor);
                        float distance=Vector3.Distance(chosen,other);
                        if(distance<1.5f || distance>10 || !CivilianResponses.Sees(chosen+Vector3.up,other+Vector3.up,20)) continue;
                        victim.Place(other,Vector3.forward,true);
                        if(!victim.gameObject.activeInHierarchy) continue;
                        game.PlayerMotor.transform.position=other+Vector3.up*1.2f; placed=true; break;
                    }
                    if(placed) break;
                }
                if(!placed || !witness.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("No separated visible escape fixture positions.");
                Physics.SyncTransforms();
                string id = witness.Resident.Id, name = witness.Person.NameNPC;
                var graphField = typeof(StreetPopulation).GetField("indoorGeometry", BindingFlags.Instance | BindingFlags.NonPublic);
                var actualGraph = graphField.GetValue(streets);
                graphField.SetValue(streets, null);
                try
                {
                    game.WeaponManager.WeaponDamage(null, false, false, victim.transform, victim.transform.position, Vector3.forward);
                    if (guards.Roster.Reports.Count != 0)
                        throw new InvalidOperationException("Unavailable escape graph invented an immediate guard report.");
                }
                finally { graphField.SetValue(streets, actualGraph); }
                var store = (WorldStateStore)typeof(LivingCitiesMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                var alarm = store.ActiveAlarm(id, clock.ToSeconds());
                var person = alarm == null ? null : alarm.People.Find(p => p.ResidentId == id);
                if (person == null || !person.DirectWitness)
                    throw new InvalidOperationException("Controlled direct escape witness did not identify visible attacker: active="+
                        witness.gameObject.activeInHierarchy+"; sleep="+streets.GetState(id,clock.ToSeconds()).Activity+
                        "; invisible="+game.PlayerEntity.IsInvisible+"; seesVictim="+CivilianResponses.Sees(witness.transform.position,victim.transform.position,20)+
                        "; seesPlayer="+CivilianResponses.Sees(witness.transform.position,game.PlayerMotor.transform.position,20));
                float retryDeadline = Time.realtimeSinceStartup + 2;
                while (person != null && person.Escape == null && Time.realtimeSinceStartup < retryDeadline) yield return null;
                if (person == null || person.Escape == null)
                {
                    var inside = geometry.RouteToExit(chosen);
                    var outside = guards.WitnessRoute(building, clock.ToSeconds());
                    throw new InvalidOperationException("Interior witness has no saved escape route: inside=" + inside.Count +
                        "; outside=" + (outside == null ? -1 : outside.Count) + "; reaction=" + (person == null ? "missing" : person.Reaction.ToString()) +
                        "; direct=" + (person != null && person.DirectWitness));
                }
                if (guards.Roster.Reports.Count != 0) throw new InvalidOperationException("Guards learned murder before witness escaped.");
                Vector3 before = witness.transform.position;
                yield return new WaitForSecondsRealtime(0.4f);
                if (!witness || Vector3.Distance(before, witness.transform.position) < 0.1f || witness.Person.NameNPC != name)
                    throw new InvalidOperationException("Witness did not physically follow escape route.");
                string json = SaveLoadManager.Serialize(typeof(WorldState), adapter.GetSaveData());
                double fleeSpeed = ResidentMovement.FleeSpeed(witness.Resident.AppearanceSeed);
                if (person.Escape.Speed != fleeSpeed || fleeSpeed < ResidentMovement.WalkSpeed * 2.5 ||
                    fleeSpeed > ResidentMovement.WalkSpeed * 3.5)
                    throw new InvalidOperationException("Runtime flight did not use the resident's stable randomized speed.");
                var restored = new WorldStateStore(0,LivingCitiesMod.ConversationArchiveRoot()); restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
                var saved = restored.ActiveAlarm(id, clock.ToSeconds()).People.Find(p => p.ResidentId == id);
                if(person.EscapeAdvancedAt<=0 || Math.Abs(saved.EscapeAdvancedAt-person.EscapeAdvancedAt)>0.001)
                    throw new InvalidOperationException("Native serialization lost witness simulation time.");
                double savedAdvance=person.EscapeAdvancedAt;
                // Native decimal JSON can round fractional absolute epochs by a few
                // microseconds. Keep a sub-frame timing tolerance, not bit equality.
                if (saved.Escape == null || Math.Abs(saved.Escape.ReportAt - person.Escape.ReportAt) > 0.001 || saved.Escape.Speed != fleeSpeed)
                    throw new InvalidOperationException("Escape timing or speed lost in native serialization: speed=" + fleeSpeed.ToString("R") +
                        "; saved=" + (saved.Escape == null ? "null" : saved.Escape.Speed.ToString("R")) +
                        "; timingDelta=" + (saved.Escape == null ? 0 : saved.Escape.ReportAt - person.Escape.ReportAt));
                double reportAt = person.Escape.ReportAt;
                game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - escaping witness");
                deadline = Time.realtimeSinceStartup + 30;
                while (!savedEvent && Time.realtimeSinceStartup < deadline) yield return null;
                if (!savedEvent) throw new InvalidOperationException("Escape save timed out.");
                int saveKey = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - escaping witness");
                game.SaveLoadManager.Load(saveKey); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 60;
                while ((!loadedEvent || !streets.Ready || guards.Roster == null) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loadedEvent || !streets.Ready || guards.Roster == null) throw new InvalidOperationException("Escape reload timed out.");
                yield return null;
                clock = DaggerfallUnity.Instance.WorldTime.Now;
                store = (WorldStateStore)typeof(LivingCitiesMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                alarm = store.ActiveAlarm(id, clock.ToSeconds()); person = alarm == null ? null : alarm.People.Find(p => p.ResidentId == id);
                if (person == null || person.Escape == null || Math.Abs(person.Escape.ReportAt - reportAt) > 0.001 || person.Escape.Speed != fleeSpeed)
                    throw new InvalidOperationException("Real reload reset witness journey.");
                if(person.EscapeAdvancedAt<savedAdvance-0.001 || person.EscapeAdvancedAt>clock.ToSeconds()+1)
                    throw new InvalidOperationException("Real reload lost or invented witness simulation time.");
                clock.RaiseTime((float)Math.Max(0, person.Escape.ExitAt - clock.ToSeconds() + 1));
                yield return null; yield return null;
                if (streets.FindActor(id) || person.BuildingKey != -1) throw new InvalidOperationException("Escaped witness remains inside.");
                if (clock.ToSeconds() < person.Escape.ReportAt && guards.Roster.Reports.Count != 0) throw new InvalidOperationException("Report arrived before witness reached guard.");
                game.PlayerEnterExit.TransitionExterior(); yield return new WaitForSecondsRealtime(0.5f);
                witness = streets.FindActor(id);
                if (!witness || witness.Person.NameNPC != name) throw new InvalidOperationException("Escaped identity missing outside the door.");
                // The attacker must not block the escape route in this timing regression.
                // Threat avoidance is allowed to hold a trapped witness at the doorway.
                game.PlayerMotor.transform.position=witness.transform.position+new Vector3(40,0,40);
                yield return new WaitForSecondsRealtime(0.8f);
                before = witness.transform.position; yield return new WaitForSecondsRealtime(0.4f);
                if (Vector3.Distance(before, witness.transform.position) < 0.1f) throw new InvalidOperationException("Escaped witness is not running toward guard outside.");
                yield return MovingAttacker(guards,streets,alarm,person,game.StreamingWorld.GetCurrentBuildingDirectory().transform,id);
                game.PlayerMotor.transform.position=witness.transform.position+new Vector3(40,0,40);
                yield return new WaitForSecondsRealtime(0.8f);
                clock.RaiseTime((float)Math.Max(0, person.Escape.ReportAt - clock.ToSeconds() + 1));
                yield return null; yield return null;
                var report = guards.Roster.Reports.Find(r => r.Id == "civilian:" + alarm.Id);
                if (report == null || report.BuildingKey != building || report.GuardIds.Count < 1 || report.GuardIds.Count > 4)
                    throw new InvalidOperationException("Escaped witness failed to report original crime building.");
                Debug.Log("LIVING_CITIES_TOWN_INTERIOR_ESCAPE_PASS: physical doorway route, delayed report, exterior identity/movement, finite building response and real save/load; building=" + building + "; save=" + saveKey);
            }
            finally
            {
                SaveLoadManager.OnSave -= onSave; SaveLoadManager.OnLoad -= onLoad;
                guards.Clear(); flag.SetValue(adapter, previous); game.PlayerEntity.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
            }
        }
        private static IEnumerator MovingAttacker(GuardPopulation guards,StreetPopulation streets,CivilianIncident alarm,
            AlarmPerson person,Transform origin,string id)
        {
            var game=GameManager.Instance;
            var from=new SavedRoutePoint{X=person.X,Z=person.Z};
            var normal=guards.Roster.PlanWitnessRoute(alarm.BuildingKey,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),from);
            if(normal==null || normal.Length<3) throw new InvalidOperationException("Moving attacker fixture needs a remaining guard route.");
            Vector3 direction=Vector3.zero;
            foreach(var point in normal.Route)
            {
                direction=new Vector3((float)(point.X-from.X),0,(float)(point.Z-from.Z));
                if(direction.magnitude>0.5f) break;
            }
            direction.Normalize();
            int movingPhases=0;
            for(int phase=0;phase<2;phase++)
            {
                from=new SavedRoutePoint{X=person.X,Z=person.Z};
                var danger=new SavedRoutePoint{X=from.X+direction.x*2,Z=from.Z+direction.z*2};
                var target=origin.TransformPoint(new Vector3((float)danger.X,0,(float)danger.Z));
                target.y=streets.FindActor(id).transform.position.y+0.2f;
                game.PlayerMotor.transform.position=target;
                var plan=guards.Roster.PlanWitnessRoute(alarm.BuildingKey,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),from,danger,true);
                bool canRetreat=plan!=null && plan.Length>0.5;
                if(canRetreat) movingPhases++;
                var actorBefore=streets.FindActor(id).transform.position;
                var viewStart=origin.InverseTransformPoint(actorBefore);
                double viewGap=Math.Sqrt(Math.Pow(viewStart.x-danger.X,2)+Math.Pow(viewStart.z-danger.Z,2));
                float end=Time.realtimeSinceStartup+0.65f;
                while(Time.realtimeSinceStartup<end)
                {
                    yield return null;
                    double dx=person.X-danger.X,dz=person.Z-danger.Z;
                    if(Math.Sqrt(dx*dx+dz*dz)<1.8)
                        throw new InvalidOperationException("Witness approached moving attacker: phase="+phase+"; gap="+Math.Sqrt(dx*dx+dz*dz));
                    var actor=streets.FindActor(id);
                    if(!actor || !actor.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("Moving attacker caused escaped witness view to disappear.");
                    var view=origin.InverseTransformPoint(actor.transform.position);
                    double viewNow=Math.Sqrt(Math.Pow(view.x-danger.X,2)+Math.Pow(view.z-danger.Z,2));
                    if(viewNow<Math.Min(1.8,viewGap-0.2))
                        throw new InvalidOperationException("Rendered witness approached moving attacker: phase="+phase+"; gap="+viewNow);
                }
                double movedX=person.X-from.X,movedZ=person.Z-from.Z;
                if(canRetreat && Math.Sqrt(movedX*movedX+movedZ*movedZ)<0.15)
                    throw new InvalidOperationException("Witness held despite an available safe retreat: phase="+phase);
                if(canRetreat && Vector3.Distance(actorBefore,streets.FindActor(id).transform.position)<0.15f)
                    throw new InvalidOperationException("Witness state retreated but rendered actor stayed still: phase="+phase);
                direction=-direction;
            }
            if(movingPhases==0) throw new InvalidOperationException("Moving attacker fixture offered no retreat in either phase.");
            Debug.Log("LIVING_CITIES_WITNESS_MOVING_ATTACKER_PASS: live escape retains clearance when attacker switches sides; reachable retreat moves.");
        }
    }
}
