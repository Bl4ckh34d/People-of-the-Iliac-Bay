using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class RoutineRecoveryPhysicalProbe
    {
        private const BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;

        // Runs only after the ordinary Purchase -> Transfer task reaches terminal.
        // It observes natural schedule recovery; it never calls the production save hook.
        public static IEnumerator Observe(StreetPopulation streets,ResidentActor actor,DaggerfallInterior room,
            WorldStateStore store,NpcTaskPlan task,int sourceStopIndex)
        {
            Require(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-routine-recovery-physical-probe")>=0,
                "Physical routine recovery requires its explicit Player flag.");
            Require(streets && actor && room && store!=null && task!=null && task.Phase==NpcTaskPhase.Completed &&
                task.Steps.Count==2 && task.Steps[0].Kind==NpcActionKind.Purchase && task.Steps[1].Kind==NpcActionKind.Transfer,
                "Physical recovery requires the completed native Purchase -> Transfer task.");
            Require(sourceStopIndex>=0,"Physical recovery fixture has no source itinerary stop index.");

            string residentId=task.ResidentId;
            var game=GameManager.Instance;
            int origin=room.EntryDoor.buildingKey;
            var geometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",PrivateInstance).GetValue(streets);
            Require(geometry!=null && geometry.EntryGround.HasValue,"Physical recovery has no surveyed native entry ground.");
            Vector3 doorway=geometry.EntryGround.Value+Vector3.up;
            Require(!store.HasRoutineRecovery(residentId),"Physical recovery preflight found an existing plan for the selected resident.");

            float started=Time.realtimeSinceStartup,deadline=started+120f;
            DaggerfallUI.AddHUDText("Testing NPC schedule recovery: walking back to the exit.",4f);
            Vector3 previous=actor.transform.position;
            float walked=0,maxStep=0,doorGap=float.PositiveInfinity,minDoorGap=float.PositiveInfinity;
            float previousYaw=0,maximumYawStep=0,maximumYawRate=0,previousDesiredYaw=0;
            int headingFrames=0,smallAlternatingCorrections=0,previousCorrectionSign=0;
            bool hasPreviousHeading=false,hasPreviousDesiredYaw=false;
            bool sawIndoorDoor=false;
            bool reloaded=false;
            RoutineRecoveryPlan saved=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                if(actor)
                {
                    SleepProbe.AimCamera(actor.transform.position+Vector3.up*0.35f);
                    Vector3 current=actor.transform.position,prior=previous;
                    float step=Vector3.Distance(previous,current);
                    walked+=step;maxStep=Mathf.Max(maxStep,step);previous=current;
                    bool observeHeading=!GameManager.IsGamePaused && step>0.001f &&
                        !actor.IsSeated && !actor.IsStandingStill && !actor.IsConversing;
                    if(observeHeading)
                    {
                        float currentYaw=actor.transform.eulerAngles.y;
                        if(hasPreviousHeading)
                        {
                            float yawStep=Mathf.Abs(Mathf.DeltaAngle(previousYaw,currentYaw));
                            // UnscaledDeltaTime matches the runtime's walking turn clock;
                            // 0.2 degrees covers float-angle and sample-boundary rounding.
                            float allowedYawStep=1080f*Time.unscaledDeltaTime+0.2f;
                            if(yawStep>allowedYawStep)
                                throw new InvalidOperationException("Recovery walker heading exceeded its 1080-degree-per-second turn cap: "+
                                    "yawStep="+yawStep.ToString("F3")+"; allowed="+allowedYawStep.ToString("F3")+"; dt="+Time.unscaledDeltaTime.ToString("F4"));
                            headingFrames++;
                            maximumYawStep=Mathf.Max(maximumYawStep,yawStep);
                            maximumYawRate=Mathf.Max(maximumYawRate,yawStep/Mathf.Max(0.0001f,Time.unscaledDeltaTime));
                        }
                        previousYaw=currentYaw;hasPreviousHeading=true;
                        Vector3 displacement=current-prior;displacement.y=0;
                        if(displacement.sqrMagnitude>0.0001f)
                        {
                            float desiredYaw=Mathf.Atan2(displacement.x,displacement.z)*Mathf.Rad2Deg;
                            if(hasPreviousDesiredYaw)
                            {
                                float correction=Mathf.DeltaAngle(previousDesiredYaw,desiredYaw);
                                if(Mathf.Abs(correction)>=0.05f && Mathf.Abs(correction)<=2f)
                                {
                                    int sign=correction>0?1:-1;
                                    if(previousCorrectionSign!=0 && sign==-previousCorrectionSign)
                                        smallAlternatingCorrections++;
                                    previousCorrectionSign=sign;
                                }
                                else previousCorrectionSign=0;
                            }
                            previousDesiredYaw=desiredYaw;hasPreviousDesiredYaw=true;
                        }
                    }
                    else
                    {
                        // Pauses, idle activities, conversation, and seated target
                        // facing may intentionally snap. Do not measure across them.
                        hasPreviousHeading=false;hasPreviousDesiredYaw=false;previousCorrectionSign=0;
                    }
                    doorGap=Vector3.Distance(current,doorway);
                    minDoorGap=Mathf.Min(minDoorGap,doorGap);
                    if(game.PlayerEnterExit.Interior==room && doorGap<=2.5f)sawIndoorDoor=true;
                    if(!reloaded && walked>=1f && doorGap>2f && !store.HasRoutineRecovery(residentId))
                    {
                        var restoredReturn=new RoutineRecoveryReloadProbe.Result();
                        float reloadStarted=Time.realtimeSinceStartup;
                        yield return RoutineRecoveryReloadProbe.Run(streets,residentId,task.Id+":step:1",restoredReturn);
                        reloaded=true;store=restoredReturn.Store;actor=restoredReturn.Actor;
                        room=restoredReturn.Room;geometry=restoredReturn.Geometry;
                        doorway=geometry.EntryGround.Value+Vector3.up;previous=actor.transform.position;
                        hasPreviousHeading=false;hasPreviousDesiredYaw=false;previousCorrectionSign=0;
                        deadline+=Time.realtimeSinceStartup-reloadStarted;
                    }
                }
                if(store.HasRoutineRecovery(residentId))
                {
                    saved=store.Snapshot().RoutineRecoveries.Find(p=>p.ResidentId==residentId);
                    if(saved!=null)break;
                }
                yield return null;
            }

            Require(saved!=null,"Production routine recovery was not saved after the completed task and bounded physical return.");
            Require(reloaded,"Physical recovery never exercised the real disk reload checkpoint.");
            Require(sawIndoorDoor && minDoorGap<=0.75f && walked>0.5f && maxStep<1.5f,
                "Recovery save lacked observed indoor doorway arrival or smooth physical movement.");
            Require(headingFrames>0,"Recovery probe observed no consecutive unpaused walking frames for heading verification.");
            Require(saved.OriginBuildingKey==origin && saved.OriginOutside &&
                saved.AbsoluteDay==(int)Math.Floor(saved.CreatedAt/86400d) &&
                saved.CreatedAt>=task.UpdatedAt && saved.Legs.Count>=2,
                "Saved recovery has wrong indoor origin, outside state, day, time, or no later visit plus home return.");
            Require(saved.Legs[saved.Legs.Count-1].ReturnsHome && saved.Legs[saved.Legs.Count-1].BuildingKey==saved.HomeBuildingKey,
                "Saved recovery omitted final home return.");
            Require(saved.Legs.Exists(leg=>!leg.ReturnsHome && leg.OriginalStopIndex>=0 && leg.OriginalStopIndex!=sourceStopIndex),
                "Saved recovery omitted every later feasible optional itinerary stop.");
            Require(!saved.Legs.Exists(leg=>leg.OriginalStopIndex==sourceStopIndex),
                "Saved recovery replayed the source itinerary stop.");
            Require(saved.SkippedStopIndices.Contains(sourceStopIndex),
                "Saved recovery did not mark expired source stop as skipped.");
            var world=store.Snapshot();
            var town=world.Routines.Find(item=>item.SettlementId==saved.SettlementId);
            var routine=town==null?null:town.Residents.Find(item=>item.ResidentId==task.ResidentId);
            Require(routine!=null && routine.Itinerary!=null,"Saved recovery has no source itinerary for skip validation.");
            foreach(int skipped in saved.SkippedStopIndices)
                Require(skipped>=0 && skipped<routine.Itinerary.Stops.Count,
                    "Saved recovery skipped an index outside the real saved itinerary.");

            var terminal=store.ReadNpcAction(task.Id+":step:1");
            Require(terminal!=null && terminal.Phase==NpcActionPhase.Completed && terminal.LastPosition!=null &&
                terminal.LastPosition.Kind==NpcActionTargetKind.WorldPoint && !terminal.ReturningToRoutine,
                "Committed doorway recovery did not leave terminal action at exterior world point with routine ownership released.");
            // Interior geometry lives in a separate vertical coordinate space.
            // Verify physical indoor arrival above, then the matching saved exterior node.
            var sourceRoute=routine.Itinerary.Stops[sourceStopIndex].Route;
            var sourceDoor=sourceRoute[sourceRoute.Count-1];
            double dx=terminal.LastPosition.X-sourceDoor.X,dz=terminal.LastPosition.Z-sourceDoor.Z;
            Require(Math.Sqrt(dx*dx+dz*dz)<0.001,
                "Terminal outside action point is not the saved source building doorway.");

            var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());
            restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),
                SaveLoadManager.Serialize(typeof(WorldState),world)));
            var disk=restored.Snapshot().RoutineRecoveries.Find(p=>p.ResidentId==task.ResidentId);
            Require(disk!=null && disk.OriginBuildingKey==origin && disk.CreatedAt.ToString("G15",CultureInfo.InvariantCulture)==
                saved.CreatedAt.ToString("G15",CultureInfo.InvariantCulture) &&
                disk.Legs.Count==saved.Legs.Count && disk.SkippedStopIndices.Count==saved.SkippedStopIndices.Count,
                "Native world-state serialization lost the physical recovery plan.");

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_PHYSICAL_PASS: resident="+residentId+
                "; origin="+origin+"; sourceStop="+sourceStopIndex+"; task=Purchase-Transfer-completed"+
                "; walked="+walked.ToString("F2")+"; maxStep="+maxStep.ToString("F3")+
                "; headingFrames="+headingFrames+"; maxYawStep="+maximumYawStep.ToString("F3")+
                "; maxYawRate="+maximumYawRate.ToString("F1")+"; maxYawCap=1080.0; alternatingSmallCorrections="+smallAlternatingCorrections+
                "; minDoorGap="+minDoorGap.ToString("F3")+"; exteriorNodeGap="+Math.Sqrt(dx*dx+dz*dz).ToString("F3")+
                "; originOutside=true; returning=false; legs="+saved.Legs.Count+
                "; skipped="+saved.SkippedStopIndices.Count+"; home=true; nativeSerialize=true");
        }

        private static void Require(bool value,string message)
        {if(!value)throw new InvalidOperationException(message);}
    }
}
