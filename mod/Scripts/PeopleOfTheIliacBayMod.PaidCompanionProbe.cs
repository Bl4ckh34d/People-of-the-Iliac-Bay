using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        // Discovery reads actual generated visitors and their visible actors.
        // It never changes a civilian's profile to manufacture a hire candidate.
        public string FindPaidCompanionProbeCandidate()
        {
            if(streets==null || !streets.Ready || store==null)return null;
            foreach(var traveler in store.Snapshot().Travelers)
            {
                if(!traveler.IsAlive || !TravelerAdventurerPolicy.IsAdventurer(
                    TravelerAdventurerPolicy.Archetype(traveler.AppearanceSeed)))continue;
                var actor=streets.FindActor(traveler.Id);
                if(!actor || !actor.gameObject.activeInHierarchy)continue;
                foreach(var option in streets.ConversationActionCandidates(traveler.Id))
                {
                    if(!option.PaidCompanion)continue;
                    int fee;string reason;
                    var decision=new NpcActionDecision {Option=option,DurationSeconds=NpcPaidCompanionAction.DurationSeconds};
                    if(streets.TryQuotePaidCompanion(traveler.Id,decision,out fee,out reason))return traveler.Id;
                }
            }
            return null;
        }

        // Explicit disposable-player probe. Run through an external native harness;
        // this code never starts the game, chooses Yes, changes time, or writes a save.
        public IEnumerator ProbePaidCompanionConsentAndDismiss(string residentId,string expectedCharacter)
        {
            var game = GameManager.Instance;
            PaidCompanionProbeRequire(Array.IndexOf(Environment.GetCommandLineArgs(),
                "-peopleoftheiliacbay-paid-companion-probe") >= 0 && game != null &&
                game.PlayerEntity != null && !string.IsNullOrEmpty(expectedCharacter) &&
                expectedCharacter.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal) &&
                game.PlayerEntity.Name == expectedCharacter && streets != null && streets.Ready && store != null,
                "Paid companion probe requires an explicitly owned disposable native fixture.");
            if(string.IsNullOrEmpty(residentId))residentId=FindPaidCompanionProbeCandidate();
            var traveler=store.Traveler(residentId);
            PaidCompanionProbeRequire(traveler!=null && traveler.IsAlive &&
                TravelerAdventurerPolicy.IsAdventurer(TravelerAdventurerPolicy.Archetype(traveler.AppearanceSeed)) &&
                store.ResolveNpcArchetype(residentId)==TravelerAdventurerPolicy.Archetype(traveler.AppearanceSeed),
                "Paid companion probe requires an actual generated adventurer traveler; no visible eligible traveler is available.");
            var nativeActor=streets.FindActor(residentId);
            var nativeBody=nativeActor?nativeActor.GetComponent<DaggerfallEntityBehaviour>():null;
            PaidCompanionProbeRequire(nativeBody && nativeBody.Entity!=null,"Generated adventurer has no native body.");
            var profile=store.ReadNpcActor(residentId,store.ResolveNpcArchetype(residentId));
            for(int stat=0;stat<8;stat++)PaidCompanionProbeRequire(
                nativeBody.Entity.Stats.GetPermanentStatValue(stat)==profile.Profile.Attribute(stat),
                "Generated adventurer native attributes differ from the saved seeded profile.");
            for(int skill=0;skill<35;skill++)PaidCompanionProbeRequire(
                nativeBody.Entity.Skills.GetPermanentSkillValue(skill)==profile.Skill(skill),
                "Generated adventurer native skills differ from the saved seeded profile.");
            if(profile.Profile.Archetype==NpcArchetype.Warrior || profile.Profile.Archetype==NpcArchetype.Thief)
            {
                var weapon=nativeBody.Entity.ItemEquipTable.GetItem(EquipSlots.RightHand);
                PaidCompanionProbeRequire(weapon!=null && weapon.ItemGroup==ItemGroups.Weapons && weapon.currentCondition>0 &&
                    ReferenceEquals(nativeBody.Entity.Items.GetItem(weapon.UID),weapon),
                    "Generated melee adventurer has no actual owned equipped native weapon.");
                var owner=nativeActor.GetComponent<NativeNpcInventoryOwner>();
                PaidCompanionProbeRequire(owner!=null,"Generated adventurer has no saved inventory owner.");
                owner.Capture();
            }
            NpcActionDecision decision = null;
            foreach (var option in streets.ConversationActionCandidates(residentId))
                if (option.PaidCompanion)
                { decision = new NpcActionDecision { Option = option,DurationSeconds = NpcPaidCompanionAction.DurationSeconds }; break; }
            PaidCompanionProbeRequire(decision != null,"No eligible paid companion candidate is available.");
            int fee; string reason;
            PaidCompanionProbeRequire(streets.TryQuotePaidCompanion(residentId,decision,out fee,out reason),reason);
            var player = game.PlayerEntity;
            int goldBefore = player.GetGoldAmount();
            int walletBefore = store.ReadNpcInventory(residentId).WalletGold;
            long ledgerBefore = store.ReadNpcServiceContracts().Revision;
            PaidCompanionProbeRequire(!NpcProbeCommandSource.TryAcceptAction(streets,residentId,
                "paid-probe-bypass:" + Guid.NewGuid().ToString("N"),decision,out reason) &&
                player.GetGoldAmount() == goldBefore && store.ReadNpcServiceContracts().Revision == ledgerBefore,
                "Ordinary action acceptance bypassed paid consent or moved gold.");
            bool answered = false,accepted = false;
            var confirm = new PersonalCommerceConfirmBox(
                "Native fixture: hire for 120 gold / 24 game hours, then dismiss immediately?",
                choice => { accepted = choice; answered = true; });
            DaggerfallUI.UIManager.PushWindow(confirm);
            while (!answered)
            {
                PaidCompanionProbeRequire(player.GetGoldAmount() == goldBefore &&
                    store.ReadNpcServiceContracts().Revision == ledgerBefore,
                    "Gold or service ledger changed before the native consent answer.");
                yield return null;
            }
            if (!accepted)
            {
                PaidCompanionProbeRequire(player.GetGoldAmount() == goldBefore &&
                    store.ReadNpcServiceContracts().Revision == ledgerBefore,
                    "Declined native consent changed gold or service contracts.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PAID_COMPANION_PROBE_DECLINED: no debit, no contract");
                yield break;
            }
            string sessionId = "paid-probe:" + Guid.NewGuid().ToString("N");
            NpcConversationCommandSession session;
            PaidCompanionProbeRequire(store.TryBeginNpcConversationCommandSession(residentId,sessionId,out session),
                "Native paid fixture could not open its player command session.");
            try
            {
                var binding = new ConversationContextBinding(ConversationActorCategory.Mobile,residentId,null,
                    sessionId,1,1,store.NpcFrameworkScopeId,"","native-paid-companion-probe",1);
                NpcConversationCommandTurn turn;
                PaidCompanionProbeRequire(store.TryCaptureNpcConversationCommandTurn(session,binding,
                    "Hire you for 120 gold for one day.",out turn),"Native paid fixture command was not captured.");
                PaidCompanionProbeRequire(streets.TryHirePaidCompanion(residentId,
                    "paid-probe:" + Guid.NewGuid().ToString("N"),decision,turn,out reason),reason);
                PaidCompanionProbeRequire(streets.HasBoundTravelerActionPlan(residentId),
                    "The actual generated traveler is not bound to its managed runtime action plan.");
                var job = store.ActiveNpcAction(residentId);
                PaidCompanionProbeRequire(NpcPaidCompanionAction.IsPaidFollowJob(job) &&
                    player.GetGoldAmount() == goldBefore - fee,"Native hire did not debit exactly the quoted daily gold.");
                string contractId = NpcPaidCompanionAction.ContractId(job);
                double paidUntil=job.FinishAt;
                int healthBefore=nativeBody.Entity.CurrentHealth;
                int walletAfterHire=store.ReadNpcInventory(residentId).WalletGold;
                string itemsBefore=NativeItems(nativeBody.Entity);
                yield return VerifyPaidCompanionPublicDoorAndExpiry(residentId,nativeActor,nativeBody,contractId,
                    paidUntil,healthBefore,walletAfterHire,itemsBefore);
                job=store.ReadNpcAction(job.Id);
                PaidCompanionProbeRequire(NpcActionExecutor.IsTerminal(job),"Paid Follow did not terminate at paidUntil.");
                PaidCompanionProbeRequire(streets.TrySettlePaidCompanionAction(job,NpcServiceEndReason.NaturalExpiry,out reason),reason);
                var contract = store.ReadNpcServiceContracts().Read(contractId);
                PaidCompanionProbeRequire(contract != null && contract.Status == NpcServiceContractStatus.Terminated &&
                    contract.EndReason == NpcServiceEndReason.NaturalExpiry && contract.FundedGold == fee &&
                    player.GetGoldAmount() == goldBefore - fee &&
                    store.ReadNpcInventory(residentId).WalletGold == walletBefore + contract.ReleasedGold,
                    "Native expiry did not settle exact NPC earnings.");
                int afterSettlement = player.GetGoldAmount();
                job = store.ReadNpcAction(job.Id);
                PaidCompanionProbeRequire(streets.TrySettlePaidCompanionAction(job,
                    NpcServiceEndReason.NaturalExpiry,out reason) && player.GetGoldAmount() == afterSettlement,
                    "Repeated paid expiry settlement moved native gold again.");
                var decoded = SaveLoadManager.Deserialize(typeof(WorldState),
                    SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot()),true) as WorldState;
                var restored = new WorldStateStore(1); restored.Restore(decoded);
                PaidCompanionProbeRequire(NpcPaidCompanionAction.IsPaidFollowJob(restored.ReadNpcAction(job.Id)) &&
                    restored.ReadNpcServiceContracts().Read(contractId).Status == NpcServiceContractStatus.Terminated &&
                    restored.ReadNpcServiceContracts().Read(contractId).EndReason == NpcServiceEndReason.NaturalExpiry,
                    "Native serialization lost paid action binding or terminal expiry settlement.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PAID_COMPANION_PROBE_PASS: debit=" + fee +
                    "; refund=" + contract.RefundedGold + "; earnings=" + contract.ReleasedGold +
                    "; native consent, public door, expiry, replay and restore verified");
            }
            finally { store.TryCloseNpcConversationCommandSession(session); }
        }

        private static void PaidCompanionProbeRequire(bool condition,string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private IEnumerator VerifyPaidCompanionPublicDoorAndExpiry(string residentId,ResidentActor source,
            DaggerfallEntityBehaviour sourceBody,string contractId,double paidUntil,int healthBefore,
            int walletBefore,string itemsBefore)
        {
            var game=GameManager.Instance;
            PaidCompanionProbeRequire(!game.PlayerEnterExit.IsPlayerInsideBuilding,
                "Paid companion public-door probe must start outside.");
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            DaggerfallStaticDoors owner=null;StaticDoor entry=default(StaticDoor);
            foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                foreach(var door in candidate.Doors)
                {
                    BuildingSummary summary;
                    if(door.doorType!=DoorTypes.Building || !directory.GetBuildingSummary(door.buildingKey,out summary) ||
                        summary.BuildingType!=DFLocation.BuildingTypes.GeneralStore || !game.PlayerActivate.BuildingIsUnlocked(summary))continue;
                    owner=candidate;entry=door;break;
                }
                if(owner)break;
            }
            PaidCompanionProbeRequire(owner,"Paid companion probe requires an unlocked actual GeneralStore door.");
            // Put only disposable fixture player at the surveyed, real door, then
            // wait for the hired body to reach native adapter range by Follow.
            game.PlayerMotor.transform.position=StreetPopulation.FollowExteriorDoorGround(entry)+Vector3.up*1.8f;
            float approachDeadline=Time.realtimeSinceStartup+45f;
            while(Vector3.Distance(source.transform.position-Vector3.up,
                StreetPopulation.FollowExteriorDoorGround(entry))>28f && Time.realtimeSinceStartup<approachDeadline)yield return null;
            PaidCompanionProbeRequire(Vector3.Distance(source.transform.position-Vector3.up,
                StreetPopulation.FollowExteriorDoorGround(entry))<=28f,"Paid companion did not physically approach real public door.");
            // Native activation, not a fabricated scene or transition callback.
            TownSmokeHarness.EnterInterior(owner.transform,entry);
            float deadline=Time.realtimeSinceStartup+45f;
            while(Time.realtimeSinceStartup<deadline)
            {
                var job=store.ActiveNpcAction(residentId);var actor=streets.FindActor(residentId);
                if(game.PlayerEnterExit.IsPlayerInsideBuilding && job!=null && job.FollowTransition==null && actor)break;
                AssertActivePaidFollow(residentId,contractId,paidUntil);
                yield return null;
            }
            var destination=streets.FindActor(residentId);
            PaidCompanionProbeRequire(game.PlayerEnterExit.IsPlayerInsideBuilding &&
                game.PlayerEnterExit.BuildingType==DFLocation.BuildingTypes.GeneralStore && ReferenceEquals(destination,source) &&
                ReferenceEquals(destination.GetComponent<DaggerfallEntityBehaviour>(),sourceBody),
                "Native public-door transfer did not retain same paid companion body.");
            PaidCompanionProbeRequire(sourceBody.Entity.CurrentHealth==healthBefore && NativeItems(sourceBody.Entity)==itemsBefore &&
                store.ReadNpcInventory(residentId).WalletGold==walletBefore,
                "Native public-door transfer changed paid companion vitals, items, or wallet.");
            AssertActivePaidFollow(residentId,contractId,paidUntil);
            int bodies=0;foreach(var actor in Resources.FindObjectsOfTypeAll<ResidentActor>())
                if(actor && actor.gameObject.activeInHierarchy && actor.Resident!=null && actor.Resident.Id==residentId)bodies++;
            PaidCompanionProbeRequire(bodies==1,"Native public-door transfer duplicated paid companion body.");
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)Math.Max(1d,paidUntil-DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()+1d));
            deadline=Time.realtimeSinceStartup+15f;
            while(Time.realtimeSinceStartup<deadline && !NpcActionExecutor.IsTerminal(store.ActiveNpcAction(residentId)))yield return null;
            PaidCompanionProbeRequire(NpcActionExecutor.IsTerminal(store.ActiveNpcAction(residentId)) && source.gameObject.activeInHierarchy,
                "Paid expiry did not stop Follow while retaining visible native body.");
        }

        private void AssertActivePaidFollow(string residentId,string contractId,double paidUntil)
        {
            NpcServiceContract contract;var job=store.ActiveNpcAction(residentId);
            PaidCompanionProbeRequire(NpcPaidCompanionTravel.TryReadActiveTravel(store,job,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),out contract) && contract.ContractId==contractId && job.FinishAt==paidUntil,
                "Public-door transfer changed active paid Follow or paidUntil.");
        }

        private static string NativeItems(DaggerfallEntity entity)
        {
            var rows=new List<string>();for(int i=0;i<entity.Items.Count;i++)
            {var item=entity.Items.GetItem(i);rows.Add(item.UID+":"+item.TemplateIndex+":"+item.stackCount+":"+item.currentCondition);}
            rows.Sort(StringComparer.Ordinal);return string.Join("|",rows.ToArray());
        }
    }
}
