using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Shared Inn departures start after native banks close at 15:00. This
        // focused fixture extends their service window, preserving the actual
        // household schedule. Production bank hours are restored even on failure.
        internal IEnumerator VerifyFamilyCashVisit()
        {
            int bank=(int)DFLocation.BuildingTypes.Bank;
            byte close=PlayerActivate.closeHours[bank];
            PlayerActivate.closeHours[bank]=20;
            var probe=VerifyFamilyCashDuringOpenBank();
            try
            {
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_FIXTURE: nativeBankClose="+close+
                    "; testBankClose=20; controlledWallet=true; controlledClockAndObserver=true");
                while(probe.MoveNext())yield return probe.Current;
            }
            finally {PlayerActivate.closeHours[bank]=close;(probe as IDisposable)?.Dispose();}
        }

        private IEnumerator VerifyFamilyCashDuringOpenBank()
        {
            var game=GameManager.Instance;DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            // SAVE82 predates shared outings: its already published household
            // days contain no JointInn. Let normal dawn planning create a new
            // day while both parents are still home instead of injecting a plan.
            double now=ActionNow();
            double dawn=(Math.Floor(now/86400d)+1)*86400d+5*3600;
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(dawn-now));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_NEW_DAY: hour=5; normalPlanning=true");
            HouseholdDayPlan day=null;float deadline=Time.realtimeSinceStartup+180;
            double bankDistance=double.PositiveInfinity;
            while(day==null && Time.realtimeSinceStartup<deadline)
            {
                if(Ready && EntranceSurveyReady && geometry!=null && geometry.UsesNativeNavigation)
                    day=ChooseFamilyCashProbe(out bankDistance);
                if(day==null)yield return new WaitForSecondsRealtime(0.5f);
            }
            if(day==null)throw new InvalidOperationException("No prepared, eligible family outing near an open bank is available: "+familyCashSelectionStatus);
            var firstId=day.First.ResidentId;var secondId=day.Second.ResidentId;
            double departure=day.AbsoluteDay*86400d+Math.Max(day.JointInn.First.DepartureSeconds,day.JointInn.Second.DepartureSeconds);
            var path=day.JointInn.First.Itinerary.Stops[0].Route;
            var home=geometry.ScenePoint(new RoutePoint(path[0].X,path[0].Z));
            var next=geometry.ScenePoint(new RoutePoint(path[1].X,path[1].Z));
            var forward=(next-home).normalized;var side=Vector3.Cross(Vector3.up,forward);
            game.PlayerMotor.transform.position=home+forward*4+side*4+Vector3.up;Physics.SyncTransforms();
            double advance=departure+5-ActionNow();
            if(advance>0)DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)advance);
            ResidentActor first=null,second=null;HouseholdJointProgress progress=null;
            deadline=Time.realtimeSinceStartup+60;
            while(Time.realtimeSinceStartup<deadline)
            {
                first=FindActor(firstId);second=FindActor(secondId);
                progress=worldStore.ReadHouseholdJointProgress(day.SettlementId,day.HomeKey);
                if(first && second && first.gameObject.activeInHierarchy && second.gameObject.activeInHierarchy &&
                    progress!=null && progress.Phase==HouseholdJointPhase.Outbound && progress.First.Distance>2 && progress.Second.Distance>2)break;
                yield return null;
            }
            if(!first || !second || progress==null || progress.Phase!=HouseholdJointPhase.Outbound ||
                progress.First.Distance<=2 || progress.Second.Distance<=2)
                throw new InvalidOperationException("Selected family did not physically depart together: phase="+progress?.Phase);
            var owner=first.GetComponent<NativeNpcInventoryOwner>();var wallet=worldStore.ReadNpcInventory(firstId);
            var otherWallet=worldStore.ReadNpcInventory(secondId);
            if(owner==null || wallet==null || otherWallet==null || otherWallet.WalletGold<1 ||
                !worldStore.CommitNpcInventoryTransaction("family-cash-empty:"+wallet.Revision,new[]{new NpcInventoryUpdate {
                    PersonId=firstId,ExpectedRevision=wallet.Revision,WalletGold=0,Items=wallet.Items}}))
                throw new InvalidOperationException("Could not establish the one-empty-wallet family fixture.");
            owner.Binding.Revision=worldStore.ReadNpcInventory(firstId).Revision;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_START: first="+firstId+"; partner="+secondId+
                "; plannedBankDistance="+bankDistance+"; bothDeparted=true; otherGold="+otherWallet.WalletGold);
            var host=GetComponent<PeopleOfTheIliacBayMod>();NpcCashVisit visit=null;
            deadline=Time.realtimeSinceStartup+20;
            while((visit=worldStore.ReadNpcCashVisit(firstId))==null && Time.realtimeSinceStartup<deadline)yield return null;
            if(visit==null || !visit.Bank || visit.WaitingPartnerId!=secondId || visit.Rendezvous==null || visit.PartnerWaitPosition==null)
                throw new InvalidOperationException("Empty family wallet did not bind one bank collector and one waiting partner.");
            var rendezvous=NpcActionExecutor.CopyTarget(visit.Rendezvous);
            Vector3 waiting=second.transform.position,previous=first.transform.position;
            double travelled=0;float maxWaitingDrift=0,maxStep=0;bool bankService=false,paid=false,waitingContext=false,collectorContext=false;
            int expectedGold=NativeNpcInventoryBridge.CashWithdrawalGold(worldStore,firstId);long paidRevision=-1;
            bool sawBody=true;deadline=Time.realtimeSinceStartup+300;
            while(Time.realtimeSinceStartup<deadline)
            {
                visit=worldStore.ReadNpcCashVisit(firstId);first=FindActor(firstId);second=FindActor(secondId);
                if(visit==null)break;
                if(!second || !second.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Waiting partner disappeared while the observer remained nearby.");
                maxWaitingDrift=Mathf.Max(maxWaitingDrift,Vector3.Distance(waiting,second.transform.position));
                if(maxWaitingDrift>0.75f || !second.IsStandingStill)
                    throw new InvalidOperationException("Waiting partner drifted or walked in place: drift="+maxWaitingDrift);
                string context=host.CashJourneyForProbe(second,worldStore,ActionNow());
                waitingContext|=context!=null && context.Contains("waiting here for my partner") && context.Contains("collecting money");
                if(first && first.gameObject.activeInHierarchy)
                {
                    if(sawBody){float step=Vector3.Distance(previous,first.transform.position);maxStep=Mathf.Max(maxStep,step);travelled+=step;
                        if(step>2)throw new InvalidOperationException("Family collector jumped while visible: metres="+step);}
                    previous=first.transform.position;sawBody=true;
                    context=host.CashJourneyForProbe(first,worldStore,ActionNow());
                    collectorContext|=context!=null && context.Contains("my partner is waiting for me");
                }
                else sawBody=false;
                bankService|=visit.CounterJourney!=null;
                var current=worldStore.ReadNpcInventory(firstId);
                if(current.WalletGold>0)
                {
                    if(!paid){paid=true;paidRevision=current.Revision;Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_PAID: gold="+current.WalletGold+"; bankService="+bankService);}
                    if(current.WalletGold!=expectedGold || current.Revision!=paidRevision)
                        throw new InvalidOperationException("Family cash payout differs or repeats.");
                }
                if(worldStore.ReadNpcCashVisit(secondId)!=null || worldStore.ReadNpcInventory(secondId).WalletGold!=otherWallet.WalletGold)
                    throw new InvalidOperationException("Waiting partner started a second errand or lost money.");
                yield return null;
            }
            if(worldStore.ReadNpcCashVisit(firstId)!=null || !paid || !bankService || !waitingContext || !collectorContext || travelled<5)
                throw new InvalidOperationException("Family cash errand incomplete: paid="+paid+"; service="+bankService+
                    "; waitingContext="+waitingContext+"; collectorContext="+collectorContext+"; travelled="+travelled);
            if(worldStore.ReadNpcCashWait(secondId)!=null)throw new InvalidOperationException("Reunited partner still has a waiting context.");
            first=FindActor(firstId);second=FindActor(secondId);
            if(!first || !second || Vector3.Distance(first.transform.position-Vector3.up,ActionWorldPoint(rendezvous))>1)
                throw new InvalidOperationException("Cash collector did not reach the original rendezvous.");
            var resumed=worldStore.ReadHouseholdJointProgress(day.SettlementId,day.HomeKey);
            double firstDistance=resumed.First.Distance,secondDistance=resumed.Second.Distance;
            Vector3 firstPrevious=first.transform.position,secondPrevious=second.transform.position;
            deadline=Time.realtimeSinceStartup+25;
            while(Time.realtimeSinceStartup<deadline)
            {
                first=FindActor(firstId);second=FindActor(secondId);
                if(!first || !second || !first.gameObject.activeInHierarchy || !second.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Reunited pair lost an exterior body before resuming.");
                maxStep=Mathf.Max(maxStep,Vector3.Distance(firstPrevious,first.transform.position),Vector3.Distance(secondPrevious,second.transform.position));
                if(maxStep>2)throw new InvalidOperationException("Reunited family snapped to its old route: metres="+maxStep);
                firstPrevious=first.transform.position;secondPrevious=second.transform.position;
                resumed=worldStore.ReadHouseholdJointProgress(day.SettlementId,day.HomeKey);
                if(resumed.First.Distance-firstDistance>2 && resumed.Second.Distance-secondDistance>2)break;
                yield return null;
            }
            if(resumed.First.Distance-firstDistance<=2 || resumed.Second.Distance-secondDistance<=2)
                throw new InvalidOperationException("Reunited family does not resume together: phase="+resumed.Phase+
                    "; first="+(resumed.First.Distance-firstDistance)+"; second="+(resumed.Second.Distance-secondDistance));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_PASS: naturalOuting=true; waitingContext=true; collectorContext=true; bankService=true; gold="+
                expectedGold+"; collectorTravelMetres="+travelled+"; waitingDrift="+maxWaitingDrift+"; maxStep="+maxStep+
                "; resumedFirst="+(resumed.First.Distance-firstDistance)+"; resumedSecond="+(resumed.Second.Distance-secondDistance)+
                "; testBankClose=20; bankInteriorUnobserved=true; innTableNotTested=true; bothEmptyNotTested=true");
        }

        private string familyCashSelectionStatus="native navigation or entrance survey not ready";
        private HouseholdDayPlan ChooseFamilyCashProbe(out double bankDistance)
        {
            bankDistance=double.PositiveInfinity;HouseholdDayPlan selected=null;double now=ActionNow();
            int active=0,waiting=0,serviceWindow=0,eligible=0,routes=0;
            var banks=directory.GetBuildingsOfType(DFLocation.BuildingTypes.Bank);
            foreach(var progress in worldStore.ReadActiveHouseholdJoints(population.SettlementId))
            {
                active++;
                if(progress.Phase!=HouseholdJointPhase.Waiting)continue;
                waiting++;
                var day=worldStore.ReadHouseholdDay(progress.SettlementId,progress.HomeKey,progress.AbsoluteDay);
                if(day?.JointInn==null)continue;
                double departure=day.AbsoluteDay*86400d+Math.Max(day.JointInn.First.DepartureSeconds,day.JointInn.Second.DepartureSeconds);
                int hour=(int)(departure%86400/3600);
                if(departure<now || hour<PlayerActivate.openHours[(int)DFLocation.BuildingTypes.Bank] ||
                    hour+1>=PlayerActivate.closeHours[(int)DFLocation.BuildingTypes.Bank])continue;
                serviceWindow++;
                var first=plans.Find(p=>p.Resident.Id==day.First.ResidentId);var second=plans.Find(p=>p.Resident.Id==day.Second.ResidentId);
                if(first==null || second==null || !MedicalPriorityAvailable(first,now,false) || !MedicalPriorityAvailable(second,now,false) ||
                    HasActiveNpcHealthCare(first.Resident.Id) || HasActiveNpcHealthCare(second.Resident.Id) ||
                    worldStore.ReadNpcCashVisit(first.Resident.Id)!=null || worldStore.ReadNpcCashVisit(second.Resident.Id)!=null)continue;
                eligible++;
                var start=day.JointInn.First.Itinerary.Stops[0].Route[0];
                foreach(var bank in banks)
                {
                    TownGeometry.Entrance entrance;if(!geometry.Entrances.TryGetValue(bank.buildingKey,out entrance))continue;
                    var route=geometry.RouteBetweenPoints(start,new SavedRoutePoint{X=entrance.LocalDoor.x,Z=entrance.LocalDoor.z});
                    if(route.Count<2)continue;routes++;double distance=0;
                    for(int i=1;i<route.Count;i++){double x=route[i].X-route[i-1].X,z=route[i].Z-route[i-1].Z;distance+=Math.Sqrt(x*x+z*z);}
                    if(distance<10 || distance>100 || distance>=bankDistance)continue;
                    selected=day;bankDistance=distance;
                }
            }
            familyCashSelectionStatus="active="+active+"; waiting="+waiting+"; serviceWindow="+serviceWindow+
                "; eligible="+eligible+"; bankRoutes="+routes+"; banks="+banks.Count;
            return selected;
        }
    }
}
