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
        // Observe ordinary demand and rental preparation. Do not inject a guest,
        // booking, bed inventory, route or admission transaction.
        private IEnumerator VerifyOrdinaryInnAdmission(int building)
        {
            float deadline=Time.realtimeSinceStartup+240,nextReport=0;
            int doorReceipts=0;
            var watched=new HashSet<int>();var prematureDisappearances=new List<string>();
            List<TravelerVisit> visits=null;
            int pending=0,admitted=0,refused=0;
            while(Time.realtimeSinceStartup<deadline)
            {
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                visits=worldStore.TravelerVisitsFor(directory.MapID).FindAll(v=>v.TavernKey==building);
                pending=0;admitted=0;refused=0;
                foreach(var visit in visits)
                {
                    var journey=worldStore.TravelerJourneyFor(visit.Id);
                    if(journey.CheckInAt>now)continue;
                    if(visit.Admission==TravelerAdmission.Pending)pending++;
                    if(visit.Admission==TravelerAdmission.Admitted && now<journey.CheckOutAt)admitted++;
                    if(visit.Admission==TravelerAdmission.Refused)refused++;
                    var actor=FindActor(visit.PersonId);
                    TravelerPlan runtimePlan;
                    if(visit.Admission==TravelerAdmission.Pending && journey.Evaluate(now).Phase!=TravelerPhase.Arriving &&
                        actor && actor.gameObject.activeInHierarchy && actor.transform.parent==directory.transform &&
                        travelerPlans.TryGetValue(visit.PersonId,out runtimePlan) && !runtimePlan.ObservedOutdoorArrival)
                        throw new InvalidOperationException("Catch-up guest materialized outside without an observed approach: "+visit.PersonId);
                    if(actor && actor.gameObject.activeInHierarchy && actor.transform.parent==directory.transform && watched.Add(actor.GetInstanceID()))
                    {
                        var boundVisit=visit;
                        var last=visit.Route[visit.Route.Count-1];
                        var door=geometry.ScenePoint(new RoutePoint(last.X,last.Z));
                        var witness=actor.gameObject.AddComponent<NativeInnDoorBodyWitness>();
                        witness.DisabledAt=position=>
                        {
                            if(GameManager.Instance.IsPlayerInside || boundVisit.Admission!=TravelerAdmission.Admitted ||
                                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()>=worldStore.TravelerJourneyFor(boundVisit.Id).CheckOutAt)return;
                            float gap=Vector3.Distance(position,door);
                            if(gap>.56f)prematureDisappearances.Add(boundVisit.PersonId+":"+gap);
                            else doorReceipts++;
                        };
                    }
                }
                if(prematureDisappearances.Count>0)throw new InvalidOperationException("Admitted body vanished before reaching Inn door: "+prematureDisappearances[0]);
                if(Time.realtimeSinceStartup>=nextReport)
                {
                    nextReport=Time.realtimeSinceStartup+10;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_ADMISSION_WAIT: building="+building+"; visits="+visits.Count+
                        "; duePending="+pending+"; admittedNow="+admitted+"; refused="+refused);
                }
                if(visits.Count>0 && pending==0 && admitted>0)break;
                yield return null;
            }
            if(visits==null || visits.Count==0 || pending!=0 || admitted==0)
                throw new InvalidOperationException("Ordinary Inn admission did not complete: building="+building+
                    "; duePending="+pending+"; admitted="+admitted+"; refused="+refused);
            // Let observed outdoor arrivals finish their real door approach.
            deadline=Time.realtimeSinceStartup+90;
            int outside=0;
            do
            {
                if(prematureDisappearances.Count>0)throw new InvalidOperationException("Admitted body vanished before reaching Inn door: "+prematureDisappearances[0]);
                outside=0;double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                foreach(var visit in visits)
                {
                    var journey=worldStore.TravelerJourneyFor(visit.Id);
                    if(visit.Admission!=TravelerAdmission.Admitted || now<journey.CheckInAt || now>=journey.CheckOutAt)continue;
                    var actor=FindActor(visit.PersonId);
                    if(actor && actor.gameObject.activeInHierarchy && actor.transform.parent==directory.transform)outside++;
                }
                if(outside==0)break;
                yield return null;
            }while(Time.realtimeSinceStartup<deadline);
            if(outside!=0)throw new InvalidOperationException("Admitted guests still blocked outdoors: "+outside);
            if(prematureDisappearances.Count>0)throw new InvalidOperationException("Admitted body vanished before reaching Inn door: "+prematureDisappearances[0]);
            DaggerfallStaticDoors owner=null;StaticDoor entry=default(StaticDoor);
            foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var door in candidate.Doors)
                    if(door.buildingKey==building && door.doorType==DoorTypes.Building){owner=candidate;entry=door;break;}
            if(!owner)throw new InvalidOperationException("Ordinary Inn entry absent.");
            TownSmokeHarness.EnterInterior(owner.transform,entry);
            deadline=Time.realtimeSinceStartup+45;
            int bodies=0;
            while(Time.realtimeSinceStartup<deadline)
            {
                bodies=0;
                if(interior && interior.EntryDoor.buildingKey==building)
                    foreach(var visit in visits)
                    {
                        var actor=FindActor(visit.PersonId);
                        if(visit.Admission==TravelerAdmission.Admitted && actor && actor.gameObject.activeInHierarchy &&
                            actor.transform.parent==interior.transform)bodies++;
                    }
                if(bodies>=admitted)break;
                yield return null;
            }
            if(bodies<admitted)throw new InvalidOperationException("Not all admitted guests materialized inside: "+bodies+"/"+admitted);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ORDINARY_INN_ENTRY_PASS: building="+building+
                "; admitted="+admitted+"; activeInteriorTravelers="+bodies+"; observedDoorBodyReceipts="+doorReceipts+
                "; catchupExteriorBodies=0; duePending=0; blockedAdmittedOutside=0; noGuestOrBookingInjection=true");
        }
    }

    internal sealed class NativeInnDoorBodyWitness : MonoBehaviour
    {
        internal Action<Vector3> DisabledAt;
        private void OnDisable(){if(DisabledAt!=null)DisabledAt(transform.position-Vector3.up);}
    }
}
