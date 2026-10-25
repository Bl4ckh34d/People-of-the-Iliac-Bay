using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in disposable-save acceptance. The actor walks under its real Follow
    // action; a controlled native poison and remedy are confined to this save.
    public static class CompanionHealthContactProbe
    {
        private const string Flag="-peopleoftheiliacbay-companion-health-contact-probe";
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;

        public static IEnumerator RunFixture(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            string save=CommandLineValue("-peopleoftheiliacbay-fixture-save");
            var game=GameManager.Instance;
            var args=Environment.GetCommandLineArgs();
            bool optedIn=Array.IndexOf(args,Flag)>=0 ||
                Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE")=="companion-health" &&
                Array.IndexOf(args,"-peopleoftheiliacbay-outdoor-navigation-probe")>=0;
            Require(optedIn &&
                NativeProbeText.IsTestCharacter(character??"") &&
                !string.IsNullOrWhiteSpace(save) && host && streets && game!=null &&
                game.PlayerEntity!=null && game.PlayerEntity.Name==character,
                "Companion health contact probe requires its explicit flag and exact owned disposable character/save.");
            game.SaveLoadManager.EnumerateSaves();
            Require(game.SaveLoadManager.FindSaveFolderByNames(character,save)>=0,
                "The exact original disposable fixture save is missing.");

            string failure=null;
            IEnumerator checks=RunChecks(host,streets);
            while(true)
            {
                bool more;
                try {more=checks.MoveNext();}
                catch(Exception error) {failure=error.ToString();break;}
                if(!more)break;
                yield return checks.Current;
            }
            try {(checks as IDisposable)?.Dispose();}
            catch(Exception error) {failure=(failure==null?"":failure+" | ")+"Fixture cleanup failed: "+error;}
            // No test mutation is saved. Exact baseline reload discards poison,
            // Follow action, contact receipt, and any fixture inventory changes.
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            yield return ConversationMoodProbe.LoadSave(host,streets,character,save,false);
            Require(streets.Ready,"Original disposable fixture did not reload to a ready town.");
            if(failure!=null)throw new InvalidOperationException("Companion health contact probe failed before baseline restore: "+failure);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_HEALTH_CONTACT_PASS: acceptedFollow=true; nativePoison=true; " +
                "noOwnRemedy=true; ordinaryFollowRange=true; exactNativeTalkReceipt=true; " +
                "exactPlayerRemedyHandoff=true; nativeCure=true; followPreserved=true; unboundRejected=true; " +
                "noConditionRejected=true; noModel=true; exactBaselineRestored=true");
        }

        private static IEnumerator RunChecks(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            var game=GameManager.Instance;
            if(game.PlayerEnterExit.IsPlayerInside)
            {
                game.PlayerEnterExit.TransitionExterior(true);
                float exteriorUntil=Time.realtimeSinceStartup+60;
                while((game.PlayerEnterExit.IsPlayerInside || !streets.Ready) &&
                    Time.realtimeSinceStartup<exteriorUntil)yield return null;
            }
            Require(!game.PlayerEnterExit.IsPlayerInside && streets.Ready &&
                !GameManager.IsGamePaused && game.IsPlayerOnHUD &&
                !game.PlayerEntity.IsInvisible && !game.PlayerEntity.IsMagicallyConcealed &&
                !game.AreEnemiesNearby(false,false),
                "The owned exterior fixture is not in a safe visible contact state.");
            var store=Store(streets);
            Require(store!=null,"No live resident store belongs to the fixture town.");
            // The prepared morning save is at dawn, before ordinary residents
            // leave their houses. Observe the existing noon routines instead.
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((43200-clock.ToSeconds()%86400+86400)%86400));
            yield return null;
            var geometry=(TownGeometry)typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets);
            var directory=(BuildingDirectory)typeof(StreetPopulation).GetField("directory",Private).GetValue(streets);
            Require(geometry!=null && directory,"The owned exterior fixture has no surveyed town geometry.");
            ResidentActor actor=null;
            Vector3 observerPoint=Vector3.zero;
            float candidateUntil=Time.realtimeSinceStartup+90;
            while(Time.realtimeSinceStartup<candidateUntil)
            {
                actor=FindRemoteCandidate(streets,store,geometry,directory,out observerPoint);
                if(actor)break;
                yield return null;
            }
            Require(actor,"No standing, unbound adult has a surveyed clear observer point in the loaded town; bodies="+
                UnityEngine.Object.FindObjectsOfType<ResidentActor>().Length+"; time="+clock.ToSeconds()%86400);
            // The fixture's save starts away from the residents. Move only the
            // test observer to a surveyed open point; the NPC remains untouched.
            var motor=game.PlayerMotor;
            var controller=motor.GetComponent<CharacterController>();
            bool motorEnabled=motor.enabled,controllerEnabled=controller && controller.enabled;
            motor.enabled=false;if(controller)controller.enabled=false;
            motor.transform.position=observerPoint+Vector3.up;
            Physics.SyncTransforms();
            if(controller)controller.enabled=controllerEnabled;motor.enabled=motorEnabled;
            yield return null;
            actor=streets.FindActor(actor.Resident.Id);
            Require(actor && actor.gameObject.activeInHierarchy &&
                Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)>=3f &&
                Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)<=6f,
                "The surveyed observer point did not retain the selected live resident at 3–6 m.");
            var manager=actor.GetComponent<EntityEffectManager>();
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            Require(manager && owner && owner.Binding!=null && body && body.Entity!=null &&
                manager.PoisonCount==0 && manager.DiseaseCount==0 && body.Entity.SpellbookCount()==0 &&
                !OwnsCurePoisonPotion(owner),"Selected fixture actor did not begin with a known no-remedy state.");
            string residentId=actor.Resident.Id;
            string reason="No Follow option was offered.";
            NpcActionJob follow=null;
            foreach(var option in streets.ConversationActionOptions(residentId,"Follow me."))
                if(option.Kind==NpcActionKind.Follow && option.Target!=null &&
                    option.Target.Kind==NpcActionTargetKind.Person &&
                    option.Target.PersonId==NpcDirectedRelationship.PlayerTargetId &&
                    NpcProbeCommandSource.TryAcceptAction(streets,residentId,
                        "companion-health-probe:"+Guid.NewGuid().ToString("N"),
                        new NpcActionDecision{Option=option,DurationSeconds=300},out reason))
                {follow=store.ActiveNpcAction(residentId);break;}
            Require(follow!=null && follow.Kind==NpcActionKind.Follow && follow.Target!=null &&
                follow.Target.PersonId==NpcDirectedRelationship.PlayerTargetId,
                "The actual player-bound Follow action was not accepted: "+reason);
            Require(!streets.TryObservedCompanionHealthCondition(actor,Now(),out _),
                "A healthy follower qualified for a health request.");

            Vector3 start=actor.transform.position;
            float arriveUntil=Time.realtimeSinceStartup+45;
            float separation=float.PositiveInfinity;
            while(Time.realtimeSinceStartup<arriveUntil)
            {
                var current=store.ActiveNpcAction(residentId);
                Require(current!=null && current.Id==follow.Id && !NpcActionExecutor.IsTerminal(current),
                    "Follow ownership ended before the ordinary arrival radius was observed.");
                actor=streets.FindActor(residentId);
                Require(actor && actor.gameObject.activeInHierarchy,"The followed resident body left the loaded scene.");
                separation=Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position);
                if(separation>1.35f && separation<=StreetPopulation.CompanionHealthTalkDistance &&
                    Vector3.Distance(start,actor.transform.position)>=0.5f &&
                    CivilianResponses.Sees(actor.transform.position+Vector3.up,
                        game.PlayerMotor.transform.position+Vector3.up,StreetPopulation.CompanionHealthTalkDistance))break;
                yield return null;
            }
            Require(separation>1.35f && separation<=StreetPopulation.CompanionHealthTalkDistance &&
                Vector3.Distance(start,actor.transform.position)>=0.5f,
                "The real follower did not stop within the >1.35 m native talk acceptance band; distance="+separation);

            // Reserve the existing local-dialogue dispatch slot. The production
            // native Talk handoff still opens and claims its receipt; its verified
            // fallback renders without sending any model or GPU request.
            var client=(LocalDialogueClient)typeof(PeopleOfTheIliacBayMod).GetField("localDialogue",Private).GetValue(host);
            Require(client && !client.IsBusy,"A model request was already active; refusing the no-model acceptance run.");
            var dispatch=(LocalDialogueDispatch)typeof(LocalDialogueClient).GetField("dispatch",Private).GetValue(client);
            var voice=typeof(PeopleOfTheIliacBayMod).GetField("breezeTtsTrial",Private);
            object oldVoice=voice.GetValue(host);
            Require(dispatch!=null && dispatch.TryBegin()!=0,"Could not reserve the no-model dialogue fallback.");
            long modelCompletions=client.ReplyCompletionCount;
            try
            {
                voice.SetValue(host,null);
                owner.Capture();
                var poison=manager.CreatePoison(Poisons.Nux_Vomica);
                Require(poison!=null,"DFU could not create the classic native poison bundle.");
                manager.AssignBundle(poison,AssignBundleFlags.BypassSavingThrows|AssignBundleFlags.BypassChance);
                Require(manager.PoisonCount>0 && !NativeNpcConditionTreatment.HasOwnTreatment(actor,store,()=>true),
                    "Native poison was not retained or the follower owns a usable remedy.");
                double appliedAt=Now();
                Require(streets.TryObservedCompanionHealthCondition(actor,appliedAt,out string condition) &&
                    condition=="poison","The poisoned, player-bound follower did not qualify.");
                Require(store.TryGetCompanionHealthContactOffer(residentId,"poison",appliedAt,out var expected),
                    "The existing contact ledger rejected the observed companion condition.");
                float talkUntil=Time.realtimeSinceStartup+12;
                ProactiveContactRecord opened=null;
                while(Time.realtimeSinceStartup<talkUntil)
                {
                    foreach(var record in store.Snapshot().RecentProactiveContacts)
                        if(record.ContactId==expected.ContactId && record.OpenedAt>=0)
                        {opened=record;break;}
                    if(opened!=null)break;
                    yield return null;
                }
                var talk=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                float claimedDistance=Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position);
                Require(opened!=null && opened.Kind==ProactiveContactKind.CompanionHealthRequest &&
                    opened.ResidentId==residentId && opened.Purpose=="poison" &&
                    opened.NoticedAt>=appliedAt && opened.OpenedAt>=opened.NoticedAt &&
                    talk!=null && game.TalkManager.CurrentNPCType==TalkManager.NPCType.Mobile &&
                    game.TalkManager.MobileNPC==actor.Person && claimedDistance>1.35f &&
                    claimedDistance<=StreetPopulation.CompanionHealthTalkDistance &&
                    client.ReplyCompletionCount==modelCompletions,
                    "Native Talk did not claim the exact companion-health receipt at ordinary Follow distance; " +
                    "distance="+claimedDistance+"; receipt="+(opened==null?"none":opened.ContactId));
                Require(store.ActiveNpcAction(residentId)!=null && store.ActiveNpcAction(residentId).Id==follow.Id,
                    "The health request displaced the original Follow commitment.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_HEALTH_TALK_CLAIM: resident="+residentId+
                    "; contact="+opened.ContactId+"; follow="+follow.Id+"; distance="+
                    claimedDistance.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));

                // The selected native antidote enters through the same existing
                // Transfer option and typed command source as ordinary gifts.
                int recipe=NativeNpcMedicalService.RecipeFor(CurePoison.EffectKey);
                Require(recipe>=0,"The native antidote recipe is unavailable.");
                var remedy=ItemBuilder.CreatePotion(recipe);
                Require(remedy!=null && NativeNpcConditionTreatment.MatchesPotion(remedy,CurePoison.EffectKey),
                    "The native fixture antidote is not an exact poison remedy.");
                game.PlayerEntity.Items.AddItem(remedy,noStack:true);
                ulong remedyUid=remedy.UID;
                string handoffText="Please take one "+remedy.LongName+" from me.";
                NpcActionOption transfer=null;
                foreach(var option in streets.ConversationActionOptions(residentId,handoffText))
                    if(option.Kind==NpcActionKind.Transfer && option.Target!=null &&
                        option.Target.Kind==NpcActionTargetKind.Person &&
                        option.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc &&
                        option.Target.PersonId==NpcDirectedRelationship.PlayerTargetId &&
                        option.Target.ItemId==remedyUid.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                        option.Target.Quantity==1)
                    {transfer=option;break;}
                Require(transfer!=null,"The poisoned follower did not offer the exact owned antidote Transfer option.");
                string handoffReason;
                bool handed=NpcProbeCommandSource.TryAcceptAction(streets,residentId,
                    "companion-health-handoff-probe:"+Guid.NewGuid().ToString("N"),
                    new NpcActionDecision{Option=transfer,DurationSeconds=60},handoffText,out handoffReason);
                Require(handed && handoffReason!=null &&
                    handoffReason.StartsWith(StreetPopulation.CompanionHealthHandoffReasonPrefix,StringComparison.Ordinal),
                    "The exact native antidote handoff was not confirmed: "+handoffReason);
                var received=store.ReadNpcInventory(residentId);
                Require(game.PlayerEntity.Items.GetItem(remedyUid)==null &&
                    owner.Binding.Items.GetItem(remedyUid)!=null &&
                    received!=null && !string.IsNullOrWhiteSpace(received.LastTransactionId) &&
                    received.LastTransactionId.EndsWith(":companion-health-handoff",StringComparison.Ordinal) &&
                    manager.PoisonCount>0 && DaggerfallUI.UIManager.TopWindow==talk &&
                    store.ActiveNpcAction(residentId)!=null && store.ActiveNpcAction(residentId).Id==follow.Id,
                    "Handoff did not move one exact native item with a durable receipt while preserving poisoned Follow and Talk.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_HEALTH_TRANSFER_PROBE: resident="+residentId+
                    "; playerUid="+remedyUid+"; transaction="+received.LastTransactionId+
                    "; follow="+follow.Id+"; poisonedInTalk=true");

                DaggerfallUI.Instance.PopToHUD();
                float cureUntil=Time.realtimeSinceStartup+45;
                while(manager.PoisonCount>0 && Time.realtimeSinceStartup<cureUntil)
                {
                    Require(store.ActiveNpcAction(residentId)!=null &&
                        store.ActiveNpcAction(residentId).Id==follow.Id,
                        "Follow was displaced before the received native remedy could be used.");
                    yield return null;
                }
                Require(manager.PoisonCount==0 && game.PlayerEntity.Items.GetItem(remedyUid)==null &&
                    !OwnsCurePoisonPotion(owner) && actor && !actor.IsConversing &&
                    store.ActiveNpcAction(residentId)!=null && store.ActiveNpcAction(residentId).Id==follow.Id &&
                    !NpcActionExecutor.IsTerminal(store.ActiveNpcAction(residentId)),
                    "The native antidote did not cure poison and resume the same Follow after Talk closed.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_HEALTH_CURE_PROBE: resident="+residentId+
                    "; cured=true; remedyConsumed=true; sameFollow="+follow.Id);
                if(Environment.GetEnvironmentVariable("POTIB_COMPANION_CAST")=="1")
                    NativeNpcCompanionCastProbe.Verify(store,actor,game.PlayerEntity.Name);

                // Remove only the player's binding. Reapply the same controlled
                // native poison so the unbound gate is tested independently of cure.
                string stopReason;
                Require(streets.TryStopConversationAction(residentId,out stopReason),
                    "Could not end the controlled Follow: "+stopReason);
                var secondPoison=manager.CreatePoison(Poisons.Nux_Vomica);
                Require(secondPoison!=null,"DFU could not create the negative-control native poison bundle.");
                manager.AssignBundle(secondPoison,AssignBundleFlags.BypassSavingThrows|AssignBundleFlags.BypassChance);
                var stoppedFollow=store.ActiveNpcAction(residentId);
                Require(manager.PoisonCount>0 && (stoppedFollow==null || NpcActionExecutor.IsTerminal(stoppedFollow)) &&
                    !streets.TryObservedCompanionHealthCondition(actor,Now(),out _),
                    "An unbound poisoned citizen qualified for the player's help request.");
            }
            finally
            {
                client.Cancel();
                voice.SetValue(host,oldVoice);
            }
        }

        private static ResidentActor FindCandidate(StreetPopulation streets,WorldStateStore store,
            string exclude,float minimumDistance,float maximumDistance)
        {
            var game=GameManager.Instance;ResidentActor best=null;float bestDistance=float.PositiveInfinity;
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if(!EligibleCandidate(streets,store,actor,exclude))continue;
                float distance=Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position);
                if(distance<minimumDistance || distance>maximumDistance || distance>=bestDistance ||
                    !CivilianResponses.Sees(actor.transform.position,game.PlayerMotor.transform.position,maximumDistance))continue;
                best=actor;bestDistance=distance;
            }
            return best;
        }

        private static ResidentActor FindRemoteCandidate(StreetPopulation streets,WorldStateStore store,
            TownGeometry geometry,BuildingDirectory directory,out Vector3 observerPoint)
        {
            observerPoint=Vector3.zero;
            var candidates=new List<ResidentActor>();
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if(EligibleCandidate(streets,store,actor,null) && actor.transform.parent==directory.transform)
                    candidates.Add(actor);
            Vector3 player=GameManager.Instance.PlayerMotor.transform.position;
            candidates.Sort((a,b)=>Vector3.SqrMagnitude(a.transform.position-player)
                .CompareTo(Vector3.SqrMagnitude(b.transform.position-player)));
            foreach(var actor in candidates)
                if(TrySurveyObserverPoint(geometry,directory,actor,out observerPoint))return actor;
            return null;
        }

        private static bool TrySurveyObserverPoint(TownGeometry geometry,BuildingDirectory directory,
            ResidentActor actor,out Vector3 point)
        {
            point=Vector3.zero;
            Vector3 local=directory.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            var from=new SavedRoutePoint{X=local.x,Z=local.z};
            foreach(var direction in new[]{Vector2.right,Vector2.left,Vector2.up,Vector2.down,
                new Vector2(0.7071f,0.7071f),new Vector2(-0.7071f,0.7071f),
                new Vector2(0.7071f,-0.7071f),new Vector2(-0.7071f,-0.7071f)})
            {
                var to=new SavedRoutePoint{X=local.x+direction.x*4.5f,Z=local.z+direction.y*4.5f};
                var route=geometry.RouteBetweenPoints(from,to);
                if(route==null || route.Count<2)continue;
                Vector3 ground=geometry.ScenePoint(new RoutePoint(to.X,to.Z));
                float distance=Vector3.Distance(actor.transform.position,ground+Vector3.up);
                if(distance<3f || distance>6f ||
                    !CivilianResponses.Sees(actor.transform.position+Vector3.up,ground+Vector3.up,6f))continue;
                bool blocked=false;
                foreach(var collider in Physics.OverlapCapsule(ground+Vector3.up*0.35f,
                    ground+Vector3.up*1.5f,0.28f,~0,QueryTriggerInteraction.Ignore))
                    if(CivilianResponses.Blocker(collider)){blocked=true;break;}
                if(blocked)continue;
                point=ground;return true;
            }
            return false;
        }

        private static bool EligibleCandidate(StreetPopulation streets,WorldStateStore store,
            ResidentActor actor,string exclude)
        {
            if(!actor || !actor.gameObject.activeInHierarchy || actor.Resident==null ||
                actor.Resident.Id==exclude || !actor.Resident.IsAlive || actor.Resident.IsQuestProtected ||
                actor.Resident.AgeGroup!=ResidentAgeGroup.Adult || actor.IsSleeping || actor.IsSeated ||
                actor.IsConversing || streets.FindActor(actor.Resident.Id)!=actor ||
                streets.IsAlarmed(actor.Resident.Id) || store.IsResidentAway(actor.Resident.Id,Now()) ||
                store.ActiveNpcAction(actor.Resident.Id)!=null || store.ActiveNpcTask(actor.Resident.Id)!=null ||
                HasPlayerFollowContract(store,actor.Resident.Id))return false;
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            var manager=actor.GetComponent<EntityEffectManager>();
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            var vitals=actor.GetComponent<NativeNpcVitalsBinding>();
            return body && body.Entity!=null && body.Entity.SpellbookCount()==0 && manager &&
                manager.PoisonCount==0 && manager.DiseaseCount==0 && owner && owner.Binding!=null &&
                vitals && !OwnsCurePoisonPotion(owner);
        }

        private static bool OwnsCurePoisonPotion(NativeNpcInventoryOwner owner)
        {
            var broker=GameManager.Instance.EntityEffectBroker;
            var items=owner.Binding.Items;
            for(int i=0;i<items.Count;i++)
            {
                DaggerfallUnityItem item=items.GetItem(i);
                if(item==null || !item.IsPotion || item.stackCount<1)continue;
                var recipe=broker.GetPotionRecipe(item.PotionRecipeKey);
                if(recipe==null)continue;
                var effect=broker.GetPotionRecipeEffect(recipe);
                if(effect!=null && effect.Key==CurePoison.EffectKey)return true;
            }
            return false;
        }

        private static bool HasPlayerFollowContract(WorldStateStore store,string residentId)
        {
            double now=Now();
            foreach(var contract in store.CaptureNpcServiceContractWriteTicket().SourceLedger.Contracts)
                if(contract.NpcId==residentId && contract.PayerId==NpcDirectedRelationship.PlayerTargetId &&
                    contract.Status==NpcServiceContractStatus.Active && now>=contract.BlockStartAt &&
                    now<contract.BlockEndAt && (contract.Coverage & NpcServiceCoverage.Follow)!=0)return true;
            return false;
        }

        private static WorldStateStore Store(StreetPopulation streets)
        {return (WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);}
        private static double Now()
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            return clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));
        }
        private static string CommandLineValue(string key)
        {
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++)if(args[i]==key)return args[i+1];
            return null;
        }
        private static void Require(bool ok,string message)
        {if(!ok)throw new InvalidOperationException(message);}
    }
}
