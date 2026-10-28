using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in acceptance of the existing native-item-repair service. It uses one
    // existing NPC-owned UID and the normal Purchase runtime; no service state,
    // stock, actor, or repair completion is synthesized by the probe.
    internal static class NativeNpcRepairDropoffProbe
    {
        internal const string RunFlag = "-peopleoftheiliacbay-native-repair-probe";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private sealed class Fixture
        {
            internal ResidentActor Actor;
            internal NativeNpcInventoryOwner Owner;
            internal DaggerfallUnityItem Item;
            internal int DamagedCondition, Price, Duration;
        }
        private const string ShopReadyCheckpoint="People - Daggerfall repair ready";
        private sealed class Journey { internal string PersonId,TaskId; }
        private sealed class ActualDoor { internal DaggerfallStaticDoors Owner;internal StaticDoor Door; }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            if ((Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) < 0 && Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE")!="native-repair") || string.IsNullOrWhiteSpace(character) ||
                !NativeProbeText.IsTestCharacter(character))
                throw new InvalidOperationException("Native repair probe requires its explicit flag and owned disposable character.");
            var game = GameManager.Instance;
            var store = Store(host);
            if(game.PlayerEnterExit.Interior && RMBLayout.IsRepairShop(game.PlayerEnterExit.Interior.BuildingData.BuildingType))
                foreach(var pending in store.PendingNpcActions())
                {
                    ulong pendingUid;
                    if(pending.Phase!=NpcActionPhase.Completed || pending.ServiceCode!=NpcShopCommerce.RepairServiceCode || pending.ServicePrice<=0 ||
                        pending.Target==null || pending.Target.BuildingKey!=game.PlayerEnterExit.Interior.EntryDoor.buildingKey ||
                        !ulong.TryParse(pending.Target.ItemId,out pendingUid))continue;
                    float ownerDeadline=Time.realtimeSinceStartup+30;ResidentActor pendingActor=null;NativeNpcInventoryOwner pendingOwner=null;DaggerfallUnityItem pendingItem=null;
                    while(Time.realtimeSinceStartup<ownerDeadline)
                    {
                        pendingActor=streets.FindActor(pending.ResidentId);
                        pendingOwner=pendingActor?pendingActor.GetComponent<NativeNpcInventoryOwner>():null;
                        pendingItem=pendingOwner && pendingOwner.Binding!=null?pendingOwner.Binding.Items.GetItem(pendingUid):null;
                        if(pendingItem!=null)break;
                        yield return null;
                    }
                    if(pendingItem==null || !pendingItem.RepairData.IsBeingRepaired() || !pendingItem.RepairData.IsBeingRepairedHere())
                        throw new InvalidOperationException("Saved pending repair has no actual same-shop owner/UID binding: "+pending.ResidentId+"; uid="+pendingUid);
                    var resumed=new Fixture {Actor=pendingActor,Owner=pendingOwner,Item=pendingItem,DamagedCondition=pendingItem.currentCondition,
                        Price=pending.ServicePrice,Duration=pendingItem.RepairData.RepairTime};
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_REPAIR_PENDING_RESUME: resident="+pending.ResidentId+"; itemUid="+pendingUid+
                        "; dueAt="+pendingItem.RepairData.GetTimeDone()+"; actualLoadedPendingItem=true; dropoffRepeated=false; homeWalkRepeated=false; diskProofAlreadyAccepted19=true");
                    yield return CollectPendingRepair(streets,store,resumed,pending,pendingItem.RepairData.GetTimeDone());
                    yield break;
                }
            var journey=new Journey();
            yield return EnterActualRepairShop(streets,journey);
            if (!host || !streets || !streets.Ready || !game || game.PlayerEntity == null || game.PlayerEntity.Name != character ||
                !game.PlayerEnterExit.Interior || !game.PlayerEnterExit.IsPlayerInsideOpenShop ||
                !RMBLayout.IsRepairShop(game.PlayerEnterExit.Interior.BuildingData.BuildingType) ||
                !PlayerActivate.IsBuildingOpen(game.PlayerEnterExit.Interior.BuildingData.BuildingType) || store == null)
                throw new InvalidOperationException("Native repair probe requires an open, loaded native repair shop on its owned character.");

            Fixture fixture = null;float fixtureDeadline=Time.realtimeSinceStartup+30;
            while(fixture==null && Time.realtimeSinceStartup<fixtureDeadline)
            {fixture=FindFixture(streets,store,journey.PersonId);if(fixture==null)yield return null;}
            if (fixture == null)
                throw new InvalidOperationException("Native repair owned-item readiness missing: "+RepairInventoryState(streets,store,journey.PersonId));
            bool originallyEquipped=fixture.Item.IsEquipped;
            if(originallyEquipped && !fixture.Actor.GetComponent<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>().Entity.ItemEquipTable.UnequipItem(fixture.Item))
                throw new InvalidOperationException("Existing native owner could not remove its own item for the repair fixture.");
            fixture.Item.currentCondition = fixture.DamagedCondition;
            string captureReason;
            if (!NativeNpcInventoryBridge.TryCapture(store, fixture.Owner.Binding, out captureReason))
                throw new InvalidOperationException("Native repair probe could not persist the controlled damaged owned item: " + captureReason);

            string itemName = fixture.Item.LongName;
            ulong itemUid = fixture.Item.UID;
            string residentId = fixture.Actor.Resident.Id;
            int walletBefore = store.ReadNpcInventory(residentId).WalletGold;
            NpcActionOption dropoff = FindRepairOption(streets, residentId, "Repair my " + itemName + ".", itemUid, false);
            if (dropoff == null || dropoff.Target.QuotedGold != fixture.Price || dropoff.Target.QuotedGold <= 0)
                throw new InvalidOperationException("Native repair drop-off did not retain the exact native repair quote for the owned UID.");
            string reason;
            if(journey.TaskId!=null && !streets.TryStopConversationAction(residentId,out reason))
                throw new InvalidOperationException("The accepted shop Wait could not stop before its repair handoff: "+reason);
            // No yield: the existing terminal-action takeover gives this exact
            // normal repair quote control before ordinary routine return.
            if (!NpcProbeCommandSource.TryAcceptAction(streets, residentId, "native-repair-dropoff:" + Guid.NewGuid().ToString("N"),
                new NpcActionDecision { Option = Copy(dropoff), DurationSeconds = 300 }, out reason))
                throw new InvalidOperationException("Native repair drop-off was not accepted: " + reason);
            var acceptedDropoff = store.ActiveNpcAction(residentId);
            if (acceptedDropoff == null || acceptedDropoff.ServiceCode != NpcShopCommerce.RepairServiceCode ||
                acceptedDropoff.Target.ItemId != itemUid.ToString(CultureInfo.InvariantCulture) || acceptedDropoff.ServicePrice != fixture.Price ||
                acceptedDropoff.Target.QuotedGold != fixture.Price || acceptedDropoff.Target.Quantity != 1)
                throw new InvalidOperationException("Native repair drop-off lost its exact UID, native price, or single-item contract.");
            yield return WaitForTerminal(store, acceptedDropoff.Id, 180f);
            var dropped = store.ReadNpcAction(acceptedDropoff.Id);
            if (dropped == null || dropped.Phase != NpcActionPhase.Completed)
                throw new InvalidOperationException("Native repair drop-off did not complete: " + TerminalReason(dropped));
            DaggerfallUnityItem live = fixture.Owner.Binding.Items.GetItem(itemUid);
            if (live == null || live.UID != itemUid || !live.RepairData.IsBeingRepaired() || live.currentCondition != fixture.DamagedCondition ||
                live.RepairData.RepairTime != fixture.Duration || store.ReadNpcInventory(residentId).WalletGold != walletBefore - fixture.Price)
                throw new InvalidOperationException("Native repair drop-off did not preserve UID, native duration, pending condition, or one own-wallet payment.");
            bool replayApplied = false;
            if (store.TryCompleteNpcActionEffect(dropped.Id, dropped.Revision, Now(), () => { replayApplied = true; return true; }) || replayApplied)
                throw new InvalidOperationException("Completed native repair drop-off accepted a duplicate effect.");
            if (FindRepairOption(streets, residentId, "Collect my " + itemName + ".", itemUid, true) != null ||
                live.RepairData.IsRepairFinished() || live.currentCondition == live.maxCondition)
                throw new InvalidOperationException("Native repair collection became available before the native repair duration completed.");

            ulong dueAt = live.RepairData.GetTimeDone();
            if (NativeNpcRepairContext.Describe(live) == null)
                throw new InvalidOperationException("Pending native repair lost its inventory custody context.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_REPAIR_DROPOFF_PASS: resident="+residentId+"; itemUid="+itemUid+
                "; paid="+fixture.Price+"; dueAt="+dueAt+"; sameUid=true; oneOwnWalletPayment=true; pendingCondition=true; earlyCollectionDenied=true; duplicateEffectDenied=true");
            string checkpoint="People - Repair "+Guid.NewGuid().ToString("N").Substring(0,8);
            bool collectionCompleted=false;
            try
            {
            // Reuse the existing real SaveLoadManager fixture path. The old
            // native collection lease must never survive into the loaded branch.
            var save=typeof(ConversationMoodProbe).GetMethod("SaveCheckpoint",BindingFlags.Static|BindingFlags.NonPublic,null,
                new[]{typeof(GameManager),typeof(string),typeof(string)},null);
            if(save==null)throw new InvalidOperationException("Existing native disk checkpoint path is unavailable.");
            yield return (IEnumerator)save.Invoke(null,new object[]{game,character,checkpoint});
            yield return ConversationMoodProbe.LoadSave(host,streets,character,checkpoint,true);
            store=Store(host);fixtureDeadline=Time.realtimeSinceStartup+30;
            ResidentActor restoredActor=null;NativeNpcInventoryOwner restoredOwner=null;
            while(Time.realtimeSinceStartup<fixtureDeadline)
            {
                restoredActor=streets.FindActor(residentId);
                if(!restoredActor)
                    foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                        if(candidate.Resident!=null && candidate.Resident.Id==residentId){restoredActor=candidate;break;}
                restoredOwner=restoredActor?restoredActor.GetComponent<NativeNpcInventoryOwner>():null;
                if(restoredActor && restoredActor.gameObject.activeInHierarchy && restoredOwner && restoredOwner.Binding!=null)break;
                yield return null;
            }
            if(!restoredActor || !restoredActor.gameObject.activeInHierarchy || !restoredOwner || restoredOwner.Binding==null)
                throw new InvalidOperationException("Native repair disk load did not restore the actual owner body and inventory binding.");
            fixture.Actor=restoredActor;fixture.Owner=restoredOwner;live=restoredOwner.Binding.Items.GetItem(itemUid);
            if(live==null || live.IsEquipped || live.stackCount!=1 || live.currentCondition!=fixture.DamagedCondition ||
                !live.RepairData.IsBeingRepaired() || !live.RepairData.IsBeingRepairedHere() || live.RepairData.GetTimeDone()!=dueAt ||
                store.ReadNpcInventory(residentId).WalletGold!=walletBefore-fixture.Price ||
                !game.PlayerEnterExit.Interior || game.PlayerEnterExit.Interior.EntryDoor.buildingKey!=dropped.Target.BuildingKey)
                throw new InvalidOperationException("Native disk load changed pending repair UID, custody, equipment, condition, due time, wallet or shop.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_REPAIR_DISK_PASS: resident="+residentId+"; itemUid="+itemUid+
                "; building="+dropped.Target.BuildingKey+"; dueAt="+dueAt+"; pending=true; unequipped=true; exactWallet=true; nativeDiskReload=true; originallyEquipped="+originallyEquipped);
            if(dueAt<=Now())throw new InvalidOperationException("Native repair duration was not future-dated after drop-off.");
            yield return CollectPendingRepair(streets,store,fixture,dropped,dueAt);
            collectionCompleted=true;
            }
            finally
            {
                if(collectionCompleted)ConversationMoodProbe.DeleteCheckpoint(game,character,checkpoint);
                else Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_REPAIR_PENDING_CHECKPOINT_RETAINED: title="+checkpoint+
                    "; resident="+residentId+"; itemUid="+itemUid+"; dueAt="+dueAt+"; pendingDiskStatePreserved=true");
            }
        }

        private static IEnumerator CollectPendingRepair(StreetPopulation streets,WorldStateStore store,Fixture fixture,NpcActionJob dropped,ulong dueAt)
        {
            var game=GameManager.Instance;string residentId=dropped.ResidentId;
            ulong itemUid=fixture.Item.UID;string itemName=fixture.Item.LongName;string reason;bool replayApplied=false;
            DaggerfallUnityItem live;
            double now = Now();
            if(dueAt>now)DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(dueAt-now+1));
            // Accept the explicit collection before ordinary routine catch-up
            // can dismiss this observed body at the controlled next-day clock.
            live = fixture.Owner.Binding.Items.GetItem(itemUid);
            if (live == null || !live.RepairData.IsBeingRepaired() || !live.RepairData.IsRepairFinished() || live.currentCondition != fixture.DamagedCondition)
                throw new InvalidOperationException("Native repair duration did not finish while preserving damaged condition until collection.");

            NpcActionOption collect = FindRepairOption(streets, residentId, "Collect my " + itemName + ".", itemUid, true);
            if(collect==null)
            {
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_REPAIR_COLLECT_GATES: "+RepairCollectState(streets,fixture,itemUid));
                float gap=Vector3.Distance(fixture.Actor.GroundPosition,game.PlayerMotor.transform.position-Vector3.up);
                if(gap>8)
                {
                    // The owner walked to the counter for drop-off while the
                    // observer stayed at the entrance. Normal conversation
                    // still requires the player to approach this actual owner.
                    game.PlayerMotor.transform.position=fixture.Actor.GroundPosition+Vector3.right*2+Vector3.up;Physics.SyncTransforms();
                    collect=FindRepairOption(streets,residentId,"Collect my "+itemName+".",itemUid,true);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_REPAIR_COLLECT_APPROACH: priorGap="+gap+
                        "; normalOfferAfter="+(collect!=null)+"; observerOnly=true; npcMovedByProbe=false");
                }
            }
            if (collect == null || collect.Target.QuotedGold != 0 || collect.Target.PersonId != dropped.Target.PersonId ||
                collect.Target.SettlementId != dropped.Target.SettlementId || collect.Target.BuildingKey != dropped.Target.BuildingKey)
                throw new InvalidOperationException("Finished native repair collection quote mismatch: null="+(collect==null)+
                    "; expectedKeeper="+dropped.Target.PersonId+"; expectedMap="+dropped.Target.SettlementId+"; expectedShop="+dropped.Target.BuildingKey+
                    "; actualKeeper="+(collect==null?"none":collect.Target.PersonId)+"; actualMap="+(collect==null?-1:collect.Target.SettlementId)+
                    "; actualShop="+(collect==null?-1:collect.Target.BuildingKey)+"; actualQuote="+(collect==null?-1:collect.Target.QuotedGold)+
                    "; "+RepairCollectState(streets,fixture,itemUid));
            int walletAfterDropoff = store.ReadNpcInventory(residentId).WalletGold;
            if (!NpcProbeCommandSource.TryAcceptAction(streets, residentId, "native-repair-collect:" + Guid.NewGuid().ToString("N"),
                new NpcActionDecision { Option = Copy(collect), DurationSeconds = 300 }, out reason))
                throw new InvalidOperationException("Native repair collection was not accepted: " + reason);
            var acceptedCollection = store.ActiveNpcAction(residentId);
            if (acceptedCollection == null || acceptedCollection.ServiceCode != NpcShopCommerce.RepairServiceCode ||
                acceptedCollection.ServicePrice != 0 || acceptedCollection.Target.QuotedGold != 0 ||
                acceptedCollection.Target.ItemId != itemUid.ToString(CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Native repair collection lost same UID or introduced a second payment.");
            yield return WaitForTerminal(store, acceptedCollection.Id, 180f);
            var collected = store.ReadNpcAction(acceptedCollection.Id);
            var oldOwner=fixture.Owner;
            // A day jump legitimately rebuilds the observed interior bodies.
            // Read the live owner that executed the action, not its retired
            // pre-catchup ItemCollection lease.
            var currentActor=streets.FindActor(residentId);
            var currentOwner=currentActor?currentActor.GetComponent<NativeNpcInventoryOwner>():null;
            live=currentOwner && currentOwner.Binding!=null?currentOwner.Binding.Items.GetItem(itemUid):null;
            int actualWallet=store.ReadNpcInventory(residentId).WalletGold;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_REPAIR_COLLECT_RESULT: phase="+(collected==null?"missing":collected.Phase.ToString())+
                "; failure="+(collected==null?"missing":collected.FailureReason)+"; expectedUid="+itemUid+"; actualUid="+(live==null?0:live.UID)+
                "; condition="+(live==null?-1:live.currentCondition)+"; maxCondition="+(live==null?-1:live.maxCondition)+
                "; pending="+(live!=null && live.RepairData.IsBeingRepaired())+"; wallet="+actualWallet+"; expectedWallet="+walletAfterDropoff+
                "; currentOwner="+(bool)currentOwner+"; ownerRebound="+(!ReferenceEquals(oldOwner,currentOwner)));
            if(currentOwner && currentOwner.Binding!=null){fixture.Actor=currentActor;fixture.Owner=currentOwner;}
            if (collected == null || collected.Phase != NpcActionPhase.Completed || live == null || live.UID != itemUid ||
                live.RepairData.IsBeingRepaired() || live.currentCondition != live.maxCondition ||
                store.ReadNpcInventory(residentId).WalletGold != walletAfterDropoff)
                throw new InvalidOperationException("Native repair collection result mismatch: phase="+(collected==null?"missing":collected.Phase.ToString())+
                    "; reason="+(collected==null?"missing":collected.FailureReason)+"; uid="+(live==null?0:live.UID)+"/"+itemUid+
                    "; condition="+(live==null?-1:live.currentCondition)+"/"+(live==null?-1:live.maxCondition)+
                    "; pending="+(live!=null && live.RepairData.IsBeingRepaired())+"; wallet="+actualWallet+"/"+walletAfterDropoff+
                    "; currentOwner="+(bool)currentOwner+"; ownerRebound="+(!ReferenceEquals(oldOwner,currentOwner)));
            replayApplied=false;
            if(store.TryCompleteNpcActionEffect(collected.Id,collected.Revision,Now(),()=>{replayApplied=true;return true;}) || replayApplied ||
                FindRepairOption(streets,residentId,"Collect my "+itemName+".",itemUid,true)!=null)
                throw new InvalidOperationException("Collected native repair could be completed or collected twice.");
            if (NativeNpcRepairContext.Describe(live) != null)
                throw new InvalidOperationException("Collected native repair still reports unavailable custody.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_REPAIR_PASS: resident=" + residentId + "; keeper=" + dropped.Target.PersonId +
                "; itemUid=" + itemUid + "; price=" + fixture.Price + "; duration=" + fixture.Duration +
                "; onePayment=true; pendingCondition=true; collectedCondition=full; sameKeeper=true; nativeDiskReload=true; pendingUnequipped=true; collectionReplayDenied=true.");
        }

        private static string RepairCollectState(StreetPopulation streets,Fixture fixture,ulong uid)
        {
            var game=GameManager.Instance;var actor=fixture.Actor;
            var item=fixture.Owner.Binding.Items.GetItem(uid);
            var raw=new List<NpcActionOption>();
            typeof(StreetPopulation).GetMethod("OfferNativeRepairAction",Private).Invoke(streets,new object[]{actor,null,raw,false});
            var normal=streets.ConversationActionOptions(actor.Resident.Id,"Collect my finished item.");
            var keeperArguments=new object[]{null,null};
            bool keeper=(bool)typeof(StreetPopulation).GetMethod("TryNativeRepairKeeper",Private).Invoke(streets,keeperArguments);
            return "normalCandidates="+normal.Count+"; rawRepairOffers="+raw.Count+"; actor="+(bool)actor+
                "; active="+(actor && actor.gameObject.activeInHierarchy)+"; playerGap="+Vector3.Distance(actor.GroundPosition,game.PlayerMotor.transform.position-Vector3.up)+
                "; ready="+streets.Ready+"; openShop="+game.PlayerEnterExit.IsPlayerInsideOpenShop+"; keeper="+keeper+
                "; keeperId="+(keeper?((NativePerson)keeperArguments[0]).Id:"none")+"; itemPresent="+(item!=null)+
                "; pending="+(item!=null && item.RepairData.IsBeingRepaired())+"; here="+(item!=null && item.RepairData.IsBeingRepairedHere())+
                "; finished="+(item!=null && item.RepairData.IsRepairFinished())+"; rawQuote="+(raw.Count==0?-1:raw[0].Target.QuotedGold)+
                "; rawUid="+(raw.Count==0?"none":raw[0].Target.ItemId);
        }

        private static IEnumerator EnterActualRepairShop(StreetPopulation streets,Journey journey)
        {
            var game=GameManager.Instance;
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            if(game.PlayerEnterExit.Interior && game.PlayerEnterExit.IsPlayerInsideOpenShop &&
                RMBLayout.IsRepairShop(game.PlayerEnterExit.Interior.BuildingData.BuildingType))
            {
                var currentStore=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
                foreach(var task in currentStore.PendingNpcTasks())
                {
                    int index=task.CurrentStepIndex;
                    if(index<1 || task.Steps[index].Kind!=NpcActionKind.Wait || task.Steps[index].Target==null ||
                        task.Steps[index].Target.BuildingKey!=game.PlayerEnterExit.Interior.EntryDoor.buildingKey)continue;
                    var entered=currentStore.ReadNpcAction(task.Id+":step:"+(index-1));
                    if(!NpcTaskPositionBinding.HasCompletedStaticArrivalReceipt(task.Steps[index-1],entered,task.Steps[index]))continue;
                    journey.PersonId=task.ResidentId;journey.TaskId=task.Id;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_REPAIR_RESUME: resident="+task.ResidentId+"; task="+task.Id+
                        "; actualShopWait=true; homeWalkRepeated=false");break;
                }
                yield break;
            }
            if(game.PlayerEnterExit.IsPlayerInside)
            {
                game.PlayerEnterExit.TransitionExterior(true);float exitDeadline=Time.realtimeSinceStartup+30;
                while(game.PlayerEnterExit.IsPlayerInside && Time.realtimeSinceStartup<exitDeadline)yield return null;
                if(game.PlayerEnterExit.IsPlayerInside)throw new InvalidOperationException("Native repair fixture could not leave its previous interior.");
            }
            float readyDeadline=Time.realtimeSinceStartup+30;
            while(!streets.Ready && Time.realtimeSinceStartup<readyDeadline)yield return null;
            if(!streets.Ready)
                throw new InvalidOperationException("Native repair fixture lacks its already loaded town routines.");
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var world=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
            var town=world.CurrentPopulation(directory.MapID);
            var doorsByBuilding=new Dictionary<int,ActualDoor>();
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var door in owner.Doors)
                    if(door.doorType==DoorTypes.Building && !doorsByBuilding.ContainsKey(door.buildingKey))
                        doorsByBuilding.Add(door.buildingKey,new ActualDoor {Owner=owner,Door=door});
            ActualDoor selectedHome=null,selectedShop=null;float nearest=float.PositiveInfinity;
            double current=Now();
            foreach(var home in town.Households)
            {
                foreach(var person in home.Residents)
                {
                    if(!person.IsAlive || person.IsQuestProtected || person.AgeGroup!=ResidentAgeGroup.Adult ||
                        world.ActiveNpcAction(person.Id)!=null || world.ActiveNpcTask(person.Id)!=null)continue;
                    if(world.ReadNpcTreatmentVisit(person.Id)!=null || world.HasCurrentNpcServiceDuty(person.Id,current) ||
                        (bool)typeof(StreetPopulation).GetMethod("HasActiveNpcHealthCare",Private).Invoke(streets,new object[]{person.Id}) ||
                        !(bool)typeof(StreetPopulation).GetMethod("FamilyAllowsOptionalOuting",Private).Invoke(streets,new object[]{person,current%86400,(current+900)%86400}))continue;
                    var state=streets.GetState(person.Id,current);var future=streets.GetState(person.Id,current+900);
                    ActualDoor ownDoor;
                    if(person.NativeIdentity!=null || state.Activity!=Activity.Home || future.Activity==Activity.Work || future.Activity==Activity.Sleep ||
                        streets.IsAlarmed(person.Id) || world.HasActiveNpcCustody(person.Id) ||
                        !doorsByBuilding.TryGetValue(person.HomeKey,out ownDoor))continue;
                    foreach(var pair in doorsByBuilding)
                    {
                        BuildingSummary building;
                        if(!directory.GetBuildingSummary(pair.Key,out building) || !RMBLayout.IsRepairShop(building.BuildingType) ||
                            !PlayerActivate.IsBuildingOpen(building.BuildingType) || !game.PlayerActivate.BuildingIsUnlocked(building))continue;
                        float distance=Vector3.Distance(ownDoor.Owner.transform.TransformPoint(ownDoor.Door.buildingMatrix.MultiplyPoint3x4(ownDoor.Door.centre)),
                            pair.Value.Owner.transform.TransformPoint(pair.Value.Door.buildingMatrix.MultiplyPoint3x4(pair.Value.Door.centre)));
                        if(distance>=nearest)continue;
                        nearest=distance;selectedHome=ownDoor;selectedShop=pair.Value;journey.PersonId=person.Id;
                    }
                }
            }
            if(selectedHome==null || selectedShop==null)
                throw new InvalidOperationException("Current actual fixture has no awake free-home adult and unlocked real repair shop; no duty override or time-window search is allowed.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_REPAIR_FIXTURE: resident="+journey.PersonId+"; home="+selectedHome.Door.buildingKey+
                "; shop="+selectedShop.Door.buildingKey+"; actualOpenShop=true; existingFreeHomeTime=true");
            // Survey this actual destination before asking the resident to
            // leave. A completed Exit must not wait outside for a new survey
            // while its ordinary return-to-routine adapter is already running.
            var preparedDoorArguments=new object[]{selectedShop.Door.buildingKey,null,default(StaticDoor)};
            var readActualDoor=typeof(StreetPopulation).GetMethod("ReadBuildingActionDoor",Private);
            float prepareDoorDeadline=Time.realtimeSinceStartup+30;
            while(!(bool)readActualDoor.Invoke(streets,preparedDoorArguments) && Time.realtimeSinceStartup<prepareDoorDeadline)yield return null;
            if(!(bool)readActualDoor.Invoke(streets,preparedDoorArguments))
            {
                var actualGeometry=typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets) as TownGeometry;
                throw new InvalidOperationException("Real repair destination has no surveyed canonical native entry after30 seconds: building="+
                    selectedShop.Door.buildingKey+"; ready="+streets.Ready+"; entranceSurvey="+streets.EntranceSurveyReady+
                    "; knownGeometryDoor="+(actualGeometry!=null && actualGeometry.Entrances.ContainsKey(selectedShop.Door.buildingKey)));
            }
            TownSmokeHarness.EnterInterior(selectedHome.Owner.transform,selectedHome.Door);
            float deadline=Time.realtimeSinceStartup+30;
            ResidentActor actor=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                actor=streets.FindActor(journey.PersonId);
                if(streets.Ready && game.PlayerEnterExit.Interior &&
                    game.PlayerEnterExit.Interior.EntryDoor.buildingKey==selectedHome.Door.buildingKey && actor && actor.gameObject.activeInHierarchy)break;
                yield return null;
            }
            if(!actor || !actor.gameObject.activeInHierarchy || actor.IsSleeping)
                throw new InvalidOperationException("The selected real free-home adult did not materialize awake in its own home within30 seconds.");
            game.PlayerMotor.transform.position=actor.GroundPosition+Vector3.right*2+Vector3.up;Physics.SyncTransforms();
            var exit=AcceptDoor(streets,world,actor,NpcActionKind.Exit,selectedHome.Door.buildingKey);
            yield return WaitForTerminal(world,exit.Id,120);
            RequireDoorReceipt(world,exit.Id,selectedHome.Door.buildingKey);
            game.PlayerEnterExit.TransitionExterior(true);
            deadline=Time.realtimeSinceStartup+30;actor=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                actor=streets.FindActor(journey.PersonId);
                if(!game.PlayerEnterExit.IsPlayerInside && actor && actor.gameObject.activeInHierarchy)break;
                yield return null;
            }
            if(!actor || !actor.gameObject.activeInHierarchy || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("The accepted Exit did not materialize the same adult at its actual home door.");
            game.PlayerMotor.transform.position=actor.GroundPosition+Vector3.right*2+Vector3.up;Physics.SyncTransforms();
            deadline=Time.realtimeSinceStartup+30;float nextDoorDiagnostic=Time.realtimeSinceStartup;
            while(FindDoorOption(streets,actor,NpcActionKind.Enter,selectedShop.Door.buildingKey)==null)
            {
                if(Time.realtimeSinceStartup>=nextDoorDiagnostic)
                {Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_REPAIR_ENTER_READINESS: "+RepairDoorReadiness(streets,actor,selectedShop.Door.buildingKey));nextDoorDiagnostic=Time.realtimeSinceStartup+5;}
                var currentInterior=typeof(StreetPopulation).GetField("interior",Private).GetValue(streets) as DaggerfallInterior;
                if(Time.realtimeSinceStartup>=deadline || streets.Ready && streets.EntranceSurveyReady && !currentInterior)
                    throw new InvalidOperationException("The actual repair Enter quote is unavailable: "+RepairDoorReadiness(streets,actor,selectedShop.Door.buildingKey));
                yield return null;
            }
            var enterOption=FindDoorOption(streets,actor,NpcActionKind.Enter,selectedShop.Door.buildingKey);
            NpcTaskPlan serviceTask;string serviceReason;
            if(!NpcProbeCommandSource.TryAcceptEnterWait(streets,world,actor.Resident.Id,"native-repair-enter-wait:"+Guid.NewGuid().ToString("N"),
                enterOption,900,300,out serviceTask,out serviceReason))
                throw new InvalidOperationException("The real repair Enter/Wait task was refused: "+serviceReason);
            journey.TaskId=serviceTask.Id;
            var enter=world.ReadNpcAction(serviceTask.Id+":step:0");
            Vector3 previous=actor.GroundPosition;float walked=0,maxStep=0;
            deadline=Time.realtimeSinceStartup+180;
            while(Time.realtimeSinceStartup<deadline)
            {
                actor=streets.FindActor(journey.PersonId);
                if(actor && actor.gameObject.activeInHierarchy)
                {
                    var ground=actor.GroundPosition;float step=Vector3.Distance(previous,ground);
                    walked+=step;maxStep=Math.Max(maxStep,step);previous=ground;
                    // Only the observer follows. The existing accepted Enter
                    // adapter owns every NPC coordinate and the native doorway.
                    game.PlayerMotor.transform.position=ground+Vector3.right*3+Vector3.up;
                }
                var job=world.ReadNpcAction(enter.Id);
                if(job==null || NpcActionExecutor.IsTerminal(job))break;
                yield return null;
            }
            RequireDoorReceipt(world,enter.Id,selectedShop.Door.buildingKey);
            if(walked<1)throw new InvalidOperationException("The accepted repair errand did not show an actual exterior walk to its shop.");
            var actualDoorArguments=new object[]{selectedShop.Door.buildingKey,enter.Target.DoorId,default(StaticDoor)};
            if(!(bool)typeof(StreetPopulation).GetMethod("ReadBuildingActionDoor",Private).Invoke(streets,actualDoorArguments))
                throw new InvalidOperationException("The accepted repair entrance lost its exact native doorway.");
            TownSmokeHarness.EnterInterior(selectedShop.Owner.transform,(StaticDoor)actualDoorArguments[2]);
            deadline=Time.realtimeSinceStartup+30;
            NpcActionJob waiting=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                waiting=world.ActiveNpcAction(journey.PersonId);actor=streets.FindActor(journey.PersonId);
                if(game.PlayerEnterExit.Interior && game.PlayerEnterExit.Interior.EntryDoor.buildingKey==selectedShop.Door.buildingKey &&
                    streets.Ready && actor && actor.gameObject.activeInHierarchy && waiting!=null && waiting.Kind==NpcActionKind.Wait &&
                    waiting.Phase==NpcActionPhase.Executing)break;
                yield return null;
            }
            if(!game.PlayerEnterExit.Interior || game.PlayerEnterExit.Interior.EntryDoor.buildingKey!=selectedShop.Door.buildingKey)
                throw new InvalidOperationException("The repair observer did not enter the adult's actual accepted destination.");
            if(waiting==null || waiting.Kind!=NpcActionKind.Wait || waiting.Phase!=NpcActionPhase.Executing)
                throw new InvalidOperationException("The exact accepted Enter did not hand the real adult to its bound shop Wait: "+TerminalReason(waiting));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_REPAIR_JOURNEY_PASS: resident="+journey.PersonId+"; home="+selectedHome.Door.buildingKey+
                "; building="+selectedShop.Door.buildingKey+"; walked="+walked+"; maxStep="+maxStep+
                "; nativeExit=true; nativeEnter=true; actualDoorReceipts=true; npcMovedByProbe=false; noDutyOverride=true");
            game.SaveLoadManager.EnumerateSaves();
            if(game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name,ShopReadyCheckpoint)<0)
                yield return ConversationMoodProbe.SaveCheckpoint(game,game.PlayerEntity.Name,ShopReadyCheckpoint);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_REPAIR_SHOP_CHECKPOINT: title="+ShopReadyCheckpoint+"; resident="+journey.PersonId+
                "; task="+journey.TaskId+"; actualBoundWait=true; futureDropoffCanResume=true");
        }

        private static NpcActionJob AcceptDoor(StreetPopulation streets,WorldStateStore world,ResidentActor actor,NpcActionKind kind,int building)
        {
            var option=FindDoorOption(streets,actor,kind,building);
            if(option==null)throw new InvalidOperationException("The actual repair doorway has no normal "+kind+" option: "+RepairDoorReadiness(streets,actor,building));
            string reason;
            if(!NpcProbeCommandSource.TryAcceptAction(streets,actor.Resident.Id,"native-repair-door:"+Guid.NewGuid().ToString("N"),
                new NpcActionDecision {Option=Copy(option),DurationSeconds=900},out reason))
                throw new InvalidOperationException("The real repair adult refused its normal "+kind+" quote: "+reason);
            return world.ActiveNpcAction(actor.Resident.Id);
        }

        private static NpcActionOption FindDoorOption(StreetPopulation streets,ResidentActor actor,NpcActionKind kind,int building)
        {
            var options=streets.ConversationActionCandidates(actor.Resident.Id);
            if(kind==NpcActionKind.Enter)
            {
                options=new List<NpcActionOption>();
                typeof(StreetPopulation).GetMethod("OfferBuildingEntry",Private).Invoke(streets,new object[]{actor,options,building,
                    GameManager.Instance.TalkManager.GetBuildingNameForBuildingKey(building),null,true});
            }
            foreach(var option in options)
                if(option!=null && option.Kind==kind && option.Target!=null && option.Target.BuildingKey==building)
                    return option;
            return null;
        }

        private static string RepairDoorReadiness(StreetPopulation streets,ResidentActor actor,int building)
        {
            var geometry=typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets) as TownGeometry;
            var currentInterior=typeof(StreetPopulation).GetField("interior",Private).GetValue(streets) as DaggerfallInterior;
            var arguments=new object[]{building,null,default(StaticDoor)};
            bool actualDoor=(bool)typeof(StreetPopulation).GetMethod("ReadBuildingActionDoor",Private).Invoke(streets,arguments);
            bool unlocked=(bool)typeof(StreetPopulation).GetMethod("CanEnterBuilding",Private).Invoke(streets,new object[]{actor,building});
            return "resident="+actor.Resident.Id+"; building="+building+"; ready="+streets.Ready+"; entranceSurvey="+streets.EntranceSurveyReady+
                "; nativePlayerInside="+GameManager.Instance.PlayerEnterExit.IsPlayerInside+"; streetsInterior="+(currentInterior?currentInterior.EntryDoor.buildingKey:-1)+
                "; knownGeometryDoor="+(geometry!=null && geometry.Entrances.ContainsKey(building))+"; exactNativeDoor="+actualDoor+
                "; unlockedPublicBuilding="+unlocked+"; nativeMap="+GameManager.Instance.PlayerGPS.CurrentMapID;
        }

        private static void RequireDoorReceipt(WorldStateStore world,string actionId,int building)
        {
            var job=world.ReadNpcAction(actionId);
            if(job==null || job.Phase!=NpcActionPhase.Completed || job.BuildingTransitionReceipt==null ||
                job.BuildingTransitionReceipt.BuildingKey!=building)
                throw new InvalidOperationException("The normal repair doorway action failed: "+TerminalReason(job));
        }

        private static Fixture FindFixture(StreetPopulation streets, WorldStateStore store,string requiredPerson)
        {
            foreach (ResidentActor actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if (!actor || !actor.gameObject.activeInHierarchy || actor.Resident == null || !actor.Resident.IsAlive || actor.IsSleeping ||
                    actor.IsConversing || actor.Resident.IsQuestProtected || requiredPerson!=null && actor.Resident.Id!=requiredPerson || streets.IsAlarmed(actor.Resident.Id) ||
                    Vector3.Distance(actor.transform.position, GameManager.Instance.PlayerMotor.transform.position) > 8f) continue;
                // A keeper currently working here has stock and money, but the
                // existing admission correctly refuses abandoning fixed work.
                // Genuine free-home time can use the same voluntary repair
                // adapters; the actual admission still checks every duty.
                if(!RoutineAllowsRepair(streets,actor.Resident.Id))continue;
                var owner = actor.GetComponent<NativeNpcInventoryOwner>();
                string ignored;
                if (!owner || owner.Binding == null || !NativeNpcInventoryBridge.TryCapture(store, owner.Binding, out ignored)) continue;
                var inventory = store.ReadNpcInventory(actor.Resident.Id);
                if (inventory == null) continue;
                for (int index = 0; index < owner.Binding.Items.Count; index++)
                {
                    DaggerfallUnityItem item = owner.Binding.Items.GetItem(index);
                    if (item == null || item.UID == 0 || item.IsQuestItem || item.IsSummoned || item.ItemTemplate.isNotRepairable ||
                        item.RepairData.IsBeingRepaired() || item.maxCondition < 2 || item.stackCount != 1 ||
                        item.IsEnchanted && !DaggerfallUnity.Settings.AllowMagicRepairs) continue;
                    int condition = Math.Max(1, item.maxCondition - 1);
                    int price = FormulaHelper.CalculateItemRepairCost(item.value, GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.quality,
                        condition, item.maxCondition, null);
                    int duration = FormulaHelper.CalculateItemRepairTime(condition, item.maxCondition);
                    if (price > 0 && duration >= DaggerfallDateTime.SecondsPerDay && price <= inventory.WalletGold)
                        return new Fixture { Actor = actor, Owner = owner, Item = item, DamagedCondition = condition, Price = price, Duration = duration };
                }
            }
            return null;
        }

        private static bool RoutineAllowsRepair(StreetPopulation streets,string personId)
        {
            var find=typeof(StreetPopulation).GetMethod("FindNpcActionPlan",Private);
            var scheduled=typeof(StreetPopulation).GetMethod("Scheduled",Private);
            var plan=find==null?null:find.Invoke(streets,new object[]{personId});
            if(plan==null || scheduled==null)return false;
            double now=Now();
            var state=(ActivitySnapshot)scheduled.Invoke(streets,new[]{plan,(object)now});
            var future=(ActivitySnapshot)scheduled.Invoke(streets,new[]{plan,(object)(now+300)});
            return (state.Activity==Activity.Home || state.Activity==Activity.Visit || state.Activity==Activity.Travel) &&
                future.Activity!=Activity.Work && future.Activity!=Activity.Sleep;
        }

        private static string RepairInventoryState(StreetPopulation streets,WorldStateStore store,string person)
        {
            var actor=person==null?null:streets.FindActor(person);
            var owner=actor?actor.GetComponent<NativeNpcInventoryOwner>():null;
            var inventory=person==null?null:store.ReadNpcInventory(person);
            string text="resident="+person+"; actor="+(bool)actor+"; active="+(actor && actor.gameObject.activeInHierarchy)+
                "; sleeping="+(actor && actor.IsSleeping)+"; conversing="+(actor && actor.IsConversing)+
                "; binding="+(owner && owner.Binding!=null)+"; wallet="+(inventory==null?-1:inventory.WalletGold)+
                "; routineEligible="+(actor && RoutineAllowsRepair(streets,person));
            if(owner && owner.Binding!=null)for(int i=0;i<Math.Min(8,owner.Binding.Items.Count);i++)
            {
                var item=owner.Binding.Items.GetItem(i);if(item==null)continue;
                text+=" | uid="+item.UID+"; item="+item.LongName+"; value="+item.value+"; maxCondition="+item.maxCondition+
                    "; equipped="+item.IsEquipped+"; notRepairable="+item.ItemTemplate.isNotRepairable+"; quantity="+item.stackCount;
            }
            return text;
        }

        private static NpcActionOption FindRepairOption(StreetPopulation streets, string residentId, string request, ulong itemUid, bool collection)
        {
            foreach (NpcActionOption option in streets.ConversationActionOptions(residentId, request))
                if (option != null && option.Kind == NpcActionKind.Purchase && option.Target != null &&
                    option.Target.ContainerId == NpcShopCommerce.RepairTargetContainerId && option.Target.ItemId == itemUid.ToString(CultureInfo.InvariantCulture) &&
                    (collection ? option.Target.QuotedGold == 0 : option.Target.QuotedGold > 0)) return option;
            return null;
        }

        private static IEnumerator WaitForTerminal(WorldStateStore store, string actionId, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                NpcActionJob job = store.ReadNpcAction(actionId);
                if (job == null || NpcActionExecutor.IsTerminal(job)) yield break;
                yield return null;
            }
        }

        private static NpcActionOption Copy(NpcActionOption option)
        { return new NpcActionOption { Id = option.Id, Kind = option.Kind, Label = option.Label, ItemSourceStep = option.ItemSourceStep,
            MaximumQuantity = option.MaximumQuantity, Target = NpcActionExecutor.CopyTarget(option.Target) }; }
        private static WorldStateStore Store(PeopleOfTheIliacBayMod host)
        { var field = typeof(PeopleOfTheIliacBayMod).GetField("store", Private); return field == null ? null : field.GetValue(host) as WorldStateStore; }
        private static double Now() { return DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(); }
        private static string TerminalReason(NpcActionJob job) { return job == null ? "missing" : job.Phase + " " + job.FailureReason; }
    }
}
