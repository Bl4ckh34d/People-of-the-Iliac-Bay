using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

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
                foreach(var entrance in geometry.Entrances.Values)
                {
                    var wait=new ClosedShopWait {Building=entrance.BuildingKey,LocalDoor=entrance.LocalDoor,StartedAt=now,
                        Opening=now+600,ReturnAt=now+1500,EndsAt=now+3600,Pastime=ClosedShopPastime.Conversation,
                        Seed=chosen[0].Resident.AppearanceSeed,Source=ActiveSchedule(firstPlan)};
                    ChooseClosedShopTarget(firstPlan,wait,now);
                    if(wait.HasTarget && wait.Group!=null){first=wait;break;}
                }
                if(first==null)throw new InvalidOperationException("No surveyed door has a safe conversation apron; entrances="+
                    geometry.Entrances.Count+"; surveyed="+EntranceSurveyReady+"; nativeReady="+(NativeNavigationWorld.Current && NativeNavigationWorld.Current.Ready));
                closedShopWaits[chosen[0].Resident.Id]=first;
                for(int i=1;i<3;i++)
                {
                    var wait=new ClosedShopWait {Building=first.Building,LocalDoor=first.LocalDoor,StartedAt=now,Opening=first.Opening,
                        ReturnAt=first.ReturnAt+i*100,EndsAt=first.EndsAt,Pastime=ClosedShopPastime.Conversation,
                        Seed=chosen[i].Resident.AppearanceSeed,Source=ActiveSchedule(plans.Find(x=>x.Resident.Id==chosen[i].Resident.Id))};
                    if(!TryClosedShopGroupSlot(wait,first.Group,chosen[i].Resident.Id))throw new InvalidOperationException("A three-person group has no safe distinct slots.");
                    closedShopWaits[chosen[i].Resident.Id]=wait;
                }
                foreach(var body in chosen)
                {
                    var wait=closedShopWaits[body.Resident.Id];wait.NextMove=now+3600;
                    var native=body.GetComponent<NativeResidentNavigation>();if(native)native.ResetPlacement();
                    body.GetComponent<CrowdMotion>().RestorePlacement(directory.transform.TransformPoint(wait.LocalTarget));
                }
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
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LEISURE_PASS: group=3; minimumGap="+minimum+"; partnerAlignment="+alignment+
                    "; closePlayerIgnored=true; realPartnerContext=true; releasedSlotReusable=true");
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
