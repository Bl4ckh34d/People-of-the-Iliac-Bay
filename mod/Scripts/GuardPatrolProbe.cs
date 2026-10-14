using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class GuardPatrolProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var controller = UnityEngine.Object.FindObjectOfType<GuardPopulation>();
            var enabled = typeof(GuardPopulation).GetField("NightPatrols");
            if (enabled == null) throw new InvalidOperationException("Playable paired night patrols are missing.");
            var adapter = controller.GetComponent<LivingCitiesMod>();
            typeof(LivingCitiesMod).GetField("exteriorGuardsEnabled", BindingFlags.Instance|BindingFlags.NonPublic).SetValue(adapter,true);
            enabled.SetValue(controller, true); controller.MaximumPatrolPairs = 2;
            game.PlayerEntity.GodMode = true; game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((20 * 3600 - clock.ToSeconds() % 86400 + 86400) % 86400));
            float deadline = Time.realtimeSinceStartup + 75;
            while ((controller.Roster == null || controller.Roster.Patrols.Count == 0) && Time.realtimeSinceStartup < deadline) yield return null;
            if (controller.Roster == null || controller.Roster.Patrols.Count == 0) throw new InvalidOperationException("No surveyed paired patrol started in the test town.");
            var plan = controller.Roster.Patrols[0].Plan;
            string leader = plan.LeaderId, follower = plan.FollowerId;
            clock.RaiseTime((float)Math.Max(0, plan.CircuitStartsAt + 60 - ExactNow()));
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry = new TownGeometry(directory,game.StreamingWorld.GetCurrentCityNavigation(),false);
            var point = controller.Roster.Position(leader,ExactNow());
            game.PlayerMotor.transform.position = geometry.ScenePoint(new RoutePoint(point.X,point.Z)) + Vector3.up + Vector3.forward*4;
            yield return new WaitForSecondsRealtime(1.2f);
            GuardActor a = Find(leader), b = Find(follower);
            if (!a || !b) throw new InvalidOperationException("Both patrol actors must appear near the player.");
            float gap = Vector3.Distance(a.transform.position,b.transform.position);
            if (gap < 0.95f || gap > 3f) throw new InvalidOperationException("Patrol pair separated or overlapped; gap="+gap);
            var framesA = new HashSet<int>(); var framesB = new HashSet<int>();
            var startA = a.transform.position;
            deadline = Time.realtimeSinceStartup+1;
            while(Time.realtimeSinceStartup<deadline)
            {
                framesA.Add(a.GetComponentInChildren<DaggerfallMobileUnit>().CurrentFrame);
                framesB.Add(b.GetComponentInChildren<DaggerfallMobileUnit>().CurrentFrame);
                yield return null;
            }
            if (framesA.Count<2 || framesB.Count<2 || Vector3.Distance(startA,a.transform.position)<0.5f)
                throw new InvalidOperationException("Both patrol guards must walk with animated sprites.");
            if (!a.GetComponentInChildren<Light>() || !a.GetComponentInChildren<Light>().enabled)
                throw new InvalidOperationException("Nearby night patrol must carry a lit torch.");
            bool visible=false; game.PlayerMotor.CancelMovement=true;
            foreach(var direction in new[] { Vector3.forward,Vector3.back,Vector3.right,Vector3.left })
            {
                var center=(a.transform.position+b.transform.position)*0.5f;
                game.PlayerMotor.transform.position=center+direction*4;
                yield return new WaitForSecondsRealtime(0.1f);
                SleepProbe.AimCamera((a.transform.position+b.transform.position)*0.5f+Vector3.up*0.3f);
                yield return new WaitForEndOfFrame();
                if(Sight(a.transform.position+Vector3.up*0.3f)&&Sight(b.transform.position+Vector3.up*0.3f))
                { visible=true; break; }
            }
            if(!visible) throw new InvalidOperationException("No unobstructed camera view of both patrolling guards.");
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,"LivingCities-night-patrol.png"));
            bool saved=false,loaded=false;
            SaveLoadManager.OnSaveEventHandler onSave=ignored=>saved=true;
            SaveLoadManager.OnLoadEventHandler onLoad=ignored=>loaded=true;
            SaveLoadManager.OnSave+=onSave; SaveLoadManager.OnLoad+=onLoad;
            try
            {
                game.SaveLoadManager.Save(game.PlayerEntity.Name,"Living Cities - night patrol");
                deadline=Time.realtimeSinceStartup+30;
                while(!saved&&Time.realtimeSinceStartup<deadline) yield return null;
                if(!saved) throw new InvalidOperationException("Patrol save timed out.");
                int key=game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name,"Living Cities - night patrol");
                game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                deadline=Time.realtimeSinceStartup+50;
                while((!loaded||controller.Roster==null||!Find(leader))&&Time.realtimeSinceStartup<deadline) yield return null;
                if(!loaded||controller.Roster==null) throw new InvalidOperationException("Patrol reload timed out.");
                yield return new WaitForSecondsRealtime(0.5f);
                var restored=controller.Roster.Patrols.Find(p=>p.Plan.LeaderId==leader);
                if(restored==null||Math.Abs(restored.Plan.StartsAt-plan.StartsAt)>0.001||restored.InterruptedAt.HasValue)
                    throw new InvalidOperationException("Real reload rerolled patrol phase or interruption: missing="+(restored==null)+
                        "; originalStart="+plan.StartsAt.ToString("R")+"; savedStart="+(restored==null?"none":restored.Plan.StartsAt.ToString("R"))+
                        "; stop="+(restored==null?"none":restored.InterruptedAt.ToString())+"; enabled="+controller.NightPatrols+
                        "; hour="+DaggerfallUnity.Instance.WorldTime.Now.Hour+"; now="+ExactNow());
                a=Find(leader); b=Find(follower);
                if(!a||!b) throw new InvalidOperationException("Reload lost a patrol actor.");
                point=controller.Roster.Position(leader,ExactNow());
                var actual=game.StreamingWorld.GetCurrentBuildingDirectory().transform.InverseTransformPoint(a.transform.position);
                if(Vector2.Distance(new Vector2((float)point.X,(float)point.Z),new Vector2(actual.x,actual.z))>0.2f)
                    throw new InvalidOperationException("Reloaded patrol actor disagrees with saved schedule.");
                controller.NightPatrols=false;
                yield return new WaitForSecondsRealtime(1.2f);
                restored=controller.Roster.Patrols.Find(p=>p.Plan.LeaderId==leader);
                if(restored==null||!restored.InterruptedAt.HasValue) throw new InvalidOperationException("Patrol toggle did not start physical return.");
                point=controller.Roster.Position(leader,ExactNow());
                game.PlayerMotor.transform.position=a.transform.position+Vector3.forward;
                game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Theft;
                game.PlayerEntity.HaveShownSurrenderToGuardsDialogue=true;
                game.PlayerEntity.SpawnCityGuards(true);
                var guard=controller.Roster.Guards.Find(g=>g.Id==leader);
                if(guard.IncidentId==null||guard.ResponseOrigin==null||
                    Vector2.Distance(new Vector2((float)point.X,(float)point.Z),new Vector2((float)guard.ResponseOrigin.X,(float)guard.ResponseOrigin.Z))>0.25f)
                    throw new InvalidOperationException("Crime response did not use the actual returning patrol position.");
                if(controller.Roster.Guards.Count!=10) throw new InvalidOperationException("Patrol or alarm created extra guard identities.");
                Debug.Log("LIVING_CITIES_GUARD_PATROL_PASS: surveyed pair, spacing, both walking animations, torch, real phase reload, toggle return and crime from actual position; save="+key);
            }
            finally { SaveLoadManager.OnSave-=onSave; SaveLoadManager.OnLoad-=onLoad; game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None; }
        }
        private static double ExactNow()
        { var now=DaggerfallUnity.Instance.WorldTime.Now; return (double)now.ToSeconds()+now.Second-Math.Floor(now.Second); }
        private static GuardActor Find(string id)
        { foreach(var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>()) if(actor.GuardId==id) return actor; return null; }
        private static bool Sight(Vector3 target)
        {
            var camera=Camera.main; var view=camera.WorldToViewportPoint(target);
            if(view.z<=0||view.x<0.05f||view.x>0.95f||view.y<0.05f||view.y>0.95f) return false;
            foreach(var hit in Physics.RaycastAll(camera.transform.position,target-camera.transform.position,
                Vector3.Distance(camera.transform.position,target),~0,QueryTriggerInteraction.Ignore))
                if(!hit.collider.GetComponentInParent<DaggerfallEntityBehaviour>()&&!hit.collider.GetComponentInParent<PlayerMotor>()) return false;
            return true;
        }
    }
}
