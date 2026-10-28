using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class GuardWallTrespassProbe
    {
        // Resume only the remaining NPC case. Native day surrender, refusal and
        // direct-hit evidence already passed and are not prerequisites again.
        internal static IEnumerator RunNonPlayer(GuardPopulation controller)
        {
            var game=GameManager.Instance;var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry=Field<TownGeometry>(controller,"geometry");
            var guard=controller.Roster.Guards.Find(value=>value.IsAlive && GuardShifts.OnDuty(value,Now()) &&
                value.Refuge!=null && value.Refuge.WallRoute!=null && value.Refuge.Evaluate(Now()).Phase==RefugePhase.WallPosted);
            if(guard==null)throw new InvalidOperationException("NPC wall case requires one actually reached current-shift post.");
            var wall=guard.Refuge.WallRoute;var last=wall[wall.Count-1];var previous=wall[wall.Count-2];
            var post=directory.transform.TransformPoint(new Vector3((float)last.X,(float)last.Y,(float)last.Z));
            var before=directory.transform.TransformPoint(new Vector3((float)previous.X,(float)previous.Y,(float)previous.Z));
            var intruder=post+(before-post).normalized*Mathf.Min(8,Vector3.Distance(before,post)*.7f);
            var local=directory.transform.InverseTransformPoint(intruder);
            var observer=geometry.ScenePoint(new RoutePoint(local.x,local.z))+Vector3.up;
            var oldPosition=game.PlayerMotor.transform.position;var oldCrime=game.PlayerEntity.CrimeCommitted;
            bool oldGod=game.PlayerEntity.GodMode;GameObject npc=null;
            try
            {
                game.PlayerMotor.transform.position=observer;game.PlayerEntity.GodMode=true;
                game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;
                yield return new WaitForSecondsRealtime(1);
                var actor=Find(guard.Id);if(!actor)throw new InvalidOperationException("Reached wall post lacks its actual guard body.");
                actor.EndCombat();
                npc=GameObjectHelper.CreateFoeGameObjects(intruder+Vector3.up,MobileTypes.Barbarian,1,MobileReactions.Passive)[0];
                npc.name="Owned non-player wall-trespass fixture";npc.GetComponent<DaggerfallEnemy>().LoadID=0;
                npc.GetComponent<EnemyMotor>().enabled=false;npc.GetComponent<EnemySenses>().enabled=false;
                npc.GetComponent<EnemyAttack>().enabled=false;npc.SetActive(true);
                var target=npc.GetComponent<DaggerfallEntityBehaviour>();
                yield return null;Physics.SyncTransforms();
                if(!controller.IsWallIntruder(target))throw new InvalidOperationException("Active non-player on actual wall surface was rejected.");
                actor.transform.forward=(target.transform.position-actor.transform.position).normalized;
                int arrows=guard.ArrowsRemaining;float deadline=Time.realtimeSinceStartup+8;
                while(guard.ArrowsRemaining==arrows && Time.realtimeSinceStartup<deadline)yield return null;
                if(guard.ArrowsRemaining>=arrows || actor.GetComponent<EnemySenses>().Target!=target)
                    throw new InvalidOperationException("Visible actual non-player wall intruder received no native guard arrow.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_NONPLAYER_PASS: guard="+guard.Id+"; shift="+GuardShifts.At(Now())+
                    "; nativeBodyActive=true; actualWallClassification=true; nativeArrowSpent=true; exactNativeTarget=true; priorPlayerCases=alreadyAccepted13; fullGarrisonPrerequisite=false");
            }
            finally
            {
                if(npc)UnityEngine.Object.Destroy(npc);game.PlayerMotor.transform.position=oldPosition;
                game.PlayerEntity.CrimeCommitted=oldCrime;game.PlayerEntity.GodMode=oldGod;
            }
        }
    }
}
