using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in native-model acceptance for current-message busy intent, persistence,
    // transcript grounding, and manual Talk while proactive contacts are paused.
    internal static class SemanticConversationProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string BaselineSave = "People - Gothway Garden";
        private const string PauseSavePrefix = "People of the Iliac Bay - proactive contact pause ";
        private const string BusyText = "I have to head out now. I can't keep talking.";
        private static readonly string[] Controls =
        {
            "I am not busy. I want to keep talking right now.",
            "My friend said, 'I have to leave now because I am busy.' I can stay and talk.",
            "Yesterday I had to leave early because I was busy.",
            "If I say I am busy next week, I will need to leave then."
        };

        private sealed class TurnResult
        {
            public double SubmittedAt;
            public NpcMemoryEntry Entry;
        }

        public static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            var game = GameManager.Instance;
            if (!host || !streets || !game || !character.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal))
                throw new InvalidOperationException("Semantic conversation probe requires the owned disposable town fixture.");
            if (game.PlayerEnterExit.IsPlayerInsideBuilding)
                throw new InvalidOperationException("Semantic conversation probe requires the exterior town fixture.");

            var store = GetStore(host);
            var actor = FindEligibleActor(store);
            float actorDeadline = Time.realtimeSinceStartup + 45f;
            while (!actor && Time.realtimeSinceStartup < actorDeadline)
            {
                yield return new WaitForSecondsRealtime(0.25f);
                store = GetStore(host);
                actor = FindEligibleActor(store);
            }
            if (!actor) throw new InvalidOperationException("No eligible existing adult resident is available for semantic conversation acceptance.");
            string residentId = actor.Resident.Id;
            string pauseSave = PauseSavePrefix + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            double baselineBlockedUntil = store.Snapshot().PlayerProactiveContactBlockedUntil;
            double initialNow = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            if (baselineBlockedUntil > initialNow)
                throw new InvalidOperationException("Baseline fixture already has an active player contact pause; reload the clean People - Gothway Garden save.");
            AssertNoExistingTurn(store, residentId, BusyText);

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_APPROACH_SCOPE: physical approach movement and exact proactive opener are outside this probe's coverage.");

            DaggerfallTalkWindow talk = null;
            yield return OpenNativeTalk(game, host, actor, window => talk = window);
            var positive = new TurnResult();
            yield return SubmitTurn(host, residentId, talk, BusyText, positive);

            store = GetStore(host);
            double blockedUntil = store.Snapshot().PlayerProactiveContactBlockedUntil;
            double pauseSeconds = blockedUntil - positive.SubmittedAt;
            if (!store.IsNpcProactiveContactSuppressed(positive.SubmittedAt) ||
                pauseSeconds < 6 * 60 * 60 || pauseSeconds > 12 * 60 * 60)
                throw new InvalidOperationException("Direct busy/departure paraphrase did not persist a bounded 6-12 game-hour proactive-contact pause.");
            AssertTranscript(store, residentId, BusyText);
            if (positive.Entry == null || positive.Entry.PlayerText != BusyText || string.IsNullOrWhiteSpace(positive.Entry.NpcText))
                throw new InvalidOperationException("Actual busy statement and nonempty native-model reply were not saved as one exact exchange.");
            if (DaggerfallUI.UIManager.TopWindow != talk || !IsConversationSessionOpen(host) ||
                GetConversationPersonId(host) != residentId)
                throw new InvalidOperationException("Busy intent changed the native speaker or closed the conversation unexpectedly.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_BUSY_INTENT_PASS: resident=" + residentId +
                "; submittedAt=" + positive.SubmittedAt.ToString("F0") + "; pauseSeconds=" + pauseSeconds.ToString("F0") +
                "; exactText=true; savedReply=true; activeTalk=true.");

            DaggerfallUI.Instance.PopToHUD();
            yield return null;
            yield return SaveAndReload(game, streets, character, pauseSave);
            store = GetStore(host);
            double restoredBlockedUntil = store.Snapshot().PlayerProactiveContactBlockedUntil;
            double restoredNow = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            if (Math.Abs(restoredBlockedUntil - blockedUntil) > 1.0 || !store.IsNpcProactiveContactSuppressed(restoredNow))
                throw new InvalidOperationException("Native disk save/reload did not preserve active proactive-contact suppression.");
            AssertTranscript(store, residentId, BusyText);
            actor = FindActor(residentId);
            float reloadActorDeadline = Time.realtimeSinceStartup + 35f;
            while (!actor && Time.realtimeSinceStartup < reloadActorDeadline)
            {
                yield return new WaitForSecondsRealtime(0.25f);
                actor = FindActor(residentId);
            }
            if (!actor) throw new InvalidOperationException("The exact resident identity did not rematerialize after proactive-contact pause reload.");
            if (store.TryGetProactiveContactOffer(residentId, restoredNow, out _))
                throw new InvalidOperationException("A proactive offer remained available while player busy suppression was active.");

            yield return OpenNativeTalk(game, host, actor, window => talk = window);
            if (talk == null || !IsConversationSessionOpen(host) || GetConversationPersonId(host) != residentId)
                throw new InvalidOperationException("Manual native Talk was blocked by automatic proactive-contact suppression.");
            DaggerfallUI.Instance.PopToHUD();
            yield return null;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_PAUSE_DISK_PASS: blockedUntil=" + restoredBlockedUntil.ToString("F0") +
                "; nativeRestore=true; automaticOfferSuppressed=true; manualTalk=true.");

            for (int i = 0; i < Controls.Length; i++)
            {
                yield return LoadSave(game, streets, character, BaselineSave);
                store = GetStore(host);
                double controlNow = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                if (Math.Abs(store.Snapshot().PlayerProactiveContactBlockedUntil - baselineBlockedUntil) > 1.0 ||
                    store.IsNpcProactiveContactSuppressed(controlNow))
                    throw new InvalidOperationException("Clean baseline save did not restore before negative-attribution control " + i + ".");
                AssertNoExistingTurn(store, residentId, Controls[i]);
                actor = FindActor(residentId);
                actorDeadline = Time.realtimeSinceStartup + 35f;
                while (!actor && Time.realtimeSinceStartup < actorDeadline)
                {
                    yield return new WaitForSecondsRealtime(0.25f);
                    actor = FindActor(residentId);
                }
                if (!actor) throw new InvalidOperationException("Baseline resident did not rematerialize for negative-attribution control " + i + ".");
                talk = null;
                yield return OpenNativeTalk(game, host, actor, window => talk = window);
                var control = new TurnResult();
                yield return SubmitTurn(host, residentId, talk, Controls[i], control);
                store = GetStore(host);
                double after = store.Snapshot().PlayerProactiveContactBlockedUntil;
                if (Math.Abs(after - baselineBlockedUntil) > 1.0 || store.IsNpcProactiveContactSuppressed(control.SubmittedAt))
                    throw new InvalidOperationException("Quoted, negative, or future busy wording changed proactive-contact suppression: " + Controls[i]);
                if (control.Entry == null || control.Entry.PlayerText != Controls[i] || string.IsNullOrWhiteSpace(control.Entry.NpcText))
                    throw new InvalidOperationException("Negative-attribution control was not retained as its exact player statement and reply.");
                AssertTranscript(store, residentId, Controls[i]);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PROACTIVE_BUSY_CONTROL_PASS: index=" + i + "; exactText=true; noPause=true; historyReply=true.");
                DaggerfallUI.Instance.PopToHUD();
                yield return null;
            }

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SEMANTIC_CONVERSATION_PASS: native-model busy attribution, negative/historical/conditional controls, exact transcript, disk pause restore, and manual Talk verified; physical proactive approach not exercised.");
        }

        private static IEnumerator OpenNativeTalk(GameManager game, PeopleOfTheIliacBayMod host, ResidentActor actor,
            Action<DaggerfallTalkWindow> ready)
        {
            if (!actor || !actor.Person || actor.Resident == null || !actor.Resident.IsAlive || actor.IsSleeping || actor.IsConversing)
                throw new InvalidOperationException("Selected native resident is not available for Talk.");
            game.TalkManager.TalkToMobileNPC(actor.Person);
            float deadline = Time.realtimeSinceStartup + 25f;
            while (!(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow) && Time.realtimeSinceStartup < deadline)
                yield return null;
            var talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            if (talk == null || game.TalkManager.CurrentNPCType != TalkManager.NPCType.Mobile || game.TalkManager.MobileNPC != actor.Person)
                throw new InvalidOperationException("Native Talk did not select the expected mobile resident.");
            deadline = Time.realtimeSinceStartup + 25f;
            while ((!talk.IsSetup || !IsConversationSessionOpen(host) || IsConversationBusy(host) ||
                    GetConversationPersonId(host) != actor.Resident.Id) && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (!talk.IsSetup || !IsConversationSessionOpen(host) || IsConversationBusy(host) ||
                GetConversationPersonId(host) != actor.Resident.Id)
                throw new InvalidOperationException("Native managed-resident conversation session did not initialize.");
            ready(talk);
        }

        private static IEnumerator SubmitTurn(PeopleOfTheIliacBayMod host, string residentId, DaggerfallTalkWindow talk,
            string text, TurnResult result)
        {
            if (talk == null || DaggerfallUI.UIManager.TopWindow != talk || !IsConversationSessionOpen(host) ||
                GetConversationPersonId(host) != residentId)
                throw new InvalidOperationException("Native Talk speaker/session changed before text submission.");
            result.SubmittedAt = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var submit = typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText", Private);
            if (submit == null) throw new InvalidOperationException("Production SubmitFreeText entry point is unavailable.");
            submit.Invoke(host, new object[] { talk, text });
            float deadline = Time.realtimeSinceStartup + 75f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var store = GetStore(host);
                var entries = store.ConversationTranscript(residentId,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() + 2, 256);
                var matches = entries.Where(entry => entry.Source != "observed" && entry.PlayerText == text).ToList();
                if (matches.Count > 0 && !IsConversationBusy(host))
                {
                    var candidate=matches[matches.Count - 1];
                    if(VisibleExchangeMatches(talk,text,candidate.NpcText))
                    { result.Entry=candidate;break; }
                }
                if (DaggerfallUI.UIManager.TopWindow != talk || !IsConversationSessionOpen(host) ||
                    GetConversationPersonId(host) != residentId)
                    throw new InvalidOperationException("Native reply changed or closed the active conversation unexpectedly.");
                yield return null;
            }
            if (result.Entry == null)
                throw new InvalidOperationException("Native-model reply did not finish, persist the exact player statement, and render the matching native Talk exchange.");
            if (DaggerfallUI.UIManager.TopWindow != talk || GetConversationPersonId(host) != residentId)
                throw new InvalidOperationException("Native speaker changed before reply verification completed.");
        }

        private static bool VisibleExchangeMatches(DaggerfallTalkWindow talk,string question,string answer)
        {
            var field=typeof(PeopleOfTheIliacBayMod).GetField("conversationListField",BindingFlags.Static|BindingFlags.NonPublic);
            var listField=field==null?null:field.GetValue(null) as FieldInfo;
            var transcript=talk==null||listField==null?null:listField.GetValue(talk) as ListBox;
            if(transcript==null||string.IsNullOrWhiteSpace(answer))return false;
            string expectedQuestion=NormalizeVisibleText(question),expectedAnswer=NormalizeVisibleText(answer);
            bool foundQuestion=false;
            for(int i=0;i<transcript.Count;i++)
            {
                var item=transcript.GetItem(i);
                if(item==null||item.textLabel==null)continue;
                string visible=NormalizeVisibleText(item.textLabel.Text);
                if(visible.Length==0)continue;
                if(!foundQuestion)
                {
                    if(item.textColor==DaggerfallUI.DaggerfallQuestionTextColor&&visible==expectedQuestion)
                        foundQuestion=true;
                    continue;
                }
                return item.textColor!=DaggerfallUI.DaggerfallQuestionTextColor&&visible==expectedAnswer;
            }
            return false;
        }

        private static string NormalizeVisibleText(string value)
        {
            if(string.IsNullOrEmpty(value))return string.Empty;
            var result=new System.Text.StringBuilder(value.Length);
            bool pendingSpace=false;
            foreach(char character in value)
            {
                if(char.IsWhiteSpace(character)){pendingSpace=result.Length>0;continue;}
                if(pendingSpace)result.Append(' ');
                result.Append(character);pendingSpace=false;
            }
            return result.ToString();
        }

        private static IEnumerator SaveAndReload(GameManager game, StreetPopulation streets, string character, string title)
        {
            bool saved = false;
            bool loaded = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSave += onSave;
            SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.Save(character, title);
                float deadline = Time.realtimeSinceStartup + 60f;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Native proactive-contact pause save timed out.");
                game.SaveLoadManager.EnumerateSaves();
                int slot = game.SaveLoadManager.FindSaveFolderByNames(character, title);
                if (slot < 0) throw new InvalidOperationException("Native proactive-contact pause save slot was not found.");
                game.SaveLoadManager.Load(slot);
                DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 75f;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Native proactive-contact pause disk reload timed out.");
            }
            finally
            {
                SaveLoadManager.OnSave -= onSave;
                SaveLoadManager.OnLoad -= onLoad;
            }
            yield return null;
        }

        private static IEnumerator LoadSave(GameManager game, StreetPopulation streets, string character, string title)
        {
            bool loaded = false;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.EnumerateSaves();
                int slot = game.SaveLoadManager.FindSaveFolderByNames(character, title);
                if (slot < 0) throw new InvalidOperationException("Required fixture save is missing: " + title);
                game.SaveLoadManager.Load(slot);
                DaggerfallUI.Instance.PopToHUD();
                float deadline = Time.realtimeSinceStartup + 75f;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Fixture disk reload timed out: " + title);
            }
            finally { SaveLoadManager.OnLoad -= onLoad; }
            yield return null;
        }

        private static void AssertTranscript(WorldStateStore store, string residentId, string text)
        {
            var matches = store.ConversationTranscript(residentId,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() + 2, 256)
                .Where(entry => entry.Source != "observed" && entry.PlayerText == text).ToList();
            if (matches.Count != 1 || string.IsNullOrWhiteSpace(matches[0].NpcText))
                throw new InvalidOperationException("Exact player turn and model reply were not stored exactly once: " + text);
        }

        private static void AssertNoExistingTurn(WorldStateStore store, string residentId, string text)
        {
            var matches = store.ConversationTranscript(residentId,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() + 2, 256)
                .Where(entry => entry.Source != "observed" && entry.PlayerText == text).ToList();
            if (matches.Count != 0)
                throw new InvalidOperationException("Fixture transcript already contains acceptance text; refusing stale-result pass: " + text);
        }

        private static ResidentActor FindEligibleActor(WorldStateStore store)
        {
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if (!actor || !actor.Person || actor.Resident == null || !actor.Resident.IsAlive ||
                    actor.Resident.AgeGroup != ResidentAgeGroup.Adult || actor.Resident.IsQuestProtected ||
                    actor.IsConversing || actor.IsSleeping) continue;
                if (store.ActiveNpcAction(actor.Resident.Id) != null || store.ActiveNpcTask(actor.Resident.Id) != null ||
                    store.TryGetProactiveContactOffer(actor.Resident.Id, now, out _)) continue;
                if (store.TryGetActualConversationDisposition(actor.Resident.Id, out int disposition) && disposition <= -100) continue;
                if (store.GetDialogueComplaint(actor.Resident.Id) != null) continue;
                return actor;
            }
            return null;
        }

        private static ResidentActor FindActor(string residentId)
        {
            foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if (actor && actor.Person && actor.Resident != null && actor.Resident.Id == residentId) return actor;
            return null;
        }

        private static WorldStateStore GetStore(PeopleOfTheIliacBayMod host)
        { return typeof(PeopleOfTheIliacBayMod).GetField("store", Private).GetValue(host) as WorldStateStore; }
        private static string GetConversationPersonId(PeopleOfTheIliacBayMod host)
        { return (string)typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId", Private).GetValue(host); }
        private static bool IsConversationBusy(PeopleOfTheIliacBayMod host)
        { return (bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy", Private).GetValue(host); }
        private static bool IsConversationSessionOpen(PeopleOfTheIliacBayMod host)
        { return (bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen", Private).GetValue(host); }
    }
}
