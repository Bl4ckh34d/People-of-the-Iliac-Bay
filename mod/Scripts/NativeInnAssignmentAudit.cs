using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        internal IEnumerator AuditAllInnAssignments()
        {
            var game=GameManager.Instance;
            if(!NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) || game.IsPlayerInside)
                throw new InvalidOperationException("Inn audit requires an owned exterior fixture.");
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            yield return nativeRentals.AuditAssignments(store,directory,ConversationBuildingName);
        }
    }

    public sealed partial class NativeTavernRentals
    {
        private bool assignmentAudit;

        private static string DescribeInnServiceConnections(PeopleOfTheIliacBay.Core.NpcInnServiceCatalog draft)
        {
            if(draft==null)return "serviceCatalog=none";
            if(draft.EntryToCounter==null || draft.EntryToCounter.Count==0)return "serviceCatalog=missing-entry-route";
            var entry=draft.EntryToCounter[0];
            var counter=draft.EntryToCounter[draft.EntryToCounter.Count-1];
            var details=new List<string>();
            for(int i=0;i<draft.Pairs.Count;i++)
            {
                var pair=draft.Pairs[i];
                if(pair==null)continue;
                Action<string,List<PeopleOfTheIliacBay.Core.GuardPoint3>,PeopleOfTheIliacBay.Core.TableSeatPosition> add=(label,route,seat)=>
                {
                    if(route==null || route.Count==0){details.Add(i+":"+label+"=empty");return;}
                    var first=route[0];var last=route[route.Count-1];
                    if(label.Contains("counter"))
                        details.Add(i+":"+label+"StartDelta="+Distance(counter,first).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+
                            ";seatDelta="+HorizontalDistance(last,seat).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                    else
                        details.Add(i+":"+label+"SeatDelta="+HorizontalDistance(first,seat).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+
                            ";entryDelta="+Distance(last,entry).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                };
                add("first-counter",pair.FirstCounterToSeat,pair.First);
                add("second-counter",pair.SecondCounterToSeat,pair.Second);
                add("first-exit",pair.FirstSeatToEntry,pair.First);
                add("second-exit",pair.SecondSeatToEntry,pair.Second);
            }
            return "servicePairs="+draft.Pairs.Count+"; "+string.Join(" | ",details.ToArray());
        }

        private static double Distance(PeopleOfTheIliacBay.Core.GuardPoint3 a,PeopleOfTheIliacBay.Core.GuardPoint3 b)
        {
            double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;return Math.Sqrt(x*x+y*y+z*z);
        }

        private static double HorizontalDistance(PeopleOfTheIliacBay.Core.GuardPoint3 point,PeopleOfTheIliacBay.Core.TableSeatPosition seat)
        {
            double x=point.X-seat.X,z=point.Z-seat.Z;return Math.Sqrt(x*x+z*z);
        }

        internal IEnumerator AuditAssignments(PeopleOfTheIliacBay.Core.WorldStateStore store,BuildingDirectory directory,Func<int,string> name)
        {
            CancelSurvey();assignmentAudit=true;
            var failures=new List<string>();int checkedInns=0;
            try
            {
                var inns=directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern);
                inns.Sort((a,b)=>a.buildingKey.CompareTo(b.buildingKey));
                foreach(var inn in inns)
                {
                    int key=inn.buildingKey;string failure=null;
                    DaggerfallStaticDoors owner=null;StaticDoor entry=default(StaticDoor);
                    foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                        foreach(var door in candidate.Doors)
                            if(door.doorType==DoorTypes.Building && door.buildingKey==key){owner=candidate;entry=door;break;}
                    var result=new TavernBedSurvey.Result();
                    IEnumerator operation=null;
                    if(!owner)failure="native entrance missing";
                    else operation=TavernBedSurvey.ReadServiceSteps(owner.transform,entry,directory.LocationData.Climate.ClimateType,result,()=>directory);
                    if(operation!=null)
                    {
                        try
                        {
                            while(true)
                            {
                                bool more=false;
                                try{more=operation.MoveNext();}catch(Exception error){failure=error.Message;}
                                if(failure!=null || !more)break;
                                yield return operation.Current;
                            }
                        }
                        finally{(operation as IDisposable)?.Dispose();}
                    }
                    int beds=0,markers=0,pending=0,admitted=0,refused=0;
                    if(failure==null)
                    {
                        try
                        {
                            if(result.Beds==null || result.Markers==null)throw new InvalidOperationException("native bed survey incomplete");
                            beds=result.Beds.Count;markers=result.Markers.Count;
                            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                            RegisterInventory(store,directory.MapID,directory.LocationData,entry,result,
                                GameManager.Instance.PlayerEntity.GetRentedRoom(directory.MapID,key),now);
                            store.AdvanceInnAdmissions(directory.MapID,now,100,key);
                            foreach(var visit in store.TravelerVisitsFor(directory.MapID))
                            {
                                if(visit.TavernKey!=key || store.TravelerJourneyFor(visit.Id).CheckInAt>now)continue;
                                if(visit.Admission==PeopleOfTheIliacBay.Core.TravelerAdmission.Pending)pending++;
                                else if(visit.Admission==PeopleOfTheIliacBay.Core.TravelerAdmission.Admitted)admitted++;
                                else refused++;
                            }
                            if(pending>0)throw new InvalidOperationException("due admissions remain pending: "+pending);
                        }
                        catch(Exception error)
                        {
                            failure=error.Message;
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_SERVICE_CONNECTIONS: building="+key+"; "+
                                DescribeInnServiceConnections(result.ServiceCatalog));
                        }
                    }
                    checkedInns++;
                    if(failure!=null)failures.Add(key+":"+failure);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_ASSIGNMENT_AUDIT: building="+key+"; name="+name(key)+
                        "; status="+(failure==null?"PASS":"FAIL")+"; beds="+beds+"; markers="+markers+
                        "; duePending="+pending+"; admitted="+admitted+"; refused="+refused+"; reason="+(failure??"none")+
                        "; explicitNativeBatchPreparation=true; physicalEntryNotAsserted=true");
                    yield return null;
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_ASSIGNMENT_AUDIT_SUMMARY: checked="+checkedInns+
                    "; failed="+failures.Count+"; failures="+string.Join(" | ",failures.ToArray()));
                if(checkedInns==0 || failures.Count>0)throw new InvalidOperationException("Inn assignment audit: "+failures.Count+"/"+checkedInns+" failed. "+string.Join(" | ",failures.ToArray()));
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_ASSIGNMENT_AUDIT_PASS: allNativeInns="+checkedInns);
            }
            finally{assignmentAudit=false;}
        }
    }
}
