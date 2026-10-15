using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class GuardDefenseProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var controller = UnityEngine.Object.FindObjectOfType<GuardPopulation>();
            var option = typeof(GuardPopulation).GetField("EnemyDefense");
            if (option == null) throw new InvalidOperationException("Finite guard enemy defense is missing.");
            var adapter = controller.GetComponent<LivingCitiesMod>();
            typeof(LivingCitiesMod).GetField("exteriorGuardsEnabled", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(adapter,true);
            controller.NightPatrols = false; option.SetValue(controller,true);
            game.PlayerEntity.GodMode = true; game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
            game.PlayerEntity.NoTargetMode = false;
            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((12 * 3600 - clock.ToSeconds() % 86400 + 86400) % 86400));
            float deadline = Time.realtimeSinceStartup + 25;
            while ((controller.Roster == null || controller.Roster.Patrols.Count != 0) && Time.realtimeSinceStartup < deadline) yield return null;
            var roster = controller.Roster;
            if (roster == null) throw new InvalidOperationException("Defense roster did not initialize.");
            var guard = roster.Guards.Find(g => g.IsAlive && g.Duty == GuardDuty.Posted);
            if (guard == null) throw new InvalidOperationException("No available defender.");
            var geometry = new TownGeometry(game.StreamingWorld.GetCurrentBuildingDirectory(), game.StreamingWorld.GetCurrentCityNavigation(),false);
            var ground = geometry.ScenePoint(new RoutePoint(guard.Post.X,guard.Post.Z));
            game.PlayerMotor.transform.position = ground + Vector3.up + Vector3.forward * 8;
            var enemy = GameObjectHelper.CreateFoeGameObjects(ground + Vector3.up + Vector3.right * 2,MobileTypes.Rat,1,MobileReactions.Passive)[0];
            ulong enemyId = enemy.GetComponent<DaggerfallEnemy>().LoadID;
            enemy.SetActive(true);
            bool saved = false, loaded = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSave += onSave; SaveLoadManager.OnLoad += onLoad;
            bool infighting = DaggerfallUnity.Settings.EnemyInfighting;
            GameObject blocker = null;
            try
            {
                DaggerfallUnity.Settings.EnemyInfighting = false;
                yield return new WaitForSecondsRealtime(2);
                if (roster.Reports.Exists(r => r.ThreatId != null && r.GuardIds.Count > 0))
                    throw new InvalidOperationException("Pacified enemy caused guard defense.");
                var target = enemy.GetComponent<DaggerfallEntityBehaviour>();
                target.Entity.MaxHealth = 500; target.Entity.SetHealth(500,true);
                // Keep the controlled enemy in place so the test observes native guard movement and damage.
                enemy.GetComponent<EnemyMotor>().IsHostile = true;
                enemy.GetComponent<EnemyMotor>().enabled = false;
                enemy.GetComponent<EnemyAttack>().enabled = false;
                enemy.GetComponent<EnemySenses>().enabled = false;
                enemy.SetActive(false);
                yield return new WaitForSecondsRealtime(1.2f);
                if (roster.Reports.Exists(r => r.ThreatId != null && r.GuardIds.Count > 0))
                    throw new InvalidOperationException("Inactive hostile enemy caused defense.");
                enemy.SetActive(true);
                blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.transform.position = enemy.transform.position;
                blocker.transform.localScale = new Vector3(0.8f,3,0.8f);
                Physics.SyncTransforms();
                yield return new WaitForSecondsRealtime(1.2f);
                if (roster.Reports.Exists(r => r.ThreatId != null && r.GuardIds.Count > 0))
                    throw new InvalidOperationException("Guard detected hostile enemy through solid wall.");
                UnityEngine.Object.Destroy(blocker); blocker = null;
                yield return null;
                deadline = Time.realtimeSinceStartup + 20;
                GuardActor defender = null;
                while (Time.realtimeSinceStartup < deadline)
                {
                    foreach (var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                        if (actor.CombatReady && actor.GetComponent<EnemySenses>().Target == target) defender = actor;
                    if (defender && target.Entity.CurrentHealth < 500) break;
                    yield return null;
                }
                if (!defender || target.Entity.CurrentHealth >= 500)
                    throw new InvalidOperationException("Finite guard did not deal native damage to hostile enemy; reports=" + roster.Reports.Count + "; health=" + target.Entity.CurrentHealth);
                if (defender.HaltWarnings != 0 || defender.GetComponent<EnemySounds>().enabled)
                    throw new InvalidOperationException("Guard shouted Halt at a creature.");
                if (game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None || roster.Guards.Count != 15)
                    throw new InvalidOperationException("Defense charged innocent player or created extra guards.");
                string id = defender.GuardId;
                var assigned = roster.Guards.Find(g => g.Id == id);
                if (roster.Reports.Find(r => r.Id == assigned.IncidentId).ThreatId == null)
                    throw new InvalidOperationException("Enemy engagement lost saved defense identity.");
                var body = defender.GetComponent<DaggerfallEntityBehaviour>();
                body.Entity.SetHealth(Math.Max(1,(int)(body.Entity.MaxHealth*0.8)),true);
                var now = DaggerfallUnity.Instance.WorldTime.Now;
                defender.Capture((double)now.ToSeconds()+now.Second-Math.Floor(now.Second));
                double woundedHealth = assigned.HealthFraction;
                string threatId = roster.Reports.Find(r => r.Id == assigned.IncidentId).ThreatId;
                game.SaveLoadManager.Save(game.PlayerEntity.Name,"Living Cities - guard defense");
                deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Defense save timed out.");
                int key = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name,"Living Cities - guard defense");
                game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 50;
                while ((!loaded || controller.Roster == null) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || controller.Roster == null) throw new InvalidOperationException("Defense reload timed out.");
                roster = controller.Roster; assigned = roster.Guards.Find(g => g.Id == id);
                enemy = null;
                foreach (var candidate in UnityEngine.Object.FindObjectsOfType<DaggerfallEnemy>())
                    if (candidate.LoadID == enemyId) enemy = candidate.gameObject;
                if (!enemy) throw new InvalidOperationException("Native enemy identity did not restore for defense fixture.");
                target = enemy.GetComponent<DaggerfallEntityBehaviour>();
                enemy.GetComponent<EnemyMotor>().enabled = false;
                enemy.GetComponent<EnemyAttack>().enabled = false;
                enemy.GetComponent<EnemySenses>().enabled = false;
                deadline = Time.realtimeSinceStartup + 5;
                defender = null;
                while (Time.realtimeSinceStartup < deadline)
                {
                    foreach (var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                        if (actor.GuardId == id && actor.CombatReady && actor.GetComponent<EnemySenses>().Target == target) defender = actor;
                    if (defender) break;
                    yield return null;
                }
                if (!defender || Math.Abs(assigned.HealthFraction-woundedHealth)>0.02 ||
                    roster.Reports.Find(r => r.Id == assigned.IncidentId).ThreatId != threatId ||
                    game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None)
                    throw new InvalidOperationException("Reload lost defensive target/health or accused innocent player.");
                yield return VerifyOutdoorWelfare(adapter,controller,defender,target);
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Theft;
                target.Entity.SetHealth(0);
                deadline = Time.realtimeSinceStartup + 8;
                while (assigned.Duty == GuardDuty.Engaged && Time.realtimeSinceStartup < deadline) yield return null;
                if (assigned.Duty != GuardDuty.Returning && assigned.Duty != GuardDuty.Posted)
                    throw new InvalidOperationException("Defender did not physically return after target death.");
                if (game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.Theft)
                    throw new InvalidOperationException("Enemy defense cleared existing bounty.");
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
                deadline = Time.realtimeSinceStartup + 15;
                while (assigned.Duty != GuardDuty.Posted && Time.realtimeSinceStartup < deadline) yield return null;
                if (assigned.Duty != GuardDuty.Posted) throw new InvalidOperationException("Defender never reached original post.");
                var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                geometry = new TownGeometry(directory,game.StreamingWorld.GetCurrentCityNavigation(),false);
                ground = geometry.ScenePoint(new RoutePoint(assigned.Post.X,assigned.Post.Z));
                if (enemy) UnityEngine.Object.Destroy(enemy);
                enemy = GameObjectHelper.CreateFoeGameObjects(ground+Vector3.up+Vector3.right*2,MobileTypes.Thief)[0];
                enemy.GetComponent<DaggerfallEnemy>().LoadID = 0; enemy.SetActive(true);
                var questEnemy=enemy.AddComponent<DaggerfallWorkshop.Game.Questing.QuestResourceBehaviour>();
                questEnemy.enabled=false; questEnemy.IsAttackableByAI=false;
                enemy.GetComponent<EnemySenses>().QuestBehaviour=questEnemy;
                yield return null;
                enemy.GetComponent<DaggerfallEntityBehaviour>().Entity.MaxHealth = 500;
                enemy.GetComponent<DaggerfallEntityBehaviour>().Entity.SetHealth(500,true);
                enemy.GetComponent<EnemyMotor>().enabled = false; enemy.GetComponent<EnemySenses>().enabled = false;
                enemy.GetComponent<EnemyAttack>().enabled = false;
                deadline = Time.realtimeSinceStartup + 20; defender = null;
                while (Time.realtimeSinceStartup < deadline)
                {
                    foreach (var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                        if (actor.DefenseTarget && actor.CombatReady) defender = actor;
                    if (defender && enemy.GetComponent<DaggerfallEntityBehaviour>().Entity.CurrentHealth<500) break;
                    yield return null;
                }
                if (!defender) throw new InvalidOperationException("Assault check has no active defender.");
                if(enemy.GetComponent<DaggerfallEntityBehaviour>().Entity.CurrentHealth>=500 || questEnemy.IsAttackableByAI)
                    throw new InvalidOperationException("Guard did not damage hostile quest-owned attacker while preserving its AI flag.");
                Debug.Log("LIVING_CITIES_QUEST_ATTACKER_DEFENSE_PASS: native damage to hostile quest-tagged human; quest AI flag preserved.");
                if (defender.HaltWarnings != 1 || defender.GetComponent<EnemySounds>().enabled)
                    throw new InvalidOperationException("Hostile human did not receive exactly one initial Halt warning.");
                enemy.transform.position = defender.transform.position + Vector3.forward;
                Physics.SyncTransforms();
                var assaultSenses = defender.GetComponent<EnemySenses>();
                deadline = Time.realtimeSinceStartup + 3;
                while ((!assaultSenses.TargetInSight || assaultSenses.DistanceToTarget > 2) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!assaultSenses.TargetInSight || assaultSenses.DistanceToTarget > 2)
                    throw new InvalidOperationException("Assault fixture did not establish an adjacent visible enemy target.");
                defender.GetComponent<EnemyMotor>().MakeEnemyHostileToAttacker(game.PlayerEntityBehaviour);
                deadline = Time.realtimeSinceStartup + 4;
                while (Time.realtimeSinceStartup < deadline && (defender.DefenseTarget ||
                    defender.GetComponent<EnemySenses>().Target != game.PlayerEntityBehaviour)) yield return null;
                if (defender.DefenseTarget || defender.GetComponent<EnemySenses>().Target != game.PlayerEntityBehaviour ||
                    game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.Assault)
                    throw new InvalidOperationException("Player assault did not transfer defender to native criminal response.");
                Debug.Log("LIVING_CITIES_GUARD_DEFENSE_PASS: pacified/inactive/blocked ignored; native enemy damage with infighting off; real enemy/guard reload and wounded health; innocent player uncharged; death return; prior crime retained; creatures silent; human Halt once; player assault takeover; save="+key);
            }
            finally
            {
                DaggerfallUnity.Settings.EnemyInfighting = infighting;
                SaveLoadManager.OnSave -= onSave; SaveLoadManager.OnLoad -= onLoad;
                if (blocker) UnityEngine.Object.Destroy(blocker);
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
                if (enemy) UnityEngine.Object.Destroy(enemy);
            }
        }
        private static IEnumerator VerifyOutdoorWelfare(LivingCitiesMod mod,GuardPopulation controller,GuardActor guard,DaggerfallEntityBehaviour target)
        {
            var game=GameManager.Instance; var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry=new TownGeometry(directory,game.StreamingWorld.GetCurrentCityNavigation(),false);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var store=(WorldStateStore)typeof(LivingCitiesMod).GetField("store",flags).GetValue(mod);
            var origin=directory.transform.InverseTransformPoint(guard.transform.position);
            bool placed=false; bool god=game.PlayerEntity.GodMode; int health=game.PlayerEntity.CurrentHealth;
            var windowField=typeof(GuardPopulation).GetField("welfareWindow",flags);
            try
            {
                for(int direction=0;direction<8;direction++)
                {
                    float angle=direction*Mathf.PI/4;
                    var destination=new SavedRoutePoint { X=origin.x+Math.Cos(angle)*6,Z=origin.z+Math.Sin(angle)*6 };
                    var candidate=geometry.ScenePoint(new RoutePoint(destination.X,destination.Z))+Vector3.up;
                    if(!CivilianResponses.Sees(guard.transform.position,candidate,20) ||
                        !CivilianResponses.Sees(guard.transform.position,target.transform.position,20) ||
                        geometry.RouteBetweenPoints(new SavedRoutePoint { X=origin.x,Z=origin.z },destination).Count<2) continue;
                    game.PlayerMotor.transform.position=candidate; placed=true; break;
                }
                if(!placed) throw new InvalidOperationException("No surveyed outdoor welfare player position.");
                game.PlayerEntity.GodMode=false; game.PlayerEntity.SetHealth(Math.Max(1,game.PlayerEntity.MaxHealth/2),true);
                Physics.SyncTransforms();
                var start=guard.transform.position;
                target.DamageHealthFromSource(game.PlayerEntityBehaviour,target.Entity.CurrentHealth,false,Vector3.zero);
                string id="native:"+target.GetComponent<DaggerfallEnemy>().LoadID;
                var record=store.GetDefenseCombatant(id);
                if(record==null || record.Death==null || !record.Death.LootEntitled ||
                    !record.Death.InformedGuards.Contains(guard.GuardId))
                    throw new InvalidOperationException("Outdoor witnessed kill did not establish welfare eligibility.");
                DaggerfallWorkshop.Game.UserInterfaceWindows.DaggerfallMessageBox window=null;
                float deadline=Time.realtimeSinceStartup+22;
                while(window==null && Time.realtimeSinceStartup<deadline)
                { window=windowField.GetValue(controller) as DaggerfallWorkshop.Game.UserInterfaceWindows.DaggerfallMessageBox; yield return null; }
                if(window==null || Vector3.Distance(start,guard.transform.position)<1 ||
                    Vector3.Distance(guard.transform.position,game.PlayerMotor.transform.position)>=2.5f)
                    throw new InvalidOperationException("Outdoor responder failed physical welfare approach.");
                window.CloseWindow(); yield return null; yield return null;
                if(!record.Death.WelfareConsumed || record.Death.PotionGranted || game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None)
                    throw new InvalidOperationException("Outdoor welfare changed innocence or failed once-only decline.");
                Debug.Log("LIVING_CITIES_OUTDOOR_WELFARE_PASS: actual defender witnesses native lawful kill, physically approaches and completes courtesy without charging player.");
            }
            finally
            {
                DaggerfallUI.Instance.PopToHUD(); game.PlayerEntity.GodMode=god; game.PlayerEntity.SetHealth(health,true);
            }
        }
    }
}
