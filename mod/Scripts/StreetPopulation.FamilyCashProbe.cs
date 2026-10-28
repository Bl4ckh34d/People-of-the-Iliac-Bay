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
        // Isolate a peaceful bank trip: control clock, observer and wallets,
        // and disable existing local hostile enemies in this disposable fixture.
        // Native bank hours and ordinary planning still supply the actual trip.
        internal IEnumerator VerifyFamilyCashVisit(bool continueToInn=false)
        {
            var game=GameManager.Instance;DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            double current=ActionNow();
            game.PlayerEntity.PreventEnemySpawns=true;
            if(!PlayerActivate.IsBuildingOpen(DFLocation.BuildingTypes.Bank))
                throw new InvalidOperationException("Current family bank observation requires the actual native bank opening window; the fixture does not replan another day.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_FIXTURE: seed="+worldStore.WorldSeed+"; day="+(int)(current/86400)+
                "; hour="+(current%86400/3600)+"; nativeBankClose="+PlayerActivate.closeHours[(int)DFLocation.BuildingTypes.Bank]+
                "; controlledWallet=true; controlledObserver=true; normalHouseholdProducer=true; dayAdvance=false; readinessLimit=30");
            // Observe one actual natural shared-day household. Only the normal
            // producer's work order changes; source, Home, admission and payer
            // selection all remain owned by UpdateHouseholdDays.
            HouseholdDayPlan day=null;float deadline=Time.realtimeSinceStartup+30;double bankDistance=0;
            int observedHome=-1;
            while(day==null && Time.realtimeSinceStartup<deadline)
            {
                if(Ready && EntranceSurveyReady && geometry!=null && geometry.UsesNativeNavigation)
                {
                    if(observedHome<0)observedHome=ObserveFamilyCashHousehold();
                    if(observedHome>=0)
                    {
                        nextHouseholdDay=population.Households.FindIndex(household=>household.BuildingKey==observedHome);
                        UpdateHouseholdDays(ActionNow());
                        day=ChooseFamilyCashProbe(out bankDistance,observedHome);
                    }
                }
                if(day==null)yield return new WaitForSecondsRealtime(0.5f);
            }
            if(day==null)throw new InvalidOperationException("Observed natural family outing did not become bank-ready within 30 seconds: "+familyCashSelectionStatus);
            string payer=day.JointInn.PayerId,partner=payer==day.First.ResidentId?day.Second.ResidentId:day.First.ResidentId;
            var plan=plans.Find(p=>p.Resident.Id==payer);
            var funding=worldStore.ReadPlannedHouseholdInnFunding(payer,ActionNow());
            if(funding==null || funding.RequiredGold<1)throw new InvalidOperationException("Shared Inn has no native budget.");
            foreach(var spawner in UnityEngine.Object.FindObjectsOfType<DaggerfallWorkshop.Game.Utility.FoeSpawner>())
            {
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_ISOLATION: pendingSpawner="+spawner.name+"; type="+spawner.FoeType+"; lastType="+spawner.lastFoeType);
                spawner.enabled=false;
            }
            foreach(var target in UnityEngine.Object.FindObjectsOfType<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>())
            {
                if(!GuardPopulation.IsEnemyThreat(target))continue;
                var scene=DefenseScene.For(target);
                if(scene==null || scene.Root!=directory.transform || !scene.PlayerPresent)
                    throw new InvalidOperationException("Cash fixture has an enemy outside the expected town scene: "+target.name+"; root="+scene?.Root?.name);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_ISOLATION: disabled="+target.name+"; root="+scene.Root.name+"; position="+target.transform.position);
                target.gameObject.SetActive(false);
            }
            foreach(string id in new[]{payer,partner})
            {
                var wallet=worldStore.ReadNpcInventory(id);
                if(wallet==null || !worldStore.CommitNpcInventoryTransaction("family-cash-fixture:"+id+":"+wallet.Revision,
                    new[]{new NpcInventoryUpdate{PersonId=id,ExpectedRevision=wallet.Revision,WalletGold=0,Items=wallet.Items}}))
                    throw new InvalidOperationException("Cannot establish two-empty-wallet fixture.");
                var body=FindActor(id);var owner=body?body.GetComponent<NativeNpcInventoryOwner>():null;
                if(owner && owner.Binding!=null)owner.Binding.Revision=worldStore.ReadNpcInventory(id).Revision;
            }
            var home=geometry.Entrances[day.HomeKey];
            Vector3 homePoint=geometry.ScenePoint(new RoutePoint(home.LocalDoor.x,home.LocalDoor.z));
            game.PlayerMotor.transform.position=homePoint+new Vector3(4,1,4);Physics.SyncTransforms();
            game.PlayerEntity.PreventEnemySpawns=true;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_START: payer="+payer+"; partner="+partner+
                "; requiredGold="+funding.RequiredGold+"; bankDistance="+bankDistance+"; bothEmpty=true");
            NpcCashVisit visit=null;deadline=Time.realtimeSinceStartup+30;
            while((visit=worldStore.ReadNpcCashVisit(payer))==null && Time.realtimeSinceStartup<deadline)yield return null;
            if(visit==null || !visit.Bank || visit.PlannedHouseholdDayId!=day.Id || visit.RequiredGold!=funding.RequiredGold || visit.WaitingPartnerId!=null)
                throw new InvalidOperationException("Morning budget did not start one bank collector: activity="+ActiveSchedule(plan).EvaluateSeconds(ActionNow()).Activity);
            int expectedGold=CashWithdrawalAmount(payer,visit);bool paid=false,service=false,context=false,visible=false;
            long paidRevision=-1;float maxStep=0,homeDistance=float.PositiveInfinity;double travelled=0;Vector3 previous=Vector3.zero;
            var host=GetComponent<PeopleOfTheIliacBayMod>();deadline=Time.realtimeSinceStartup+240;
            while(Time.realtimeSinceStartup<deadline)
            {
                visit=worldStore.ReadNpcCashVisit(payer);
                if(visit==null)
                {
                    if(!paid)
                    {
                        var alarm=worldStore.ActiveAlarm(payer,ActionNow());
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_DANGER: incident="+alarm?.Id+
                            "; threat="+alarm?.ThreatId+"; victim="+alarm?.VictimId+"; started="+alarm?.StartedAt);
                        foreach(var target in UnityEngine.Object.FindObjectsOfType<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>())
                            if(GuardPopulation.IsEnemyThreat(target))Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_ENEMY: name="+
                                target.name+"; type="+((DaggerfallWorkshop.Game.Entity.EnemyEntity)target.Entity).MobileEnemy.ID+
                                "; position="+target.transform.position+"; resident="+target.GetComponent<ResidentActor>()?.Resident?.Id);
                    }
                    break;
                }
                var actor=FindActor(payer);
                if(visit.WaitingPartnerId!=null || worldStore.ReadNpcCashWait(partner)!=null || worldStore.ReadNpcCashVisit(partner)!=null || worldStore.ReadNpcInventory(partner).WalletGold!=0)
                    throw new InvalidOperationException("Morning funding held the partner or duplicated the cash trip.");
                if(actor && actor.gameObject.activeInHierarchy)
                {
                    if(visible){float step=Vector3.Distance(previous,actor.transform.position);maxStep=Mathf.Max(maxStep,step);travelled+=step;
                        if(step>2)throw new InvalidOperationException("Cash collector jumped: metres="+step);}
                    previous=actor.transform.position;visible=true;
                    if(visit.Returning)homeDistance=Vector3.Distance(actor.transform.position-Vector3.up,homePoint);
                    string journey=host.CashJourneyForProbe(actor,worldStore,ActionNow());
                    context|=journey!=null && journey.Contains("partner") && journey.Contains("money");
                }
                else visible=false;
                service|=visit.CounterJourney!=null;
                var wallet=worldStore.ReadNpcInventory(payer);
                if(wallet.WalletGold>0)
                {
                    if(!paid){paid=true;paidRevision=wallet.Revision;Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_PAID: hour="+(ActionNow()%86400/3600)+"; gold="+wallet.WalletGold);}
                    if(wallet.WalletGold!=expectedGold || wallet.Revision!=paidRevision)throw new InvalidOperationException("Shared payout differs or repeats.");
                }
                yield return null;
            }
            if(worldStore.ReadNpcCashVisit(payer)!=null || !paid || !service || !context || travelled<=0 || homeDistance>1.2f || ActionNow()>=funding.DepartureAt)
                throw new InvalidOperationException("Proactive cash incomplete: paid="+paid+"; service="+service+"; context="+context+"; travelled="+travelled+
                    "; homeDistance="+homeDistance+"; active="+(worldStore.ReadNpcCashVisit(payer)!=null));
            if(worldStore.NeedsNpcCashErrand(payer,ActionNow()) || worldStore.NeedsNpcCashErrand(partner,ActionNow()))throw new InvalidOperationException("Funded family still needs cash.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_HOME: travelled="+travelled+"; maxStep="+maxStep+"; homeDistance="+homeDistance+"; hour="+(ActionNow()%86400/3600));
            game.PlayerMotor.transform.position=homePoint+new Vector3(4,1,4);Physics.SyncTransforms();
            game.PlayerEntity.PreventEnemySpawns=true;
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(funding.DepartureAt+5-ActionNow()));
            HouseholdJointProgress progress=null;bool departed=false;deadline=Time.realtimeSinceStartup+45;
            while(Time.realtimeSinceStartup<deadline)
            {
                progress=worldStore.ReadHouseholdJointProgress(day.SettlementId,day.HomeKey);
                var first=FindActor(day.First.ResidentId);var second=FindActor(day.Second.ResidentId);
                if(first && second && first.gameObject.activeInHierarchy && second.gameObject.activeInHierarchy && progress!=null &&
                    progress.Phase==HouseholdJointPhase.Outbound && progress.First.Distance>2 && progress.Second.Distance>2){departed=true;break;}
                yield return null;
            }
            if(!departed)
                throw new InvalidOperationException("Funded family did not depart together: phase="+progress?.Phase);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_PASS: nativeBankHours=true; bothEmpty=true; onePayer=true; morningContext=true; bankService=true; gold="+
                expectedGold+"; travelledMetres="+travelled+"; maxStep="+maxStep+"; firstDistance="+progress.First.Distance+
                "; secondDistance="+progress.Second.Distance+"; bankInteriorUnobserved=true; innPaymentNotTested=true");
            if(continueToInn || Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-verify-family-inn")>=0)
                yield return VerifyFundedFamilyInn(day,payer,partner);
        }

        private void LogFamilyCashAdmission()
        {
            double now=ActionNow();int day=(int)(now/86400);
            foreach(var home in population.Households)
            {
                if(!HouseholdDayPlanner.SharedDay(worldStore.WorldSeed,population.SettlementId,home,day))continue;
                JointCandidate candidate;householdJointCandidates.TryGetValue(home.BuildingKey,out candidate);
                var stored=worldStore.ReadHouseholdDay(population.SettlementId,home.BuildingKey,day);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_ADMISSION: home="+home.BuildingKey+"; stored="+(stored!=null)+
                    "; candidate="+(candidate!=null)+"; ready="+candidate?.Ready+"; inn="+candidate?.Inn?.InnKey+"; survey="+householdJointSurvey);
                foreach(var resident in home.Residents)
                {
                    if(resident.AgeGroup!=ResidentAgeGroup.Adult)continue;
                    var plan=plans.Find(p=>p.Resident.Id==resident.Id);if(plan==null)continue;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_PARENT: id="+resident.Id+"; prepared="+FamilySourcePrepared(plan)+
                        "; home="+FamilyParentHome(plan,now)+"; itinerary="+(plan.Routine.Itinerary!=null)+
                        "; recovery="+worldStore.HasRoutineRecovery(resident.Id)+"; away="+worldStore.IsResidentAway(resident.Id,now));
                }
            }
        }

        private string familyCashSelectionStatus="native navigation or entrance survey not ready";
        private int ObserveFamilyCashHousehold()
        {
            string requested=Environment.GetEnvironmentVariable("POTIB_EDITOR_FAMILY_HOME_KEY");int requestedKey=-1;
            if(!string.IsNullOrEmpty(requested) && (!int.TryParse(requested,out requestedKey) || requestedKey<0))
                throw new InvalidOperationException("Invalid controlled family home key.");
            double now=ActionNow();int day=(int)Math.Floor(now/86400d);
            foreach(var home in population.Households)
            {
                if(requestedKey>=0 && home.BuildingKey!=requestedKey ||
                    !HouseholdDayPlanner.SharedDay(worldStore.WorldSeed,population.SettlementId,home,day))continue;
                var published=worldStore.ReadHouseholdDay(population.SettlementId,home.BuildingKey,day);
                if(published!=null && (published.JointInn==null || published.JointSuppressed))continue;
                var child=home.Residents.Find(person=>person.IsAlive && person.AgeGroup==ResidentAgeGroup.Child &&
                    person.MotherId!=null && person.FatherId!=null);
                if(child==null)continue;
                var first=plans.Find(plan=>plan.Resident.Id==child.MotherId);
                var second=plans.Find(plan=>plan.Resident.Id==child.FatherId);
                if(first==null || second==null || !first.Resident.IsAlive || !second.Resident.IsAlive ||
                    first.Resident.IsQuestProtected || second.Resident.IsQuestProtected ||
                    !FamilyParentHome(first,now) || !FamilyParentHome(second,now))continue;
                TownGeometry.Entrance entrance;if(!geometry.Entrances.TryGetValue(home.BuildingKey,out entrance))continue;
                Vector3 observer;
                var ground=geometry.ScenePoint(new RoutePoint(entrance.LocalDoor.x,entrance.LocalDoor.z));
                if(!NativeMoveToWaitProbe.TryObserverGround(geometry,directory,ground,out observer))continue;
                GameManager.Instance.PlayerMotor.transform.position=observer+Vector3.up;Physics.SyncTransforms();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CASH_OBSERVED: home="+home.BuildingKey+"; first="+first.Resident.Id+
                    "; second="+second.Resident.Id+"; firstPrepared="+FamilySourcePrepared(first)+"; secondPrepared="+FamilySourcePrepared(second)+
                    "; sharedDay=true; bothHome=true; planInjected=false; payerInjected=false; observer="+observer.ToString("R"));
                return home.BuildingKey;
            }
            familyCashSelectionStatus="requestedHome="+requestedKey+"; naturalSharedHouseholdCurrentlyHome=false";
            return -1;
        }
        private HouseholdDayPlan ChooseFamilyCashProbe(out double bankDistance,int observedHome=-1)
        {
            bankDistance=double.PositiveInfinity;HouseholdDayPlan selected=null;double now=ActionNow();
            string requestedHome=Environment.GetEnvironmentVariable("POTIB_EDITOR_FAMILY_HOME_KEY");
            int homeKey=observedHome;
            if(!string.IsNullOrEmpty(requestedHome) && (!int.TryParse(requestedHome,out homeKey) || homeKey<0))
                throw new InvalidOperationException("Invalid controlled family home key.");
            int active=0,waiting=0,funded=0,eligible=0,routes=0;var banks=directory.GetBuildingsOfType(DFLocation.BuildingTypes.Bank);
            foreach(var progress in worldStore.ReadActiveHouseholdJoints(population.SettlementId))
            {
                if(homeKey>=0 && progress.HomeKey!=homeKey)continue;
                active++;if(progress.Phase!=HouseholdJointPhase.Waiting)continue;waiting++;
                var day=worldStore.ReadHouseholdDay(progress.SettlementId,progress.HomeKey,progress.AbsoluteDay);
                if(day?.JointInn?.PayerId==null || day.AbsoluteDay!=(int)(now/86400))continue;funded++;
                var payer=plans.Find(p=>p.Resident.Id==day.JointInn.PayerId);
                if(payer==null || !MedicalPriorityAvailable(payer,now,false) || HasActiveNpcHealthCare(payer.Resident.Id) || worldStore.ReadNpcCashVisit(payer.Resident.Id)!=null)continue;
                var other=plans.Find(p=>p.Resident.Id==(day.JointInn.PayerId==day.First.ResidentId?day.Second.ResidentId:day.First.ResidentId));
                var funding=worldStore.ReadPlannedHouseholdInnFunding(payer.Resident.Id,now);
                if(other==null || !FamilyParentHome(payer,now) || !FamilyParentHome(other,now) || !PlannedCashWindowOpen(payer,funding,now))continue;
                eligible++;var start=day.JointInn.First.Itinerary.Stops[0].Route[0];
                foreach(var bank in banks)
                {
                    TownGeometry.Entrance entrance;if(!geometry.Entrances.TryGetValue(bank.buildingKey,out entrance))continue;
                    var route=geometry.RouteBetweenPoints(start,new SavedRoutePoint{X=entrance.LocalDoor.x,Z=entrance.LocalDoor.z});
                    if(route.Count<2)continue;routes++;double distance=RouteLength(route);
                    if(distance<=0 || distance>=bankDistance)continue;
                    selected=day;bankDistance=distance;
                }
            }
            familyCashSelectionStatus="home="+homeKey+"; active="+active+"; waiting="+waiting+"; funded="+funded+"; eligible="+eligible+"; bankRoutes="+routes+"; banks="+banks.Count;
            return selected;
        }
    }
}
