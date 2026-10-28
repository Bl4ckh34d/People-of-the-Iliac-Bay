using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class ClosedShopGroup
        {
            internal Vector3 Center;
            internal readonly List<string> Members=new List<string>();
        }
        private sealed class ClosedShopWait
        {
            internal int Building,Inn=-1;
            internal double Opening,ReturnAt,EndsAt,StartedAt,NextMove,InnWalkSeconds;
            internal Vector3 LocalDoor,LocalTarget,LocalLook;
            internal bool HasTarget;
            internal uint Seed;
            internal ClosedShopPastime Pastime;
            internal ClosedShopGroup Group;
            internal DailySchedule Source,Schedule;
            internal List<SavedRoutePoint> InnRoute,InnReturnRoute;
            internal ResidentDailyOuting Reordered;
        }
        private readonly Dictionary<string,ClosedShopWait> closedShopWaits=new Dictionary<string,ClosedShopWait>();
        private int nextClosedShopPlan;

        private void PrepareClosedShopLeisure(double now)
        {
            if(GameManager.IsGamePaused || !directory || geometry==null || !EntranceSurveyReady)return;
            // Four cheap checks and at most one preparation job per frame.
            for(int n=0;n<Math.Min(4,plans.Count);n++)
            {
                if(nextClosedShopPlan>=plans.Count)nextClosedShopPlan=0;
                var plan=plans[nextClosedShopPlan++];string id=plan.Resident.Id;
                ClosedShopWait old;
                if(closedShopWaits.TryGetValue(id,out old))
                {
                    if(now>=old.EndsAt || now<old.StartedAt || !ClosedShopEligible(plan,now))RemoveClosedShopWait(id);
                    else continue;
                }
                if(!ClosedShopEligible(plan,now))continue;
                var schedule=ActiveSchedule(plan);var state=schedule.EvaluateSeconds(now);
                if(!state.WaitingForBuildingKey.HasValue)continue;
                if(!BackgroundWork.TryStart())return;
                double opening,end;int key=state.WaitingForBuildingKey.Value;
                if(!schedule.TryVisitWindowForBuilding(now,key,out opening,out end) || now>=opening)continue;
                TownGeometry.Entrance door;
                if(!geometry.Entrances.TryGetValue(key,out door))continue;
                var wait=new ClosedShopWait {Building=key,Opening=opening,EndsAt=end,StartedAt=now,
                    ReturnAt=ClosedShopLeisure.ReturnAt(plan.Resident.AppearanceSeed,key,opening,end),
                    LocalDoor=door.LocalDoor,Source=schedule,Seed=ClosedShopLeisure.Seed(plan.Resident.AppearanceSeed,key,opening)};
                var wallet=worldStore.ReadNpcInventory(id);
                wait.Pastime=ClosedShopLeisure.Choose(plan.Resident.AppearanceSeed,key,opening,wallet!=null && wallet.WalletGold>0,true);
                if(!TryAdvanceClosedShopVisit(plan,wait,now) && wait.Pastime==ClosedShopPastime.Inn && !TryClosedShopInn(plan,wait,now))
                    wait.Pastime=ClosedShopPastime.Stroll;
                closedShopWaits[id]=wait;
                SaveClosedShopWait(id,wait);
                if(wait.Schedule!=null)shopVisitDay=-1;
                return;
            }
        }
        private bool ClosedShopEligible(Plan plan,double now)
        {
            string id=plan.Resident.Id;ActivitySnapshot joint;
            return plan.Resident.IsAlive && !QuestOwned(plan.Resident) && !IsAlarmed(id) &&
                !worldStore.HasRoutineRecovery(id) && !worldStore.IsResidentAway(id,now) &&
                !TryHouseholdJointTravelState(plan,out joint) &&
                worldStore.ActiveNpcAction(id)==null && worldStore.ActiveNpcTask(id)==null &&
                worldStore.ReadNpcCashVisit(id)==null && worldStore.ReadNpcCashWait(id)==null &&
                worldStore.ReadNpcTreatmentVisit(id)==null && !HasActiveNpcHealthCare(id);
        }
        private void RemoveClosedShopWait(string id)
        {
            ClosedShopWait wait;
            if(closedShopWaits.TryGetValue(id,out wait) && wait.Group!=null)wait.Group.Members.Remove(id);
            closedShopWaits.Remove(id);
            if(worldStore!=null)worldStore.ForgetClosedShopVisit(id);
        }
        private DailySchedule ClosedShopSchedule(Plan plan,DailySchedule source,double now)
        {
            ClosedShopWait wait;
            if(!closedShopWaits.ContainsKey(plan.Resident.Id))RestoreClosedShopWait(plan,source,now);
            if(!closedShopWaits.TryGetValue(plan.Resident.Id,out wait) || now<wait.StartedAt || now>=wait.EndsAt)return source;
            if(!ReferenceEquals(source,wait.Source))
            {
                if(!source.ContainsVisitBuilding(wait.Building)){RemoveClosedShopWait(plan.Resident.Id);return source;}
                if(wait.Schedule==null)wait.Source=source;
                else
                {
                    closedShopWaits.Remove(plan.Resident.Id);RestoreClosedShopWait(plan,source,now);
                    if(!closedShopWaits.TryGetValue(plan.Resident.Id,out wait))return source;
                }
            }
            return wait.Schedule??source;
        }
        private ActivitySnapshot ClosedShopState(Plan plan,double now,ActivitySnapshot source)
        {
            ClosedShopWait wait;
            if(!closedShopWaits.TryGetValue(plan.Resident.Id,out wait))return source;
            RefreshInactiveShopWait(plan,wait,now);
            if(wait.Schedule!=null || now<wait.StartedAt || now>=wait.ReturnAt)return source;
            var point=wait.HasTarget?wait.LocalTarget:wait.LocalDoor;var position=new RoutePoint(point.x,point.z);
            return new ActivitySnapshot(Activity.Travel,-1,position,0,position,wait.Building);
        }
        private bool PlaceClosedShopWait(Plan plan,ResidentActor actor,ActivitySnapshot state,double now)
        {
            ClosedShopWait wait;
            if(!closedShopWaits.TryGetValue(plan.Resident.Id,out wait))return false;
            RefreshInactiveShopWait(plan,wait,now);
            if(wait.Schedule!=null || now>=wait.ReturnAt)return false;
            if(GameManager.IsGamePaused || actor.IsConversing)return true;
            if(!NativeNavigationWorld.Current || !NativeNavigationWorld.Current.Ready)return false;
            if((!wait.HasTarget || now>=wait.NextMove) && BackgroundWork.TryStart())ChooseClosedShopTarget(plan,wait,now);
            if(!wait.HasTarget)return false;
            var target=directory.transform.TransformPoint(wait.LocalTarget);
            var look=directory.transform.TransformPoint(wait.LocalLook);
            if(wait.Group!=null)
            {
                Vector3 center=Vector3.zero;int count=0;
                foreach(string id in wait.Group.Members)
                {
                    ClosedShopWait other;var partner=FindActor(id);
                    if(id==plan.Resident.Id || !partner || !closedShopWaits.TryGetValue(id,out other) || now>=other.ReturnAt ||
                        partner.IsConversing || Vector3.Distance(partner.transform.position,actor.transform.position)>3)continue;
                    center+=partner.transform.position-Vector3.up;count++;
                }
                if(count>0)look=center/count;
            }
            var crowd=actor.GetComponent<CrowdMotion>();if(crowd)crowd.ResumeFromPause();
            bool arrived=Vector3.Distance(actor.transform.position-Vector3.up,target)<0.4f;
            // Occupied activities own attention, not free-standing player watching.
            actor.Place(target,look-(actor.transform.position-Vector3.up),standing:arrived,navigationDestination:target);
            return true;
        }
        private void RefreshInactiveShopWait(Plan plan,ClosedShopWait wait,double now)
        {
            if(wait.Schedule!=null || now<wait.Opening || now>=wait.ReturnAt)return;
            var actor=FindActor(plan.Resident.Id);bool occupied=false;
            if(actor && actor.gameObject.activeInHierarchy)
            {
                occupied=actor.IsConversing || HasMarketActivity(plan.Resident.Id,now) || wait.HasTarget && !actor.IsStandingStill &&
                    Vector3.Distance(actor.transform.position-Vector3.up,directory.transform.TransformPoint(wait.LocalTarget))>0.6f;
                bool arrived=wait.HasTarget && Vector3.Distance(actor.transform.position-Vector3.up,
                    directory.transform.TransformPoint(wait.LocalTarget))<=0.6f;
                if(!occupied && arrived && wait.Group!=null)foreach(string id in wait.Group.Members)
                {
                    var other=FindActor(id);ClosedShopWait otherWait;
                    if(id==plan.Resident.Id || !other || !other.gameObject.activeInHierarchy || other.IsConversing ||
                        !closedShopWaits.TryGetValue(id,out otherWait) || otherWait.Group!=wait.Group || !otherWait.HasTarget ||
                        now>=otherWait.ReturnAt || Vector3.Distance(other.transform.position,actor.transform.position)>3 ||
                        Vector3.Distance(other.transform.position-Vector3.up,directory.transform.TransformPoint(otherWait.LocalTarget))>0.6f)continue;
                    occupied=true;break;
                }
            }
            double end=ClosedShopLeisure.ObservedReturnAt(plan.Resident.AppearanceSeed,wait.Building,wait.Opening,wait.ReturnAt,now,occupied);
            if(end>=wait.ReturnAt)return;
            wait.ReturnAt=end;SaveClosedShopWait(plan.Resident.Id,wait);
        }
        private void ChooseClosedShopTarget(Plan plan,ClosedShopWait wait,double now)
        {
            wait.NextMove=now+300+wait.Seed%421;
            if(wait.Group!=null)
            {
                bool companion=false;
                foreach(string id in wait.Group.Members)
                {ClosedShopWait other;if(id!=plan.Resident.Id && closedShopWaits.TryGetValue(id,out other) && now<other.ReturnAt)companion=true;}
                if(companion)return;
                wait.Group.Members.Remove(plan.Resident.Id);wait.Group=null;
            }
            if(wait.Pastime==ClosedShopPastime.Conversation)
                foreach(var pair in closedShopWaits)
                {
                    var other=pair.Value;
                    if(pair.Key==plan.Resident.Id || other.Building!=wait.Building || other.Group==null || now>=other.ReturnAt)continue;
                    other.Group.Members.RemoveAll(id=>!closedShopWaits.ContainsKey(id) || now>=closedShopWaits[id].ReturnAt);
                    if(other.Group.Members.Count>=3)continue;
                    if(TryClosedShopGroupSlot(wait,other.Group,plan.Resident.Id))return;
                }
            Vector3 door=directory.transform.TransformPoint(wait.LocalDoor);
            uint phase=(uint)(now/420);float angle=((wait.Seed+phase*53)%360)*Mathf.Deg2Rad;
            for(int attempt=0;attempt<8;attempt++)
            {
                float turn=angle+attempt*Mathf.PI/4;
                float distance=wait.Pastime==ClosedShopPastime.Browse?4:5+wait.Seed%8;
                Vector3 candidate=door+new Vector3(Mathf.Cos(turn),0,Mathf.Sin(turn))*distance,point;
                if(!TryClosedShopPoint(door,candidate,out point))continue;
                var local=directory.transform.InverseTransformPoint(point);
                if(wait.Pastime==ClosedShopPastime.Conversation)
                {
                    var group=new ClosedShopGroup {Center=local};
                    if(TryClosedShopGroupSlot(wait,group,plan.Resident.Id))return;
                }
                wait.LocalTarget=local;wait.LocalLook=wait.LocalDoor;wait.HasTarget=true;return;
            }
        }
        private bool TryClosedShopGroupSlot(ClosedShopWait wait,ClosedShopGroup group,string id)
        {
            Vector3 center=directory.transform.TransformPoint(group.Center);
            for(int slot=0;slot<3;slot++)
            {
                float angle=(slot*120+wait.Building%360)*Mathf.Deg2Rad;
                Vector3 candidate=center+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*0.72f,point;
                if(!TryClosedShopPoint(directory.transform.TransformPoint(wait.LocalDoor),candidate,out point))continue;
                bool occupied=false;
                foreach(string member in group.Members)
                {
                    ClosedShopWait other;
                    if(closedShopWaits.TryGetValue(member,out other) && Vector3.Distance(directory.transform.TransformPoint(other.LocalTarget),point)<1.05f)occupied=true;
                }
                if(occupied)continue;
                group.Members.Add(id);wait.Group=group;wait.LocalTarget=directory.transform.InverseTransformPoint(point);
                wait.LocalLook=group.Center;wait.HasTarget=true;return true;
            }
            return false;
        }
        private bool TryClosedShopPoint(Vector3 door,Vector3 candidate,out Vector3 point)
        {
            point=Vector3.zero;NavMeshHit start,end,blocked;
            // Native doors expose their centre at sprite height. Navigation and
            // same-floor checks must start at terrain ground, not that centre.
            var local=directory.transform.InverseTransformPoint(door);
            door=geometry.ScenePoint(new RoutePoint(local.x,local.z));candidate.y=door.y;
            if(!NavMesh.SamplePosition(door,out start,0.8f,NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(candidate,out end,0.5f,NavMesh.AllAreas) ||
                Mathf.Abs(end.position.y-start.position.y)>0.6f || Vector3.Distance(end.position,start.position)<3 ||
                NavMesh.Raycast(start.position,end.position,out blocked,NavMesh.AllAreas))return false;
            foreach(var entrance in geometry.Entrances.Values)
            {
                var gap=end.position-geometry.SceneDoor(entrance);gap.y=0;
                if(gap.sqrMagnitude<4)return false;
            }
            point=end.position;return true;
        }
    }
}
