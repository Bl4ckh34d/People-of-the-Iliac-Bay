using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Explicit disposable editor fixture. Production selection, navigation,
        // facing and context run against actual resident bodies and surveyed doors.
        internal IEnumerator VerifyClosedShopLeisure()
        {
            var game=GameManager.Instance;var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((43200-clock.ToSeconds()%86400+86400)%86400));
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            var chosen=new List<ResidentActor>();float deadline=Time.realtimeSinceStartup+75;
            while(chosen.Count<3 && Time.realtimeSinceStartup<deadline)
            {
                chosen.Clear();foreach(var body in actors.Values)
                    if(body && body.gameObject.activeInHierarchy && body.Resident.IsAlive && !body.IsSleeping && !body.IsConversing &&
                        body.GetComponent<CrowdMotion>() && body.GetComponent<CrowdMotion>().HasPlacement && chosen.Count<3)chosen.Add(body);
                if(chosen.Count<3)yield return null;
            }
            if(chosen.Count<3)throw new InvalidOperationException("Leisure fixture requires three native residents.");
            deadline=Time.realtimeSinceStartup+75;
            while((!EntranceSurveyReady || geometry==null || geometry.Entrances.Count==0 || !NativeNavigationWorld.Current ||
                !NativeNavigationWorld.Current.Ready) && Time.realtimeSinceStartup<deadline)yield return null;
            bool enabledBefore=enabled;var playerBefore=game.PlayerMotor.transform.position;var starts=new List<Vector3>();
            foreach(var body in chosen)starts.Add(body.transform.position);
            var previous=new Dictionary<string,ClosedShopWait>(closedShopWaits);enabled=false;closedShopWaits.Clear();
            try
            {
                double now=clock.ToSeconds();ClosedShopWait first=null;Plan firstPlan=plans.Find(x=>x.Resident.Id==chosen[0].Resident.Id);
                var approachStarts=new List<Vector3>();var travelled=new float[chosen.Count];int surveyed=0;
                foreach(var entrance in geometry.Entrances.Values)
                {
                    if(++surveyed%8==0)yield return null;
                    closedShopWaits.Clear();approachStarts.Clear();
                    var wait=new ClosedShopWait {Building=entrance.BuildingKey,LocalDoor=entrance.LocalDoor,StartedAt=now,
                        Opening=now+600,ReturnAt=now+1500,EndsAt=now+3600,Pastime=ClosedShopPastime.Conversation,
                        Seed=chosen[0].Resident.AppearanceSeed,Source=ActiveSchedule(firstPlan)};
                    ChooseClosedShopTarget(firstPlan,wait,now);
                    if(!wait.HasTarget || wait.Group==null)continue;
                    closedShopWaits[chosen[0].Resident.Id]=wait;
                    for(int i=1;i<3;i++)
                    {
                        var member=new ClosedShopWait {Building=wait.Building,LocalDoor=wait.LocalDoor,StartedAt=now,Opening=wait.Opening,
                            ReturnAt=wait.ReturnAt+i*100,EndsAt=wait.EndsAt,Pastime=ClosedShopPastime.Conversation,
                            Seed=chosen[i].Resident.AppearanceSeed,Source=ActiveSchedule(plans.Find(x=>x.Resident.Id==chosen[i].Resident.Id))};
                        if(!TryClosedShopGroupSlot(member,wait.Group,chosen[i].Resident.Id))break;
                        closedShopWaits[chosen[i].Resident.Id]=member;
                    }
                    if(closedShopWaits.Count!=3)continue;
                    var center=directory.transform.TransformPoint(wait.Group.Center);
                    foreach(var body in chosen)
                    {
                        var member=closedShopWaits[body.Resident.Id];member.NextMove=now+3600;
                        var target=directory.transform.TransformPoint(member.LocalTarget);
                        var outward=target-center;outward.y=0;Vector3 approach;NavMeshHit blocked;
                        if(!TryClosedShopPoint(directory.transform.TransformPoint(member.LocalDoor),target+outward.normalized*2.4f,out approach) ||
                            Vector3.Distance(approach,target)<2 || NavMesh.Raycast(approach,target,out blocked,NavMesh.AllAreas))break;
                        approachStarts.Add(approach);
                    }
                    if(approachStarts.Count==3){first=wait;break;}
                }
                if(first==null)throw new InvalidOperationException("No surveyed door has three safe conversation approaches; entrances="+
                    geometry.Entrances.Count+"; surveyed="+EntranceSurveyReady+"; nativeReady="+(NativeNavigationWorld.Current && NativeNavigationWorld.Current.Ready));
                for(int i=0;i<chosen.Count;i++)
                {
                    var body=chosen[i];
                    var native=body.GetComponent<NativeResidentNavigation>();if(native)native.ResetPlacement();
                    body.GetComponent<CrowdMotion>().RestorePlacement(approachStarts[i]);
                }
                // Only fixture starting positions are placed directly. Every step
                // to the group uses the ordinary activity/navigation adapter.
                float maximumStep=0;bool allArrived=false;deadline=Time.realtimeSinceStartup+45;
                while(Time.realtimeSinceStartup<deadline && !allArrived)
                {
                    allArrived=true;
                    for(int i=0;i<chosen.Count;i++)
                    {
                        var body=chosen[i];var before=body.transform.position;
                        PlaceClosedShopWait(plans.Find(x=>x.Resident.Id==body.Resident.Id),body,default(ActivitySnapshot),now);
                        float step=Vector3.Distance(before,body.transform.position);maximumStep=Mathf.Max(maximumStep,step);travelled[i]+=step;
                        if(step>0.7f)throw new InvalidOperationException("Group approach jumped: "+step);
                        var target=directory.transform.TransformPoint(closedShopWaits[body.Resident.Id].LocalTarget);
                        if(Vector3.Distance(body.transform.position-Vector3.up,target)>0.4f)allArrived=false;
                    }
                    yield return null;
                }
                if(!allArrived)throw new InvalidOperationException("Group members did not physically reach their conversation slots.");
                foreach(float distance in travelled)if(distance<1.8f)throw new InvalidOperationException("Group approach lacks actual body travel.");
                game.PlayerMotor.transform.position=directory.transform.TransformPoint(first.LocalTarget)+Vector3.up+Vector3.right*1.5f;
                deadline=Time.realtimeSinceStartup+2;
                while(Time.realtimeSinceStartup<deadline)
                {
                    foreach(var body in chosen)PlaceClosedShopWait(plans.Find(x=>x.Resident.Id==body.Resident.Id),body,default(ActivitySnapshot),now);
                    yield return null;
                }
                float minimum=float.MaxValue,alignment=1;
                foreach(var body in chosen)
                {
                    if(!body.IsStandingStill)throw new InvalidOperationException("Arrived group member has no standing pose.");
                    var billboard=body.Person.Asset as MobilePersonBillboard;
                    // Directional standing uses a frozen native walking record;
                    // IsIdle is deliberately false to preserve its bearings.
                    if(billboard && !billboard.IsIdle && (float)typeof(MobilePersonBillboard).GetField("animSpeed",
                        BindingFlags.Instance|BindingFlags.NonPublic).GetValue(billboard)!=0)
                        throw new InvalidOperationException("Arrived group member is still playing a walking animation.");
                    Vector3 others=Vector3.zero;foreach(var partner in chosen)if(partner!=body)
                    {others+=partner.transform.position;minimum=Mathf.Min(minimum,Vector3.Distance(body.transform.position,partner.transform.position));}
                    var direction=others/2-body.transform.position;direction.y=0;
                    alignment=Mathf.Min(alignment,Vector3.Dot(body.transform.forward,direction.normalized));
                    string context;if(!TryClosedShopContext(body,now,game.TalkManager.GetBuildingNameForBuildingKey,out context) || !context.Contains("talking with"))
                        throw new InvalidOperationException("Active group context omitted its real partners.");
                }
                if(minimum<1 || alignment<0.8f)throw new InvalidOperationException("Group facing/spacing failed: gap="+minimum+"; alignment="+alignment);
                var middle=closedShopWaits[chosen[1].Resident.Id];first.Group.Members.Remove(chosen[1].Resident.Id);
                if(!TryClosedShopGroupSlot(middle,first.Group,chosen[1].Resident.Id))throw new InvalidOperationException("Released middle group slot was not reusable.");
                // A due return releases activity ownership and removes the
                // departed participant from the remaining speakers' live context.
                first.ReturnAt=now;
                if(PlaceClosedShopWait(firstPlan,chosen[0],default(ActivitySnapshot),now))
                    throw new InvalidOperationException("Expired group wait still owns movement.");
                string remainingContext;
                if(!TryClosedShopContext(chosen[1],now,game.TalkManager.GetBuildingNameForBuildingKey,out remainingContext) ||
                    remainingContext.Contains(chosen[0].Person.NameNPC))
                    throw new InvalidOperationException("Remaining speakers still claim the departed partner is in their conversation.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LEISURE_PASS: group=3; minimumGap="+minimum+"; partnerAlignment="+alignment+
                    "; closePlayerIgnored=true; realPartnerContext=true; releasedSlotReusable=true; physicalApproach=true; maximumStep="+
                    maximumStep+"; bodyTravel="+string.Join(",",Array.ConvertAll(travelled,x=>x.ToString("F3")))+"; idleAnimation=true; expiredWaitReleased=true");
            }
            finally
            {
                closedShopWaits.Clear();foreach(var entry in previous)closedShopWaits.Add(entry.Key,entry.Value);
                for(int i=0;i<chosen.Count;i++)if(chosen[i])
                {var native=chosen[i].GetComponent<NativeResidentNavigation>();if(native)native.ResetPlacement();chosen[i].GetComponent<CrowdMotion>().RestorePlacement(starts[i]-Vector3.up);}
                game.PlayerMotor.transform.position=playerBefore;enabled=enabledBefore;
            }
        }
    }
}
