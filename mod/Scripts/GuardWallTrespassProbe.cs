using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class GuardWallTrespassProbe
    {
        internal static IEnumerator Run(GuardPopulation controller,string selectedGuard=null,bool contactOnly=false)
        {
            var game=GameManager.Instance;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry=Field<TownGeometry>(controller,"geometry");
            var shift=GuardShifts.At(Now());
            var record=controller.Roster.Guards.Find(g=>(selectedGuard==null || g.Id==selectedGuard) && GuardShifts.OnDuty(g,Now()) && g.Refuge!=null &&
                g.Refuge.WallRoute!=null && g.Refuge.Evaluate(Now()).Phase==RefugePhase.WallPosted);
            if(record==null)throw new InvalidOperationException("Wall trespass test requires an arrived current-shift wall guard.");
            var wall=record.Refuge.WallRoute;
            var last=wall[wall.Count-1];var previous=wall[wall.Count-2];
            var post=directory.transform.TransformPoint(new Vector3((float)last.X,(float)last.Y,(float)last.Z));
            var before=directory.transform.TransformPoint(new Vector3((float)previous.X,(float)previous.Y,(float)previous.Z));
            var intruder=post+(before-post).normalized*Mathf.Min(8,Vector3.Distance(before,post)*.7f);
            var lowerLocal=directory.transform.InverseTransformPoint(intruder);
            var lower=geometry.ScenePoint(new RoutePoint(lowerLocal.x,lowerLocal.z))+Vector3.up;
            game.PlayerMotor.transform.position=lower;
            yield return new WaitForSecondsRealtime(1);
            var actor=Find(record.Id);
            if(!actor)throw new InvalidOperationException("Wall guard has no actual body near the intruder.");
            bool oldGod=game.PlayerEntity.GodMode;
            var oldCrime=game.PlayerEntity.CrimeCommitted;
            bool oldOffer=game.PlayerEntity.HaveShownSurrenderToGuardsDialogue;
            GameObject npc=null,blocker=null;
            try
            {
                game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;
                game.PlayerEntity.HaveShownSurrenderToGuardsDialogue=false;
                game.PlayerEntity.GodMode=false;game.PlayerEntity.SetHealth(game.PlayerEntity.MaxHealth,true);
                yield return new WaitForSecondsRealtime(1.25f);
                if(controller.IsWallIntruder(game.PlayerEntityBehaviour) || game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None)
                    throw new InvalidOperationException("Nearby lower street created a wall accusation.");
                game.PlayerMotor.transform.position=intruder+Vector3.up;
                actor.transform.forward=(intruder-actor.GroundPosition).normalized;
                Physics.SyncTransforms();
                if(!controller.IsWallIntruder(game.PlayerEntityBehaviour) || controller.IsWallIntruder(actor.GetComponent<DaggerfallEntityBehaviour>()))
                    throw new InvalidOperationException("Real upper-wall player/legitimate guard classification failed.");
                blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.name="Owned wall-trespass LOS fixture";
                blocker.transform.position=(actor.transform.position+game.PlayerEntityBehaviour.transform.position)*.5f;
                blocker.transform.localScale=new Vector3(2,4,2);
                Physics.SyncTransforms();
                if(controller.WallGuardSees(actor,game.PlayerEntityBehaviour))
                    throw new InvalidOperationException("Wall guard sees the intruder through the controlled solid blocker.");
                UnityEngine.Object.Destroy(blocker);blocker=null;yield return null;
                yield return NativeOfferAndRefusal(actor,"upper-wall-"+shift,PlayerEntity.Crimes.Trespassing);
                if(contactOnly)yield break;

                // Enter the native hit boundary before another population tick
                // can classify this separate close player placement as trespass.
                game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;
                game.PlayerEntity.HaveShownSurrenderToGuardsDialogue=false;
                actor.EndCombat();
                game.PlayerMotor.transform.position=actor.transform.position+(intruder-post).normalized*1.4f;
                actor.transform.forward=(game.PlayerMotor.transform.position-actor.transform.position).normalized;
                var victim=actor.GetComponent<DaggerfallEntityBehaviour>();
                int guardHealth=victim.Entity.CurrentHealth;
                for(int attempt=0;attempt<64 && victim.Entity.CurrentHealth==guardHealth;attempt++)
                    game.WeaponManager.WeaponDamage(null,false,false,actor.transform,actor.transform.position,
                        (actor.transform.position-game.PlayerMotor.transform.position).normalized);
                if(victim.Entity.CurrentHealth>=guardHealth || victim.Entity.CurrentHealth<=0 ||
                    actor.WallAttacker!=game.PlayerEntityBehaviour || game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.Assault)
                    throw new InvalidOperationException("Actual native wall-guard melee damage did not reach the exact assault response.");
                yield return NativeOfferAndRefusal(actor,"direct-wall-guard-hit-"+shift,PlayerEntity.Crimes.Assault);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_GUARD_HIT_PASS: shift="+shift+"; guard="+record.Id+
                    "; actualWeaponDamage=true; healthBefore="+guardHealth+"; healthAfter="+victim.Entity.CurrentHealth+
                    "; exactAttacker=true; nativeAssault=true; nativeOffer=true; refusalDamage=true");

                game.PlayerMotor.transform.position=lower;
                game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;
                yield return new WaitForSecondsRealtime(1.25f);
                if(controller.IsWallIntruder(game.PlayerEntityBehaviour) || game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None)
                    throw new InvalidOperationException("Returning below the wall retained a resolved assault or created trespass.");
                game.PlayerEntity.GodMode=oldGod;
                npc=GameObjectHelper.CreateFoeGameObjects(intruder+Vector3.up,MobileTypes.Barbarian,1,MobileReactions.Passive)[0];
                npc.name="Owned non-player wall-trespass fixture";
                npc.GetComponent<DaggerfallEnemy>().LoadID=0;
                npc.GetComponent<EnemyMotor>().enabled=false;
                npc.GetComponent<EnemySenses>().enabled=false;
                npc.GetComponent<EnemyAttack>().enabled=false;
                // Native factory deliberately returns an inactive body.
                npc.SetActive(true);
                var target=npc.GetComponent<DaggerfallEntityBehaviour>();
                yield return null;Physics.SyncTransforms();
                if(!controller.IsWallIntruder(target))throw new InvalidOperationException("Non-player on actual wall surface was rejected.");
                actor.transform.forward=(npc.transform.position-actor.transform.position).normalized;
                int npcArrows=record.ArrowsRemaining;float deadline=Time.realtimeSinceStartup+8;
                while(record.ArrowsRemaining==npcArrows && Time.realtimeSinceStartup<deadline)yield return null;
                if(record.ArrowsRemaining>=npcArrows || actor.GetComponent<EnemySenses>().Target!=target)
                    throw new InvalidOperationException("Visible non-player on restricted wall did not receive native guard arrows.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_TRESPASS_PASS: guard="+record.Id+
                    "; shift="+shift+"; nativeTrespass=true; nativeOffer=true; refused=true; actualPlayerDamage=true; directNativeHit=true; nonPlayerArrow=true; nativeProjectile=true; belowWallRejected=true; legitimateGuardExcluded=true; solidLOSBlocked=true; acceptedCourt=notExercised");
            }
            finally
            {
                if(npc)UnityEngine.Object.Destroy(npc);if(blocker)UnityEngine.Object.Destroy(blocker);
                game.PlayerEntity.GodMode=oldGod;game.PlayerEntity.CrimeCommitted=oldCrime;
                game.PlayerEntity.HaveShownSurrenderToGuardsDialogue=oldOffer;
                game.PlayerMotor.transform.position=lower;
            }
        }
        private static IEnumerator NativeOfferAndRefusal(GuardActor actor,string label,PlayerEntity.Crimes crime)
        {
            var game=GameManager.Instance;int before=game.PlayerEntity.CurrentHealth;
            var population=UnityEngine.Object.FindObjectOfType<GuardPopulation>();
            var record=population.Roster.Guards.Find(g=>g.Id==actor.GuardId);
            int arrowsBefore=record.ArrowsRemaining;
            float deadline=Time.realtimeSinceStartup+30;
            while(!game.PlayerEntity.HaveShownSurrenderToGuardsDialogue && Time.realtimeSinceStartup<deadline)
            {ObserveNativeWallProjectiles(actor);yield return null;}
            var offer=DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;
            if(offer==null || game.PlayerEntity.CrimeCommitted!=crime || game.PlayerEntity.CurrentHealth!=before ||
                !actor.GetComponent<EnemyMotor>().IsHostile || actor.GetComponent<EnemySenses>().Target!=game.PlayerEntityBehaviour)
                throw new InvalidOperationException("Wall guard did not offer actual native surrender before damage: "+label+
                    "; crime="+game.PlayerEntity.CrimeCommitted+"; shown="+game.PlayerEntity.HaveShownSurrenderToGuardsDialogue+
                    "; healthBefore="+before+"; healthAfter="+game.PlayerEntity.CurrentHealth+"; "+AttackDiagnostic(actor,record,arrowsBefore));
            Button refusal=null;
            var buttons=(IEnumerable)typeof(DaggerfallMessageBox).GetField("buttons",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(offer);
            foreach(var item in buttons)
            {
                var button=item as Button;
                if(button!=null && (DaggerfallMessageBox.MessageBoxButtons)button.Tag==DaggerfallMessageBox.MessageBoxButtons.No)
                {refusal=button;break;}
            }
            if(refusal==null)throw new InvalidOperationException("Actual native surrender box has no No button.");
            typeof(DaggerfallMessageBox).GetMethod("ActivateButton",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                .Invoke(offer,new object[]{refusal});
            var previousPosition=actor.GroundPosition;float walked=0;float wallHeight=previousPosition.y;
            deadline=Time.realtimeSinceStartup+30;
            while(game.PlayerEntity.CurrentHealth>=before && Time.realtimeSinceStartup<deadline)
            {
                ObserveNativeWallProjectiles(actor);VerifyWallCombatSurface(actor,wallHeight);
                walked+=Vector3.Distance(previousPosition,actor.GroundPosition);previousPosition=actor.GroundPosition;
                yield return null;
            }
            if(game.PlayerEntity.CurrentHealth>=before || !game.PlayerEntity.HaveShownSurrenderToGuardsDialogue ||
                DaggerfallUI.UIManager.TopWindow is DaggerfallMessageBox)
                throw new InvalidOperationException("Refused wall arrest produced no actual damage or repeated its offer: "+label+"; "+AttackDiagnostic(actor,record,arrowsBefore));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_NATIVE_ARREST_PASS: case="+label+
                "; nativePopup=true; noDamageBeforeOffer=true; nativeNo=true; actualDamage="+(before-game.PlayerEntity.CurrentHealth)+
                "; actualNativeMovement="+walked+"; supportedWallSurface=true");
            game.PlayerEntity.SetHealth(game.PlayerEntity.MaxHealth,true);
        }
        private static string AttackDiagnostic(GuardActor actor,GuardRecord record,int arrowsBefore)
        {
            var senses=actor.GetComponent<EnemySenses>();var attack=actor.GetComponent<EnemyAttack>();
            return "distance="+Vector3.Distance(actor.transform.position,GameManager.Instance.PlayerMotor.transform.position)+
                "; arrows="+arrowsBefore+"/"+record.ArrowsRemaining+"; attackEnabled="+attack.enabled+
                "; nativeSight="+senses.TargetInSight+"; nativeDistance="+senses.DistanceToTarget+
                "; wallLaw="+actor.WallLawActive+"; playerTarget="+(senses.Target==GameManager.Instance.PlayerEntityBehaviour)+
                "; projectilePrefab="+(attack.ArrowMissilePrefab!=null);
        }
        private static T Field<T>(object owner,string name)
        {return (T)owner.GetType().GetField(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(owner);}
        private static GuardActor Find(string id)
        {foreach(var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>())if(actor.GuardId==id && actor.gameObject.activeInHierarchy)return actor;return null;}
        private static double Now(){return DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();}
    }
}
