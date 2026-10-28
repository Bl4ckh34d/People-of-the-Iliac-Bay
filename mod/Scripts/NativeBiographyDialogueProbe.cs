using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Passive native acceptance. Every utterance uses the ordinary free-text path.
    // Natural replies are preserved for review, never judged with text patterns.
    internal static class NativeBiographyDialogueProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Character = "Hakon Ironhand";
        private static DialogueRequest observedRequest;
        private static string observedReply;

        internal static IEnumerator RunFixture(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            Require(host && streets && character == Character && GameManager.Instance.IsReady &&
                Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE") == "dialogue-biography",
                "Biography dialogue requires the explicit owned native fixture.");
            var game = GameManager.Instance;
            var store = Store(host);
            ResidentActor adult = null, partner = null;
            yield return EnterActualFamilyHome(host, streets);
            store = Store(host);
            float until = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < until)
            {
                FindPair(store, out adult, out partner);
                if (adult && partner) break;
                yield return null;
            }
            Require(adult && partner, "No actual awake resident couple appeared in this loaded fixture.");
            string person = adult.Resident.Id, partnerId = partner.Resident.Id;
            Require(store.ReadNpcBiography(person) == null && store.ReadNpcBiography(partnerId) == null,
                "Biography fixture requires two actual family members without an existing biography.");
            var anchor = store.ReadNpcBiographyAnchor(person);
            var partnerAnchor = store.ReadNpcBiographyAnchor(partnerId);
            Require(anchor != null && anchor.FamilyId != null && partnerAnchor != null &&
                anchor.FamilyId == partnerAnchor.FamilyId, "Actual couple does not share the same native biography family.");
            string traits = null;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BIOGRAPHY_NATIVE_SOURCE: " + anchor.Context +
                "; partner=" + partnerAnchor.Context);

            var client = (LocalDialogueClient)Field(host, "localDialogue").GetValue(host);
            var previousReplyTrace = client.ForegroundReplyTrace;
            var previousClassificationTrace = client.ForegroundClassificationTrace;
            string checkpoint = "People - Biography " + Guid.NewGuid().ToString("N").Substring(0, 8);
            bool originalEnabled = (bool)Field(host, "localDialogueEnabled").GetValue(host);
            bool originalPrewarm = (bool)Field(host, "dialoguePrewarmEnabled").GetValue(host);
            Field(host, "localDialogueEnabled").SetValue(host, true);
            Field(host, "dialoguePrewarmEnabled").SetValue(host, false);
            client.ForegroundReplyTrace = (request, requestJson, responseJson, reply) =>
            {
                observedRequest = request; observedReply = reply;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BIOGRAPHY_ACTUAL_REQUEST: " + requestJson);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BIOGRAPHY_ACTUAL_REPLY: " + reply);
                if (previousReplyTrace != null) previousReplyTrace(request, requestJson, responseJson, reply);
            };
            client.ForegroundClassificationTrace = (request, response, choice) =>
            {
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BIOGRAPHY_ACTUAL_CLASSIFICATION: request=" + request +
                    "; response=" + response + "; choice=" + choice);
                if (previousClassificationTrace != null) previousClassificationTrace(request, response, choice);
            };
            try
            {
                DaggerfallTalkWindow talk = null;
                yield return ConversationMoodProbe.OpenTalk(host, game, adult, value => talk = value);
                yield return Turn(host, client, talk, "Hello. It is good to meet you.", "greeting");
                Require(store.ReadNpcBiography(person) == null, "Ordinary greeting created a durable biography.");
                var profile = store.ReadNpcPersonality(person);
                Require(profile != null && profile.Version == 3 && profile.SelectedTraits.Count == 3,
                    "Ordinary native Talk did not initialize three saved personality traits.");
                traits = string.Join("|", profile.SelectedTraits.OrderBy(item => item.Key).ToArray());
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BIOGRAPHY_PERSONALITY_SOURCE: savedTraits=" + traits);
                yield return Turn(host, client, talk,
                    "How did you first learn your work? What everyday experience shaped you, and what do you hope for?", "first-history");
                var first = store.ReadNpcBiography(person);
                Require(first != null && first.FamilyFacts.Count >= 3 && first.FamilyFacts.Count <= 4,
                    "Real personal-history conversation produced no valid biography and shared family past.");
                string fingerprint = Fingerprint(first);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BIOGRAPHY_FIXED_FACTS: person=" + person + "; " + NpcBiographyChat.Context(first));
                yield return ConversationMoodProbe.CloseTalk(host);
                yield return ConversationMoodProbe.OpenTalk(host, game, adult, value => talk = value);
                yield return Turn(host, client, talk,
                    "Tell me again about how your working life began and an ordinary memory your family shares.", "reopened-history");
                Require(Fingerprint(store.ReadNpcBiography(person)) == fingerprint, "Reopened Talk rewrote the fixed biography.");
                yield return ConversationMoodProbe.CloseTalk(host);

                partner = Find(partnerId);
                Require(partner, "Actual partner left the loaded scene before the family interview.");
                yield return ConversationMoodProbe.OpenTalk(host, game, partner, value => talk = value);
                string familyContext = (string)typeof(PeopleOfTheIliacBayMod).GetMethod("ConversationBiographyContext", Private)
                    .Invoke(host, new object[] { partnerId });
                foreach (string fact in first.FamilyFacts)
                    Require(familyContext.IndexOf(fact, StringComparison.Ordinal) >= 0, "Partner context lost a fixed shared family fact.");
                yield return Turn(host, client, talk,
                    "What ordinary memories do you and your family share? How did your own work begin?", "partner-history");
                var partnerBiography = store.ReadNpcBiography(partnerId);
                Require(partnerBiography != null && Same(first.FamilyFacts, partnerBiography.FamilyFacts),
                    "Actual partner did not retain exactly the same shared family past.");
                yield return Turn(host, client, talk,
                    "What is your role here, who is your partner, and what are you doing today?", "role-partner-activity");
                yield return ConversationMoodProbe.CloseTalk(host);

                adult = Find(person);
                Require(adult, "Actual interviewed resident left its home before the shared memory disk checkpoint.");
                var interiorMemory = streets.CaptureInteriorSceneMemoryForDiskProbe(adult);
                yield return Save(game, checkpoint);
                yield return ConversationMoodProbe.LoadSave(host, streets, Character, checkpoint, true);
                store = Store(host);
                streets.VerifyInteriorSceneMemoryAfterDiskProbe(interiorMemory);
                Require(Fingerprint(store.ReadNpcBiography(person)) == fingerprint &&
                    Same(first.FamilyFacts, store.ReadNpcBiography(partnerId).FamilyFacts),
                    "Native disk load changed personal or shared biography facts.");
                adult = null; until = Time.realtimeSinceStartup + 60f;
                while (!adult && Time.realtimeSinceStartup < until) { adult = Find(person); yield return null; }
                Require(adult, "Disk load did not restore the exact interviewed native resident.");
                yield return ConversationMoodProbe.OpenTalk(host, game, adult, value => talk = value);
                yield return Turn(host, client, talk,
                    "Do you still remember how you learned your work and those ordinary family experiences?", "disk-reload-history");
                Require(Fingerprint(store.ReadNpcBiography(person)) == fingerprint, "Post-load conversation changed the fixed biography.");
                var afterProfile = store.ReadNpcPersonality(person);
                Require(afterProfile != null && afterProfile.Version == 3 && afterProfile.SelectedTraits.Count == 3 &&
                    string.Join("|", afterProfile.SelectedTraits.OrderBy(item => item.Key).ToArray()) == traits,
                    "Actual conversations or disk load changed the three saved personality traits.");
                yield return ConversationMoodProbe.CloseTalk(host);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BIOGRAPHY_NATIVE_PASS: actualTalk=true; actualGemma=true; greetingCreatesNothing=true; " +
                    "nativeOriginAndFamilyAnchored=true; personalFactsImmutable=true; actualPartnerSharedFactsExact=true; " +
                    "nativeDiskReload=true; actualInteriorMemorySameDiskRoundtrip=true; savedTraits=3; traitsStable=true; " +
                    "naturalReplySemanticReview=required; childCareer=not-tested");
            }
            finally
            {
                client.ForegroundReplyTrace = previousReplyTrace;
                client.ForegroundClassificationTrace = previousClassificationTrace;
                Field(host, "localDialogueEnabled").SetValue(host, originalEnabled);
                Field(host, "dialoguePrewarmEnabled").SetValue(host, originalPrewarm);
                client.Cancel(); DaggerfallUI.Instance.PopToHUD();
                ConversationMoodProbe.DeleteCheckpoint(game, Character, checkpoint);
            }
        }

        private static IEnumerator Turn(PeopleOfTheIliacBayMod host, LocalDialogueClient client,
            DaggerfallTalkWindow talk, string message, string label)
        {
            var busy = Field(host, "conversationBusy");
            Require(!(bool)busy.GetValue(host) && !client.IsBusy, "Previous biography turn has not finished.");
            var rows = (ListBox)typeof(DaggerfallTalkWindow).GetField("listboxConversation", Private).GetValue(talk);
            int before = rows.Count;
            long completions = client.ReplyCompletionCount;
            observedRequest = null; observedReply = null;
            typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText", Private).Invoke(host, new object[] { talk, message });
            float until = Time.realtimeSinceStartup + 180f;
            while (Time.realtimeSinceStartup < until &&
                ((bool)busy.GetValue(host) || client.IsBusy || client.ReplyCompletionCount <= completions)) yield return null;
            yield return null;
            Require(!(bool)busy.GetValue(host) && !client.IsBusy && client.ReplyCompletionCount > completions,
                "Real Gemma biography turn did not complete: " + label);
            until = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < until && !AllNewAnswersRevealed(rows, before)) yield return null;
            Require(AllNewAnswersRevealed(rows, before), "Native answer reveal did not finish: " + label);
            var answer = new StringBuilder();
            for (int i = before; i < rows.Count; i++)
            {
                var row = rows.GetItem(i);
                if (row != null && row.textLabel != null && row.textColor != DaggerfallUI.DaggerfallQuestionTextColor)
                    answer.Append(row.textLabel.Text).Append(' ');
            }
            Require(answer.Length > 0, "Completed biography turn has no visible NPC answer: " + label);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BIOGRAPHY_VISIBLE_TURN: case=" + label + "; question=" + message + "; answer=" + answer);
            var snapshot = Field(host, "conversationContextCurrentSnapshot").GetValue(host) as ConversationContextSnapshot;
            Require(snapshot != null, "Completed biography turn lost its actual captured speaker context: " + label);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BIOGRAPHY_CAPTURED_CONTEXT: case=" + label + "; " +
                ConversationContextFormatter.ForSocialReply(snapshot, 3072));
            if (observedRequest != null)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BIOGRAPHY_SOCIAL_PROMPT: " + LocalDialogue.Build(observedRequest).Prompt);
            // Streaming replies use the same production client and visible transcript.
            // The passive buffered trace is optional and must never force another path.
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BIOGRAPHY_TURN_TRANSPORT: case=" + label +
                "; freshBackendCompletion=true; bufferedTrace=" + (observedReply != null));
        }

        internal static bool AllNewAnswersRevealed(ListBox rows, int firstNewRow)
        {
            bool found = false;
            var canonical = typeof(PeopleOfTheIliacBayMod).GetMethod("CanonicalTranscriptText", BindingFlags.Static | BindingFlags.NonPublic);
            for (int i = firstNewRow; i < rows.Count; i++)
            {
                var row = rows.GetItem(i);
                if (row == null || row.textLabel == null || row.textColor == DaggerfallUI.DaggerfallQuestionTextColor) continue;
                found = true;
                string full = (string)canonical.Invoke(null, new object[] { row });
                if (string.IsNullOrWhiteSpace(full) || !string.Equals(full, row.textLabel.Text, StringComparison.Ordinal)) return false;
            }
            return found;
        }

        private static IEnumerator Save(GameManager game, string title)
        {
            bool saved = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnSave += onSave;
            try
            {
                game.SaveLoadManager.Save(Character, title);
                float until = Time.realtimeSinceStartup + 60f;
                while (!saved && Time.realtimeSinceStartup < until) yield return null;
                Require(saved, "Native biography disk checkpoint timed out.");
            }
            finally { SaveLoadManager.OnSave -= onSave; }
        }

        internal static IEnumerator EnterActualFamilyHome(PeopleOfTheIliacBayMod host, StreetPopulation streets)
        {
            var game = GameManager.Instance;
            if (game.PlayerEnterExit.IsPlayerInside) game.PlayerEnterExit.TransitionExterior(true);
            float until = Time.realtimeSinceStartup + 90f;
            while ((game.PlayerEnterExit.IsPlayerInside || !streets.Ready || !streets.EntranceSurveyReady) &&
                Time.realtimeSinceStartup < until) yield return null;
            Require(!game.PlayerEnterExit.IsPlayerInside && streets.Ready && streets.EntranceSurveyReady,
                "Family biography needs the actual prepared exterior home catalog.");
            var store = Store(host);
            var population = (SettlementPopulation)typeof(StreetPopulation).GetField("population", Private).GetValue(streets);
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            DaggerfallStaticDoors selectedOwner = null;
            StaticDoor selectedDoor = default(StaticDoor);
            string firstId = null, secondId = null;
            foreach (var home in population.Households)
            {
                foreach (var resident in home.Residents)
                {
                    var partner = home.Residents.Find(value => value.Id == resident.PartnerId);
                    if (!resident.IsAlive || resident.IsQuestProtected || resident.AgeGroup != ResidentAgeGroup.Adult ||
                        partner == null || !partner.IsAlive || partner.IsQuestProtected || partner.AgeGroup != ResidentAgeGroup.Adult ||
                        store.ReadNpcBiography(resident.Id) != null || store.ReadNpcBiography(partner.Id) != null ||
                        store.ActiveNpcAction(resident.Id) != null || store.ActiveNpcTask(resident.Id) != null ||
                        store.ActiveNpcAction(partner.Id) != null || store.ActiveNpcTask(partner.Id) != null) continue;
                    foreach (var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach (var door in owner.Doors)
                        if (door.doorType == DoorTypes.Building && door.buildingKey == home.BuildingKey)
                        { selectedOwner = owner; selectedDoor = door; firstId = resident.Id; secondId = partner.Id; break; }
                    if (selectedOwner) break;
                }
                if (selectedOwner) break;
            }
            Require(selectedOwner, "Native catalog contains no eligible actual family home door.");
            int wakeHour = (int)typeof(StreetPopulation).GetField("wakeHour", Private).GetValue(streets);
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            double dawn = Math.Floor(now / 86400d) * 86400d + wakeHour * 3600d + 60d;
            if (dawn < now) dawn += 86400d;
            if (dawn > now) DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(dawn - now));
            TownSmokeHarness.EnterInterior(selectedOwner.transform, selectedDoor);
            until = Time.realtimeSinceStartup + 30f;
            while ((!game.PlayerEnterExit.IsPlayerInsideBuilding || !Find(firstId) || !Find(secondId)) &&
                Time.realtimeSinceStartup < until) yield return null;
            Require(game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerEnterExit.Interior.EntryDoor.buildingKey == selectedDoor.buildingKey &&
                Find(firstId) && Find(secondId), "Ordinary native home entry did not materialize both actual family members.");
            DaggerfallUI.Instance.PopToHUD();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BIOGRAPHY_HOME_SOURCE: building=" + selectedDoor.buildingKey +
                "; first=" + firstId + "; partner=" + secondId + "; actualHomeEntry=true; ordinaryMaterialization=true; dawn=" + dawn);
        }

        private static void FindPair(WorldStateStore store, out ResidentActor adult, out ResidentActor partner)
        {
            adult = null; partner = null;
            foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if (!Eligible(actor) || string.IsNullOrEmpty(actor.Resident.PartnerId) ||
                    store.ReadNpcBiography(actor.Resident.Id) != null) continue;
                var other = Find(actor.Resident.PartnerId);
                if (!Eligible(other) || store.ReadNpcBiography(other.Resident.Id) != null) continue;
                adult = actor; partner = other; return;
            }
        }
        private static bool Eligible(ResidentActor actor)
        { return actor && actor.Person && actor.Resident != null && actor.Resident.IsAlive &&
            actor.Resident.AgeGroup == ResidentAgeGroup.Adult && !actor.IsSleeping && !actor.IsConversing; }
        private static ResidentActor Find(string person)
        {
            foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if (actor && actor.Person && actor.Resident != null && actor.Resident.Id == person && actor.Resident.IsAlive)
                    return actor;
            return null;
        }
        private static WorldStateStore Store(PeopleOfTheIliacBayMod host)
        { return (WorldStateStore)Field(host, "store").GetValue(host); }
        private static FieldInfo Field(PeopleOfTheIliacBayMod host, string name)
        { return host.GetType().GetField(name, Private); }
        private static string Fingerprint(NpcBiographyRecord record)
        { return record == null ? null : record.PersonId + "|" + record.IdentityAnchor + "|" + record.FamilyId + "|" +
            record.CareerStart + "|" + record.FormativeExperience + "|" + record.Wish + "|" + string.Join("|", record.FamilyFacts.ToArray()); }
        private static bool Same(IList<string> first, IList<string> second)
        {
            if (first == null || second == null || first.Count != second.Count) return false;
            for (int i = 0; i < first.Count; i++) if (first[i] != second[i]) return false;
            return true;
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
