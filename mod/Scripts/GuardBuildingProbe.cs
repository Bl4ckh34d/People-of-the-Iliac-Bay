using System;
using System.Collections;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class GuardBuildingProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var flag = typeof(LivingCitiesMod).GetField("exteriorGuardsEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
            bool enabled = (bool)flag.GetValue(adapter); flag.SetValue(adapter, true);
            var controller = adapter.GetComponent<GuardPopulation>();
            var postDirectory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var postSites=new TownGeometry(postDirectory,game.StreamingWorld.GetCurrentCityNavigation(),false).SurveyGuardPosts(350);
            if(postSites.Count==0) throw new InvalidOperationException("No public guard post sites surveyed.");
            foreach(var posted in controller.Roster.Guards)
                if(posted.IsAlive && posted.Duty==GuardDuty.Posted && !controller.Roster.IsOnPatrol(posted.Id) &&
                    !postSites.Exists(p=>Math.Abs(p.X-posted.Post.X)<0.05 && Math.Abs(p.Z-posted.Post.Z)<0.05))
                    throw new InvalidOperationException("Idle saved guard retained a doorway post: "+posted.Id);
            Debug.Log("LIVING_CITIES_PUBLIC_POSTS_PASS: available="+postSites.Count+"; roster="+controller.Roster.Guards.Count);
            bool saved = false, loaded = false;
            GuardBuildingApproach withheld = null;
            GuardRoster withheldRoster = null;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSave += onSave; SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.PlayerEntity.GodMode = true;
                var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                float deadline = Time.realtimeSinceStartup + 45;
                GuardBuildingApproach approach = null;
                while (approach == null && Time.realtimeSinceStartup < deadline)
                {
                    if (controller.Roster != null)
                        foreach (var candidate in controller.Roster.BuildingApproaches)
                        {
                            BuildingSummary building;
                            if (directory.GetBuildingSummary(candidate.BuildingKey, out building) &&
                                (building.BuildingType == DFLocation.BuildingTypes.Alchemist ||
                                building.BuildingType == DFLocation.BuildingTypes.GeneralStore ||
                                building.BuildingType == DFLocation.BuildingTypes.PawnShop ||
                                building.BuildingType == DFLocation.BuildingTypes.Armorer))
                            { approach = candidate; break; }
                        }
                    yield return null;
                }
                if (approach == null) throw new InvalidOperationException("No surveyed shop approach available.");
                DaggerfallStaticDoors owner = null; StaticDoor door = default(StaticDoor);
                foreach (var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach (var item in candidate.Doors)
                        if (item.doorType == DoorTypes.Building && item.buildingKey == approach.BuildingKey)
                        { owner = candidate; door = item; break; }
                if (!owner) throw new InvalidOperationException("Surveyed shop has no native door.");
                TownSmokeHarness.EnterInterior(owner.transform, door);
                yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
                game.PlayerEntity.HaveShownSurrenderToGuardsDialogue = true;
                withheld = approach; withheldRoster = controller.Roster;
                withheldRoster.BuildingApproaches.Remove(withheld);
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Theft;
                game.PlayerEntity.SpawnCityGuards(true); game.PlayerEntity.SpawnCityGuards(true);
                yield return new WaitForSecondsRealtime(1.2f);
                var pending = controller.Roster.Reports.Find(r => r.BuildingKey == approach.BuildingKey);
                if (pending == null || !pending.AwaitingApproach || pending.GuardIds.Count != 0 || controller.ActiveCount != 0)
                    throw new InvalidOperationException("Missing approach lost native alarm or created immediate responders.");
                string cancelledId = pending.Id;
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
                yield return new WaitForSecondsRealtime(1.2f);
                if (controller.Roster.Reports.Exists(r => r.Id == cancelledId))
                    throw new InvalidOperationException("Cleared crime retained an unassigned native alarm.");
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Theft;
                game.PlayerEntity.SpawnCityGuards(true);
                pending = controller.Roster.Reports.Find(r => r.BuildingKey == approach.BuildingKey);
                if (pending == null || !pending.AwaitingApproach || pending.GuardIds.Count != 0)
                    throw new InvalidOperationException("New crime failed to create a fresh pending alarm.");
                string pendingId = pending.Id;
                game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - pending shop alarm");
                deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Pending alarm save timed out.");
                int pendingKey = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - pending shop alarm");
                game.SaveLoadManager.Load(pendingKey); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || controller.Roster == null) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || controller.Roster == null) throw new InvalidOperationException("Pending alarm reload timed out.");
                pending = controller.Roster.Reports.Find(r => r.Id == pendingId);
                if (!loaded || pending == null || !pending.AwaitingApproach || pending.GuardIds.Count != 0 || !controller.OwnsSettlement)
                    throw new InvalidOperationException("Real reload lost pending native building alarm.");
                game.PlayerEntity.SpawnCityGuards(true); game.PlayerEntity.SpawnCityGuards(true);
                if (controller.Roster.Reports.FindAll(r => r.BuildingKey == approach.BuildingKey &&
                    r.Id.StartsWith("native:", StringComparison.Ordinal)).Count != 1)
                    throw new InvalidOperationException("Repeated native alarm after reload duplicated the pending report.");
                controller.Roster.BuildingApproaches.Add(approach);
                withheld = null;
                yield return new WaitForSecondsRealtime(1.2f);
                saved = false; loaded = false;
                Debug.Log("LIVING_CITIES_PENDING_GUARD_PASS: repeated native alarm, no instant responders, cleared-crime cancellation, real pending save/reload, automatic surveyed-route retry.");
                var roster = controller.Roster;
                GuardReport report = roster.Reports.Find(r => r.BuildingKey == approach.BuildingKey);
                if (report == null || report.GuardIds.Count < 1 || report.GuardIds.Count > 4)
                    throw new InvalidOperationException("Native indoor alarm did not dispatch finite building responders.");
                double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                var first = roster.Guards.Find(g => g.Id == report.GuardIds[0]);
                if (first.ArrivesAt <= now || UnityEngine.Object.FindObjectsOfType<GuardActor>().Length != 0)
                    throw new InvalidOperationException("Indoor guards appeared before their physical travel deadline.");
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(first.ArrivesAt - now + 1));
                yield return null; yield return null;
                GuardActor actor = null;
                foreach (var item in UnityEngine.Object.FindObjectsOfType<GuardActor>()) if (item.GuardId == first.Id) actor = item;
                if (!actor || !actor.IsInterior || first.Interior == null)
                    throw new InvalidOperationException("Arrived guard did not enter the reported shop: duty=" + first.Duty +
                        "; now=" + DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() + "; arrival=" + first.ArrivesAt +
                        "; playing=" + game.IsPlayingGame() + "; inside=" + game.PlayerEnterExit.IsPlayerInsideBuilding +
                        "; landing=" + typeof(GuardPopulation).GetField("entryGround", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller));
                var landing = new InteriorGeometry(game.PlayerEnterExit.Interior).EntryGround;
                if (!landing.HasValue || Vector3.Distance(actor.transform.position, landing.Value + Vector3.up) > 3)
                    throw new InvalidOperationException("Guard did not enter through the interior doorway.");
                string id = first.Id;
                bool visible = false;
                foreach (var anchor in new InteriorGeometry(game.PlayerEnterExit.Interior).LocalAnchors)
                {
                    var spot = game.PlayerEnterExit.Interior.transform.TransformPoint(anchor) + Vector3.up;
                    float distance = Vector3.Distance(actor.transform.position, spot);
                    if (distance < 2 || distance > 8 || !CivilianResponses.Sees(actor.transform.position, spot, 20)) continue;
                    game.PlayerMotor.transform.position = spot; visible = true; break;
                }
                if (!visible) throw new InvalidOperationException("No visible reachable interior combat test position.");
                deadline = Time.realtimeSinceStartup + 5;
                while (!actor.CombatReady && Time.realtimeSinceStartup < deadline) yield return null;
                if (!actor.CombatReady) throw new InvalidOperationException("Indoor guard did not activate native combat on sight: crime=" +
                    game.PlayerEntity.CrimeCommitted + "; playing=" + game.IsPlayingGame() + "; invisible=" + game.PlayerEntity.IsInvisible +
                    "; sight=" + CivilianResponses.Sees(actor.transform.position, game.PlayerMotor.transform.position, 20));
                var entity = actor.GetComponent<DaggerfallEntityBehaviour>().Entity;
                entity.SetHealth(Math.Max(1, entity.MaxHealth / 2), true); controller.Capture();
                double health = first.HealthFraction;
                game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - shop guards");
                deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Shop guard save timed out.");
                int key = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - shop guards");
                game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || controller.Roster == null) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || controller.Roster == null) throw new InvalidOperationException("Direct indoor guard reload timed out.");
                first = controller.Roster.Guards.Find(g => g.Id == id);
                if (first.Interior == null || first.Interior.BuildingKey != approach.BuildingKey ||
                    Math.Abs(first.HealthFraction - health) > 0.02)
                    throw new InvalidOperationException("Indoor reload lost location or healed guard.");
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
                yield return null; yield return null;
                if (first.Interior != null && !first.Interior.Returning)
                    throw new InvalidOperationException("Cleared crime did not end the indoor search.");
                foreach (var item in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                    if (item.IsInterior && item.CombatReady) throw new InvalidOperationException("Cleared crime left an indoor guard hostile.");
                game.PlayerEnterExit.TransitionExterior(false);
                DaggerfallUI.Instance.PopToHUD(); yield return null;
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime(3600);
                yield return null; yield return null;
                if (first.Interior != null || first.Duty != GuardDuty.Posted)
                    throw new InvalidOperationException("Guard failed to leave the interior and return to its post.");
                Debug.Log("LIVING_CITIES_TOWN_GUARD_BUILDING_PASS: native indoor alarm, finite delayed arrival, doorway entry, wounded interior save/load and return; save=" + key);
                // Reuse this real shop to exercise the player's reported escape.
                roster=controller.Roster;
                now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                var pursuit=roster.DispatchBuilding("probe-door-pursuit",approach.BuildingKey,now,1,2.8,12,
                    false,new System.Collections.Generic.HashSet<string> { id });
                if(pursuit.Count!=1) throw new InvalidOperationException("Door pursuit fixture could not dispatch original guard.");
                first=pursuit[0];
                game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Theft;
                // Native save/reload reconstructs exterior objects; do not reuse
                // the pre-reload door component reference.
                owner=null;
                foreach(var candidate in game.StreamingWorld.GetCurrentBuildingDirectory().GetComponentsInChildren<DaggerfallStaticDoors>())
                    if(candidate.Doors!=null) foreach(var item in candidate.Doors)
                        if(item.doorType==DoorTypes.Building && item.buildingKey==approach.BuildingKey)
                        { owner=candidate; door=item; break; }
                if(!owner) throw new InvalidOperationException("Reloaded pursuit shop door absent.");
                TownSmokeHarness.EnterInterior(owner.transform,door); DaggerfallUI.Instance.PopToHUD();
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(first.ArrivesAt-now+1));
                deadline=Time.realtimeSinceStartup+10; actor=null;
                while(Time.realtimeSinceStartup<deadline)
                {
                    foreach(var item in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                        if(item.GuardId==id && item.IsInterior) actor=item;
                    if(actor && actor.CombatReady) break; yield return null;
                }
                if(!actor || !actor.CombatReady) throw new InvalidOperationException("Door pursuit fixture guard did not engage indoors.");
                game.PlayerEnterExit.TransitionExterior(false); DaggerfallUI.Instance.PopToHUD(); yield return null;
                if(first.Interior!=null && first.Interior.ExteriorPursuit==null)
                    throw new InvalidOperationException("Departing player did not start guard doorway pursuit.");
                if(first.Interior!=null) DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)Math.Max(0,
                    first.Interior.ExitAt-DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()+1));
                deadline=Time.realtimeSinceStartup+10; actor=null;
                while(Time.realtimeSinceStartup<deadline)
                {
                    foreach(var item in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                        if(item.GuardId==id && !item.IsInterior) actor=item;
                    if(actor && actor.CombatReady) break; yield return null;
                }
                if(first.Interior!=null || first.Duty!=GuardDuty.Engaged || !actor || !actor.CombatReady ||
                    actor.GetComponent<EnemySenses>().Target!=game.PlayerEntityBehaviour)
                    throw new InvalidOperationException("Original guard did not emerge and resume native exterior pursuit.");
                var outsideGeometry=new TownGeometry(game.StreamingWorld.GetCurrentBuildingDirectory(),game.StreamingWorld.GetCurrentCityNavigation(),false);
                var chaseRoute=outsideGeometry.RouteBetweenPoints(first.EngagementPosition,first.Post);
                LivingCities.Core.SavedRoutePoint chasePoint=null;
                foreach(var point in chaseRoute)
                    if(Math.Pow(point.X-first.EngagementPosition.X,2)+Math.Pow(point.Z-first.EngagementPosition.Z,2)>=36)
                    { chasePoint=point; break; }
                if(chasePoint==null) throw new InvalidOperationException("No checked exterior chase destination for doorway fixture.");
                game.PlayerMotor.transform.position=outsideGeometry.ScenePoint(new RoutePoint(chasePoint.X,chasePoint.Z))+Vector3.up*2;
                var beforeChase=actor.transform.position; deadline=Time.realtimeSinceStartup+4;
                while(Time.realtimeSinceStartup<deadline && Vector3.Distance(beforeChase,actor.transform.position)<0.5f)
                { DaggerfallUI.Instance.PopToHUD(); yield return null; }
                if(Vector3.Distance(beforeChase,actor.transform.position)<0.5f)
                    throw new InvalidOperationException("Exterior pursuer targets player but does not physically move.");
                if(Math.Abs(first.HealthFraction-health)>0.02) throw new InvalidOperationException("Doorway pursuit healed the guard.");
                Debug.Log("LIVING_CITIES_GUARD_DOOR_PURSUIT_PASS: same wounded guard follows player through shop door and physically pursues outside.");
            }
            finally
            {
                if (withheld != null && !withheldRoster.BuildingApproaches.Exists(a => a.BuildingKey == withheld.BuildingKey))
                    withheldRoster.BuildingApproaches.Add(withheld);
                SaveLoadManager.OnSave -= onSave; SaveLoadManager.OnLoad -= onLoad;
                controller.Clear(); flag.SetValue(adapter, enabled);
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
            }
        }
    }
}
