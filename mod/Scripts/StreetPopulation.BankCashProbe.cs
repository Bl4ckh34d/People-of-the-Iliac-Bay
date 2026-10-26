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
        internal IEnumerator VerifyBankCashVisit()
        {
            var game=GameManager.Instance;var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((43200-clock.ToSeconds()%86400+86400)%86400));
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            float deadline=Time.realtimeSinceStartup+90;
            while((!EntranceSurveyReady || geometry==null || !NativeNavigationWorld.Current || !NativeNavigationWorld.Current.Ready ||
                NativeNavigationWorld.Current.Scene!="city-"+population.SettlementId) && Time.realtimeSinceStartup<deadline)yield return null;
            if(!EntranceSurveyReady || geometry==null || !geometry.UsesNativeNavigation)
                throw new InvalidOperationException("Bank fixture requires completed native exterior entrance/navigation survey.");
            var banks=directory.GetBuildingsOfType(DFLocation.BuildingTypes.Bank);
            DaggerfallStaticDoors owner=null;StaticDoor selected=default(StaticDoor);float nearest=float.MaxValue;
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                for(int i=0;i<(doors.Doors==null?0:doors.Doors.Length);i++)
                {
                    var door=doors.Doors[i];
                    if(door.doorType!=DoorTypes.Building || !banks.Exists(b=>b.buildingKey==door.buildingKey) ||
                        !geometry.Entrances.ContainsKey(door.buildingKey) || !CashBankOpen(door.buildingKey))continue;
                    float distance=(doors.GetDoorPosition(i)-game.PlayerMotor.transform.position).sqrMagnitude;
                    if(distance<nearest){nearest=distance;owner=doors;selected=door;}
                }
            if(!owner)throw new InvalidOperationException("Bank fixture has no open native bank entrance.");
            Plan selectedPlan=null;int outdoorHomePoints=0;
            var bankStart=MedicalDoorPosition(geometry.Entrances[selected.buildingKey]);
            foreach(var candidate in plans)
            {
                if(!candidate.Resident.IsAlive || candidate.Resident.AgeGroup!=ResidentAgeGroup.Adult || QuestOwned(candidate.Resident) ||
                    worldStore.HasActiveHouseholdJoint(population.SettlementId,candidate.Resident.Id) ||
                    worldStore.ActiveNpcAction(candidate.Resident.Id)!=null || worldStore.ActiveNpcTask(candidate.Resident.Id)!=null ||
                    HasActiveNpcHealthCare(candidate.Resident.Id) || worldStore.ReadNpcCashWait(candidate.Resident.Id)!=null)continue;
                var activity=ActiveSchedule(candidate).EvaluateSeconds(clock.ToSeconds()).Activity;
                if(activity==Activity.Work || activity==Activity.Sleep || NativeNpcInventoryBridge.CashWithdrawalGold(worldStore,candidate.Resident.Id)<1)continue;
                var choice=ChooseCashVisit(candidate,null,bankStart);
                if(choice==null || !choice.Bank || choice.ProviderKey!=selected.buildingKey)continue;
                SavedRoutePoint home;TryRoutineRecoveryDoor(candidate,candidate.Resident.HomeKey,out home);
                outdoorHomePoints=geometry.RouteBetweenPoints(new SavedRoutePoint{X=bankStart.X,Z=bankStart.Z},home).Count;
                selectedPlan=candidate;break;
            }
            if(selectedPlan==null)throw new InvalidOperationException("No eligible visitor can select this bank while outdoors.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BANK_CASH_START: resident="+selectedPlan.Resident.Id+"; bank="+selected.buildingKey+"; outdoorHomeRoutePoints="+outdoorHomePoints);
            TownSmokeHarness.EnterInterior(owner.transform,selected);
            deadline=Time.realtimeSinceStartup+90;
            while((!interior || interior.EntryDoor.buildingKey!=selected.buildingKey || indoorGeometry==null ||
                !indoorGeometry.EntryGround.HasValue || shopGeometry==null) && Time.realtimeSinceStartup<deadline)yield return null;
            if(!interior || interior.EntryDoor.buildingKey!=selected.buildingKey || indoorGeometry==null || !indoorGeometry.EntryGround.HasValue || shopGeometry==null)
                throw new InvalidOperationException("Native bank interior did not become ready.");
            bool wasEnabled=enabled;enabled=false;ClearActors();
            try
            {
                var start=ActionPosition(indoorGeometry.EntryGround.Value);var plan=plans.Find(p=>p.Resident.Id==selectedPlan.Resident.Id);
                double now=clock.ToSeconds();
                if(plan==null)throw new InvalidOperationException("The bank visitor lost its routine after entry.");
                var insideChoice=ChooseCashVisit(plan,null,start);
                if(insideChoice==null || !insideChoice.Bank || insideChoice.ProviderKey!=selected.buildingKey)
                {
                    SavedRoutePoint home;bool hasHome=TryRoutineRecoveryDoor(plan,plan.Resident.HomeKey,out home);
                    int points=hasHome?geometry.RouteBetweenPoints(new SavedRoutePoint{X=bankStart.X,Z=bankStart.Z},home).Count:-1;
                    throw new InvalidOperationException("Bank selection changed across native entry: outsidePoints="+outdoorHomePoints+
                        "; insidePoints="+points+"; hasHome="+hasHome+"; bankOpen="+CashBankOpen(selected.buildingKey)+
                        "; nativeCityNavigation="+geometry.UsesNativeNavigation+"; choiceBank="+(insideChoice!=null && insideChoice.Bank));
                }
                string id=plan.Resident.Id;ResidentActor actor=null;
                EnsureCashActor(plan,ref actor,start);
                if(!actor)throw new InvalidOperationException("Bank fixture could not create its ordinary visitor at the entry.");
                var inventoryOwner=actor.GetComponent<NativeNpcInventoryOwner>();
                var inventory=worldStore.ReadNpcInventory(id);
                if(inventoryOwner==null || inventory==null || !worldStore.CommitNpcInventoryTransaction("bank-probe-empty:"+inventory.Revision,
                    new[]{new NpcInventoryUpdate {PersonId=id,ExpectedRevision=inventory.Revision,WalletGold=0,Items=inventory.Items}}))
                    throw new InvalidOperationException("Could not establish the controlled empty-wallet start.");
                inventoryOwner.Binding.Revision=worldStore.ReadNpcInventory(id).Revision;
                if(!worldStore.NeedsNpcCashErrand(id,now))throw new InvalidOperationException("Empty visitor does not need a cash errand.");
                var host=GetComponent<PeopleOfTheIliacBayMod>();
                // Clear the player's capsule from the entry and counter route.
                game.PlayerMotor.transform.position=interior.transform.TransformPoint(indoorGeometry.LocalAnchors[indoorGeometry.LocalAnchors.Count-1])+Vector3.up;
                actor.Place(indoorGeometry.EntryGround.Value,Vector3.forward,true,preferNative:false);
                Physics.SyncTransforms();
                float counterAt=-1,paidAt=-1;bool context=false,returnedContext=false;double metres=0;Vector3 previous=actor.transform.position;
                float counterDistance=0,minimumClerkFacing=1;
                int expectedGold=NativeNpcInventoryBridge.CashWithdrawalGold(worldStore,id);long paidRevision=-1;
                deadline=Time.realtimeSinceStartup+200;
                while(Time.realtimeSinceStartup<deadline)
                {
                    now=clock.ToSeconds();UpdateBankCounterSurvey();
                    if(!ApplyNpcCashVisit(plan,ref actor,now,false))throw new InvalidOperationException(
                        "Native cash overlay declined visitor: paused="+GameManager.IsGamePaused+
                        "; sleeping="+(actor && actor.IsSleeping)+"; conversing="+(actor && actor.IsConversing)+
                        "; needsCash="+worldStore.NeedsNpcCashErrand(id,now)+"; activity="+ActiveSchedule(plan).EvaluateSeconds(now).Activity+
                        "; medicalPriority="+MedicalPriorityAvailable(plan,now,false)+"; healthcare="+HasActiveNpcHealthCare(id)+
                        "; entrancesReady="+EntranceSurveyReady+"; wallet="+worldStore.ReadNpcInventory(id).WalletGold+
                        "; away="+worldStore.IsResidentAway(id,now)+"; serviceDuty="+worldStore.HasCurrentNpcServiceDuty(id,now)+
                        "; appointment="+HasCurrentAppointmentDuty(id,now));
                    var visit=worldStore.ReadNpcCashVisit(id);
                    if(visit==null || !visit.Bank || visit.ProviderKey!=selected.buildingKey)
                        throw new InvalidOperationException("Cash visitor lost the selected bank.");
                    if(actor)
                    {
                        metres+=Vector3.Distance(previous,actor.transform.position);previous=actor.transform.position;
                        string journey=host.CashJourneyForProbe(actor,worldStore,now);
                        if(!visit.Returning && journey!=null && journey.Contains("I have no spending money with me; collecting money at the bank "))context=true;
                        if(visit.Returning && journey=="returning from a cash errand")returnedContext=true;
                    }
                    PhysicalShopVisit physical;
                    if(bankVisitors.TryGetValue(id,out physical) && physical.Snapshot.Phase==ShopVisitPhase.Haggling)
                    {
                        if(counterAt<0)counterAt=Time.realtimeSinceStartup;
                        if(!actor || !actor.IsStandingStill)throw new InvalidOperationException("Bank visitor walks in place at the clerk.");
                        if(Time.realtimeSinceStartup-counterAt>1)
                        {
                            StaticNPC clerk=null;
                            foreach(var npc in interior.GetComponentsInChildren<StaticNPC>())
                                if(npc.Data.hash==visit.ClerkHash && NativeBusinessServices.IsBankClerk(npc)){clerk=npc;break;}
                            if(!clerk)throw new InvalidOperationException("Bank visitor has no actual clerk at the counter.");
                            var facing=clerk.transform.position-actor.transform.position;facing.y=0;
                            counterDistance=Mathf.Max(counterDistance,facing.magnitude);
                            minimumClerkFacing=Mathf.Min(minimumClerkFacing,Vector3.Dot(actor.transform.forward,facing.normalized));
                            if(facing.magnitude<0.6f || facing.magnitude>3.5f || minimumClerkFacing<0.9f)
                                throw new InvalidOperationException("Bank visitor is not facing the nearby clerk: metres="+facing.magnitude+"; dot="+minimumClerkFacing);
                        }
                    }
                    var wallet=worldStore.ReadNpcInventory(id);
                    if(wallet.WalletGold>0)
                    {
                        if(paidAt<0)
                        {
                            paidAt=Time.realtimeSinceStartup;paidRevision=wallet.Revision;
                            float dwell=paidAt-counterAt;
                            if(counterAt<0 || dwell<7.8f || dwell>15.5f || wallet.WalletGold!=expectedGold)
                                throw new InvalidOperationException("Bank payout timing/value invalid: dwell="+dwell+"; gold="+wallet.WalletGold);
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BANK_CASH_PAID: resident="+id+"; gold="+wallet.WalletGold+"; dwellRealSeconds="+dwell+"; walkedMetres="+metres);
                        }
                        if(wallet.Revision!=paidRevision || wallet.WalletGold!=expectedGold)
                            throw new InvalidOperationException("Bank payout repeated or wallet changed before exit.");
                    }
                    if(visit.Returning && visit.CounterJourney==null && visit.Position.Kind==NpcActionTargetKind.WorldPoint)break;
                    yield return null;
                }
                var completed=worldStore.ReadNpcCashVisit(id);
                if(paidAt<0 || completed==null || completed.CounterJourney!=null || !completed.Returning || !context || !returnedContext || metres<2)
                    throw new InvalidOperationException("Bank visit incomplete: paid="+(paidAt>=0)+"; counter="+(completed?.CounterJourney!=null)+"; context="+context+"; returnContext="+returnedContext+"; metres="+metres);
                var memories=worldStore.Snapshot().SceneMemories.FindAll(m=>m.ObserverId==id && m.Kind==NpcSceneEventKind.CashVisit && m.At>=now-600);
                if(memories.Count!=1 || !memories[0].Observation.Contains("from the bank"))throw new InvalidOperationException("Bank visit lacks one factual cash memory.");
                var copy=new WorldStateStore(1);copy.Restore(worldStore.Snapshot());
                if(copy.ReadNpcInventory(id).WalletGold!=expectedGold || !copy.ReadNpcCashVisit(id).Returning)
                    throw new InvalidOperationException("Cash wallet/return state did not survive snapshot restore.");
                enabled=wasEnabled;
                game.PlayerEnterExit.TransitionExterior(true);
                deadline=Time.realtimeSinceStartup+30;
                while((game.PlayerEnterExit.IsPlayerInside || interior) && Time.realtimeSinceStartup<deadline)yield return null;
                if(game.PlayerEnterExit.IsPlayerInside || interior)throw new InvalidOperationException("Player could not leave the tested bank.");
                float moved=0;bool sawBody=false;deadline=Time.realtimeSinceStartup+30;
                while(Time.realtimeSinceStartup<deadline && moved<2)
                {
                    actor=FindActor(id);
                    if(actor && actor.gameObject.activeInHierarchy)
                    {
                        if(sawBody)moved+=Vector3.Distance(previous,actor.transform.position);
                        previous=actor.transform.position;sawBody=true;
                    }
                    yield return null;
                }
                if(moved<2)throw new InvalidOperationException("Bank visitor did not physically resume after exit: metres="+moved);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BANK_CASH_PASS: nativeBank=true; clerkDwellSeconds="+(paidAt-counterAt)+
                    "; paidGold="+expectedGold+"; context=true; memory=true; snapshotRestore=true; playerFadedExit=true; returnMetres="+moved+
                    "; counterMetres="+counterDistance+"; minimumClerkFacing="+minimumClerkFacing+
                    "; completeHomeJourneyNotTested=true; familyNotTested=true");
            }
            finally{enabled=wasEnabled;}
        }
    }

    public sealed partial class PeopleOfTheIliacBayMod
    {
        internal string CashJourneyForProbe(ResidentActor actor,WorldStateStore world,double now)
        {
            var speaker=new ConversationSpeakerCapture {ManagedActor=actor,PersonId=actor.Resident.Id,NativeObject=actor,Transform=actor.transform};
            var fact=CaptureCurrentJourney(world,speaker,now,ConversationContextFact<bool>.Verified(
                !GameManager.Instance.PlayerEnterExit.IsPlayerInside,ConversationContextFactSource.LiveObservation,now));
            string value;return fact!=null && fact.TryGetVerified(out value)?value:null;
        }
    }
}
