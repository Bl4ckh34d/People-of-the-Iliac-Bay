using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class DialogueClockProbe
    {
        private const string LegacyFlag = "-peopleoftheiliacbay-clock-legacy-probe";

        // The town smoke harness runs with the live dialogue backend enabled.
        // Start a single asynchronous turn; never grade model wording here.
        public static IEnumerator Run(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk, ListBox transcript)
        {
            if (HasArgument(LegacyFlag))
            {
                RunLegacy(host, talk, transcript);
                yield break;
            }
            if (host == null || talk == null || transcript == null)
                throw new ArgumentNullException("The AI-on clock probe requires the active dialogue window and transcript.");
            yield return VerifyRoutedClockTurn(host, talk, transcript);
        }

        private static IEnumerator VerifyRoutedClockTurn(PeopleOfTheIliacBayMod host,
            DaggerfallTalkWindow talk, ListBox transcript)
        {
            Type hostType = typeof(PeopleOfTheIliacBayMod);
            MethodInfo submit = hostType.GetMethod("SubmitFreeText", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo dialogueField = hostType.GetField("localDialogue", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo snapshotField = hostType.GetField("conversationContextCurrentSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo capturedTextField = hostType.GetField("conversationContextCapturedPlayerText", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo busyField = hostType.GetField("conversationBusy", BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo requestCountProperty = typeof(LocalDialogueClient).GetProperty("ReplyRequestCount",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo completionCountProperty = typeof(LocalDialogueClient).GetProperty("ReplyCompletionCount",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (submit == null || dialogueField == null || snapshotField == null || capturedTextField == null || busyField == null ||
                requestCountProperty == null || completionCountProperty == null)
                throw new InvalidOperationException("AI-on clock probe could not find its existing dialogue observation points.");

            var client = dialogueField.GetValue(host) as LocalDialogueClient;
            if (client == null) throw new InvalidOperationException("AI-on clock probe has no active LocalDialogueClient.");

            var previousSnapshot = snapshotField.GetValue(host) as ConversationContextSnapshot;
            long previousRevision = previousSnapshot == null || previousSnapshot.Binding == null
                ? -1 : previousSnapshot.Binding.RequestRevision;
            string previousSession = previousSnapshot == null || previousSnapshot.Binding == null
                ? null : previousSnapshot.Binding.SessionId;
            var submittedTime = DaggerfallUnity.Instance.WorldTime.Now;
            string expectedClock = ConversationClock.Context(submittedTime.Hour, submittedTime.Minute,
                submittedTime.DateString(), submittedTime.Year, submittedTime.IsNight);
            long requestsBefore = Convert.ToInt64(requestCountProperty.GetValue(client, null));
            long completionsBefore = Convert.ToInt64(completionCountProperty.GetValue(client, null));
            int matchingQuestionsBefore = CountFinalQuestionRows(transcript, "What time is it?");
            const string question = "What time is it?";

            submit.Invoke(host, new object[] { talk, question });

            float deadline = Time.realtimeSinceStartup + 120f;
            int questionIndex, answerIndex;
            while (Time.realtimeSinceStartup < deadline &&
                (client.IsBusy || Convert.ToBoolean(busyField.GetValue(host)) ||
                 Convert.ToInt64(completionCountProperty.GetValue(client, null)) != completionsBefore + 1 ||
                 CountFinalQuestionRows(transcript, question) != matchingQuestionsBefore + 1 ||
                 !TryFindFinalReplyPair(transcript, question, out questionIndex, out answerIndex)))
                yield return null;

            var snapshot = snapshotField.GetValue(host) as ConversationContextSnapshot;
            string capturedClock;
            long requestsAfter = Convert.ToInt64(requestCountProperty.GetValue(client, null));
            if (snapshot == null || snapshot.Binding == null || snapshot.Binding.RequestRevision <= previousRevision ||
                (previousSession != null && !string.Equals(snapshot.Binding.SessionId, previousSession, StringComparison.Ordinal)) ||
                snapshot.ClockContext == null || !snapshot.ClockContext.TryGetVerified(out capturedClock) ||
                snapshot.ClockContext.Source != ConversationContextFactSource.LiveObservation ||
                !string.Equals(capturedClock, expectedClock, StringComparison.Ordinal) ||
                !string.Equals(capturedTextField.GetValue(host) as string, question, StringComparison.Ordinal))
                throw new InvalidOperationException("AI-on clock turn did not retain the exact native WorldTime in its fresh bound context snapshot.");
            if (requestsAfter != requestsBefore + 1)
                throw new InvalidOperationException("AI-on clock turn did not dispatch exactly one normal reply request; before=" +
                    requestsBefore + "; after=" + requestsAfter + ".");
            if (client.IsBusy || Convert.ToBoolean(busyField.GetValue(host)) ||
                Convert.ToInt64(completionCountProperty.GetValue(client, null)) != completionsBefore + 1 ||
                CountFinalQuestionRows(transcript, question) != matchingQuestionsBefore + 1 ||
                !TryFindFinalReplyPair(transcript, question, out questionIndex, out answerIndex))
                throw new InvalidOperationException("AI-on clock turn did not finish with one fresh model completion and a finalized transcript pair.");

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_CLOCK_RAW_REPLY: capturedClock=" + expectedClock +
                "; finalizedReply=" + transcript.GetItem(answerIndex).textLabel.Text);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,
                "PeopleOfTheIliacBay-dialogue-clock-reply.png"));
            yield return new WaitForEndOfFrame();
            yield return null;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_CLOCK_PASS: one AI-on time question retained the exact captured WorldTime in its bound context, completed one fresh reply request, and appended a finalized pair.");
        }

        // Kept for explicit offline/native-clock regression checks. This validates
        // the deterministic helper only, not the AI-on routed conversation path.
        private static void RunLegacy(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk, ListBox transcript)
        {
            var submit = typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText", BindingFlags.Instance | BindingFlags.NonPublic);
            if (submit == null) throw new InvalidOperationException("Legacy clock probe could not find SubmitFreeText.");
            if (host == null) throw new InvalidOperationException("Legacy clock probe could not find the mod host.");
            FieldInfo enabledField = typeof(PeopleOfTheIliacBayMod).GetField("localDialogueEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
            if (enabledField == null || Convert.ToBoolean(enabledField.GetValue(host)))
                throw new InvalidOperationException("The deterministic legacy clock probe is allowed only with local dialogue disabled.");
            foreach (string question in new[] { "What time is it?", "What is the current time?", "What date is it?" })
            {
                var now = DaggerfallUnity.Instance.WorldTime.Now;
                string expected = ConversationClock.Answer(now.MinTimeString(), now.DateString(), now.Year);
                int before = transcript.Count;
                submit.Invoke(host, new object[] { talk, question });
                if (transcript.Count != before + 2 || transcript.GetItem(before + 1).textLabel.Text != expected)
                    throw new InvalidOperationException("Legacy clock reply did not match native WorldTime: " + question);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_CLOCK_LEGACY_PASS: deterministic helper matched native clock.");
        }

        private static bool HasArgument(string value)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool TryFindFinalReplyPair(ListBox transcript, string question, out int questionIndex, out int answerIndex)
        {
            questionIndex = -1;
            answerIndex = -1;
            if (transcript == null) return false;
            for (int index = transcript.Count - 1; index >= 0; index--)
            {
                ListBox.ListItem row = transcript.GetItem(index);
                if (row == null || row.textLabel == null || IsEphemeralRow(row) ||
                    !string.Equals(row.textLabel.Text, question, StringComparison.Ordinal)) continue;
                for (int next = index + 1; next < transcript.Count; next++)
                {
                    ListBox.ListItem candidate = transcript.GetItem(next);
                    if (candidate == null || candidate.textLabel == null || IsEphemeralRow(candidate)) continue;
                    if (string.IsNullOrWhiteSpace(candidate.textLabel.Text) || IsUnavailableRow(candidate)) return false;
                    questionIndex = index;
                    answerIndex = next;
                    return true;
                }
                return false;
            }
            return false;
        }

        private static int CountFinalQuestionRows(ListBox transcript, string question)
        {
            int count = 0;
            if (transcript == null) return count;
            for (int index = 0; index < transcript.Count; index++)
            {
                ListBox.ListItem row = transcript.GetItem(index);
                if (row != null && row.textLabel != null && !IsEphemeralRow(row) &&
                    string.Equals(row.textLabel.Text, question, StringComparison.Ordinal)) count++;
            }
            return count;
        }

        private static bool IsEphemeralRow(ListBox.ListItem row)
        { return row.textLabel.Name == "PeopleOfTheIliacBayPendingEphemeral"; }

        private static bool IsUnavailableRow(ListBox.ListItem row)
        { return row.textLabel.Name == "PeopleOfTheIliacBayUnavailableReply"; }
    }
}
