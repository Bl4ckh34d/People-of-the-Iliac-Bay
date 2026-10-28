using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
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
                NpcCompanionCastSpec naturalSpell;
                if(Environment.GetEnvironmentVariable("POTIB_PAID_COMPANION_XP")=="1" &&
                    !TryFindNaturalPaidHeal(actor,out naturalSpell))continue;
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
        // This code never starts the game, chooses Yes, or changes time. The explicit
        // checkpoint mode saves only after actual native Yes and production hiring.
        public IEnumerator ProbePaidCompanionConsentAndDismiss(string residentId,string expectedCharacter)
        {
            var game = GameManager.Instance;
            PaidCompanionProbeRequire(Array.IndexOf(Environment.GetCommandLineArgs(),
                "-peopleoftheiliacbay-paid-companion-probe") >= 0 && game != null &&
                game.PlayerEntity != null && !string.IsNullOrEmpty(expectedCharacter) &&
                NativeProbeText.IsTestCharacter(expectedCharacter) &&
                game.PlayerEntity.Name == expectedCharacter && streets != null && streets.Ready && store != null,
                "Paid companion probe requires an explicitly owned disposable native fixture.");
            bool preview=Environment.GetEnvironmentVariable("POTIB_PAID_COMPANION_PREVIEW")=="1";
            bool checkpoint=Environment.GetEnvironmentVariable("POTIB_PAID_COMPANION_CHECKPOINT")=="1";
            PaidCompanionProbeRequire(!(preview && checkpoint),"Choose offer preview or controlled hire checkpoint separately.");
            if(preview || checkpoint)
            {
                yield return GoalSequenceProbe.EnterActualSequenceCompanion(this,streets);
                residentId=GoalSequenceProbe.SequenceCompanion;
            }
            if(preview)
            {yield return PreviewActualPaidOffer(residentId);yield break;}
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
            PaidCompanionProbeRequire(fee==nativeBody.Entity.Level*10,"Native quote does not match the companion's actual level.");
            var player = game.PlayerEntity;
            int goldBefore = player.GetGoldAmount();
            int walletBefore = store.ReadNpcInventory(residentId).WalletGold;
            long ledgerBefore = store.ReadNpcServiceContracts().Revision;
            PaidCompanionProbeRequire(!NpcProbeCommandSource.TryAcceptAction(streets,residentId,
                "paid-probe-bypass:" + Guid.NewGuid().ToString("N"),decision,out reason) &&
                player.GetGoldAmount() == goldBefore && store.ReadNpcServiceContracts().Revision == ledgerBefore,
                "Ordinary action acceptance bypassed paid consent or moved gold.");
            bool answered = false,accepted = false;
            var confirm = CreatePaidCompanionOffer(traveler.Name,fee,
                choice => { accepted = choice; answered = true; });
            DaggerfallUI.UIManager.PushWindow(confirm);
            if(checkpoint)
            {
                GoalSequenceProbe.VerifyNativeHirePopup();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PAID_COMPANION_SETUP_CONFIRM: person="+residentId+
                    "; quote="+fee+"; actualNativeYesRequired=true; controlledSetup=true; dialogueModel=false");
            }
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
            Vector3 observerBefore=game.PlayerMotor.transform.position;
            bool observerStaged=false;
            try
            {
                var binding = new ConversationContextBinding(ConversationActorCategory.Mobile,residentId,null,
                    sessionId,1,1,store.NpcFrameworkScopeId,"","native-paid-companion-probe",1);
                NpcConversationCommandTurn turn;
                PaidCompanionProbeRequire(store.TryCaptureNpcConversationCommandTurn(session,binding,
                    "Hire you for "+fee+" gold for one day.",out turn),"Native paid fixture command was not captured.");
                int wrongFee=fee>10?fee-10:fee+10;
                PaidCompanionProbeRequire(!streets.TryHirePaidCompanion(residentId,
                    "paid-probe-stale:"+Guid.NewGuid().ToString("N"),decision,turn,wrongFee,out reason) &&
                    player.GetGoldAmount()==goldBefore && store.ReadNpcServiceContracts().Revision==ledgerBefore,
                    "Changed wage bypassed consent or moved gold.");
                if(Environment.GetEnvironmentVariable("POTIB_PAID_COMPANION_XP")=="1")
                {
                    StagePaidXpObserver(nativeActor);
                    observerStaged=true;
                }
                PaidCompanionProbeRequire(streets.TryHirePaidCompanion(residentId,
                    "paid-probe:" + Guid.NewGuid().ToString("N"),decision,turn,fee,out reason),reason);
                PaidCompanionProbeRequire(streets.HasBoundTravelerActionPlan(residentId),
                    "The actual generated traveler is not bound to its managed runtime action plan.");
                var job = store.ActiveNpcAction(residentId);
                PaidCompanionProbeRequire(NpcPaidCompanionAction.IsPaidFollowJob(job) &&
                    player.GetGoldAmount() == goldBefore - fee,"Native hire did not debit exactly the quoted daily gold.");
                string contractId = NpcPaidCompanionAction.ContractId(job);
                if(checkpoint)
                {
                    yield return GoalSequenceProbe.SaveActualHiredCheckpoint(game,job,false);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PAID_COMPANION_SETUP_NATIVE_PASS: person="+residentId+"; follow="+job.Id+
                        "; contract="+contractId+"; nativeDebit="+fee+"; nativeYes=true; controlledSetup=true; dialogueModel=false; actualNativeSave=true");
                    yield break;
                }
                if(Environment.GetEnvironmentVariable("POTIB_PAID_COMPANION_XP")=="1")
                {
                    VerifyOnePaidNativeSkillUse(nativeActor,nativeBody,job,contractId,fee);
                    yield break;
                }
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
            finally
            {
                store.TryCloseNpcConversationCommandSession(session);
                if(observerStaged)PlacePaidXpObserver(observerBefore);
            }
        }

        private IEnumerator PreviewActualPaidOffer(string residentId)
        {
            var player=GameManager.Instance.PlayerEntity;
            var traveler=store.Traveler(residentId);
            var actor=streets.FindActor(residentId);
            PaidCompanionProbeRequire(traveler!=null && actor && actor.gameObject.activeInHierarchy &&
                store.ActiveNpcAction(residentId)==null && store.ActiveNpcTask(residentId)==null,
                "Offer preview requires the actual available generated traveler.");
            NpcActionDecision decision=null;
            foreach(var option in streets.ConversationActionCandidates(residentId))
                if(option.PaidCompanion)
                {decision=new NpcActionDecision {Option=option,DurationSeconds=NpcPaidCompanionAction.DurationSeconds};break;}
            int fee=0;string reason="No actual paid companion offer is available.";
            PaidCompanionProbeRequire(decision!=null && streets.TryQuotePaidCompanion(residentId,decision,out fee,out reason),reason);
            int goldBefore=player.GetGoldAmount(),walletBefore=store.ReadNpcInventory(residentId).WalletGold;
            long ledgerBefore=store.ReadNpcServiceContracts().Revision;
            bool answered=false,accepted=false;
            var confirm=CreatePaidCompanionOffer(traveler.Name,fee,choice=>{accepted=choice;answered=true;});
            DaggerfallUI.UIManager.PushWindow(confirm);
            GoalSequenceProbe.VerifyNativeHirePopup();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PAID_COMPANION_OFFER_PREVIEW_READY: person="+residentId+
                "; name="+traveler.Name+"; quote="+fee+"; chooseNo=true; dialogueModel=false; previewOnly=true");
            while(!answered)
            {
                PaidCompanionProbeRequire(player.GetGoldAmount()==goldBefore &&
                    store.ReadNpcInventory(residentId).WalletGold==walletBefore &&
                    store.ReadNpcServiceContracts().Revision==ledgerBefore,
                    "Offer preview changed native gold or the service ledger before its answer.");
                yield return null;
            }
            PaidCompanionProbeRequire(!accepted,"Offer preview requires the human native No answer; no hire was attempted.");
            PaidCompanionProbeRequire(store.ActiveNpcAction(residentId)==null && store.ActiveNpcTask(residentId)==null &&
                player.GetGoldAmount()==goldBefore && store.ReadNpcInventory(residentId).WalletGold==walletBefore &&
                store.ReadNpcServiceContracts().Revision==ledgerBefore,
                "Declined offer preview changed a job, native gold or the service ledger.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PAID_COMPANION_OFFER_PREVIEW_PASS: sameProductionOffer=true; nativeNo=true; "+
                "noJob=true; noDebit=true; ledgerUnchanged=true; dialogueModel=false");
        }

        // This one-cast fixture needs local consent range, not an unrelated inn-wide
        // route to the entrance. Keep the real caster fixed; survey only a short,
        // supported native floor segment and move the disposable observer there.
        private static void StagePaidXpObserver(ResidentActor actor)
        {
            var game=GameManager.Instance;
            var room=game.PlayerEnterExit.Interior;
            PaidCompanionProbeRequire(game.PlayerEnterExit.IsPlayerInsideBuilding && room && actor,
                "Paid XP observer staging requires the actual caster's loaded native interior.");
            Vector3 from=actor.transform.position-Vector3.up;
            var geometry=new InteriorGeometry(room,0.4f,1.65f,from,sampleFloorOnly:true);
            PaidCompanionProbeRequire(geometry.EntryGround.HasValue,
                "Actual paid caster has no safe measured local floor.");
            foreach(var direction in new[]{Vector2.right,Vector2.left,Vector2.up,Vector2.down,
                new Vector2(0.7071f,0.7071f),new Vector2(-0.7071f,0.7071f),
                new Vector2(0.7071f,-0.7071f),new Vector2(-0.7071f,-0.7071f)})
            {
                Vector3 point=from+new Vector3(direction.x,0,direction.y)*1.3f;
                var route=geometry.RouteBetween(room.transform.InverseTransformPoint(from),
                    room.transform.InverseTransformPoint(point));
                if(route==null || route.Count!=2)continue;
                bool occupied=false;
                foreach(var collider in Physics.OverlapCapsule(point+Vector3.up*0.8f,
                    point+Vector3.up*1.65f,0.4f,~0,QueryTriggerInteraction.Ignore))
                    if(collider && !collider.GetComponentInParent<PlayerMotor>()) {occupied=true;break;}
                if(occupied)continue;
                PlacePaidXpObserver(point+Vector3.up);
                PaidCompanionProbeRequire(Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)<=1.8f,
                    "Measured paid XP observer point exceeds actual local Follow arrival range.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PAID_XP_LOCAL_OBSERVER: resident="+actor.Resident.Id+
                    "; distance="+Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)+
                    "; nativeSupportedShortPath=true; casterUnmoved=true; nativeBookAndResourcesUnchanged=true");
                return;
            }
            throw new InvalidOperationException("Natural paid caster has no clear supported local observer position; no hire was attempted.");
        }

        private static void PlacePaidXpObserver(Vector3 position)
        {
            var motor=GameManager.Instance.PlayerMotor;
            var controller=motor.GetComponent<CharacterController>();
            bool motorEnabled=motor.enabled,controllerEnabled=controller && controller.enabled;
            motor.enabled=false;if(controller)controller.enabled=false;
            motor.transform.position=position;Physics.SyncTransforms();
            if(controller)controller.enabled=controllerEnabled;motor.enabled=motorEnabled;
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

        private bool TryFindNaturalPaidHeal(ResidentActor actor,out NpcCompanionCastSpec selected)
        {
            selected=null;
            var body=actor?actor.GetComponent<DaggerfallEntityBehaviour>():null;
            if(!body || body.Entity==null || body.Entity.CurrentHealth!=body.Entity.MaxHealth || body.Entity.MaxHealth<=1)return false;
            NpcCompanionCastSpec[] known,candidates;
            if(!NativeNpcCompanionCast.TryDiscoverKnownAffordable(actor.gameObject,store,out known,out candidates))return false;
            foreach(var spell in known)
                if(spell.Delivery==NpcCompanionCastDelivery.Self && spell.EffectId==HealHealth.EffectKey)
                {selected=spell;return true;}
            return false;
        }

        private void VerifyOnePaidNativeSkillUse(ResidentActor actor,DaggerfallEntityBehaviour body,
            NpcActionJob hired,string contractId,int fee)
        {
            string id=actor.Resident.Id;
            NpcCompanionCastSpec spell;
            PaidCompanionProbeRequire(TryFindNaturalPaidHeal(actor,out spell),
                "Paid XP fixture needs its actual generated caster's already owned affordable native self-heal; no spell or magicka is granted.");
            var binding=actor.GetComponent<NativeNpcVitalsBinding>();
            var template=GameManager.Instance.EntityEffectBroker.GetEffectTemplate(spell.EffectId);
            PaidCompanionProbeRequire(binding && template!=null,"Natural paid caster lacks native spell/vitals binding.");
            int skill=(int)template.Properties.MagicSkill;
            var before=store.ReadNpcActor(id,store.ResolveNpcArchetype(id));
            var progress=store.ReadNpcProgress(id);
            var row=progress==null?null:progress.Skills.Find(entry=>entry.Skill==skill);
            long experienceBefore=row==null?0:row.Experience;
            short[] skills=new short[35];for(int i=0;i<35;i++)skills[i]=body.Entity.Skills.GetPermanentSkillValue(i);
            int level=body.Entity.Level,magicka=body.Entity.CurrentMagicka;
            int playerGold=GameManager.Instance.PlayerEntity.GetGoldAmount();
            int wallet=store.ReadNpcInventory(id).WalletGold;
            string book=SaveLoadManager.Serialize(typeof(EffectBundleSettings[]),body.Entity.SerializeSpellbook(),false);
            // One explicit disposable injury establishes need. The spellbook,
            // profile, attributes, skills and real magicka remain authoritative.
            body.Entity.SetHealth(body.Entity.MaxHealth-1);binding.Capture();
            string reason;
            PaidCompanionProbeRequire(NativeNpcCompanionCast.TryExecute(actor.gameObject,store,spell,
                ()=>IsSameActivePaidCast(hired.Id,id,contractId),out reason),"Actual paid caster's own native heal failed: "+reason);
            progress=store.ReadNpcProgress(id);row=progress==null?null:progress.Skills.Find(entry=>entry.Skill==skill);
            var after=store.ReadNpcActor(id,store.ResolveNpcArchetype(id));
            PaidCompanionProbeRequire(row!=null && row.Experience==experienceBefore+1 &&
                body.Entity.CurrentHealth==body.Entity.MaxHealth && body.Entity.CurrentMagicka==magicka-spell.MagickaCost &&
                body.Entity.Level==level && after.Profile.Archetype==before.Profile.Archetype &&
                after.Profile.GenerationVersion==before.Profile.GenerationVersion &&
                after.Profile.MaximumHealth==before.Profile.MaximumHealth && after.Profile.MaximumMagicka==before.Profile.MaximumMagicka &&
                GameManager.Instance.PlayerEntity.GetGoldAmount()==playerGold &&
                store.ReadNpcInventory(id).WalletGold==wallet && IsSameActivePaidCast(hired.Id,id,contractId) &&
                SaveLoadManager.Serialize(typeof(EffectBundleSettings[]),body.Entity.SerializeSpellbook(),false)==book,
                "One actual paid native heal did not retain exact skill-use XP, native costs, book, paid binding and currency.");
            for(int stat=0;stat<8;stat++)PaidCompanionProbeRequire(
                body.Entity.Stats.GetPermanentStatValue(stat)==before.Profile.Attribute(stat) &&
                after.Profile.Attribute(stat)==before.Profile.Attribute(stat),"Paid native heal changed a seeded profile attribute.");
            for(int i=0;i<35;i++)PaidCompanionProbeRequire(body.Entity.Skills.GetPermanentSkillValue(i)==
                (i==skill?Math.Min(100,skills[i]+after.Skill(i)-before.Skill(i)):skills[i]),
                "Paid native spell changed a skill other than its actual earned native school.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PAID_COMPANION_XP_NATIVE_PASS: resident="+id+"; contract="+contractId+
                "; actualGeneratedAdventurer=true; actualNativeWageConsent=true; actualNativePayment="+fee+
                "; actualOwnedSpell="+spell.SpellId+"; effect="+spell.EffectId+"; nativeSkill="+skill+
                "; rawExperience="+experienceBefore+"->"+row.Experience+"; casts=1; nativeMagickaCost="+spell.MagickaCost+
                "; spellbookUnchanged=true; profileUnchanged=true; otherSkillsUnchanged=true; stillPaidBound=true");
        }

        private bool IsSameActivePaidCast(string actionId,string residentId,string contractId)
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            double now=clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));
            var job=store.ActiveNpcAction(residentId);NpcServiceContract contract;
            return job!=null && job.Id==actionId && NpcPaidCompanionAction.IsPaidFollowJob(job) &&
                NpcPaidCompanionTravel.TryReadActiveTravel(store,job,now,out contract) && contract.ContractId==contractId;
        }

        private static string NativeItems(DaggerfallEntity entity)
        {
            var rows=new List<string>();for(int i=0;i<entity.Items.Count;i++)
            {var item=entity.Items.GetItem(i);rows.Add(item.UID+":"+item.TemplateIndex+":"+item.stackCount+":"+item.currentCondition);}
            rows.Sort(StringComparer.Ordinal);return string.Join("|",rows.ToArray());
        }
    }
}
