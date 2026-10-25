using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in, caller-owned native fixture only. Disk saves require the separate disk flag. No
    // artificial actor placement, player charging, or background test controller.
    public static class NpcActionRuntimeProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static IEnumerator RunFixture(StreetPopulation streets,string expectedCharacter)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-action-probe")<0 ||
                !expectedCharacter.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal))
                throw new InvalidOperationException("Action placement requires an explicitly owned fixture.");
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-lawful-loot-probe")>=0)
            {
                yield return RunExistingLawfulLoot(streets,expectedCharacter);
                yield break;
            }
            var game=GameManager.Instance;var room=game.PlayerEnterExit.Interior;
            var geometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",Private).GetValue(streets);
            if(!room || geometry==null) throw new InvalidOperationException("Action fixture requires a surveyed inn.");
            bool beer=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-beer-probe")>=0;
            var before=game.PlayerMotor.transform.position;
            bool godMode=game.PlayerEntity.GodMode;
            int beforeHealth=game.PlayerEntity.CurrentHealth;
            try
            {
                if(beer)
                {
                    // DFU's GodMode SetHealth fills all HP, hiding the actual
                    // native drink increment. Use an injured ordinary body.
                    game.PlayerEntity.GodMode=false;
                    game.PlayerEntity.SetHealth(Math.Max(1,game.PlayerEntity.MaxHealth-4),true);
                }
                string placedResidentId=null;
                foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                {
                    if(!actor.gameObject.activeInHierarchy || actor.IsSleeping || actor.Resident.AgeGroup!=ResidentAgeGroup.Adult) continue;
                    foreach(var local in geometry.LocalAnchors)
                    {
                        var point=room.transform.TransformPoint(local)+Vector3.up;
                        float gap=Vector3.Distance(point,actor.transform.position);
                        if(gap<3 || gap>6 || Math.Abs(point.y-actor.transform.position.y)>0.3f) continue;
                        game.PlayerMotor.transform.position=point;
                        var options=streets.ConversationActionOptions(actor.Resident.Id,beer?"Buy me a beer.":"Come here.");
                        if(options.Find(option=>option.Kind==(beer?NpcActionKind.Purchase:NpcActionKind.MoveTo))==null) continue;
                        placedResidentId=actor.Resident.Id;break;
                    }
                    if(placedResidentId!=null) break;
                }
                if(placedResidentId==null) throw new InvalidOperationException("No safe public observer point near an actionable resident.");
                Physics.SyncTransforms();yield return null;
                yield return RunSelected(streets,expectedCharacter,beer,placedResidentId);
            }
            finally
            {
                if(beer)
                {
                    game.PlayerEntity.GodMode=false;
                    game.PlayerEntity.SetHealth(beforeHealth,true);
                    game.PlayerEntity.GodMode=godMode;
                }
                game.PlayerMotor.transform.position=before;
            }
        }
        public static IEnumerator Run(StreetPopulation streets,string expectedCharacter,bool beer=false)
        {return RunSelected(streets,expectedCharacter,beer,null);}

        // Existing-state probe only. It never spawns a corpse, item, resident, route, or claim.
        // A missing live lawful corpse/nearby resident reports not-ready instead of manufacturing evidence.
        private static IEnumerator RunExistingLawfulLoot(StreetPopulation streets,string expectedCharacter)
        {
            var game=GameManager.Instance;
            if(!streets || !streets.Ready || game.PlayerEntity.Name!=expectedCharacter || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Lawful loot probe requires the caller's owned exterior fixture.");
            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
            var actors=(Dictionary<string,ResidentActor>)typeof(StreetPopulation).GetField("actors",Private).GetValue(streets);
            var population=(SettlementPopulation)typeof(StreetPopulation).GetField("population",Private).GetValue(streets);
            var guards=streets.GetComponent<GuardPopulation>();
            if(store==null || actors==null || population==null || guards==null)
                throw new InvalidOperationException("Lawful loot probe lacks loaded population state.");
            foreach(var record in store.DefenseCorpsesFor(population.SettlementId))
            {
                if(record==null || record.Death==null || string.IsNullOrWhiteSpace(record.Death.CorpseId))continue;
                DaggerfallLoot corpse;
                if(!guards.TryGetLawfulLootClaim(population.SettlementId,record.Death.CorpseId,out corpse) || corpse.Items.Count==0)continue;
                foreach(var actor in new List<ResidentActor>(actors.Values))
                {
                    if(!actor || !actor.gameObject.activeInHierarchy || actor.IsSleeping || !actor.Resident.IsAlive || actor.IsConversing)continue;
                    for(int i=0;i<corpse.Items.Count;i++)
                    {
                        var item=corpse.Items.GetItem(i);
                        // This targeted probe verifies one preserved source UID.
                        // A partial stack receives a native split UID, so it is
                        // intentionally not evidence for this exact-UID check.
                        if(item==null || item.stackCount!=1 || string.IsNullOrWhiteSpace(item.LongName))continue;
                        string request="Take 1 "+item.LongName+" from the corpse.";
                        var option=streets.ConversationActionOptions(actor.Resident.Id,request).Find(x=>x.Kind==NpcActionKind.Transfer &&
                            x.Target!=null && x.Target.TransferDirection==NpcActionTransferDirection.ObservedLootToNpc &&
                            x.Target.ContainerId==record.Death.CorpseId && x.Target.ItemId==item.UID.ToString() && x.Target.Quantity==1);
                        if(option==null)continue;
                        string reason;var decision=new NpcActionDecision {Option=option,DurationSeconds=300};
                        if(!NpcProbeCommandSource.TryAcceptAction(streets,actor.Resident.Id,"native-lawful-loot:"+Guid.NewGuid().ToString("N"),decision,out reason))
                            throw new InvalidOperationException("Observed lawful loot offer did not accept: "+reason);
                        var accepted=store.ActiveNpcAction(actor.Resident.Id);
                        if(accepted==null || accepted.Target.TransferDirection!=NpcActionTransferDirection.ObservedLootToNpc ||
                            accepted.Target.ContainerId!=record.Death.CorpseId || accepted.Target.ItemId!=item.UID.ToString())
                            throw new InvalidOperationException("Lawful loot action lost exact corpse or item provenance.");
                        float deadline=Time.realtimeSinceStartup+90;NpcActionJob outcome=accepted;
                        while(Time.realtimeSinceStartup<deadline)
                        {
                            outcome=store.ReadNpcAction(accepted.Id);
                            if(outcome==null || NpcActionExecutor.IsTerminal(outcome))break;
                            yield return null;
                        }
                        if(outcome==null || outcome.Phase!=NpcActionPhase.Completed)
                            throw new InvalidOperationException("Observed lawful loot did not complete: "+(outcome==null?"missing":outcome.Phase+" "+outcome.FailureReason));
                        if(corpse.Items.GetItem(item.UID)!=null)
                            throw new InvalidOperationException("Observed lawful loot left the exact source UID in its corpse.");
                        var owner=actor.GetComponent<NativeNpcInventoryOwner>();string captureReason="NPC inventory owner is unavailable.";
                        if(owner==null || owner.Binding==null || !NativeNpcInventoryBridge.TryCapture(store,owner.Binding,out captureReason))
                            throw new InvalidOperationException("Observed lawful loot could not read NPC inventory: "+captureReason);
                        var received=owner.Binding.Items.GetItem(item.UID);
                        if(received==null || received.stackCount!=1)
                            throw new InvalidOperationException("Observed lawful loot did not retain the exact source UID in NPC inventory.");
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LAWFUL_LOOT_PASS: resident="+actor.Resident.Id+"; corpse="+
                            record.Death.CorpseId+"; source-item="+item.UID+"; quantity=1.");
                        yield break;
                    }
                }
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LAWFUL_LOOT_NOT_READY: no existing loaded lawful-defense corpse item had a nearby eligible resident.");
        }

        private static IEnumerator RunSelected(StreetPopulation streets,string expectedCharacter,bool beer,string placedResidentId)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-action-probe")<0)
                throw new InvalidOperationException("Action probe requires its explicit isolated-process flag.");
            var game=GameManager.Instance;
            if(game.PlayerEntity.Name!=expectedCharacter || !streets || !streets.Ready)
                throw new InvalidOperationException("Action probe requires the caller's loaded, owned fixture character.");
            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
            var actors=(Dictionary<string,ResidentActor>)typeof(StreetPopulation).GetField("actors",Private).GetValue(streets);
            bool beerChat=beer && Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-action-chat-probe")>=0;
            ResidentActor selected=null;NpcActionDecision decision=null;string reason=null;
            string request=beer?"Buy me a beer.":"Come here.";
            foreach(var actor in new List<ResidentActor>(actors.Values))
            {
                if(!actor || placedResidentId!=null && actor.Resident.Id!=placedResidentId ||
                    !actor.gameObject.activeInHierarchy || actor.IsConversing ||
                    Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)<2.5f) continue;
                var options=streets.ConversationActionOptions(actor.Resident.Id,request);
                foreach(var option in options)
                {
                    if(option.Kind!=(beer?NpcActionKind.Purchase:NpcActionKind.MoveTo)) continue;
                    decision=new NpcActionDecision {Option=option,DurationSeconds=300};
                    if(beerChat) {selected=actor;break;}
                    if(NpcProbeCommandSource.TryAcceptAction(streets,actor.Resident.Id,"native-probe:"+Guid.NewGuid().ToString("N"),decision,out reason)) {selected=actor;break;}
                }
                if(selected) break;
            }
            if(!selected) throw new InvalidOperationException("Placed fixture resident did not accept a reachable action: resident="+
                placedResidentId+"; player="+game.PlayerMotor.transform.position.ToString("F3")+"; reason="+reason);
            if(beerChat)
            {
                if(!selected.IsSeated)throw new InvalidOperationException("Beer regression requires an actual seated patron.");
                DescribeBeerCounter(streets,selected);yield return AcceptBeerThroughChat(store,selected);
            }
            var accepted=store.ActiveNpcAction(selected.Resident.Id);
            if(accepted==null || accepted.Phase!=NpcActionPhase.Accepted) throw new InvalidOperationException("Action did not commit acceptance before movement.");
            var start=selected.transform.position;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_PROBE_START: resident="+selected.Resident.Id+
                "; action="+accepted.Id+"; kind="+accepted.Kind+"; actor="+start.ToString("F3")+
                "; player="+game.PlayerMotor.transform.position.ToString("F3")+
                "; distance="+Vector3.Distance(start,game.PlayerMotor.transform.position).ToString("F3"));
            int playerGold=game.PlayerEntity.GetGoldAmount(),health=game.PlayerEntity.CurrentHealth;
            uint lastDrink=game.PlayerEntity.LastTimePlayerAteOrDrankAtTavern;
            int npcGold=beer?store.ReadNpcInventory(selected.Resident.Id).WalletGold:0;
            string encoded=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot(),false);
            var decoded=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),encoded);
            var branch=new WorldStateStore(1);branch.Restore(decoded);
            var resumed=branch.ReadNpcAction(accepted.Id);
            if(resumed==null || resumed.Target.Kind!=accepted.Target.Kind || resumed.LastPosition==null || resumed.Revision!=accepted.Revision)
                throw new InvalidOperationException("Native serializer lost action identity or spatial progress.");
            float deadline=Time.realtimeSinceStartup+180;
            NpcActionJob outcome=accepted;
            while(Time.realtimeSinceStartup<deadline)
            {
                outcome=store.ReadNpcAction(accepted.Id);
                if(outcome==null || NpcActionExecutor.IsTerminal(outcome)) break;
                yield return null;
            }
            if(outcome==null || outcome.Phase!=NpcActionPhase.Completed)
                throw new InvalidOperationException("Physical action did not complete: "+(outcome==null?"missing":outcome.Phase+" "+outcome.FailureReason)+
                    "; resident="+selected.Resident.Id+"; start="+start.ToString("F3")+
                    "; actor="+(selected?selected.transform.position.ToString("F3"):"destroyed")+
                    "; player="+game.PlayerMotor.transform.position.ToString("F3"));
            if(!selected || Vector3.Distance(start,selected.transform.position)<0.25f || Vector3.Distance(selected.transform.position,game.PlayerMotor.transform.position)>1.6f)
                throw new InvalidOperationException("Action completion lacked observed movement and nearby arrival.");
            if(beer)
            {
                if(store.ReadNpcInventory(selected.Resident.Id).WalletGold!=npcGold-outcome.ServicePrice ||
                    game.PlayerEntity.GetGoldAmount()!=playerGold || game.PlayerEntity.LastTimePlayerAteOrDrankAtTavern==lastDrink ||
                    game.PlayerEntity.CurrentHealth!=Math.Min(game.PlayerEntity.MaxHealth,health+2))
                    throw new InvalidOperationException("Native beer effect or NPC-only payment was incorrect: npcGold="+
                        npcGold+"->"+store.ReadNpcInventory(selected.Resident.Id).WalletGold+"; price="+outcome.ServicePrice+
                        "; playerGold="+playerGold+"->"+game.PlayerEntity.GetGoldAmount()+"; drinkTime="+lastDrink+"->"+
                        game.PlayerEntity.LastTimePlayerAteOrDrankAtTavern+"; health="+health+"->"+game.PlayerEntity.CurrentHealth+
                        "; maxHealth="+game.PlayerEntity.MaxHealth);
                if(store.TryPayNpcActionService(outcome.Id,outcome.Revision,outcome.UpdatedAt))
                    throw new InvalidOperationException("Completed service charged twice.");
                if(beerChat) Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BEER_CHAT_PASS: realFreeText=true; nativePayment=true; physicalKeeperAndReturn=true; nativeDrinkEffect=true; duplicatePayment=false; playerGoldUnchanged=true");
            }
            if(store.TryAcceptNpcAction(accepted,true,true,true,out reason)) throw new InvalidOperationException("Replayed proposal executed again.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_PROBE_PASS: kind="+outcome.Kind+"; serializer=pass; physical-arrival=pass; replay=blocked; player-gold="+game.PlayerEntity.GetGoldAmount());
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-command-probe")>=0)
                yield return VerifyTypedConversationCommand(streets,store,selected);
            if(!beer) yield return VerifySimpleActions(streets,store,selected);
        }

        private static IEnumerator AcceptBeerThroughChat(WorldStateStore store,ResidentActor actor)
        {
            var host=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();var game=GameManager.Instance;
            if(!host || !(bool)typeof(PeopleOfTheIliacBayMod).GetField("localDialogueEnabled",Private).GetValue(host))
                throw new InvalidOperationException("Beer chat requires the real enabled dialogue client.");
            var busy=typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy",Private);
            game.TalkManager.TalkToMobileNPC(actor.Person);yield return null;yield return null;
            var window=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            if(window==null)throw new InvalidOperationException("Could not open the beer action speaker.");
            float until=Time.realtimeSinceStartup+45;
            while((bool)busy.GetValue(host) && Time.realtimeSinceStartup<until)yield return null;
            if((bool)busy.GetValue(host))throw new InvalidOperationException("Beer greeting timed out.");
            SetFriendlyChatFixtureBaseline(store,actor,host,window,"beer-open");
            Vector3 start=actor.transform.position;
            int gold=store.ReadNpcInventory(actor.Resident.Id).WalletGold;
            uint lastDrink=game.PlayerEntity.LastTimePlayerAteOrDrankAtTavern;
            yield return SubmitAndWait(host,window,"Could you buy me a beer?");
            string transcript=(string)typeof(PeopleOfTheIliacBayMod).GetMethod("ReadConversationHistory",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{window});
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BEER_CHAT_RESPONSE: "+transcript);
            var job=store.ActiveNpcAction(actor.Resident.Id);
            if(job==null || job.Kind!=NpcActionKind.Purchase || job.ServiceCode!="tavern-beer" || job.Phase!=NpcActionPhase.Accepted)
                throw new InvalidOperationException("Real beer request did not admit the native tavern action.");
            if(Vector3.Distance(start,actor.transform.position)>0.05f || store.ReadNpcInventory(actor.Resident.Id).WalletGold!=gold ||
                game.PlayerEntity.LastTimePlayerAteOrDrankAtTavern!=lastDrink)
                throw new InvalidOperationException("Beer action executed while conversation remained open.");
            if(typeof(PeopleOfTheIliacBayMod).GetField("personalCommerceCatalog",Private).GetValue(host)!=null)
                throw new InvalidOperationException("Beer errand opened the personal trade catalog.");
            DaggerfallUI.Instance.PopToHUD();
        }

        private static void DescribeBeerCounter(StreetPopulation streets,ResidentActor actor)
        {
            var activities=(ShopVisitGeometry)typeof(StreetPopulation).GetField("shopGeometry",Private).GetValue(streets);
            var layout=(InnActivityLayout)typeof(ShopVisitGeometry).GetField("innLayout",Private).GetValue(activities);
            var room=GameManager.Instance.PlayerEnterExit.Interior;
            var actual=room.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BEER_COUNTER: actor="+actual.ToString("F3")+"; seated="+actor.IsSeated+
                "; keeper="+activities.CounterPosition+"; slots="+layout.KeeperApproaches.Count);
            foreach(var point in layout.KeeperApproaches)
            {
                var world=room.transform.TransformPoint(point);
                var neighbours=StreetPopulation.ShopDepartureNeighbours(world,actor);
                float nearest=float.PositiveInfinity;
                foreach(var other in neighbours) {var d=other-world;d.y=0;nearest=Math.Min(nearest,d.magnitude);}
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BEER_COUNTER_SLOT: point="+point.ToString("F3")+
                    "; route="+activities.CounterRouteBetween(actual,point).Count+"; nearest="+nearest);
            }
        }

        private static IEnumerator VerifyTypedConversationCommand(StreetPopulation streets,WorldStateStore store,ResidentActor actor)
        {
            const string request="Wait here.";
            var option=streets.ConversationActionOptions(actor.Resident.Id,request).Find(o=>o.Kind==NpcActionKind.Wait);
            if(option==null)throw new InvalidOperationException("Typed command fixture has no verified wait option.");
            var decision=new NpcActionDecision {Option=option,DurationSeconds=10};
            string sessionId=Guid.NewGuid().ToString("N");
            NpcConversationCommandSession session;
            if(!store.TryBeginNpcConversationCommandSession(actor.Resident.Id,sessionId,out session))
                throw new InvalidOperationException("Typed command session could not open.");
            var binding=new ConversationContextBinding(ConversationActorCategory.Mobile,actor.Resident.Id,null,
                sessionId,1,1,new string('a',32),"","native-action-command-probe",1);
            NpcConversationCommandTurn turn;
            if(!store.TryCaptureNpcConversationCommandTurn(session,binding,request,out turn))
                throw new InvalidOperationException("Typed command source turn could not be captured.");
            string reason,proposal="native-command:"+Guid.NewGuid().ToString("N");
            if(!streets.TryAcceptConversationAction(actor.Resident.Id,proposal,decision,turn,out reason))
                throw new InvalidOperationException("Typed command admission failed: "+reason);
            var admitted=store.ActiveNpcAction(actor.Resident.Id);
            if(admitted==null || admitted.ProposalId!=proposal || admitted.Phase!=NpcActionPhase.Accepted)
                throw new InvalidOperationException("Typed command did not save exact accepted action.");
            var cursor=store.Snapshot().NpcConversationCommandCursors.Find(x=>x.PersonId==actor.Resident.Id);
            if(cursor==null || cursor.LastIssuedOrdinal!=turn.Ordinal || cursor.LastAcceptedOrdinal!=turn.Ordinal)
                throw new InvalidOperationException("Typed action and durable accepted source cursor diverged.");
            float deadline=Time.realtimeSinceStartup+30;
            NpcActionJob completed=admitted;
            while(Time.realtimeSinceStartup<deadline)
            {
                completed=store.ReadNpcAction(admitted.Id);
                if(completed==null || NpcActionExecutor.IsTerminal(completed))break;
                yield return null;
            }
            if(completed==null || completed.Phase!=NpcActionPhase.Completed || completed.UpdatedAt<admitted.FinishAt)
                throw new InvalidOperationException("Typed command did not finish its native wait action.");
            int before=store.Snapshot().NpcActions.Count;
            bool replay=streets.TryAcceptConversationAction(actor.Resident.Id,
                "native-command:fresh-id:"+Guid.NewGuid().ToString("N"),decision,turn,out reason);
            if(replay || reason!="The player command is no longer current." || store.Snapshot().NpcActions.Count!=before)
                throw new InvalidOperationException("Same-turn fresh ID replay was not blocked by command identity: "+reason);
            // Close a fresh, unconsumed turn: reusing the already accepted turn
            // would pass even if closing the session failed to invalidate callbacks.
            var nextBinding=new ConversationContextBinding(ConversationActorCategory.Mobile,actor.Resident.Id,null,
                sessionId,2,1,new string('a',32),"","native-action-command-probe",1);
            NpcConversationCommandTurn pendingTurn;
            if(!store.TryCaptureNpcConversationCommandTurn(session,nextBinding,request,out pendingTurn))
                throw new InvalidOperationException("Fresh pending command could not be captured before close.");
            if(!store.TryCloseNpcConversationCommandSession(session))
                throw new InvalidOperationException("Typed command session did not close.");
            replay=streets.TryAcceptConversationAction(actor.Resident.Id,
                "native-command:after-close:"+Guid.NewGuid().ToString("N"),decision,pendingTurn,out reason);
            if(replay || reason!="The player command is no longer current." || store.Snapshot().NpcActions.Count!=before)
                throw new InvalidOperationException("Post-close callback admitted another action: "+reason);
            cursor=store.Snapshot().NpcConversationCommandCursors.Find(x=>x.PersonId==actor.Resident.Id);
            if(cursor==null || cursor.LastAcceptedOrdinal!=turn.Ordinal || cursor.LastIssuedOrdinal!=pendingTurn.Ordinal)
                throw new InvalidOperationException("Post-close rejection changed the accepted source cursor.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMMAND_PROBE_PASS: typed-admission=pass; native-wait=complete; same-turn-fresh-id=blocked; post-close=blocked; cursor="+turn.Ordinal);
        }

        private static NpcActionJob Accept(StreetPopulation streets,WorldStateStore store,ResidentActor actor,string request,NpcActionKind kind,int seconds)
        {
            string reason="No matching native option.";
            foreach(var option in streets.ConversationActionOptions(actor.Resident.Id,request))
                if(option.Kind==kind && NpcProbeCommandSource.TryAcceptAction(streets,actor.Resident.Id,"native-probe:"+Guid.NewGuid().ToString("N"),
                    new NpcActionDecision {Option=option,DurationSeconds=seconds},out reason))
                    return store.ActiveNpcAction(actor.Resident.Id);
            throw new InvalidOperationException("Native action rejected "+kind+": "+reason);
        }

        private static IEnumerator VerifySimpleActions(StreetPopulation streets,WorldStateStore store,ResidentActor actor)
        {
            var game=GameManager.Instance;
            var wait=Accept(streets,store,actor,"Wait here.",NpcActionKind.Wait,10);
            var held=actor.transform.position;float deadline=Time.realtimeSinceStartup+15;
            NpcActionJob current=wait;
            while(Time.realtimeSinceStartup<deadline)
            {
                current=store.ReadNpcAction(wait.Id);
                if(Vector3.Distance(held,actor.transform.position)>0.2f)
                    throw new InvalidOperationException("Waiting NPC moved away from the agreed place.");
                if(NpcActionExecutor.IsTerminal(current)) break;
                yield return null;
            }
            if(current.Phase!=NpcActionPhase.Completed || current.UpdatedAt<wait.FinishAt)
                throw new InvalidOperationException("Native wait ended early or failed to complete.");
            var completedWait=current;
            AssertOutcomeMemory(store,completedWait,"completed the accepted Wait action","agreed local position");

            var follow=Accept(streets,store,actor,"Follow me.",NpcActionKind.Follow,300);
            var room=game.PlayerEnterExit.Interior;
            var geometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",Private).GetValue(streets);
            var origin=actor.transform.position;bool movedObserver=false;
            foreach(var local in geometry.LocalAnchors)
            {
                var point=room.transform.TransformPoint(local)+Vector3.up;float gap=Vector3.Distance(point,origin);
                if(gap<3 || gap>5 || Math.Abs(point.y-origin.y)>0.3f) continue;
                if(geometry.RouteBetween(room.transform.InverseTransformPoint(origin-Vector3.up),local).Count<2) continue;
                game.PlayerMotor.transform.position=point;Physics.SyncTransforms();movedObserver=true;break;
            }
            if(!movedObserver) throw new InvalidOperationException("No connected public follow observation point.");
            deadline=Time.realtimeSinceStartup+45;
            while(Time.realtimeSinceStartup<deadline && Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)>1.85f)
            {
                current=store.ReadNpcAction(follow.Id);
                if(NpcActionExecutor.IsTerminal(current)) throw new InvalidOperationException("Following ended before arrival: "+current.FailureReason);
                yield return null;
            }
            if(Vector3.Distance(origin,actor.transform.position)<0.5f || Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)>1.85f)
                throw new InvalidOperationException("Following did not physically track the moved player.");
            string reason;
            if(!streets.TryStopConversationAction(actor.Resident.Id,out reason) || store.ReadNpcAction(follow.Id).Phase!=NpcActionPhase.Cancelled)
                throw new InvalidOperationException("Stop did not cancel the physical follow action: "+reason);
            var stopped=store.ReadNpcAction(follow.Id);
            AssertOutcomeMemory(store,stopped,"stopped the accepted Follow action","stopped accepted Follow");

            // Test-owned stock only. Only the explicit disk probe writes its own test slot.
            // This verifies actual handover, not procedural inventory generation.
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            if(!owner || owner.Binding==null) throw new InvalidOperationException("Action actor has no native inventory binding.");
            var item=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);item.stackCount=3;
            owner.Binding.Items.AddItem(item);owner.Capture();
            int sourceBefore=ArrowCount(owner.Binding.Items),playerBefore=ArrowCount(game.PlayerEntity.Items);
            string handoverMessage=actor.Person.NameNPC+" hands you 1 x "+item.LongName+".";
            int messagesBefore=MessageCount(handoverMessage);
            var transfer=Accept(streets,store,actor,"Give me one "+item.LongName+".",NpcActionKind.Transfer,120);
            deadline=Time.realtimeSinceStartup+45;
            while(Time.realtimeSinceStartup<deadline)
            {
                current=store.ReadNpcAction(transfer.Id);
                if(NpcActionExecutor.IsTerminal(current)) break;
                yield return null;
            }
            if(current.Phase!=NpcActionPhase.Completed || ArrowCount(owner.Binding.Items)!=sourceBefore-1 || ArrowCount(game.PlayerEntity.Items)!=playerBefore+1)
                throw new InvalidOperationException("Native handover did not move exactly one real item: "+current.Phase+" "+current.FailureReason);
            if(MessageCount(handoverMessage)!=messagesBefore+1)
                throw new InvalidOperationException("Successful handover needs exactly one native HUD/notebook confirmation with item and quantity.");
            for(int i=0;i<6;i++) yield return null;
            if(MessageCount(handoverMessage)!=messagesBefore+1)
                throw new InvalidOperationException("Routine rejoin repeated the handover confirmation.");
            AssertOutcomeMemory(store,completedWait,"completed the accepted Wait action","agreed local position",false);
            AssertOutcomeMemory(store,stopped,"stopped the accepted Follow action","stopped accepted Follow",false);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_MEMORY_PASS: completed-wait="+completedWait.Id+
                "; cancelled-follow="+stopped.Id+"; attributed-time=pass; own-read=pass; duplicate=blocked");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_FEEDBACK_PASS: confirmed native transfer; exact item and quantity; one HUD/notebook message; no rejoin repetition.");
            yield return new WaitForEndOfFrame();
            string feedbackScreenshot=System.IO.Path.Combine(Application.persistentDataPath,"peopleoftheiliacbay-action-feedback.png");
            ScreenCapture.CaptureScreenshot(feedbackScreenshot);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_FEEDBACK_SCREENSHOT: "+feedbackScreenshot);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SIMPLE_ACTIONS_PASS: timed wait; moved-player follow; cancellation; real one-item handover; no model assent tested.");
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-action-chat-probe")>=0)
                yield return VerifyChatActions(streets,store,actor);
            NpcActionJob inboundTransfer=null;
            yield return NpcInboundTransferProbe.Run(streets,store,actor,value=>inboundTransfer=value);
            yield return VerifyNpcDelivery(streets,store,actor,item.LongName);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-action-disk-probe")>=0)
            {
                if(inboundTransfer==null)throw new InvalidOperationException("Inbound transfer probe did not return its completed action identity.");
                yield return NpcActionDiskProbe.Run(streets,store,actor,transfer.Id,handoverMessage,inboundTransfer.Id);
            }
        }

        private static void AssertOutcomeMemory(WorldStateStore store,NpcActionJob action,
            string expectedOutcome,string query,bool testRetry=true)
        {
            var journal=store.Snapshot().ConversationMemories.Find(memory=>memory.PersonId==action.ResidentId);
            var entries=journal==null?null:journal.Entries.FindAll(entry=>entry.SourceActionId==action.Id);
            if(entries==null || entries.Count!=1 || entries[0].Source!="observed" ||
                entries[0].At!=Math.Floor(action.UpdatedAt) ||
                !entries[0].Observation.Contains(action.ResidentId) ||
                !entries[0].Observation.Contains(expectedOutcome))
                throw new InvalidOperationException("Native action outcome did not leave exactly one attributed, timed journal row: "+action.Id);
            NpcMemoryReadSnapshot read;
            if(!store.TryCaptureNpcMemoryRead(action.ResidentId,query,action.UpdatedAt,2000,out read) ||
                read==null || !read.Text.Contains(expectedOutcome) ||
                !store.IsNpcMemoryReadCurrent(read,action.ResidentId,action.UpdatedAt))
                throw new InvalidOperationException("Native action outcome was not retrievable through the speaker's own memory: "+action.Id);
            if(testRetry && store.TryRememberNpcActionOutcome(action.Id,store.ReadNpcAction(action.Id).Revision,
                "retry date","retry place","repeat outcome"))
                throw new InvalidOperationException("Repeated native action outcome created a second memory: "+action.Id);
            if(testRetry)
            {
                var repeated=store.Snapshot().ConversationMemories.Find(memory=>memory.PersonId==action.ResidentId);
                if(repeated==null || repeated.Entries.FindAll(entry=>entry.SourceActionId==action.Id).Count!=1)
                    throw new InvalidOperationException("Action outcome deduplication lost its one-row bound: "+action.Id);
            }
        }
        private static IEnumerator VerifyNpcDelivery(StreetPopulation streets,WorldStateStore store,ResidentActor source,string itemName)
        {
            var candidates=new List<ResidentActor>(UnityEngine.Object.FindObjectsOfType<ResidentActor>());
            candidates.Sort((a,b)=>Vector3.Distance(source.transform.position,a.transform.position).CompareTo(Vector3.Distance(source.transform.position,b.transform.position)));
            NpcActionJob job=null;ResidentActor recipient=null;string reason="No eligible nearby managed recipient.";
            foreach(var candidate in candidates)
            {
                if(candidate==source || !candidate.Person || string.IsNullOrWhiteSpace(candidate.Person.NameNPC)) continue;
                string request="Please give "+candidate.Person.NameNPC+" 2 "+itemName+".";
                var options=streets.ConversationActionOptions(source.Resident.Id,request);
                var option=options.Find(o=>o.Kind==NpcActionKind.Transfer && o.Target.PersonId==candidate.Resident.Id && o.Target.Quantity==2);
                if(option==null) continue;
                if(!NpcProbeCommandSource.TryAcceptAction(streets,source.Resident.Id,"fixture-npc-delivery:"+Guid.NewGuid().ToString("N"),
                    new NpcActionDecision {Option=option,DurationSeconds=120},out reason)) continue;
                recipient=candidate;job=store.ActiveNpcAction(source.Resident.Id);break;
            }
            if(job==null) throw new InvalidOperationException("Native NPC delivery fixture could not resolve an executable recipient (loaded actors="+candidates.Count+"): "+reason);
            var sourceOwner=source.GetComponent<NativeNpcInventoryOwner>();var targetOwner=recipient.GetComponent<NativeNpcInventoryOwner>();
            int sourceBefore=ArrowCount(sourceOwner.Binding.Items),targetBefore=ArrowCount(targetOwner.Binding.Items);
            string expected=source.Person.NameNPC+" hands "+recipient.Person.NameNPC+" 2 x "+itemName+".";
            int feedbackBefore=MessageCount(expected);
            float deadline=Time.realtimeSinceStartup+65;
            NpcActionJob current;
            do {yield return null;current=store.ReadNpcAction(job.Id);}
            while(!NpcActionExecutor.IsTerminal(current) && Time.realtimeSinceStartup<deadline);
            if(current.Phase!=NpcActionPhase.Completed || ArrowCount(sourceOwner.Binding.Items)!=sourceBefore-2 ||
                ArrowCount(targetOwner.Binding.Items)!=targetBefore+2 || MessageCount(expected)!=feedbackBefore+1)
                throw new InvalidOperationException("Native NPC delivery failed: "+current.Phase+" "+current.FailureReason);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_DELIVERY_PASS: resolved managed recipient; physical approach; two actual items; both inventories changed; one factual HUD/notebook confirmation.");
        }
        private static IEnumerator VerifyChatActions(StreetPopulation streets,WorldStateStore store,ResidentActor actor)
        {
            var host=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();var game=GameManager.Instance;
            var busy=typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy",Private);
            var submit=typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText",Private);
            var read=typeof(PeopleOfTheIliacBayMod).GetMethod("ReadConversationHistory",BindingFlags.Static|BindingFlags.NonPublic);
            if(!host || !(bool)typeof(PeopleOfTheIliacBayMod).GetField("localDialogueEnabled",Private).GetValue(host))
                throw new InvalidOperationException("Chat action acceptance requires the real enabled local dialogue client.");
            bool qwenWillingnessOptIn=HasQwenWillingnessOptIn();
            if(qwenWillingnessOptIn &&
                ((bool)typeof(PeopleOfTheIliacBayMod).GetField("decisionPlannerEnabled",Private).GetValue(host) ||
                 typeof(PeopleOfTheIliacBayMod).GetField("decisionRoutingSession",Private).GetValue(host)!=null ||
                 Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-goal-extraction")>=0))
                throw new InvalidOperationException("Qwen willingness action-chat probe requires planner off, no D3 session, and goal extraction off to reach semantic consent callsites.");
            try
            {
                game.TalkManager.TalkToMobileNPC(actor.Person);
                if(!(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow))
                {
                    DaggerfallUI.Instance.PopToHUD();
                    typeof(TalkManager).GetField("reactionToPlayer",Private).SetValue(game.TalkManager,90);
                    typeof(TalkManager).GetField("alreadyRejectedOnce",Private).SetValue(game.TalkManager,false);
                    typeof(TalkManager).GetMethod("TalkToNpc",Private).Invoke(game.TalkManager,null);
                }
                yield return null;yield return null;
                var window=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                if(window==null || (string)typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId",Private).GetValue(host)!=actor.Resident.Id)
                    throw new InvalidOperationException("Native chat opened the wrong action speaker.");
                float deadline=Time.realtimeSinceStartup+45;
                while((bool)busy.GetValue(host) && Time.realtimeSinceStartup<deadline) yield return null;
                if((bool)busy.GetValue(host)) throw new InvalidOperationException("Initial native greeting did not finish.");
                // Controlled friendly fixture; preserve the store's accumulated offset
                // and do not bypass the model's willingness decision.
                SetFriendlyChatFixtureBaseline(store,actor,host,window,"initial-open");
                var before=store.LatestNpcAction(actor.Resident.Id);var position=actor.transform.position;
                var singleConsent=CaptureQwenConsent(host);
                string singleTraceStage;
                long singleTraceBefore=LastSemanticActionTrace(out singleTraceStage);
                submit.Invoke(host,new object[]{window,"Would you mind waiting here?"});
                deadline=Time.realtimeSinceStartup+60;
                while((bool)busy.GetValue(host) && Time.realtimeSinceStartup<deadline) yield return null;
                AssertQwenAcceptedOnce(host,singleConsent,"single-wait");
                AssertSemanticActionTrace(singleConsent,singleTraceBefore,"action-single","single-wait");
                var job=store.ActiveNpcAction(actor.Resident.Id);
                string transcript=(string)read.Invoke(null,new object[]{window});
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_CHAT_RESPONSE: "+transcript);
                if((bool)busy.GetValue(host) || job==null || job.Id==before.Id || job.Kind!=NpcActionKind.Wait || job.Phase!=NpcActionPhase.Accepted)
                    throw new InvalidOperationException("Friendly native chat did not accept the wait action; inspect response.");
                if(Vector3.Distance(position,actor.transform.position)>0.05f)
                    throw new InvalidOperationException("NPC acted before the conversation closed.");
                DaggerfallUI.Instance.PopToHUD();yield return null;yield return null;
                deadline=Time.realtimeSinceStartup+5;
                while(store.ReadNpcAction(job.Id).Phase==NpcActionPhase.Accepted && Time.realtimeSinceStartup<deadline) yield return null;
                if(store.ReadNpcAction(job.Id).Phase!=NpcActionPhase.Executing)
                    throw new InvalidOperationException("Accepted native-chat wait did not begin after closing.");
                game.TalkManager.TalkToMobileNPC(actor.Person);yield return null;yield return null;
                window=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                if(window==null) throw new InvalidOperationException("Could not reopen the action speaker.");
                deadline=Time.realtimeSinceStartup+45;
                while((bool)busy.GetValue(host) && Time.realtimeSinceStartup<deadline) yield return null;
                if((bool)busy.GetValue(host)) throw new InvalidOperationException("Reopened action greeting timed out.");
                SetFriendlyChatFixtureBaseline(store,actor,host,window,"after-first-wait-reopen");
                submit.Invoke(host,new object[]{window,"You can stop now."});
                deadline=Time.realtimeSinceStartup+60;
                while((bool)busy.GetValue(host) && Time.realtimeSinceStartup<deadline) yield return null;
                if((bool)busy.GetValue(host) || store.ReadNpcAction(job.Id).Phase!=NpcActionPhase.Cancelled)
                    throw new InvalidOperationException("Native chat stop did not cancel the accepted action.");
                transcript=(string)read.Invoke(null,new object[]{window});
                if(!transcript.Contains("Would you mind waiting here?") || !transcript.Contains("You can stop now."))
                    throw new InvalidOperationException("Reopened native chat lost its action transcript.");
                // Historical/negative wording must not create a new action or an offer.
                yield return SubmitAndWait(host,window,"Yesterday you offered to follow me. I do not want company now.");
                var offerField=typeof(PeopleOfTheIliacBayMod).GetField("pendingConversationAction",Private);
                if(store.LatestNpcAction(actor.Resident.Id).Id!=job.Id || offerField.GetValue(host)!=null)
                    throw new InvalidOperationException("Historical negative chat created an action or offer.");
                yield return SubmitAndWait(host,window,"I wish I had someone to walk with me around here.");
                if(offerField.GetValue(host)==null || store.LatestNpcAction(actor.Resident.Id).Id!=job.Id)
                    throw new InvalidOperationException("Friendly companionship fixture did not offer a consent-gated action.");
                var offerAssentConsent=CaptureQwenConsent(host);
                yield return SubmitAndWait(host,window,"Yes, please.");
                AssertQwenAcceptedOnce(host,offerAssentConsent,"pending-offer-assent");
                var proposed=store.ActiveNpcAction(actor.Resident.Id);
                if(proposed==null || proposed.Id==job.Id || proposed.Kind!=NpcActionKind.Follow || proposed.Phase!=NpcActionPhase.Accepted)
                    throw new InvalidOperationException("Explicit assent did not accept the current speaker's follow offer.");
                yield return SubmitAndWait(host,window,"Stop following me.");
                if(store.ReadNpcAction(proposed.Id).Phase!=NpcActionPhase.Cancelled)
                    throw new InvalidOperationException("Chat cancellation did not cancel the accepted proposal.");
                yield return VerifyChatTask(streets,store,actor,host,window);
                // Run the positive acceptance/completion case before the forced
                // single-action controls. In particular, don't put an identical
                // previously declined chain immediately before this request.
                game.TalkManager.TalkToMobileNPC(actor.Person);yield return null;yield return null;
                window=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                if(window==null || game.TalkManager.MobileNPC!=actor.Person ||
                    (string)typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId",Private).GetValue(host)!=actor.Resident.Id)
                    throw new InvalidOperationException("Could not reopen the action speaker for isolated fallback controls.");
                deadline=Time.realtimeSinceStartup+45;
                while((bool)busy.GetValue(host) && Time.realtimeSinceStartup<deadline) yield return null;
                if((bool)busy.GetValue(host)) throw new InvalidOperationException("Fallback-control speaker greeting timed out.");
                SetFriendlyChatFixtureBaseline(store,actor,host,window,"before-fallback-controls");
                yield return VerifySingleFallbackControls(streets,store,actor,host,window);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_CHAT_PASS: native window; correct speaker; varied wording; wait held during chat and execution after close; reopened-history cancellation; historical negative creates no action/offer; natural follow proposal requires explicit assent; cancellation of proposal; qwenWillingnessOptIn="+
                    qwenWillingnessOptIn.ToString().ToLowerInvariant()+"; typedCancellation=skipped-unverified.");
            }
            finally {DaggerfallUI.Instance.PopToHUD();}
        }

        // Controlled decomposition-NONE boundary with the real willingness model.
        // This bypasses top-level routing/category choice deliberately, not consent
        // or execution checks. The unchanged multi-effect request must not become
        // a partially accepted single action.
        private static IEnumerator VerifySingleFallbackControls(StreetPopulation streets,WorldStateStore store,
            ResidentActor actor,PeopleOfTheIliacBayMod host,DaggerfallTalkWindow window)
        {
            var single=typeof(PeopleOfTheIliacBayMod).GetMethod("TrySemanticActionSingleConversation",Private);
            var read=typeof(PeopleOfTheIliacBayMod).GetMethod("ReadConversationHistory",BindingFlags.Static|BindingFlags.NonPublic);
            var busy=typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy",Private);
            var session=typeof(PeopleOfTheIliacBayMod).GetField("memorySession",Private);
            var offer=typeof(PeopleOfTheIliacBayMod).GetField("pendingConversationAction",Private);
            string[] controls={
                "I do not want you to wait here or follow me.",
                "Yesterday someone said, 'Wait here.' I am only reporting what they said.",
                "If I needed company tomorrow, I might ask you to follow me, but I am not asking now.",
                "Imagine someone asking you to wait here. That is not a request from me.",
                "Wait here for 10 seconds, then follow me for 10 seconds."
            };
            for(int index=0;index<controls.Length;index++)
            {
                string text=controls[index];
                if(NpcActionRouting.AfterDecomposition(NpcActionDecomposition.Parse("NONE",text))!=NpcActionConversationRoute.Single)
                    throw new InvalidOperationException("Controlled NONE decomposition did not route to single willingness.");
                var prior=store.LatestNpcAction(actor.Resident.Id);
                string priorId=prior==null?null:prior.Id;
                NpcActionPhase? priorPhase=prior==null?(NpcActionPhase?)null:prior.Phase;
                bool priorReturning=prior!=null && prior.ReturningToRoutine;
                int boundSession=(int)session.GetValue(host);
                var options=new List<NpcActionOption>();
                foreach(var option in streets.ConversationActionCandidates(actor.Resident.Id,text))
                    if(option!=null && (option.Kind==NpcActionKind.Wait || option.Kind==NpcActionKind.Follow))
                        options.Add(option);
                var activeBefore=store.ActiveNpcAction(actor.Resident.Id);
                if(options.Count==0 || (bool)busy.GetValue(host) || offer.GetValue(host)!=null ||
                    activeBefore!=null && !NpcActionExecutor.IsTerminal(activeBefore) || store.ActiveNpcTask(actor.Resident.Id)!=null)
                    throw new InvalidOperationException("Single-action fallback control lacks live options or an idle chat.");
                string history=(string)read.Invoke(null,new object[]{window});
                string traceStage,traceClassification;
                long traceBefore=SemanticActionModelCaptureProbe.LastTrace(out traceStage,out traceClassification);
                long requestRevision=(long)typeof(PeopleOfTheIliacBayMod).GetField("conversationContextRequestRevision",Private).GetValue(host)+1;
                var snapshot=host.CaptureConversationContext(window,text,requestRevision);
                busy.SetValue(host,true);
                bool started=(bool)single.Invoke(host,new object[]{window,text,history,snapshot,options});
                float deadline=Time.realtimeSinceStartup+60;
                while((bool)busy.GetValue(host) && Time.realtimeSinceStartup<deadline)yield return null;
                var latest=store.LatestNpcAction(actor.Resident.Id);
                string latestId=latest==null?null:latest.Id;
                NpcActionPhase? latestPhase=latest==null?(NpcActionPhase?)null:latest.Phase;
                string transcript=(string)read.Invoke(null,new object[]{window});
                long traceAfter=SemanticActionModelCaptureProbe.LastTrace(out traceStage,out traceClassification);
                string verdict=traceClassification==null?string.Empty:traceClassification.Trim();
                bool safeVerdict=verdict=="NONE" || verdict=="DECLINE" || verdict=="NEGATED" ||
                    verdict=="REPORTED" || verdict=="HYPOTHETICAL" || verdict=="CONDITIONAL";
                var activeAfter=store.ActiveNpcAction(actor.Resident.Id);
                if(!started || (bool)busy.GetValue(host) || priorId!=latestId || priorPhase!=latestPhase ||
                    (latest!=null && latest.ReturningToRoutine)!=priorReturning ||
                    activeAfter!=null && !NpcActionExecutor.IsTerminal(activeAfter) || store.ActiveNpcTask(actor.Resident.Id)!=null ||
                    !transcript.Replace("\r\n","\n").Contains("Player: "+text+"\nNPC: ") ||
                    traceAfter!=traceBefore+1 || traceStage!="action-single" || !safeVerdict ||
                    boundSession!=(int)session.GetValue(host) || offer.GetValue(host)!=null ||
                    DaggerfallUI.UIManager.TopWindow!=window || GameManager.Instance.TalkManager.MobileNPC!=actor.Person ||
                    (string)typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId",Private).GetValue(host)!=actor.Resident.Id)
                    throw new InvalidOperationException("Single-action fallback accepted an excluded or partial request at control "+index+".");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_FALLBACK_CONTROL_PASS: index="+index+
                    "; controlledDecomposition=none; realWillingness=true; verdict="+verdict+"; newAction=false; newTask=false.");
            }
        }

        private static IEnumerator VerifyChatTask(StreetPopulation streets,WorldStateStore store,ResidentActor actor,
            PeopleOfTheIliacBayMod host,DaggerfallTalkWindow window)
        {
            var initialTaskConsent=CaptureQwenConsent(host);
            string initialTaskTraceStage;
            long initialTaskTraceBefore=LastSemanticActionTrace(out initialTaskTraceStage);
            yield return SubmitAndWait(host,window,"Wait here for 10 seconds, then follow me for 10 seconds.");
            AssertQwenAcceptedOnce(host,initialTaskConsent,"ordered-task-10s");
            AssertSemanticActionTrace(initialTaskConsent,initialTaskTraceBefore,"action-step-2","ordered-task-10s");
            var task=store.ActiveNpcTask(actor.Resident.Id);
            if(task==null || task.Steps.Count!=2 || task.Steps[0].Kind!=NpcActionKind.Wait || task.Steps[1].Kind!=NpcActionKind.Follow ||
                task.Steps[0].DurationSeconds!=10 || task.Steps[1].DurationSeconds!=10 || task.CurrentStepIndex!=0)
                throw new InvalidOperationException("Native chat did not accept the exact two-step task.");
            string id=task.Id;var first=store.ReadNpcAction(id+":step:0");
            if(first==null || first.Phase!=NpcActionPhase.Accepted || store.ReadNpcAction(id+":step:1")!=null)
                throw new InvalidOperationException("Task ran before chat closed or dispatched multiple steps.");
            DaggerfallUI.Instance.PopToHUD();
            float until=Time.realtimeSinceStartup+65;bool observedFirst=false,observedSecond=false;
            do
            {
                yield return null;task=store.ReadNpcTask(id);first=store.ReadNpcAction(id+":step:0");
                var second=store.ReadNpcAction(id+":step:1");
                observedFirst|=first.Phase==NpcActionPhase.Executing;
                if(second!=null)
                {
                    if(first.Phase!=NpcActionPhase.Completed || string.IsNullOrEmpty(first.CompletedEffectId) || second.AcceptedAt<first.UpdatedAt)
                        throw new InvalidOperationException("Second task step started without confirmed first completion.");
                    observedSecond|=!NpcActionExecutor.IsTerminal(second);
                }
            }while(!NpcTaskContract.IsTerminal(task) && Time.realtimeSinceStartup<until);
            if(task.Phase!=NpcTaskPhase.Completed || !observedFirst || !observedSecond ||
                string.IsNullOrEmpty(task.Steps[0].CompletedEffectId) || string.IsNullOrEmpty(task.Steps[1].CompletedEffectId))
                throw new InvalidOperationException("Native task did not complete sequentially: "+task.Phase+" "+task.FailureReason);
            GameManager.Instance.TalkManager.TalkToMobileNPC(actor.Person);yield return null;yield return null;
            window=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            if(window==null)throw new InvalidOperationException("Could not reopen completed task speaker.");
            var busy=typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy",Private);
            until=Time.realtimeSinceStartup+45;while((bool)busy.GetValue(host) && Time.realtimeSinceStartup<until)yield return null;
            if((bool)busy.GetValue(host))throw new InvalidOperationException("Task speaker greeting timed out.");
            SetFriendlyChatFixtureBaseline(store,actor,host,window,"after-completed-task-reopen");
            var replacementTaskConsent=CaptureQwenConsent(host);
            string replacementTraceStage;
            long replacementTraceBefore=LastSemanticActionTrace(out replacementTraceStage);
            yield return SubmitAndWait(host,window,"Wait here for 30 seconds, then follow me for 30 seconds.");
            AssertQwenAcceptedOnce(host,replacementTaskConsent,"ordered-task-30s");
            AssertSemanticActionTrace(replacementTaskConsent,replacementTraceBefore,"action-step-2","ordered-task-30s");
            var cancelled=store.ActiveNpcTask(actor.Resident.Id);
            if(cancelled==null || cancelled.Id==id)throw new InvalidOperationException("Second task was not accepted after completed recovery.");
            if(cancelled.Steps.Count!=2 || cancelled.Steps[0].Kind!=NpcActionKind.Wait ||
                cancelled.Steps[1].Kind!=NpcActionKind.Follow || cancelled.Steps[0].DurationSeconds!=30 ||
                cancelled.Steps[1].DurationSeconds!=30 || cancelled.CurrentStepIndex!=0)
                throw new InvalidOperationException("Second task did not preserve the exact ordered 30-second Wait then Follow request.");
            yield return SubmitAndWait(host,window,"Stop following me.");
            if(store.ReadNpcTask(cancelled.Id).Phase!=NpcTaskPhase.Cancelled || store.ReadNpcAction(cancelled.Id+":step:1")!=null)
                throw new InvalidOperationException("Stop did not cancel the entire pending task.");
            DaggerfallUI.Instance.PopToHUD();
            until=Time.realtimeSinceStartup+3;while(Time.realtimeSinceStartup<until)yield return null;
            if(store.ActiveNpcTask(actor.Resident.Id)!=null || store.ReadNpcAction(cancelled.Id+":step:1")!=null)
                throw new InvalidOperationException("Cancelled task resumed after conversation closed.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_TASK_PASS: actual chat; exact ordered durations; held during conversation; observed completion before next dispatch; both effects persisted; whole-task cancellation prevents queued follow.");
        }
        private static IEnumerator SubmitAndWait(PeopleOfTheIliacBayMod host,DaggerfallTalkWindow window,string message)
        {
            var busy=typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy",Private);
            typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText",Private).Invoke(host,new object[]{window,message});
            float deadline=Time.realtimeSinceStartup+60;
            while((bool)busy.GetValue(host) && Time.realtimeSinceStartup<deadline)yield return null;
            string transcript=(string)typeof(PeopleOfTheIliacBayMod).GetMethod("ReadConversationHistory",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{window});
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_CHAT_CASE: "+message+"\n"+transcript);
            if((bool)busy.GetValue(host) || !transcript.Contains(message))
                throw new InvalidOperationException("Live chat submission did not finish: "+message);
        }
        private sealed class QwenConsentCheckpoint
        {
            internal readonly long DispatchCount,CompletionCount;
            internal QwenConsentCheckpoint(long dispatchCount,long completionCount)
            {DispatchCount=dispatchCount;CompletionCount=completionCount;}
        }
        private static bool HasQwenWillingnessOptIn()
        {return Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-qwen-willingness")>=0;}
        private static QwenConsentCheckpoint CaptureQwenConsent(PeopleOfTheIliacBayMod host)
        {
            if(!HasQwenWillingnessOptIn())return null;
            long dispatch=Convert.ToInt64(typeof(PeopleOfTheIliacBayMod).GetField("qwenWillingnessDispatchCount",Private).GetValue(host));
            long completion=Convert.ToInt64(typeof(PeopleOfTheIliacBayMod).GetField("qwenWillingnessCompletionCount",Private).GetValue(host));
            return new QwenConsentCheckpoint(dispatch,completion);
        }
        private static void AssertQwenAcceptedOnce(PeopleOfTheIliacBayMod host,QwenConsentCheckpoint before,string stage)
        {
            if(before==null)return;
            long dispatch=Convert.ToInt64(typeof(PeopleOfTheIliacBayMod).GetField("qwenWillingnessDispatchCount",Private).GetValue(host))-before.DispatchCount;
            long completion=Convert.ToInt64(typeof(PeopleOfTheIliacBayMod).GetField("qwenWillingnessCompletionCount",Private).GetValue(host))-before.CompletionCount;
            string choice=(string)typeof(PeopleOfTheIliacBayMod).GetField("qwenWillingnessLastChoice",Private).GetValue(host);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_CHAT_QWEN_CONSENT: stage="+stage+"; dispatch="+dispatch+
                "; completion="+completion+"; rawChoice="+(choice??"<null>")+".");
            if(dispatch!=1 || completion!=1 || !string.Equals(choice,"ACCEPT",StringComparison.Ordinal))
                throw new InvalidOperationException("Explicit Qwen willingness did not accept the native "+stage+
                    " exactly once: dispatch="+dispatch+"; completion="+completion+"; rawChoice="+(choice??"<null>")+".");
        }
        private static void AssertSemanticActionTrace(QwenConsentCheckpoint before,long traceBefore,string expectedStage,string caseName)
        {
            if(before==null)return;
            string stage,classification;
            long traceAfter=SemanticActionModelCaptureProbe.LastTrace(out stage,out classification);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_CHAT_QWEN_ROUTE: case="+caseName+"; traceDelta="+
                (traceAfter-traceBefore)+"; lastStage="+(stage??"<null>")+"; lastClassification="+(classification??"<null>")+".");
            if(traceAfter<=traceBefore || !string.Equals(stage,expectedStage,StringComparison.Ordinal))
                throw new InvalidOperationException("Explicit Qwen chat did not traverse the expected semantic callsite for "+
                    caseName+": expected="+expectedStage+"; observed="+(stage??"<null>")+"; traceDelta="+(traceAfter-traceBefore)+".");
        }
        private static long LastSemanticActionTrace(out string stage)
        {
            string classification;
            return SemanticActionModelCaptureProbe.LastTrace(out stage,out classification);
        }
        private static void SetFriendlyChatFixtureBaseline(WorldStateStore store,ResidentActor actor,
            PeopleOfTheIliacBayMod host,DaggerfallTalkWindow window,string stage)
        {
            // BeginGroundedConversation recaptures TalkManager's native reaction on
            // every new session; restore only the controlled fixture baseline here,
            // retaining the resident's accumulated offset from earlier turns.
            var manager=GameManager.Instance.TalkManager;
            var reaction=typeof(TalkManager).GetField("reactionToPlayer",Private);
            var nativeDisposition=typeof(PeopleOfTheIliacBayMod).GetField("conversationNativeDisposition",Private);
            var person=typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId",Private);
            var currentDisposition=typeof(PeopleOfTheIliacBayMod).GetMethod("CurrentDialogueDisposition",Private);
            if(store==null || !actor || actor.Resident==null || host==null || window==null ||
                DaggerfallUI.UIManager.TopWindow!=window || manager.CurrentNPCType!=TalkManager.NPCType.Mobile ||
                manager.MobileNPC!=actor.Person || person==null || reaction==null || nativeDisposition==null ||
                currentDisposition==null || (string)person.GetValue(host)!=actor.Resident.Id)
                throw new InvalidOperationException("Friendly disposition fixture is not bound to the current action speaker/window.");

            int savedOffset=store.ConversationDisposition(actor.Resident.Id);
            store.SetConversationBaseline(actor.Resident.Id,90,
                DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            int expectedReaction=ConversationEffects.Reaction(90,savedOffset);
            nativeDisposition.SetValue(host,90);
            reaction.SetValue(manager,expectedReaction);
            int nativeReaction=(int)currentDisposition.Invoke(host,null);
            int savedReaction=int.MinValue;
            if(store.ConversationDisposition(actor.Resident.Id)!=savedOffset || nativeReaction!=expectedReaction ||
                !store.TryGetActualConversationDisposition(actor.Resident.Id,out savedReaction) || savedReaction!=expectedReaction ||
                (int)nativeDisposition.GetValue(host)!=90)
                throw new InvalidOperationException("Friendly disposition fixture failed: expected baseline=90, preservedOffset="+
                    savedOffset+", effective="+expectedReaction+", native="+nativeReaction+", store="+savedReaction+".");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_FIXTURE_FRIENDLY: stage="+stage+"; baseline=90; preservedOffset="+
                savedOffset+"; effective="+expectedReaction+"; native="+nativeReaction+".");
        }
        private static int MessageCount(string expected)
        {
            int count=0;
            foreach(var tokens in GameManager.Instance.PlayerEntity.Notebook.GetMessages())
                foreach(var line in DaggerfallConnect.Arena2.TextFile.GetTokenLines(tokens))
                    if(line==expected) count++;
            return count;
        }
        private static int ArrowCount(ItemCollection items)
        {
            int count=0;for(int i=0;i<items.Count;i++)
            {var item=items.GetItem(i);if(item.ItemGroup==ItemGroups.Weapons && item.TemplateIndex==(int)Weapons.Arrow) count+=item.stackCount;}
            return count;
        }
    }
}
