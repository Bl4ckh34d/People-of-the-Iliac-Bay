using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class ClosedShopWait
        {
            internal int Building;
            internal Vector3 LocalTarget;
            internal float RefreshAt;
        }
        private readonly Dictionary<string,ClosedShopWait> closedShopWaits=new Dictionary<string,ClosedShopWait>();

        private bool PlaceClosedShopWait(Plan plan,ResidentActor actor,ActivitySnapshot state)
        {
            int? building=state.WaitingForBuildingKey;
            // The schedule already derives opening windows from native hours.
            // Do not repeat BuildingIsUnlocked (including its log) every frame.
            if(!building.HasValue)
            {closedShopWaits.Remove(plan.Resident.Id);return false;}
            if(GameManager.IsGamePaused || !NativeNavigationWorld.Current || !NativeNavigationWorld.Current.Ready)return false;
            ClosedShopWait waiting;
            closedShopWaits.TryGetValue(plan.Resident.Id,out waiting);
            if(waiting==null || waiting.Building!=building.Value || Time.unscaledTime>=waiting.RefreshAt)
            {
                // Share the existing preparation gate. A time skip must not
                // dispatch a path search for every waiting resident in one frame.
                if(!BackgroundWork.TryStart())return waiting!=null && waiting.Building==building.Value && PlaceClosedShopTarget(actor,waiting);
                var door=geometry.ScenePoint(state.Position);
                NavMeshHit start;
                if(!NavMesh.SamplePosition(door,out start,0.6f,NavMesh.AllAreas))return false;
                uint seed=plan.Resident.AppearanceSeed;
                float phase=(seed%360)*Mathf.Deg2Rad+Mathf.Floor(Time.unscaledTime/30)*0.73f;
                bool hadTarget=waiting!=null && waiting.Building==building.Value;
                if(!hadTarget)waiting=new ClosedShopWait{Building=building.Value};
                bool selected=false;
                for(int attempt=0;attempt<8;attempt++)
                {
                    float angle=phase+attempt*Mathf.PI/4;
                    float distance=3+(seed%7);
                    var candidate=start.position+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*distance;
                    NavMeshHit end,blocked;
                    if(!NavMesh.SamplePosition(candidate,out end,0.8f,NavMesh.AllAreas) ||
                        Mathf.Abs(end.position.y-start.position.y)>0.6f ||
                        Vector3.Distance(end.position,start.position)<2.5f ||
                        NavMesh.Raycast(start.position,end.position,out blocked,NavMesh.AllAreas))continue;
                    waiting.LocalTarget=directory.transform.InverseTransformPoint(end.position);selected=true;break;
                }
                if(!selected)
                {
                    if(!hadTarget)return false;
                    waiting.RefreshAt=Time.unscaledTime+15;
                    return PlaceClosedShopTarget(actor,waiting);
                }
                waiting.RefreshAt=Time.unscaledTime+15+seed%16;
                closedShopWaits[plan.Resident.Id]=waiting;
            }
            return PlaceClosedShopTarget(actor,waiting);
        }
        private bool PlaceClosedShopTarget(ResidentActor actor,ClosedShopWait waiting)
        {
            var target=directory.transform.TransformPoint(waiting.LocalTarget);
            var crowd=actor.GetComponent<CrowdMotion>();if(crowd)crowd.ResumeFromPause();
            actor.Place(target,target-(actor.transform.position-Vector3.up),freeStanding:true,navigationDestination:target);
            return true;
        }
    }
}
