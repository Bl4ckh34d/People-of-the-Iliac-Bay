using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class KnowledgeBrowsingProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string PendingRowName = "PeopleOfTheIliacBayPendingEphemeral";
        private const string UnavailableRowName = "PeopleOfTheIliacBayUnavailableReply";
        public static IEnumerator ApproachSpeaker(PeopleOfTheIliacBayMod adapter, StreetPopulation streets)
        {
            var game=GameManager.Instance;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var snapshot=(WorldState)adapter.GetSaveData();
            var population=snapshot.Settlements.Find(t=>t.SettlementId==directory.MapID);
            var adults=population.Households.SelectMany(h=>h.Residents).Where(r=>r.IsAlive && r.AgeGroup==ResidentAgeGroup.Adult && !r.IsQuestProtected).ToList();
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            foreach(var resident in adults)
            {
                var state=streets.GetState(resident.Id,now);
                if(state.Activity!=Activity.Travel) continue;
                var geometry=new TownGeometry(directory,game.StreamingWorld.GetCurrentCityNavigation(),false);
                game.PlayerMotor.transform.position=geometry.ScenePoint(state.Position)+Vector3.up+Vector3.right*2;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KNOWLEDGE_FIXTURE: approached simulated traveler "+resident.Id);
                yield return new WaitForSecondsRealtime(2);
                break;
            }
            if(UnityEngine.Object.FindObjectsOfType<ResidentActor>().Count(p=>p.Person && p.Resident.IsAlive)>=2) yield break;
            // Dialogue-only fallback for an empty streamed view. These bodies are
            // owned by the disposable test process and are never written to saves.
            foreach(var resident in adults.Where(r=>!UnityEngine.Object.FindObjectsOfType<ResidentActor>().Any(p=>p.Resident.Id==r.Id)).Take(2))
            {
                var body=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject);
                var actor=body.AddComponent<ResidentActor>();
                actor.Initialize(resident,1,game.StreamingWorld.GetCurrentCityNavigation());
                actor.Place(game.PlayerMotor.transform.position+game.PlayerMotor.transform.forward*3+Vector3.right*2,Vector3.forward);
                body.SetActive(true); // A blocked test placement must not hide the dialogue fixture.
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KNOWLEDGE_FIXTURE: temporary dialogue body "+resident.Id);
            }
            yield return new WaitForSecondsRealtime(0.5f);
        }
        private static IEnumerable<TalkManager.ListItem> Leaves(IEnumerable<TalkManager.ListItem> items)
        {
            foreach(var item in items)
                if(item.type == TalkManager.ListItemType.ItemGroup) {
                    foreach(var child in Leaves(item.listChildItems ?? new List<TalkManager.ListItem>())) yield return child;
                }
                else if(item.questionType == TalkManager.QuestionType.LocalBuilding && item.questID == 0) yield return item;
        }
        private static HashSet<int> Discovered(List<TalkManager.ListItem> places)
        { return new HashSet<int>(places.Where(t => GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(t.buildingKey)).Select(t => t.buildingKey)); }
        private static void Reaction(TalkManager manager, int value)
        {
            typeof(TalkManager).GetField("reactionToPlayer_0_1_2",Private).SetValue(manager,value);
            typeof(TalkManager).GetField("lastToneIndex",Private).SetValue(manager,1);
        }

        // Wait for a finalized pair bound to this exact submitted turn. The delayed
        // pending indicator is presentation-only and can change Count before reply.
        internal static IEnumerator SubmitAndWait(PeopleOfTheIliacBayMod adapter,DaggerfallTalkWindow window,
            MethodInfo submit,ListBox transcript,string text,Action<string> completed,float timeoutSeconds=120f)
        {
            if(adapter==null || window==null || submit==null || transcript==null || string.IsNullOrWhiteSpace(text) || completed==null)
                throw new ArgumentException("A native dialogue submission requires its live host, window, transcript, text and completion callback.");
            Type hostType=typeof(PeopleOfTheIliacBayMod);
            FieldInfo clientField=hostType.GetField("localDialogue",Private);
            FieldInfo busyField=hostType.GetField("conversationBusy",Private);
            FieldInfo snapshotField=hostType.GetField("conversationContextCurrentSnapshot",Private);
            FieldInfo capturedTextField=hostType.GetField("conversationContextCapturedPlayerText",Private);
            FieldInfo stageField=hostType.GetField("conversationContextIssuedStageToken",Private);
            PropertyInfo requestCount=typeof(LocalDialogueClient).GetProperty("ReplyRequestCount",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            PropertyInfo completionCount=typeof(LocalDialogueClient).GetProperty("ReplyCompletionCount",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(clientField==null || busyField==null || snapshotField==null || capturedTextField==null || stageField==null ||
                requestCount==null || completionCount==null)
                throw new InvalidOperationException("Dialogue wait could not find current-turn or client completion observations.");
            var client=clientField.GetValue(adapter) as LocalDialogueClient;
            if(client==null) throw new InvalidOperationException("Dialogue wait has no shared LocalDialogueClient.");
            var prior=snapshotField.GetValue(adapter) as ConversationContextSnapshot;
            long priorRevision=prior==null || prior.Binding==null ? -1 : prior.Binding.RequestRevision;
            string priorSession=prior==null || prior.Binding==null ? null : prior.Binding.SessionId;
            int priorQuestions=CountFinalQuestions(transcript,text);
            long requestsBefore=Convert.ToInt64(requestCount.GetValue(client,null));
            long completionsBefore=Convert.ToInt64(completionCount.GetValue(client,null));
            submit.Invoke(adapter,new object[]{window,text});

            float deadline=Time.realtimeSinceStartup+Mathf.Clamp(timeoutSeconds,1f,300f);
            int questionIndex=-1,answerIndex=-1;
            while(Time.realtimeSinceStartup<deadline)
            {
                var current=snapshotField.GetValue(adapter) as ConversationContextSnapshot;
                bool currentTurn=current!=null && current.Binding!=null && current.Binding.RequestRevision>priorRevision &&
                    (priorSession==null || string.Equals(current.Binding.SessionId,priorSession,StringComparison.Ordinal));
                bool pair=CountFinalQuestions(transcript,text)==priorQuestions+1 &&
                    TryFindFinalReplyPair(transcript,text,out questionIndex,out answerIndex);
                bool idle=!client.IsBusy && !Convert.ToBoolean(busyField.GetValue(adapter)) && Convert.ToInt64(stageField.GetValue(adapter))==0;
                bool captured=string.Equals(capturedTextField.GetValue(adapter) as string,text,StringComparison.Ordinal);
                if(currentTurn && pair && idle && captured) break;
                yield return null;
            }

            var final=snapshotField.GetValue(adapter) as ConversationContextSnapshot;
            long requestsAfter=Convert.ToInt64(requestCount.GetValue(client,null));
            long completionsAfter=Convert.ToInt64(completionCount.GetValue(client,null));
            if(final==null || final.Binding==null || final.Binding.RequestRevision<=priorRevision ||
                (priorSession!=null && !string.Equals(final.Binding.SessionId,priorSession,StringComparison.Ordinal)) ||
                !string.Equals(capturedTextField.GetValue(adapter) as string,text,StringComparison.Ordinal) ||
                client.IsBusy || Convert.ToBoolean(busyField.GetValue(adapter)) || Convert.ToInt64(stageField.GetValue(adapter))!=0 ||
                CountFinalQuestions(transcript,text)!=priorQuestions+1 ||
                !TryFindFinalReplyPair(transcript,text,out questionIndex,out answerIndex))
                throw new InvalidOperationException("Native dialogue turn did not settle as one current finalized pair; text="+text+
                    "; busy="+Convert.ToBoolean(busyField.GetValue(adapter))+"; clientBusy="+client.IsBusy+
                    "; stage="+Convert.ToInt64(stageField.GetValue(adapter))+"; requests="+requestsBefore+"->"+requestsAfter+
                    "; completions="+completionsBefore+"->"+completionsAfter+".");
            completed(transcript.GetItem(answerIndex).textLabel.Text);
        }

        public static IEnumerator Run(PeopleOfTheIliacBayMod adapter,DaggerfallTalkWindow window,MethodInfo submit,ListBox transcript)
        {
            var manager=GameManager.Instance.TalkManager;
            var places=Leaves(manager.ListTopicLocation).Distinct().ToList();
            var groups=manager.ListTopicLocation.Where(t=>t.type==TalkManager.ListItemType.ItemGroup).ToList();
            var inns=groups.First(g=>g.caption.IndexOf("Tavern",StringComparison.OrdinalIgnoreCase)>=0);
            var innPlaces=Leaves(inns.listChildItems).Where(t=>!GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(t.buildingKey)).Take(3).ToList();
            var temples=groups.First(g=>g.caption.IndexOf("temple",StringComparison.OrdinalIgnoreCase)>=0);
            var temple=Leaves(temples.listChildItems).First(t=>!GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(t.buildingKey));
            var shopGroup=groups.First(g=>g.caption.IndexOf("store",StringComparison.OrdinalIgnoreCase)>=0);
            var shop=Leaves(shopGroup.listChildItems).First();
            if(innPlaces.Count<3) throw new InvalidOperationException("Knowledge fixture needs three undiscovered inns.");
            if(Leaves(inns.listChildItems).Any(item=>object.ReferenceEquals(item,shop)))
                throw new InvalidOperationException("Browse fixture requires an outside-group shop distinct from the tavern leaves.");
            VerifyBoundCandidateDedup(innPlaces,shop);
            var original=places.ToDictionary(t=>t,t=>t.npcKnowledgeAboutItem);
            var mapEnabled=typeof(PeopleOfTheIliacBayMod).GetField("conversationMapRequestsEnabled",Private);
            bool originalMap=(bool)mapEnabled.GetValue(adapter);
            string initialPerson=typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId",Private).GetValue(adapter) as string;
            var voice=typeof(PeopleOfTheIliacBayMod).GetField("breezeTtsTrial",Private);
            var oldVoice=voice.GetValue(adapter);voice.SetValue(adapter,null);
            try
            {
                var before=Discovered(places); uint seed=DFRandom.Seed;
                var actual=(List<TalkManager.ListItem>)typeof(PeopleOfTheIliacBayMod).GetMethod("KnownBrowsePlaces",BindingFlags.Static|BindingFlags.NonPublic)
                    .Invoke(null,new object[]{innPlaces});
                var nativeKnowledge=typeof(TalkManager).GetMethod("GetNPCKnowledgeAboutItem",Private);
                foreach(var item in innPlaces)
                {
                    DFRandom.SaveSeed(); TalkManager.NPCKnowledgeAboutItem expected;
                    try {expected=(TalkManager.NPCKnowledgeAboutItem)nativeKnowledge.Invoke(manager,new object[]{item});}
                    finally {DFRandom.RestoreSeed();}
                    if(actual.Contains(item)!=(expected==TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem || manager.NPCsKnowEverything()))
                        throw new InvalidOperationException("Browse knowledge differs from vanilla for "+item.caption);
                }
                if(seed!=DFRandom.Seed || !before.SetEquals(Discovered(places))) throw new InvalidOperationException("Knowledge lookup changed RNG or discovered a place.");
                foreach(var item in places) item.npcKnowledgeAboutItem=TalkManager.NPCKnowledgeAboutItem.DoesNotKnowAboutItem;
                innPlaces[0].npcKnowledgeAboutItem=innPlaces[1].npcKnowledgeAboutItem=innPlaces[2].npcKnowledgeAboutItem=
                    TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem;
                shop.npcKnowledgeAboutItem=TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem;
                Reaction(manager,2);
                yield return Ask(adapter,window,submit,transcript,"Is there any inn nearby?",answer=>{
                    VerifyExactKnownFixture(innPlaces);
                    if(!before.SetEquals(Discovered(places))) throw new InvalidOperationException("Listing inns marked the map.");
                });
                VerifyUnsupportedDispatch(adapter,window,inns,innPlaces);
                Reaction(manager,2);
                yield return Ask(adapter,window,submit,transcript,"Mark one on my map",answer=>{
                    var added=Discovered(places);added.ExceptWith(before);
                    if(added.Count!=1 || !innPlaces.Any(t=>added.Contains(t.buildingKey)) || shop.buildingKey==added.FirstOrDefault())
                        throw new InvalidOperationException("Map routing did not mark exactly one currently known inn leaf.");
                    LogNativeMapEffect("inn-group",added,innPlaces);
                });
                before=Discovered(places);Reaction(manager,2);mapEnabled.SetValue(adapter,false);
                yield return Ask(adapter,window,submit,transcript,"Mark an inn on my map",answer=>{
                    if(!before.SetEquals(Discovered(places))) throw new InvalidOperationException("Disabled browse map request revealed a building.");
                });
                mapEnabled.SetValue(adapter,true);
                temple.npcKnowledgeAboutItem=TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem;Reaction(manager,2);
                before=Discovered(places);
                yield return Ask(adapter,window,submit,transcript,"Mark a temple on my map",answer=>{
                    var added=Discovered(places);added.ExceptWith(before);
                    if(added.Count!=1 || !added.Contains(temple.buildingKey))
                        throw new InvalidOperationException("Direct map route did not mark the exact known temple fixture.");
                    LogNativeMapEffect("temple-direct",added,new List<TalkManager.ListItem>{temple});
                });
                before=Discovered(places);
                Reaction(manager,2);
                yield return Ask(adapter,window,submit,transcript,"Know any temples nearby?",answer=>{
                    VerifyExactKnownFixture(new List<TalkManager.ListItem>{temple});
                    if(!before.SetEquals(Discovered(places))) throw new InvalidOperationException("Temple browse changed map discovery.");
                });
                shop.npcKnowledgeAboutItem=TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem;Reaction(manager,2);
                yield return Ask(adapter,window,submit,transcript,"Are there any shops nearby?",answer=>{
                    VerifyExactKnownFixture(new List<TalkManager.ListItem>{shop});
                });
                before=Discovered(places);Reaction(manager,0);
                yield return Ask(adapter,window,submit,transcript,"Mark an inn on my map",answer=>{
                    if(!before.SetEquals(Discovered(places))) throw new InvalidOperationException("Unwilling NPC marked a destination.");
                });
                Reaction(manager,2);
                foreach(var item in innPlaces) item.npcKnowledgeAboutItem=TalkManager.NPCKnowledgeAboutItem.DoesNotKnowAboutItem;
                yield return Ask(adapter,window,submit,transcript,"Is there any inn nearby?",answer=>{
                    if(KnownBrowsePlacesForProbe(innPlaces).Count!=0)
                        throw new InvalidOperationException("Unknown category retained a known native inn candidate.");
                });
                yield return VerifyFriendlyLead(adapter,window,submit,transcript,places,initialPerson);
                initialPerson=typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId",Private).GetValue(adapter) as string;
                var other=UnityEngine.Object.FindObjectsOfType<ResidentActor>().First(p=>p.Person!=manager.MobileNPC && p.Resident.IsAlive);
                DaggerfallUI.Instance.PopToHUD();
                yield return null;
                manager.TalkToMobileNPC(other.Person);
                yield return new WaitForSecondsRealtime(0.5f);
                string currentPerson=typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId",Private).GetValue(adapter) as string;
                if(!(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow) || string.IsNullOrEmpty(currentPerson) ||
                    string.Equals(initialPerson,currentPerson,StringComparison.Ordinal))
                    throw new InvalidOperationException("Speaker switch did not bind a new conversation identity.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KNOWLEDGE_BROWSE_PASS: native knowledge parity/RNG; exact known native candidate fixture; bound map effects; unsupported social/map dispatch no-op; map toggle; refusal; unknown places; speaker identity switch.");
            }
            finally {foreach(var entry in original) entry.Key.npcKnowledgeAboutItem=entry.Value;mapEnabled.SetValue(adapter,originalMap);voice.SetValue(adapter,oldVoice);}
        }

        private static IEnumerator VerifyFriendlyLead(PeopleOfTheIliacBayMod adapter,DaggerfallTalkWindow window,
            MethodInfo submit,ListBox transcript,List<TalkManager.ListItem> places,string person)
        {
            var game=GameManager.Instance;
            var streets=adapter.GetComponent<StreetPopulation>();
            var store=typeof(PeopleOfTheIliacBayMod).GetField("store",Private).GetValue(adapter) as WorldStateStore;
            yield return ConversationMoodProbe.CloseTalk(adapter);
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            double morning=Math.Floor(clock.ToSeconds()/86400d)*86400d+10*3600;
            if(clock.ToSeconds()<morning)clock.RaiseTime((float)(morning-clock.ToSeconds()));
            yield return ApproachSpeaker(adapter,streets);
            ResidentActor actor=null;float actorDeadline=Time.realtimeSinceStartup+30;
            while(!actor && Time.realtimeSinceStartup<actorDeadline)
            {
                actor=UnityEngine.Object.FindObjectsOfType<ResidentActor>().FirstOrDefault(candidate=>
                    candidate.Person && candidate.Resident.IsAlive && candidate.Resident.AgeGroup==ResidentAgeGroup.Adult &&
                    !candidate.Resident.IsQuestProtected && streets.FindActor(candidate.Resident.Id)==candidate);
                if(!actor)yield return new WaitForSecondsRealtime(0.25f);
            }
            if(!actor)throw new InvalidOperationException("Lead fixture requires a real managed street actor, not a dialogue-only body.");
            person=actor.Resident.Id;
            game.PlayerMotor.transform.position=actor.transform.position+Vector3.right*2;
            yield return ConversationMoodProbe.OpenTalk(adapter,game,actor,value=>window=value);
            var state=store.Snapshot();
            var attitude=state.ConversationAttitudes.Find(row=>row.PersonId==person);
            if(attitude==null)throw new InvalidOperationException("Friend fixture has no conversation attitude.");
            attitude.EncounterCount=5;attitude.Offset=0;
            var relationship=NpcDirectedRelationship.Create(person,"player");
            relationship.TrustKnown=true;relationship.ReliabilityTrust=95;
            relationship.TrustEvidenceCount=5;relationship.TrustConfidence=50;
            state.NpcRelationships.Relationships.RemoveAll(row=>row.ObserverId==person && row.TargetId=="player");
            state.NpcRelationships.Relationships.Add(relationship);
            store.Restore(state);
            // Restore replaces the speech receipt scope. Reopen native Talk so
            // this disposable relationship fixture gets a fresh real session.
            yield return ConversationMoodProbe.CloseTalk(adapter);
            yield return ConversationMoodProbe.OpenTalk(adapter,game,actor,value=>window=value);
            transcript=typeof(DaggerfallTalkWindow).GetField("listboxConversation",Private).GetValue(window) as ListBox;
            typeof(PeopleOfTheIliacBayMod).GetField("conversationNativeDisposition",Private).SetValue(adapter,95);
            typeof(TalkManager).GetField("reactionToPlayer",Private).SetValue(game.TalkManager,95);
            Reaction(game.TalkManager,2);
            yield return Ask(adapter,window,submit,transcript,"You are a fool.",answer=>{
                if(DaggerfallUI.UIManager.TopWindow!=window ||
                    store.ReadNpcConversationBoundary(person,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())!=null)
                    throw new InvalidOperationException("An ordinary insult automatically closed a trusted friend's conversation.");
            });
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FRIEND_INSULT_PASS: nativeSubmit=true; conversationOpen=true; noBoundary=true; trust=95; encounters=5.");

            yield return VerifyLocalActions(adapter,streets,store,actor,window,submit);
            window=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            transcript=typeof(DaggerfallTalkWindow).GetField("listboxConversation",Private).GetValue(window) as ListBox;

            TalkManager.ListItem target=null;
            places=Leaves(game.TalkManager.ListTopicLocation).Distinct().ToList();
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry=typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets) as TownGeometry;
            int nearby=0;
            foreach(var place in places.OrderBy(place=>{
                TownGeometry.Entrance entry;
                return geometry!=null && geometry.Entrances.TryGetValue(place.buildingKey,out entry)?
                    Vector3.Distance(geometry.ScenePoint(new RoutePoint(entry.LocalDoor.x,entry.LocalDoor.z)),actor.transform.position-Vector3.up):float.PositiveInfinity;
            }))
            {
                TownGeometry.Entrance entrance;
                if(geometry==null || !geometry.Entrances.TryGetValue(place.buildingKey,out entrance))continue;
                var gap=geometry.ScenePoint(new RoutePoint(entrance.LocalDoor.x,entrance.LocalDoor.z))-(actor.transform.position-Vector3.up);
                gap.y=0;
                if(gap.magnitude<15 || gap.magnitude>180)continue;
                nearby++;
                NpcActionOption option;
                if(!streets.TryCreateConversationBuildingMoveOption(person,directory.MapID,
                    place.buildingKey,place.caption,out option))continue;
                // Use the existing native movement observation to choose a route long enough to witness walking.
                target=place;break;
            }
            if(target==null)
            {
                throw new InvalidOperationException("No reachable native building for Lead fixture; places="+places.Count+
                    "; nearby="+nearby+"; ready="+streets.Ready+"; alarmed="+streets.IsAlarmed(person)+
                    "; playerGap="+Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)+
                    "; entrances="+(geometry==null?-1:geometry.Entrances.Count)+"; actor="+actor.transform.position+"; player="+game.PlayerMotor.transform.position);
            }
            target.npcKnowledgeAboutItem=TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem;
            Reaction(game.TalkManager,2);
            yield return Ask(adapter,window,submit,transcript,"Mark "+target.caption+" on my map.",answer=>{
                if(!game.PlayerGPS.HasDiscoveredBuilding(target.buildingKey))
                    throw new InvalidOperationException("Exact destination did not receive a native marker.");
            });
            yield return Ask(adapter,window,submit,transcript,"Can you lead me there?",answer=>{
                var action=store.ActiveNpcAction(person);
                if(action==null || action.Kind!=NpcActionKind.Lead || action.Target.BuildingKey!=target.buildingKey)
                    throw new InvalidOperationException("Guide follow-up did not accept Lead to the exact marked native building.");
            });
            string actionId=store.ActiveNpcAction(person).Id;
            Vector3 originalPlayer=game.PlayerMotor.transform.position;
            float maximumLookError=FaceLeadForTest(game,actor);
            window.CloseWindow();yield return ConversationMoodProbe.CloseTalk(adapter);
            var first=streets.CaptureLeadRuntimeSnapshot(person);
            var until=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<until && Vector3.Distance(first.ActorGround,
                streets.CaptureLeadRuntimeSnapshot(person).ActorGround)<0.5f)
            {
                maximumLookError=Mathf.Max(maximumLookError,FaceLeadForTest(game,actor));
                yield return null;
            }
            var moving=streets.CaptureLeadRuntimeSnapshot(person);
            if(Vector3.Distance(first.ActorGround,moving.ActorGround)<0.5f)
                throw new InvalidOperationException("Accepted guide did not physically walk.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KNOWLEDGE_LEAD_MOVING: goalDistance="+first.GoalDistance+"; action="+actionId);
            game.PlayerMotor.transform.position=actor.transform.position+Vector3.right*15;
            FaceLeadForTest(game,actor);
            yield return new WaitForSecondsRealtime(1);
            var paused=streets.CaptureLeadRuntimeSnapshot(person);
            yield return new WaitForSecondsRealtime(1);
            var held=streets.CaptureLeadRuntimeSnapshot(person);
            if(held.Progress!=NpcLeadProgress.WaitingForPlayer || Vector3.Distance(paused.ActorGround,held.ActorGround)>0.2f)
                throw new InvalidOperationException("Guide did not wait for distant player.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KNOWLEDGE_LEAD_PAUSED: actualDistance="+held.PlayerDistance);
            game.PlayerMotor.transform.position=originalPlayer;
            Vector3 lastActorPosition=actor.transform.position;
            float maximumNpcStep=0;
            until=Time.realtimeSinceStartup+180;
            bool resumed=false;
            while(Time.realtimeSinceStartup<until)
            {
                maximumNpcStep=Mathf.Max(maximumNpcStep,Vector3.Distance(lastActorPosition,actor.transform.position));
                lastActorPosition=actor.transform.position;
                var action=store.ReadNpcAction(actionId);
                // Completed jobs can remain the active index owner while they
                // return to a routine. Observe arrival before that return walk.
                if(action==null || NpcActionExecutor.IsTerminal(action))break;
                var gap=actor.transform.position-game.PlayerMotor.transform.position;
                gap.y=0;
                if(gap.magnitude>4)
                {
                    var behind=actor.transform.position-gap.normalized*4;
                    game.PlayerMotor.transform.position=Vector3.MoveTowards(game.PlayerMotor.transform.position,behind,
                        (float)actor.WalkingSpeed*1.25f*Time.deltaTime);
                    Physics.SyncTransforms();
                }
                maximumLookError=Mathf.Max(maximumLookError,FaceLeadForTest(game,actor));
                if(Vector3.Distance(paused.ActorGround,actor.transform.position-Vector3.up)>0.5f)resumed=true;
                yield return null;
            }
            var completed=store.ReadNpcAction(actionId);
            if(!resumed || maximumNpcStep>0.51f || maximumLookError>1 || completed==null || completed.Phase!=NpcActionPhase.Completed ||
                Vector3.Distance(actor.transform.position-Vector3.up,first.GoalGround)>1)
                throw new InvalidOperationException("Guide did not resume and reach the exact native entrance; resumed="+resumed+
                    "; phase="+(completed==null?"missing":completed.Phase.ToString())+"; reason="+(completed==null?"":completed.FailureReason)+
                    "; goalGap="+Vector3.Distance(actor.transform.position-Vector3.up,first.GoalGround));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KNOWLEDGE_LEAD_PASS: building="+target.buildingKey+
                "; marked=true; pronounBound=true; physicalWalk=true; distantPlayerPause=true; resume=true; entranceReached=true; maxNpcStep="+maximumNpcStep+"; maxLookErrorDegrees="+maximumLookError+".");
            yield return ConversationMoodProbe.OpenTalk(adapter,game,actor,value=>window=value);
        }

        private static float FaceLeadForTest(GameManager game,ResidentActor actor)
        {
            var look=game.PlayerMouseLook;
            if(!look || !game.MainCamera)throw new InvalidOperationException("Lead test camera is unavailable.");
            var direction=actor.transform.position-game.MainCamera.transform.position;direction.y=0;
            look.SetHorizontalFacing(direction);
            // TownSmokeHarness disables MouseLook.Update to release the cursor.
            // Apply the same native body/camera rotations explicitly in this test.
            if(look.characterBody)
            {
                look.transform.localEulerAngles=Vector3.zero;
                look.characterBody.transform.localEulerAngles=new Vector3(0,look.Yaw,0);
            }
            else look.transform.localEulerAngles=new Vector3(0,look.Yaw,0);
            var actual=game.MainCamera.transform.forward;actual.y=0;
            return Vector3.Angle(actual,direction);
        }
        private static IEnumerator Ask(PeopleOfTheIliacBayMod adapter,DaggerfallTalkWindow window,MethodInfo submit,ListBox transcript,string text,Action<string> check)
        {
            string answer=null;
            yield return SubmitAndWait(adapter,window,submit,transcript,text,value=>answer=value);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KNOWLEDGE_MODEL_RAW_REPLY_MANUAL_REVIEW: question="+text+"; reply="+answer);
            try { check(answer); }
            catch(InvalidOperationException error)
            {throw new InvalidOperationException("Knowledge native-fixture invariant failed after raw model reply: "+error.Message,error);}
        }

        private static int CountFinalQuestions(ListBox transcript,string text)
        {
            int count=0;
            if(transcript==null)return count;
            for(int index=0;index<transcript.Count;index++)
            {
                var row=transcript.GetItem(index);
                if(row!=null && row.textLabel!=null && row.textLabel.Name!=PendingRowName &&
                    string.Equals(row.textLabel.Text,text,StringComparison.Ordinal)) count++;
            }
            return count;
        }

        private static void VerifyBoundCandidateDedup(List<TalkManager.ListItem> knownLeaves,TalkManager.ListItem outsideGroup)
        {
            MethodInfo bound=typeof(PeopleOfTheIliacBayMod).GetMethod("BoundMapCandidates",BindingFlags.Static|BindingFlags.NonPublic);
            if(bound==null) throw new InvalidOperationException("Native group map candidate binder is unavailable.");
            var topics=new List<ConversationTopic>();
            var native=new Dictionary<int,TalkManager.ListItem>();
            for(int index=0;index<knownLeaves.Count;index++)
            {
                int id=710000+index;
                var item=knownLeaves[index];
                topics.Add(new ConversationTopic(id,item.caption,"synthetic current group leaf",false));
                native.Add(id,item);
            }
            // The unrelated item is encountered first and cannot claim a later
            // leaf's ID. This reproduces the old seen-before-identity bug.
            var known=new List<TalkManager.ListItem>{outsideGroup};
            known.AddRange(knownLeaves);
            var boundRows=bound.Invoke(null,new object[]{topics,native,known}) as List<ConversationTopic>;
            if(boundRows==null || boundRows.Count!=3 || boundRows.Any(row=>row==null || row.Id<710000 || row.Id>=710000+knownLeaves.Count))
                throw new InvalidOperationException("Bound map candidate dedup discarded a valid group leaf after an unrelated known item.");
            for(int index=0;index<knownLeaves.Count;index++)
            {
                TalkManager.ListItem mapped;
                if(!native.TryGetValue(boundRows[index].Id,out mapped) || !object.ReferenceEquals(mapped,knownLeaves[index]))
                    throw new InvalidOperationException("Bound map candidate order lost exact native leaf identity.");
            }
        }

        private static List<TalkManager.ListItem> KnownBrowsePlacesForProbe(IEnumerable<TalkManager.ListItem> places)
        {
            MethodInfo method=typeof(PeopleOfTheIliacBayMod).GetMethod("KnownBrowsePlaces",BindingFlags.Static|BindingFlags.NonPublic);
            if(method==null)throw new InvalidOperationException("Native known-place resolver is unavailable.");
            return method.Invoke(null,new object[]{places}) as List<TalkManager.ListItem> ?? new List<TalkManager.ListItem>();
        }

        private static void VerifyExactKnownFixture(List<TalkManager.ListItem> expected)
        {
            var actual=KnownBrowsePlacesForProbe(expected);
            if(actual.Count!=expected.Count || expected.Any(item=>!actual.Any(found=>object.ReferenceEquals(found,item))))
                throw new InvalidOperationException("Native known-place fixture did not resolve to the exact expected current items.");
        }

        private static void VerifyUnsupportedDispatch(PeopleOfTheIliacBayMod adapter,DaggerfallTalkWindow window,
            TalkManager.ListItem currentGroup,List<TalkManager.ListItem> knownLeaves)
        {
            MethodInfo dispatch=typeof(PeopleOfTheIliacBayMod).GetMethod("TryBrowseConversation",Private);
            FieldInfo snapshotField=typeof(PeopleOfTheIliacBayMod).GetField("conversationContextCurrentSnapshot",Private);
            if(dispatch==null || snapshotField==null)throw new InvalidOperationException("Browse dispatch probe seam is unavailable.");
            var snapshot=snapshotField.GetValue(adapter) as ConversationContextSnapshot;
            if(snapshot==null)throw new InvalidOperationException("Browse dispatch has no current typed conversation snapshot.");
            var before=Discovered(Leaves(GameManager.Instance.TalkManager.ListTopicLocation).ToList());
            var rows=new List<ConversationTopic>{new ConversationTopic(720000,"Current inn group","native fixture",true)};
            var native=new Dictionary<int,TalkManager.ListItem>{{720000,currentGroup}};
            bool social=(bool)dispatch.Invoke(adapter,new object[]{window,"typed social dispatch probe","",new ConversationSelection("social",-1,"neutral"),rows,native,snapshot});
            bool unknownMap=(bool)dispatch.Invoke(adapter,new object[]{window,"unknown map dispatch probe","",new ConversationSelection("map",729999,"neutral"),rows,native,snapshot});
            var after=Discovered(Leaves(GameManager.Instance.TalkManager.ListTopicLocation).ToList());
            if(social || unknownMap || !before.SetEquals(after))
                throw new InvalidOperationException("Lexical SOCIAL or non-offered map dispatch entered browse/map effects.");
            var eligible=knownLeaves.Select((item,index)=>new ConversationTopic(730000+index,item.caption,"native leaf",false)).ToList();
            if(ConversationKnowledge.ParseTopicSelection("map|739999","map",eligible)!=null)
                throw new InvalidOperationException("Map parser accepted an ID outside the exact offered native candidates.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KNOWLEDGE_DISPATCH_NOOP_PASS: exact typed SOCIAL and unknown map route returned to normal dispatch without map effects.");
        }

        private static void LogNativeMapEffect(string scope,HashSet<int> added,List<TalkManager.ListItem> candidates)
        {
            var target=candidates.FirstOrDefault(item=>added.Contains(item.buildingKey));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KNOWLEDGE_NATIVE_MAP_EFFECT: scope="+scope+"; building="+
                (target==null?"unknown":target.buildingKey.ToString())+"; target="+(target==null?"unknown":target.caption)+".");
        }

        private static bool TryFindFinalReplyPair(ListBox transcript,string question,out int questionIndex,out int answerIndex)
        {
            questionIndex=answerIndex=-1;
            if(transcript==null)return false;
            for(int index=transcript.Count-1;index>=0;index--)
            {
                var row=transcript.GetItem(index);
                if(row==null || row.textLabel==null || row.textLabel.Name==PendingRowName ||
                    !string.Equals(row.textLabel.Text,question,StringComparison.Ordinal)) continue;
                for(int next=index+1;next<transcript.Count;next++)
                {
                    var answer=transcript.GetItem(next);
                    if(answer==null || answer.textLabel==null || answer.textLabel.Name==PendingRowName) continue;
                    if(string.IsNullOrWhiteSpace(answer.textLabel.Text) || answer.textLabel.Name==UnavailableRowName)return false;
                    questionIndex=index;answerIndex=next;return true;
                }
                return false;
            }
            return false;
        }
    }
}
