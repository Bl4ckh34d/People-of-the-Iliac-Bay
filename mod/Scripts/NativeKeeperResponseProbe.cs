using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class NativeKeeperResponseProbe
    {
        private static void PlaceVisible(ResidentActor actor,StaticNPC npc)
        {
            foreach(float distance in new[]{0.8f,1.2f,1.8f,2.5f,3.5f})
            foreach(var offset in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back,
                new Vector3(1,0,1).normalized,new Vector3(-1,0,1).normalized,new Vector3(1,0,-1).normalized,new Vector3(-1,0,-1).normalized})
            {
                actor.Place(npc.transform.position-Vector3.up+offset*distance,Vector3.forward,true);
                Physics.SyncTransforms();
                if(actor.gameObject.activeSelf && CivilianResponses.Sees(npc.transform.position,actor.transform.position,20)) return;
            }
            throw new InvalidOperationException("No unobstructed controlled witness placement beside native keeper.");
        }
        public static IEnumerator Run(string character, string keeperId)
        {
            yield return VerifyDoorPermissions();
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var streets = adapter.GetComponent<StreetPopulation>();
            var guards = adapter.GetComponent<GuardPopulation>();
            var storeField = typeof(PeopleOfTheIliacBayMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic);
            var store = (WorldStateStore)storeField.GetValue(adapter);
            var world = store.Snapshot(); var keeper = world.NativePeople.Find(p => p.Id == keeperId);
            var town = world.Settlements.Find(t => t.SettlementId == keeper.SettlementId);
            var victim = town.Households.Find(h => h.Residents.Exists(r => r.IsAlive)).Residents.Find(r => r.IsAlive);
            var room = game.PlayerEnterExit.Interior;
            var npc = Array.Find(room.GetComponentsInChildren<StaticNPC>(true), n => n.Data.hash == keeper.NativeHash);
            foreach(var original in room.GetComponentsInChildren<StaticNPC>(true))
            {
                DaggerfallConnect.Arena2.FactionFile.FactionData faction;
                bool found=game.PlayerEntity.FactionData.GetFactionData(original.Data.factionID,out faction);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_NATIVE: flat="+original.Data.billboardArchiveIndex+"/"+original.Data.billboardRecordIndex+
                    "; name="+original.DisplayName+"; faction="+original.Data.factionID+"; group="+(found?faction.sgroup:-1)+
                    "; registered="+world.NativePeople.Exists(p=>p.NativeHash==original.Data.hash && p.WorkKey==original.Data.buildingKey));
            }
            var bookman=Array.Find(room.GetComponentsInChildren<StaticNPC>(true),n=>n.Data.billboardArchiveIndex==184 && n.Data.billboardRecordIndex==2);
            if(!bookman || !world.NativePeople.Exists(p=>p.NativeHash==bookman.Data.hash && p.WorkKey==bookman.Data.buildingKey))
                throw new InvalidOperationException("Ordinary factionless shop NPC 184/2 has no persistent combat identity.");
            streets.ReactionsEnabled = true; game.PlayerEntity.GodMode = true;
            if (!npc || streets.KeeperServiceBlocked(npc)) throw new InvalidOperationException("Keeper response fixture is not initially available.");
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var alarm = store.RecordViolence(keeper.SettlementId, victim.Id, now);
            var local = room.transform.InverseTransformPoint(npc.transform.position);
            store.LocateViolence(alarm.Id, keeper.WorkKey, local.x, local.y, local.z);
            // Controlled direct sight, not a claim of native keeper death or natural shop killing acceptance.
            game.PlayerMotor.transform.position = npc.transform.position + Vector3.up * 0.2f;
            Physics.SyncTransforms();
            if (!CivilianResponses.Sees(npc.transform.position, game.PlayerMotor.transform.position, 20))
                throw new InvalidOperationException("Controlled keeper witness cannot see the player.");
            streets.ObserveKeeperViolence(alarm, npc.transform.position, now);
            if (!streets.KeeperServiceBlocked(npc)) throw new InvalidOperationException("Witnessed violence did not block keeper service.");
            var ui = DaggerfallUI.UIManager.TopWindow;
            game.TalkManager.TalkToStaticNPC(npc);
            typeof(PlayerActivate).GetMethod("StaticNPCClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game.PlayerActivate, new object[] { npc });
            if (DaggerfallUI.UIManager.TopWindow != ui) throw new InvalidOperationException("Alarmed keeper opened dialogue or trade.");
            var initial = npc.transform.position;
            yield return new WaitForSecondsRealtime(1);
            var person = alarm.People.Find(p => p.ResidentId == keeperId);
            if (person == null || person.Escape == null || Vector3.Distance(initial, npc.transform.position) < 0.1f)
            {
                var graph = (InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(streets);
                var outside = guards.WitnessRoute(keeper.WorkKey, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                int inside = person == null ? -1 : graph.RouteToExit(room.transform.TransformPoint(new Vector3((float)person.X, (float)person.Y, (float)person.Z))).Count;
                string floors = "";
                if (graph.EntryGround.HasValue) game.PlayerMotor.transform.position = graph.EntryGround.Value + Vector3.up;
                game.PlayerMouseLook.SetHorizontalFacing(initial - game.PlayerMotor.transform.position);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "PeopleOfTheIliacBay-keeper-route.png"));
                yield return new WaitForSecondsRealtime(0.5f);
                foreach (var hit in Physics.RaycastAll(initial + Vector3.up * 2, Vector3.down, 10, ~0, QueryTriggerInteraction.Ignore))
                    floors += hit.collider.name + "@" + room.transform.InverseTransformPoint(hit.point) + ";";
                var cells = (System.Collections.Generic.Dictionary<Vector3Int, Vector3>)typeof(InteriorGeometry).GetField("floors", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(graph);
                Vector3 ground = room.transform.TransformPoint(new Vector3((float)person.X, (float)person.Y, (float)person.Z));
                float nearest = float.PositiveInfinity;
                foreach (var cell in cells.Values) nearest = Mathf.Min(nearest, Vector3.Distance(ground, cell));
                string overlaps = "";
                foreach (var overlap in Physics.OverlapCapsule(ground + Vector3.up * 0.3f, ground + Vector3.up * 1.65f, 0.24f, ~0, QueryTriggerInteraction.Ignore))
                    overlaps += overlap.name + ":npc=" + (overlap.GetComponentInParent<StaticNPC>() != null) + ";";
                Vector3 marker, door, normal;
                var check = DaggerfallStaticDoors.GetDoorPosition(room.EntryDoor);
                if (room.FindClosestEnterMarker(check, out marker)) check = marker;
                string landing = room.FindClosestInteriorDoor(check, out door, out normal) ? room.transform.InverseTransformPoint(door + normal * 0.8f).ToString() : "missing";
                throw new InvalidOperationException("Keeper failed to follow a surveyed escape route; inside=" + inside + "; outside=" + (outside == null ? -1 : outside.Count) +
                    "; position=" + (person == null ? "missing" : person.X + "," + person.Y + "," + person.Z) + "; flat=" + room.transform.InverseTransformPoint(initial) +
                    "; moved=" + Vector3.Distance(initial, npc.transform.position) + "; pause=" + GameManager.IsGamePaused + "; entry=" +
                    (graph.EntryGround.HasValue ? room.transform.InverseTransformPoint(graph.EntryGround.Value).ToString() : "missing") + "; floors=" + floors +
                    "; cells=" + cells.Count + "; nearest=" + nearest + "; overlaps=" + overlaps + "; native landing=" + landing);
            }
            if (guards.Roster.Reports.Exists(r => r.Id == "civilian:" + alarm.Id && r.GuardIds.Count > 0))
                throw new InvalidOperationException("Keeper reported before reaching a guard.");
            bool saved = false, loaded = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSave += onSave; SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.Save(character, "People of the Iliac Bay - keeper response");
                float deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Keeper response save timed out.");
                int slot = game.SaveLoadManager.FindSaveFolderByNames(character, "People of the Iliac Bay - keeper response");
                game.SaveLoadManager.Load(slot); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Keeper response reload timed out.");
                yield return new WaitForSecondsRealtime(1);
                store = (WorldStateStore)storeField.GetValue(adapter); alarm = store.NativeResponse(keeperId);
                room = game.PlayerEnterExit.Interior;
                npc = Array.Find(room.GetComponentsInChildren<StaticNPC>(true), n => n.Data.hash == keeper.NativeHash);
                if (!npc || alarm == null || !streets.KeeperServiceBlocked(npc)) throw new InvalidOperationException("Reload lost keeper response/service restriction.");
                person = alarm.People.Find(p => p.ResidentId == keeperId);
                var clock = DaggerfallUnity.Instance.WorldTime.Now;
                clock.RaiseTime((float)Math.Max(0, person.Escape.ExitAt - clock.ToSeconds() + 1));
                yield return new WaitForSecondsRealtime(0.5f);
                if (npc.gameObject.activeSelf) throw new InvalidOperationException("Keeper remained in shop after reaching exit.");
                clock.RaiseTime((float)Math.Max(0, person.Escape.ReportAt - clock.ToSeconds() + 1));
                yield return new WaitForSecondsRealtime(0.5f);
                if (!guards.Roster.Reports.Exists(r => r.Id == "civilian:" + alarm.Id && r.GuardIds.Count > 0))
                    throw new InvalidOperationException("Arrived native keeper did not notify finite guards.");
                clock.RaiseTime((float)Math.Max(0, CivilianAlarms.ActiveUntil(alarm) - clock.ToSeconds() + 1));
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Murder;
                yield return new WaitForSecondsRealtime(0.5f);
                if (!streets.KeeperServiceBlocked(npc)) throw new InvalidOperationException("Keeper resumed service before crime cleared.");
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
                yield return new WaitForSecondsRealtime(0.5f);
                if (streets.KeeperServiceBlocked(npc) || !npc.gameObject.activeSelf || npc.DisplayName != keeper.Name)
                    throw new InvalidOperationException("Keeper did not resume native identity/service after response cleared.");
                ui = DaggerfallUI.UIManager.TopWindow;
                typeof(PlayerActivate).GetMethod("StaticNPCClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game.PlayerActivate, new object[] { npc });
                yield return null;
                if (DaggerfallUI.UIManager.TopWindow == ui) throw new InvalidOperationException("Recovered keeper did not open native service.");
                DaggerfallUI.Instance.PopToHUD();
                var current = (SettlementPopulation)typeof(PeopleOfTheIliacBayMod).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                var ledger = store.Snapshot().AlarmIncidents;
                Resident hitResident = null; uint surname = 0;
                foreach (var home in current.Households)
                    foreach (var member in home.Residents)
                        if (hitResident == null && member.IsAlive && member.AgeGroup == ResidentAgeGroup.Adult &&
                            !ledger.Exists(a => a.VictimId == member.Id) && !streets.FindActor(member.Id))
                        { hitResident = member; surname = home.SurnameSeed; }
                if (hitResident == null) throw new InvalidOperationException("No fresh resident for native keeper witness hit.");
                var navigation = (CityNavigation)typeof(StreetPopulation).GetField("navigation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(streets);
                var actorMap = (System.Collections.Generic.Dictionary<string, ResidentActor>)typeof(StreetPopulation).GetField("actors", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(streets);
                var visitorObject = UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject, room.transform);
                try
                {
                    var visitor = visitorObject.AddComponent<ResidentActor>(); visitor.Initialize(hitResident, surname, navigation);
                    PlaceVisible(visitor,npc);
                    actorMap.Add(hitResident.Id, visitor); Physics.SyncTransforms();
                    game.PlayerMotor.transform.position = npc.transform.position + Vector3.up * 0.2f;
                    Physics.SyncTransforms();
                    game.WeaponManager.WeaponDamage(null, false, false, visitor.transform, visitor.transform.position, Vector3.forward);
                    var witnessed = store.NativeResponse(keeperId);
                    if (hitResident.IsAlive || witnessed == null || witnessed.VictimId != hitResident.Id ||
                        !witnessed.People.Exists(p => p.ResidentId == keeperId && p.DirectWitness) || !streets.KeeperServiceBlocked(npc))
                        throw new InvalidOperationException("Native weapon-hit path did not capture living keeper witness and suppress service; victimAlive="+
                            hitResident.IsAlive+"; response="+(witnessed==null?"none":witnessed.VictimId)+"; direct="+
                            (witnessed!=null && witnessed.People.Exists(p=>p.ResidentId==keeperId && p.DirectWitness))+
                            "; blocked="+streets.KeeperServiceBlocked(npc)+"; victimVisible="+CivilianResponses.Sees(npc.transform.position,visitor.transform.position,20)+
                            "; playerVisible="+CivilianResponses.Sees(npc.transform.position,game.PlayerMotor.transform.position,20)+
                            "; invisible="+game.PlayerEntity.IsInvisible+"; time="+DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_WEAPON_PASS: native WeaponDamage killing captures keeper sight; controlled visitor placement.");
                }
                finally { actorMap.Remove(hitResident.Id); UnityEngine.Object.Destroy(visitorObject); }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_RESPONSE_PASS: sight, service suppression, physical exit, real reload, delayed finite report and recovery; save=" + slot);
            }
            finally { SaveLoadManager.OnSave -= onSave; SaveLoadManager.OnLoad -= onLoad; }
            yield return VerifyKeeperDeath(character, keeperId);
            yield return VerifyKeeperSleep(character, keeperId);
            yield return VerifyOrdinaryShopDeath(character);
        }
        private static IEnumerator VerifyOrdinaryShopDeath(string character)
        {
            var game=GameManager.Instance;
            var adapter=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var streets=adapter.GetComponent<StreetPopulation>();
            bool loaded=false,saved=false;
            SaveLoadManager.OnLoadEventHandler onLoad=ignored=>loaded=true;
            SaveLoadManager.OnSaveEventHandler onSave=ignored=>saved=true;
            SaveLoadManager.OnLoad+=onLoad; SaveLoadManager.OnSave+=onSave;
            try
            {
                int fixture=game.SaveLoadManager.FindSaveFolderByNames(character,"People of the Iliac Bay - shopkeeper identity");
                game.SaveLoadManager.Load(fixture); DaggerfallUI.Instance.PopToHUD();
                float deadline=Time.realtimeSinceStartup+60;
                while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<deadline) yield return null;
                if(!loaded || !streets.Ready) throw new InvalidOperationException("Ordinary shop NPC fixture load timed out.");
                yield return new WaitForSecondsRealtime(1);
                var npc=Array.Find(game.PlayerEnterExit.Interior.GetComponentsInChildren<StaticNPC>(true),
                    n=>n.Data.billboardArchiveIndex==184 && n.Data.billboardRecordIndex==2);
                var world=(WorldState)adapter.GetSaveData();
                var person=world.NativePeople.Find(p=>p.NativeHash==npc.Data.hash && p.WorkKey==npc.Data.buildingKey);
                if(person==null || !person.IsAlive || person.HomeKey!=-1 || person.BedSlot!=-1)
                    throw new InvalidOperationException("Ordinary shop NPC lacks identity or stole a keeper bed.");
                string id=person.Id;
                streets.ReactionsEnabled=true;
                game.WeaponManager.WeaponDamage(null,false,false,npc.transform,npc.transform.position,Vector3.forward);
                world=(WorldState)adapter.GetSaveData();
                if(world.NativePeople.Find(p=>p.Id==id).IsAlive || npc.gameObject.activeSelf ||
                    !world.AlarmIncidents.Exists(a=>a.VictimId==id && a.Corpse!=null))
                    throw new InvalidOperationException("Factionless shop NPC survived native weapon hit or lost corpse.");
                game.SaveLoadManager.Save(character,"People of the Iliac Bay - ordinary shop NPC death");
                deadline=Time.realtimeSinceStartup+30;
                while(!saved && Time.realtimeSinceStartup<deadline) yield return null;
                if(!saved) throw new InvalidOperationException("Ordinary shop NPC death save timed out.");
                int slot=game.SaveLoadManager.FindSaveFolderByNames(character,"People of the Iliac Bay - ordinary shop NPC death");
                loaded=false; game.SaveLoadManager.Load(slot); DaggerfallUI.Instance.PopToHUD();
                deadline=Time.realtimeSinceStartup+60;
                while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<deadline) yield return null;
                if(!loaded || !streets.Ready) throw new InvalidOperationException("Ordinary shop NPC death reload timed out.");
                yield return new WaitForSecondsRealtime(1);
                world=(WorldState)adapter.GetSaveData();
                person=world.NativePeople.Find(p=>p.Id==id);
                npc=Array.Find(game.PlayerEnterExit.Interior.GetComponentsInChildren<StaticNPC>(true),n=>n.Data.hash==person.NativeHash);
                if(person.IsAlive || !npc || npc.gameObject.activeSelf || streets.CorpseCount<1)
                    throw new InvalidOperationException("Ordinary shop NPC revived or lost corpse after real reload.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ORDINARY_SHOP_DEATH_PASS: flat184/2 native weapon hit, corpse, persisted death and no keeper bed allocation.");
            }
            finally { SaveLoadManager.OnLoad-=onLoad; SaveLoadManager.OnSave-=onSave; }
        }
        private static IEnumerator VerifyKeeperSleep(string character, string keeperId)
        {
            var game = GameManager.Instance; var adapter = UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var streets = adapter.GetComponent<StreetPopulation>();
            bool loaded = false, saved = false;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoad += onLoad;
            SaveLoadManager.OnSave += onSave;
            try
            {
                int slot = game.SaveLoadManager.FindSaveFolderByNames(character, "People of the Iliac Bay - shopkeeper identity");
                game.SaveLoadManager.Load(slot); DaggerfallUI.Instance.PopToHUD();
                float deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Keeper sleep fixture load timed out.");
                yield return new WaitForSecondsRealtime(1);
                var world = (WorldState)adapter.GetSaveData(); var keeper = world.NativePeople.Find(p => p.Id == keeperId);
                var room = game.PlayerEnterExit.Interior;
                var npc = Array.Find(room.GetComponentsInChildren<StaticNPC>(true), n => n.Data.hash == keeper.NativeHash);
                var original = npc.transform.position; streets.SleepEnabled = true;
                var originalSize = npc.GetComponent<BoxCollider>().size;
                var originalCenter = npc.GetComponent<BoxCollider>().center;
                var clock = DaggerfallUnity.Instance.WorldTime.Now;
                clock.RaiseTime((float)((84600 - clock.ToSeconds() % 86400 + 86400) % 86400));
                yield return new WaitForSecondsRealtime(1);
                var pose = npc.GetComponent<SleepingPose>();
                if (!pose || !pose.Active || !streets.KeeperServiceBlocked(npc))
                    throw new InvalidOperationException("Native keeper has no reserved-bed sleeping pose/service block at night.");
                var beds = BedGeometry.Read(room); beds.Sort((a,b) => a.RecordIndex.CompareTo(b.RecordIndex));
                if (Vector3.Distance(npc.transform.position, room.transform.TransformPoint(beds[keeper.BedSlot].LocalCenter) + Vector3.up * 0.18f) > 0.02f)
                    throw new InvalidOperationException("Native keeper sleep uses wrong physical bed.");
                var ui = DaggerfallUI.UIManager.TopWindow; game.TalkManager.TalkToStaticNPC(npc);
                if (DaggerfallUI.UIManager.TopWindow != ui) throw new InvalidOperationException("Sleeping native keeper opened dialogue.");
                game.SaveLoadManager.Save(character, "People of the Iliac Bay - keeper sleeping");
                deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Keeper sleeping save timed out.");
                int sleepSlot = game.SaveLoadManager.FindSaveFolderByNames(character, "People of the Iliac Bay - keeper sleeping");
                loaded = false; game.SaveLoadManager.Load(sleepSlot); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Keeper sleeping reload timed out.");
                yield return new WaitForSecondsRealtime(1);
                room = game.PlayerEnterExit.Interior;
                npc = Array.Find(room.GetComponentsInChildren<StaticNPC>(true), n => n.Data.hash == keeper.NativeHash);
                pose = npc.GetComponent<SleepingPose>();
                if (!pose || !pose.Active || !streets.KeeperServiceBlocked(npc) || npc.DisplayName != keeper.Name ||
                    Mathf.Abs(npc.GetComponent<BoxCollider>().size.y - 0.36f) > 0.001f)
                    throw new InvalidOperationException("Reload lost native sleep identity, pose, collider or service block.");
                var cameraTarget = npc.transform.position;
                bool visible = false; game.PlayerMotor.CancelMovement = true;
                foreach (var direction in new[] { npc.transform.right, -npc.transform.right, npc.transform.forward, -npc.transform.forward })
                {
                    var desired = cameraTarget + direction * 1.2f + Vector3.up * 1.1f;
                    float nearest = float.MaxValue; Vector3 floor = Vector3.zero;
                    foreach (var hit in Physics.RaycastAll(desired, Vector3.down, 3, ~0, QueryTriggerInteraction.Ignore))
                        if (hit.normal.y > 0.9f && hit.collider.transform.IsChildOf(room.transform) && hit.distance < nearest)
                        { floor = hit.point; nearest = hit.distance; }
                    if (nearest == float.MaxValue) continue;
                    game.PlayerMotor.transform.position = floor + Vector3.up;
                    yield return new WaitForSecondsRealtime(0.2f); SleepProbe.AimCamera(cameraTarget);
                    yield return null; yield return new WaitForEndOfFrame();
                    var viewport = Camera.main.WorldToViewportPoint(cameraTarget);
                    bool blocked = false;
                    foreach (var hit in Physics.RaycastAll(Camera.main.transform.position, cameraTarget - Camera.main.transform.position,
                        Vector3.Distance(Camera.main.transform.position, cameraTarget), ~0, QueryTriggerInteraction.Ignore))
                        if (!hit.collider.transform.IsChildOf(npc.transform)) blocked = true;
                    if (viewport.z > 0 && Mathf.Abs(viewport.x - 0.5f) < 0.05f && Mathf.Abs(viewport.y - 0.5f) < 0.05f && !blocked)
                    { visible = true; break; }
                }
                if (!visible) throw new InvalidOperationException("Native sleeping keeper has no unobstructed camera view beside bed.");
                if (streets.HasTheftWitness() != false) throw new InvalidOperationException("Sleeping native keeper witnessed theft while all occupants slept.");
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "PeopleOfTheIliacBay-native-keeper-sleep.png"));
                yield return new WaitForSecondsRealtime(0.3f);
                clock = DaggerfallUnity.Instance.WorldTime.Now;
                clock.RaiseTime((float)((34200 - clock.ToSeconds() % 86400 + 86400) % 86400));
                yield return new WaitForSecondsRealtime(1);
                if (pose.Active || streets.KeeperServiceBlocked(npc) || Vector3.Distance(npc.transform.position, original) > 0.02f)
                    throw new InvalidOperationException("Native keeper failed to restore service position after waking.");
                if (npc.GetComponent<BoxCollider>().size != originalSize || npc.GetComponent<BoxCollider>().center != originalCenter ||
                    !npc.GetComponent<Renderer>().enabled) throw new InvalidOperationException("Native keeper wake failed to restore standing collider or renderer.");
                clock.RaiseTime((float)((84600 - clock.ToSeconds() % 86400 + 86400) % 86400));
                yield return new WaitForSecondsRealtime(1);
                var deathPoint = room.transform.InverseTransformPoint(npc.transform.position);
                var guards = adapter.GetComponent<GuardPopulation>();
                var missing = guards.Roster.BuildingApproaches.Find(a => a.BuildingKey == keeper.WorkKey);
                guards.Roster.BuildingApproaches.Remove(missing);
                try
                {
                    var priorCrime = game.PlayerEntity.CrimeCommitted;
                    int priorReports = guards.Roster.Reports.Count;
                    game.WeaponManager.WeaponDamage(null, false, false, npc.transform, npc.transform.position, Vector3.forward);
                    var death = ((WorldState)adapter.GetSaveData()).AlarmIncidents.Find(a => a.VictimId == keeperId);
                    if (death == null || death.Corpse == null || Vector3.Distance(deathPoint,
                        new Vector3((float)death.Corpse.X, (float)death.Corpse.Y, (float)death.Corpse.Z)) > 0.02f || npc.gameObject.activeSelf)
                        throw new InvalidOperationException("Sleeping native keeper death lost bed position or resurrected flat.");
                    if (!guards.OwnsSettlement || death.Scene == null || death.Scene.PlayerLeft || guards.Roster.Reports.Count != priorReports ||
                        game.PlayerEntity.CrimeCommitted != priorCrime)
                        throw new InvalidOperationException("Uncached sleeping keeper death lost concealment or finite ownership.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_UNCACHED_KEEPER_PASS: sleeping native keeper retains concealed workplace death and finite ownership.");
                }
                finally
                {
                    if (missing != null) guards.Roster.BuildingApproaches.Add(missing);
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_SLEEP_PASS: native identity, assigned bed, blocked service, real reload, collider/wake restoration and bed-position death; save=" + sleepSlot);
            }
            finally { SaveLoadManager.OnLoad -= onLoad; SaveLoadManager.OnSave -= onSave; }
        }
        private static IEnumerator VerifyKeeperDeath(string character, string keeperId)
        {
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var streets = adapter.GetComponent<StreetPopulation>();
            var guards = adapter.GetComponent<GuardPopulation>();
            var field = typeof(PeopleOfTheIliacBayMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic);
            bool loaded = false, saved = false;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoad += onLoad; SaveLoadManager.OnSave += onSave;
            try
            {
                int fixture = game.SaveLoadManager.FindSaveFolderByNames(character, "People of the Iliac Bay - shopkeeper identity");
                game.SaveLoadManager.Load(fixture); DaggerfallUI.Instance.PopToHUD();
                float deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Keeper death fixture load timed out.");
                yield return new WaitForSecondsRealtime(1);
                var store = (WorldStateStore)field.GetValue(adapter);
                var keeper = store.NativePeopleFor(game.PlayerGPS.CurrentLocation.MapTableData.MapId).Find(p => p.Id == keeperId);
                var room = game.PlayerEnterExit.Interior;
                var npc = Array.Find(room.GetComponentsInChildren<StaticNPC>(true), n => n.Data.hash == keeper.NativeHash);
                var town = (SettlementPopulation)typeof(PeopleOfTheIliacBayMod).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                var home = town.Households.Find(h => h.HeadId == keeperId);
                var partner = streets.FindActor(home.Residents.Find(r => r.Id != keeperId).Id);
                if (!npc || !partner) throw new InvalidOperationException("Keeper death fixture lacks keeper or partner.");
                // Controlled partner sight tests identity/violence integration, not general stair navigation.
                PlaceVisible(partner,npc);
                game.PlayerMotor.transform.position = npc.transform.position + Vector3.up * 0.2f;
                streets.ReactionsEnabled = true; Physics.SyncTransforms();
                int reports = guards.Roster.Reports.Count;
                game.WeaponManager.WeaponDamage(null, false, false, npc.transform, npc.transform.position, Vector3.forward);
                if (keeper.IsAlive || home.Residents[0].IsAlive || npc.gameObject.activeSelf)
                    throw new InvalidOperationException("Native shopkeeper weapon hit did not persist death and hide service body.");
                var alarm = store.Snapshot().AlarmIncidents.Find(a => a.VictimId == keeperId);
                if (alarm == null || alarm.Corpse == null || !alarm.People.Exists(p => p.ResidentId == partner.Resident.Id && p.DirectWitness))
                    throw new InvalidOperationException("Native keeper death lost corpse or family witness.");
                if (!streets.KeeperServiceBlocked(npc) || guards.Roster.Reports.Count != reports)
                    throw new InvalidOperationException("Dead keeper permits service or creates instant guard notification.");
                yield return new WaitForSecondsRealtime(0.5f);
                if (streets.CorpseCount < 1) throw new InvalidOperationException("Native keeper corpse has no physical view.");
                game.SaveLoadManager.Save(character, "People of the Iliac Bay - keeper death");
                deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Keeper death save timed out.");
                int slot = game.SaveLoadManager.FindSaveFolderByNames(character, "People of the Iliac Bay - keeper death");
                loaded = false; game.SaveLoadManager.Load(slot); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Keeper death reload timed out.");
                yield return new WaitForSecondsRealtime(1);
                store = (WorldStateStore)field.GetValue(adapter);
                keeper = store.NativePeopleFor(keeper.SettlementId).Find(p => p.Id == keeperId);
                npc = Array.Find(game.PlayerEnterExit.Interior.GetComponentsInChildren<StaticNPC>(true), n => n.Data.hash == keeper.NativeHash);
                if (keeper.IsAlive || !npc || npc.gameObject.activeSelf || !streets.KeeperServiceBlocked(npc) || streets.CorpseCount < 1)
                    throw new InvalidOperationException("Reload resurrected native keeper or lost physical corpse/service block.");
                var ui = DaggerfallUI.UIManager.TopWindow; game.TalkManager.TalkToStaticNPC(npc);
                if (DaggerfallUI.UIManager.TopWindow != ui) throw new InvalidOperationException("Dead native keeper opened dialogue after reload.");
                yield return VerifyVisitorAfterKeeperDeath(adapter, streets, game.PlayerEnterExit.Interior, keeper);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_DEATH_PASS: native weapon hit; household identity; family witness; no instant guards; corpse; blocked service; real reload; save=" + slot);
            }
            finally { SaveLoadManager.OnLoad -= onLoad; SaveLoadManager.OnSave -= onSave; }
        }
        private static IEnumerator VerifyVisitorAfterKeeperDeath(PeopleOfTheIliacBayMod adapter, StreetPopulation streets,
            DaggerfallInterior room, NativePerson keeper)
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var plans=(IEnumerable)typeof(StreetPopulation).GetField("plans",flags).GetValue(streets);
            object selected=null;
            foreach(var plan in plans)
            {
                var planType=plan.GetType();
                var resident=(Resident)planType.GetField("Resident").GetValue(plan);
                if(resident.IsAlive && !resident.IsQuestProtected && resident.NativeIdentity==null &&
                    resident.AgeGroup!=ResidentAgeGroup.Child && resident.HomeKey!=room.EntryDoor.buildingKey)
                { selected=plan; break; }
            }
            if(selected==null) throw new InvalidOperationException("No living non-native visitor is available for post-death shop regression.");
            var selectedType=selected.GetType();
            var visitor=(Resident)selectedType.GetField("Resident").GetValue(selected);
            var scheduleField=selectedType.GetField("Schedule");
            var originalSchedule=scheduleField.GetValue(selected);
            var routine=(ResidentRoutine)selectedType.GetField("Routine").GetValue(selected);
            int originalDestination=routine.DestinationKey;
            int building=room.EntryDoor.buildingKey;
            var schedule=DailySchedule.DayVisit(visitor.HomeKey,building,
                new[]{new RoutePoint(0,0),new RoutePoint(1,0)},540,1080,30,1.3,12,visitor.AppearanceSeed,360,1320);
            double start=0,end=0;
            for(int minute=540;minute<1080;minute++) if(schedule.TryVisitWindow(minute*60,out start,out end)) break;
            if(end<=start) throw new InvalidOperationException("Post-death shop visitor has no scheduled visit window.");
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            double day=Math.Floor(now/86400)*86400;
            start+=day; end+=day;
            if(start+1<=now) { start+=86400; end+=86400; }
            int chosenDay=0;
            while((StableSeed.Combine(visitor.AppearanceSeed,(uint)building,(uint)Math.Floor(start/86400),0x53484f50)&1)!=0)
            {
                if(++chosenDay>64) throw new InvalidOperationException("Could not schedule a purchase visit after keeper death.");
                start+=86400; end+=86400;
            }
            int nativeCountBefore=((WorldState)adapter.GetSaveData()).NativePeople.FindAll(p=>p.SettlementId==keeper.SettlementId).Count;
            int serviceCountBefore=CountKeeperStatics(room,keeper);
            if(serviceCountBefore!=1) throw new InvalidOperationException("Keeper fixture has unexpected native service-flat count before visitor: "+serviceCountBefore);
            scheduleField.SetValue(selected,schedule); routine.DestinationKey=building;
            typeof(StreetPopulation).GetField("shopVisitDay",flags).SetValue(streets,-1d);
            try
            {
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((long)(start+1-now));
                yield return null; yield return null;
                float deadline=Time.realtimeSinceStartup+15;
                AlarmPerson observer=null;
                while(Time.realtimeSinceStartup<deadline)
                {
                    AssertKeeperStillDead(adapter,room,keeper,nativeCountBefore,serviceCountBefore,"visitor discovery");
                    var world=(WorldState)adapter.GetSaveData();
                    var alarm=world.AlarmIncidents.Find(a=>a.VictimId==keeper.Id);
                    observer=alarm==null ? null : alarm.People.Find(p=>p.ResidentId==visitor.Id);
                    if(observer!=null && observer.Escape!=null) break;
                    yield return null;
                }
                if(observer==null || !observer.CorpseObserver || observer.DirectWitness || observer.Escape==null)
                    throw new InvalidOperationException("Arriving shopper did not discover corpse and plan neutral physical flight.");
                // Let the player build advance normally through the physical doorway.
                deadline=Time.realtimeSinceStartup+30;
                while(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()<observer.Escape.ExitAt && Time.realtimeSinceStartup<deadline)
                {
                    AssertKeeperStillDead(adapter,room,keeper,nativeCountBefore,serviceCountBefore,"visitor fleeing");
                    yield return null;
                }
                yield return null; yield return null;
                if(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()<observer.Escape.ExitAt || streets.FindActor(visitor.Id))
                    throw new InvalidOperationException("Corpse observer failed to leave the loaded shop.");
                AssertKeeperStillDead(adapter,room,keeper,nativeCountBefore,serviceCountBefore,"visitor exited");
                var guards=adapter.GetComponent<GuardPopulation>();
                string neutralReport="inn-alarm:murder:"+keeper.Id;
                if(guards.Roster.Reports.Exists(r=>r.Id==neutralReport))
                    throw new InvalidOperationException("Corpse observer notified guards before physical contact.");
                var priorCrime=GameManager.Instance.PlayerEntity.CrimeCommitted;
                var clock=DaggerfallUnity.Instance.WorldTime.Now;
                double advance=observer.Escape.ReportAt+1-clock.ToSeconds();
                if(advance>0) clock.RaiseTime((long)Math.Ceiling(advance));
                deadline=Time.realtimeSinceStartup+10;
                GuardReport neutral=null;
                while(Time.realtimeSinceStartup<deadline)
                {
                    neutral=guards.Roster.Reports.Find(r=>r.Id==neutralReport && r.GuardIds.Count>0);
                    if(neutral!=null) break;
                    yield return null;
                }
                if(neutral==null || !neutral.Investigation || GameManager.Instance.PlayerEntity.CrimeCommitted!=priorCrime)
                    throw new InvalidOperationException("Corpse observer contact failed neutral dispatch or changed player crime.");
                if(neutral.HasArrival) throw new InvalidOperationException("Corpse observer report teleported guards into shop.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CORPSE_VISITOR_CONTACT_PASS: unloaded street route reaches posted guard; neutral physical-contact dispatch; no instant arrival or new Murder charge.");
                var player=GameManager.Instance.PlayerEntity;
                var concealment=player.MagicalConcealmentFlags;
                try
                {
                    player.CrimeCommitted=PlayerEntity.Crimes.None;
                    player.MagicalConcealmentFlags=concealment | MagicalConcealmentFlags.InvisibleTrue;
                    double arrival=double.MaxValue;
                    foreach(var id in neutral.GuardIds)
                    { var guard=guards.Roster.Guards.Find(g=>g.Id==id); if(guard!=null) arrival=Math.Min(arrival,guard.ArrivesAt); }
                    advance=arrival+1-clock.ToSeconds();
                    if(advance>0 && arrival<double.MaxValue) clock.RaiseTime((long)Math.Ceiling(advance));
                    bool inspected=false,cleared=false;
                    deadline=Time.realtimeSinceStartup+60;
                    while(Time.realtimeSinceStartup<deadline)
                    {
                        var world=(WorldState)adapter.GetSaveData();
                        var body=world.AlarmIncidents.Find(a=>a.VictimId==keeper.Id).Corpse;
                        if(body.InspectionGuardId!=null && body.InspectionRemainingRealSeconds>0 && body.InspectionRemainingRealSeconds<5) inspected=true;
                        if(body.Cleared) { cleared=true; break; }
                        yield return null;
                    }
                    if(!inspected || !cleared) throw new InvalidOperationException("Neutral guard failed physical approach and five-second corpse cleanup.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NEUTRAL_INSPECTION_PASS: visitor report guard entered shop, approached body, inspected and cleared corpse.");
                    yield return VerifyGuardedArrival(adapter,streets,room,visitor.Id);
                }
                finally { player.CrimeCommitted=priorCrime; player.MagicalConcealmentFlags=concealment; }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_VISITOR_PASS: scheduled visitor discovered corpse, fled and exited instead of browsing/haggling; keeper stays dead; neutral posted-guard route; visitor="+visitor.Id);
            }
            finally { scheduleField.SetValue(selected,originalSchedule); routine.DestinationKey=originalDestination; }
        }
        private static IEnumerator VerifyGuardedArrival(PeopleOfTheIliacBayMod adapter,StreetPopulation streets,DaggerfallInterior room,string excluded)
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            object chosen=null; Resident resident=null;
            foreach(var item in (IEnumerable)typeof(StreetPopulation).GetField("plans",flags).GetValue(streets))
            {
                var candidate=(Resident)item.GetType().GetField("Resident").GetValue(item);
                if(candidate.Id!=excluded && candidate.IsAlive && candidate.NativeIdentity==null &&
                    candidate.HomeKey!=room.EntryDoor.buildingKey && candidate.AgeGroup!=ResidentAgeGroup.Child)
                { chosen=item; resident=candidate; break; }
            }
            if(chosen==null) throw new InvalidOperationException("No guarded-arrival test visitor.");
            var scheduleField=chosen.GetType().GetField("Schedule"); var previous=scheduleField.GetValue(chosen);
            var routine=(ResidentRoutine)chosen.GetType().GetField("Routine").GetValue(chosen);
            int destination=routine.DestinationKey;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            int minute=(int)(now%86400/60)+1;
            var schedule=DailySchedule.DayVisit(resident.HomeKey,room.EntryDoor.buildingKey,
                new[]{new RoutePoint(),new RoutePoint(1,0)},minute,minute+10,10,1.3,12,0,0,1439);
            scheduleField.SetValue(chosen,schedule); routine.DestinationKey=room.EntryDoor.buildingKey;
            try
            {
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((long)(minute*60-now%86400+1));
                var store=(WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store",flags).GetValue(adapter);
                VisitDeparture departure=null; float deadline=Time.realtimeSinceStartup+10;
                while(Time.realtimeSinceStartup<deadline)
                {
                    departure=store.VisitDepartureFor(resident.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                    if(departure!=null) break;
                    yield return null;
                }
                if(departure==null) throw new InvalidOperationException("Guarded scene failed to reject arriving shopper after corpse cleanup.");
                deadline=Time.realtimeSinceStartup+30;
                while(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()<departure.Motion.ExitAt && Time.realtimeSinceStartup<deadline) yield return null;
                yield return null; yield return null;
                if(streets.FindActor(resident.Id)) throw new InvalidOperationException("Rejected visitor stayed inside guarded shop.");
                var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); restored.Restore(store.Snapshot());
                if(restored.VisitDepartureFor(resident.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())==null)
                    throw new InvalidOperationException("Rejected visitor departure did not survive snapshot restore.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARDED_VISITOR_PASS: cleared guarded shop rejects arrival; visitor exits; cancellation survives restore.");
            }
            finally { scheduleField.SetValue(chosen,previous); routine.DestinationKey=destination; }
        }
        private static int CountKeeperStatics(DaggerfallInterior room,NativePerson keeper)
        {
            int count=0;
            foreach(var npc in room.GetComponentsInChildren<StaticNPC>(true))
                if(npc.Data.hash==keeper.NativeHash && npc.Data.buildingKey==keeper.WorkKey) count++;
            return count;
        }
        private static void AssertKeeperStillDead(PeopleOfTheIliacBayMod adapter,DaggerfallInterior room,NativePerson keeper,
            int nativeCount,int serviceCount,string stage)
        {
            var world=(WorldState)adapter.GetSaveData();
            int keeperMatches=world.NativePeople.FindAll(p=>p.Id==keeper.Id).Count;
            var current=world.NativePeople.Find(p=>p.Id==keeper.Id);
            var npc=Array.Find(room.GetComponentsInChildren<StaticNPC>(true),n=>n.Data.hash==keeper.NativeHash && n.Data.buildingKey==keeper.WorkKey);
            int currentServiceCount=CountKeeperStatics(room,keeper);
            int currentNativeCount=world.NativePeople.FindAll(p=>p.SettlementId==keeper.SettlementId).Count;
            if(keeperMatches!=1 || current==null || current.IsAlive || !npc || npc.gameObject.activeSelf ||
                currentNativeCount!=nativeCount || currentServiceCount!=serviceCount)
                throw new InvalidOperationException("Shop visitor changed/recreated dead keeper at "+stage+"; keeperMatches="+keeperMatches+
                    "; alive="+(current!=null && current.IsAlive)+"; active="+(npc && npc.gameObject.activeSelf)+
                    "; nativeCount="+currentNativeCount+"/"+nativeCount+"; serviceStatics="+currentServiceCount+"/"+serviceCount);
        }
        private static IEnumerator VerifyDoorPermissions()
        {
            var origin = new Vector3(5000, 100, 5000);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var doorObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.transform.position = origin + Vector3.forward * 0.7f + Vector3.up;
                wall.transform.localScale = new Vector3(2, 2, 0.1f);
                doorObject.transform.position = origin + Vector3.forward * 1.4f + Vector3.up;
                doorObject.transform.localScale = new Vector3(1, 2, 0.1f);
                var door = doorObject.AddComponent<DaggerfallActionDoor>(); door.PlaySounds = false;
                yield return null; Physics.SyncTransforms();
                var body = doorObject.GetComponent<BoxCollider>();
                InteriorGeometry.OpenOrdinaryDoors(origin, origin + Vector3.forward * 2);
                if (body.isTrigger) throw new InvalidOperationException("Civilian opened door through intervening wall.");
                wall.SetActive(false); Physics.SyncTransforms(); door.CurrentLockValue = 10;
                InteriorGeometry.OpenOrdinaryDoors(origin, origin + Vector3.forward * 2);
                if (body.isTrigger) throw new InvalidOperationException("Civilian opened locked door.");
                door.CurrentLockValue = 0;
                var action = doorObject.AddComponent<DaggerfallAction>();
                InteriorGeometry.OpenOrdinaryDoors(origin, origin + Vector3.forward * 2);
                if (body.isTrigger) throw new InvalidOperationException("Civilian opened scripted door.");
                UnityEngine.Object.DestroyImmediate(action);
                InteriorGeometry.OpenOrdinaryDoors(origin, origin + Vector3.forward * 2);
                if (!body.isTrigger) throw new InvalidOperationException("Civilian did not open ordinary unlocked door.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CIVILIAN_DOORS_PASS: wall, lock and script barriers retained; ordinary unlocked door opens.");
            }
            finally { UnityEngine.Object.Destroy(wall); UnityEngine.Object.Destroy(doorObject); }
        }
    }
}
