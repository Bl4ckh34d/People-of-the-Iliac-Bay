using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class GroundedConversationProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static IEnumerator Run(PeopleOfTheIliacBayMod adapter, DaggerfallTalkWindow window, MethodInfo submit, ListBox transcript, string personId)
        {
            var manager = GameManager.Instance.TalkManager;
            var store = typeof(PeopleOfTheIliacBayMod).GetField("store", Private).GetValue(adapter) as WorldStateStore;
            var lifeMethod = typeof(PeopleOfTheIliacBayMod).GetMethod("CurrentConversationLifeContext", Private);
            string life = lifeMethod == null ? string.Empty : lifeMethod.Invoke(adapter,
                new object[] { manager.NameNPC, 0 }) as string;
            int settlement = GameManager.Instance.PlayerGPS.HasCurrentLocation ?
                GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId : -1;
            bool tracked = settlement >= 0 && store.GetResidentDialogueRoutine(settlement, personId) != null;
            if (string.IsNullOrEmpty(life) || !life.Contains("Stable personality traits:") ||
                !life.Contains("Current mood:") || tracked && !life.Contains("plan today:") &&
                    !life.Contains("Current verified activity:") && !life.Contains("Current verified travel activity:"))
                throw new InvalidOperationException("Current NPC personality, mood or daily plan was absent from dialogue context: " + life);
            var all = new List<TalkManager.ListItem>();
            Flatten(manager.ListTopicLocation, all);
            var group = all.Find(t => t.type == TalkManager.ListItemType.ItemGroup && t.caption.ToLowerInvariant().Contains("temple")) ??
                all.Find(t => t.type == TalkManager.ListItemType.ItemGroup && t.listChildItems != null && t.listChildItems.Count > 1);
            if (group == null) throw new InvalidOperationException("Grounded dialogue fixture has no location groups.");
            yield return Ask(adapter, window, submit, transcript, "Where are the " + group.caption + "?", answer =>
            {
                if (answer.IndexOf("topic list", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException("General location reply refers to UI instead of speaking in character: " + answer);
                foreach (var child in group.listChildItems.Where(t => t.type != TalkManager.ListItemType.NavigationBack).Take(10))
                    if (!answer.Contains(child.caption)) throw new InvalidOperationException("Native topic missing from choices.");
            });
            yield return Ask(adapter, window, submit, transcript, "Where is the Temple of Atlantis?", answer =>
            {
                if (answer == FreeTextDialogue.FallbackAnswer)
                    throw new InvalidOperationException("Unmatched place fell back to canned clarification: " + answer);
            });
            int offset = store.ConversationDisposition(personId);
            yield return Ask(adapter, window, submit, transcript, "You are an idiot", answer => { });
            if (store.ConversationDisposition(personId) != offset - 10) throw new InvalidOperationException("Insult did not apply -10 personal disposition.");
            yield return Ask(adapter, window, submit, transcript, "Hurry up, I have no time for this", answer => { });
            if (store.ConversationDisposition(personId) != offset - 12) throw new InvalidOperationException("Rudeness did not apply -2 personal disposition.");
            yield return Ask(adapter, window, submit, transcript, "Someone called me an idiot", answer => { });
            if (store.ConversationDisposition(personId) != offset - 12) throw new InvalidOperationException("Reported insult incorrectly changed disposition.");
            yield return Ask(adapter, window, submit, transcript, "You are an idiot", answer => { });
            if (store.ConversationDisposition(personId) != offset - 37) throw new InvalidOperationException("Repeated insult did not apply -25.");
            var restored = new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); restored.Restore(store.Snapshot());
            if (restored.ConversationDisposition(personId) != offset - 37) throw new InvalidOperationException("Disposition not preserved by snapshot restore.");
            var enabledField = typeof(PeopleOfTheIliacBayMod).GetField("conversationDispositionEnabled", Private);
            var reactionMethod = typeof(TalkManager).GetMethod("GetReactionToPlayer_0_1_2", Private);
            object[] reactionArgs = { TalkManager.QuestionType.LocalBuilding, (DaggerfallConnect.Arena2.FactionFile.SocialGroups)0 };
            int baseReaction;
            enabledField.SetValue(adapter, false);
            try { baseReaction = (int)reactionMethod.Invoke(manager, reactionArgs); }
            finally { enabledField.SetValue(adapter, true); }
            int adjustedReaction = (int)reactionMethod.Invoke(manager, reactionArgs);
            if (adjustedReaction != ConversationEffects.AdjustReactionTier(baseReaction, offset - 10))
                throw new InvalidOperationException("Personal disposition did not reach native topic reaction checks.");

            var buildings = all.Where(t => t.type == TalkManager.ListItemType.Item && t.questionType == TalkManager.QuestionType.LocalBuilding).ToList();
            var target = buildings.FirstOrDefault(t => !GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(t.buildingKey));
            if (target == null) throw new InvalidOperationException("Grounded dialogue fixture needs an undiscovered local building.");
            var tier = typeof(TalkManager).GetField("reactionToPlayer_0_1_2", Private);
            var lastTone = typeof(TalkManager).GetField("lastToneIndex", Private);
            var selectedTone = typeof(DaggerfallTalkWindow).GetField("selectedTalkTone", Private);
            object oldTier = tier.GetValue(manager), oldTone = lastTone.GetValue(manager);
            var oldKnowledge = target.npcKnowledgeAboutItem;
            try
            {
                target.npcKnowledgeAboutItem = TalkManager.NPCKnowledgeAboutItem.DoesNotKnowAboutItem;
                tier.SetValue(manager, 2);
                lastTone.SetValue(manager, DaggerfallTalkWindow.TalkToneToIndex((DaggerfallTalkWindow.TalkTone)selectedTone.GetValue(window)));
                yield return Ask(adapter, window, submit, transcript, "Mark " + target.caption + " on my map", answer =>
                {
                    if (GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(target.buildingKey))
                        throw new InvalidOperationException("Unknown NPC location was marked.");
                });
                target.npcKnowledgeAboutItem = TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem;
                tier.SetValue(manager, 2);
                yield return Ask(adapter, window, submit, transcript, "Mark it on my map", answer =>
                {
                    if (!GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(target.buildingKey) || !answer.Contains(target.caption))
                        throw new InvalidOperationException("Known map request did not discover the real native building: " + answer);
                });
                var next = buildings.FirstOrDefault(t => t != target && !GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(t.buildingKey));
                if (next == null) throw new InvalidOperationException("Fixture lacks a second undiscovered building.");
                var nextKnowledge = next.npcKnowledgeAboutItem;
                try
                {
                    next.npcKnowledgeAboutItem = TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem;
                    tier.SetValue(manager, 2);
                    // Emulate a normal left-hand topic selection through the native
                    // question/answer path, then append through the native UI method.
                    var tone = (DaggerfallTalkWindow.TalkTone)selectedTone.GetValue(window);
                    string nativeQuestion = manager.GetQuestionText(next, tone);
                    string nativeAnswer = manager.GetAnswerText(next);
                    typeof(DaggerfallTalkWindow).GetMethod("SetQuestionAnswerPairInConversationListbox", Private)
                        .Invoke(window, new object[] { nativeQuestion, nativeAnswer });
                    yield return Ask(adapter, window, submit, transcript, "Mark it on my map", answer =>
                    {
                        if (!GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(next.buildingKey) || !answer.Contains(next.caption))
                            throw new InvalidOperationException("Map follow-up ignored the intervening native topic: " + answer);
                    });
                }
                finally { next.npcKnowledgeAboutItem = nextKnowledge; }
            }
            finally
            {
                target.npcKnowledgeAboutItem = oldKnowledge;
                tier.SetValue(manager, oldTier); lastTone.SetValue(manager, oldTone);
            }
            yield return Ask(adapter, window, submit, transcript, "Was that so difficult?", answer =>
            {
                if (answer == FreeTextDialogue.FallbackAnswer)
                    throw new InvalidOperationException("Ordinary reaction fell back to canned clarification: " + answer);
            });
            yield return Ask(adapter, window, submit, transcript, "What do you make of this weather?", answer =>
            {
                if (answer == FreeTextDialogue.FallbackAnswer)
                    throw new InvalidOperationException("Weather small talk fell back to canned clarification: " + answer);
            });
            var player = GameManager.Instance.PlayerEntity;
            var originalGender = player.Gender;
            var npcGender = manager.StaticNPC ? manager.StaticNPC.Data.gender : manager.MobileNPC.Gender;
            try
            {
                int beforeFlirt = store.ConversationDisposition(personId);
                player.Gender = npcGender;
                yield return Ask(adapter, window, submit, transcript, "May I court you?", answer => { });
                if(store.ConversationDisposition(personId) != beforeFlirt - 2)
                    throw new InvalidOperationException("Same-sex adult flirt did not apply unwelcome reception.");
                player.Gender = npcGender == DaggerfallWorkshop.Game.Entity.Genders.Male ?
                    DaggerfallWorkshop.Game.Entity.Genders.Female : DaggerfallWorkshop.Game.Entity.Genders.Male;
                string expected = ConversationFlirting.Tone(personId, player.Name, npcGender.ToString(), player.Gender.ToString(), false,
                    store.ConversationDisposition(personId));
                beforeFlirt = store.ConversationDisposition(personId);
                yield return Ask(adapter, window, submit, transcript, "You are beautiful. May I take you on a romantic date?", answer => { });
                if(store.ConversationDisposition(personId) != beforeFlirt + (expected == "polite" ? 1 : -2))
                    throw new InvalidOperationException("Opposite-sex adult flirt disagreed with stable reception.");
                string cue = (string)typeof(PeopleOfTheIliacBayMod).GetField("conversationResponseCue", Private).GetValue(adapter);
                if(!cue.Contains(expected == "polite" ? "flattered" : "unwelcome"))
                    throw new InvalidOperationException("Social reply omitted authoritative flirt reception.");
                beforeFlirt = store.ConversationDisposition(personId);
                yield return Ask(adapter, window, submit, transcript, "You did an excellent job", answer => { });
                if(store.ConversationDisposition(personId) != beforeFlirt + 1)
                    throw new InvalidOperationException("Ordinary compliment did not improve disposition.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FLIRT_PASS: same-sex rejection; opposite-sex stable " + expected + "; compliment; response context.");
            }
            finally { player.Gender = originalGender; }
            var childActor = manager.MobileNPC ? manager.MobileNPC.GetComponent<ResidentActor>() : null;
            var guardPopulation = adapter.GetComponent<GuardPopulation>();
            if(!childActor || !guardPopulation || !guardPopulation.OwnsSettlement)
                throw new InvalidOperationException("Child report fixture requires a resident and active finite garrison.");
            var oldAge = childActor.Resident.AgeGroup;
            var oldCrime = player.CrimeCommitted;
            int rosterCount = guardPopulation.Roster.Guards.Count;
            try
            {
                childActor.Resident.AgeGroup = ResidentAgeGroup.Child;
                player.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
                yield return Ask(adapter, window, submit, transcript, "May I court you?", answer => {
                    if(answer != "Leave me alone! Guards!") throw new InvalidOperationException("Child did not refuse and report approach.");
                });
                if(player.CrimeCommitted != DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.Trespassing ||
                    guardPopulation.Roster.Guards.Count != rosterCount ||
                    !guardPopulation.Roster.Reports.Exists(r => r.Id.StartsWith("native:") && r.GuardIds.Count > 0))
                    throw new InvalidOperationException("Child report did not dispatch finite guards with Trespassing.");
                if(DaggerfallUI.UIManager.TopWindow == window)
                    throw new InvalidOperationException("Reported approach left the game paused in dialogue.");
                player.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.Murder;
                typeof(PeopleOfTheIliacBayMod).GetMethod("ReportChildDialogueOffense", Private).Invoke(adapter, null);
                if(player.CrimeCommitted != DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.Murder)
                    throw new InvalidOperationException("Child report replaced existing Murder charge.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CHILD_DIALOGUE_REPORT_PASS: refusal; Trespassing; finite physical dispatch; prior Murder retained; dialogue closed.");
            }
            finally { childActor.Resident.AgeGroup = oldAge; player.CrimeCommitted = oldCrime; }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GROUNDED_DIALOGUE_PASS: native topics; open social replies; map knowledge gate and discovery; insult/rude/quoted/repeated tone; disposition restore and native reaction hook.");
        }

        private static IEnumerator Ask(PeopleOfTheIliacBayMod adapter, DaggerfallTalkWindow window, MethodInfo submit, ListBox transcript,
            string question, Action<string> check)
        {
            int before = transcript.Count;
            submit.Invoke(adapter, new object[] { window, question });
            float deadline = Time.realtimeSinceStartup + 30;
            while (transcript.Count == before && Time.realtimeSinceStartup < deadline) yield return null;
            if (transcript.Count != before + 2) throw new InvalidOperationException("Grounded dialogue timed out: " + question);
            string answer = transcript.GetItem(before + 1).textLabel.Text;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GROUNDED_REPLY: " + question + " => " + answer);
            check(answer);
        }

        private static void Flatten(List<TalkManager.ListItem> input, List<TalkManager.ListItem> result)
        {
            if (input == null) return;
            foreach (var item in input)
            {
                if (item.type == TalkManager.ListItemType.NavigationBack || result.Contains(item)) continue;
                result.Add(item);
                if (item.type == TalkManager.ListItemType.ItemGroup) Flatten(item.listChildItems, result);
            }
        }
    }
}
