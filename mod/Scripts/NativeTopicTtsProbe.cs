using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in native-only check: one real TalkManager Work answer is appended by
    // DFU's own transcript method, then discovered by the TTS-only row scanner.
    internal static class NativeTopicTtsProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal const string RunFlag = "-peopleoftheiliacbay-native-topic-tts-probe";
        private const string OwnedCharacter = "Hakon Ironhand";
        private const string BaselineSave = "People - Gothway Garden";

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            Require(character == OwnedCharacter && fixtureSave == BaselineSave,
                "Native topic TTS probe requires its owned disposable character and baseline save.");
            Require(host && streets && streets.Ready && GameManager.Instance && GameManager.Instance.IsReady,
                "Native topic TTS probe requires a ready controlled game fixture.");
            Require(!Read<bool>(host, "localDialogueEnabled"),
                "Native topic TTS probe must run with LLM/D3 free text disabled.");
            var bridge = Read<BreezeTtsTrialBridge>(host, "breezeTtsTrial");
            Require(bridge && bridge.VoiceEnabled && !bridge.IsTrialMode,
                "Native topic TTS probe requires production NPC voice enabled and the test bridge disabled.");
            var store = Read<WorldStateStore>(host, "store");
            var game = GameManager.Instance;
            ResidentActor actor = null;
            yield return ConversationMoodProbe.WaitForUnusedManagedAdult(host,
                game.PlayerMotor.transform.position, value => actor = value, false);
            Require(actor && actor.Resident != null && actor.Resident.IsAlive,
                "Native topic TTS probe needs an eligible living adult native resident after the bounded actor wait.");

            Vector3 originalPosition = game.PlayerMotor.transform.position;
            DaggerfallTalkWindow talk = null;
            int requestsBeforeOpening = bridge.PublishedRequestCount;
            try
            {
                ConversationMoodProbe.StagePlayerNearActor(streets, actor, game);
                DaggerfallUI.Instance.PopToHUD();
                game.TalkManager.TalkToMobileNPC(actor.Person);
                float deadline = Time.realtimeSinceStartup + 25f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                    if (talk != null && talk.IsSetup && game.TalkManager.CurrentNPCType == TalkManager.NPCType.Mobile &&
                        game.TalkManager.MobileNPC == actor.Person && Read<DaggerfallTalkWindow>(host, "nativeTopicTtsWindow") == talk)
                        break;
                    yield return null;
                }
                Require(talk != null && talk.IsSetup && Read<string>(host, "nativeTopicTtsToken") != null,
                    "Native TTS-only Talk session did not bind the selected resident.");

                var transcript = typeof(PeopleOfTheIliacBayMod).GetField("conversationListField",
                    BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) as FieldInfo;
                var list = transcript == null ? null : transcript.GetValue(talk) as ListBox;
                Require(list != null, "Native topic TTS probe cannot read the native transcript.");
                ListBox.ListItem openingRow = list.Count == 1 ? list.GetItem(0) : null;
                Require(openingRow != null && openingRow.textLabel != null &&
                    openingRow.textColor != DaggerfallUI.DaggerfallQuestionTextColor,
                    "Native first-contact opening was not one exact vanilla transcript row.");
                object openingBinding = null;
                float openingDeadline = Time.realtimeSinceStartup + 180f;
                while (Time.realtimeSinceStartup < openingDeadline)
                {
                    IDictionary rows = Read<IDictionary>(host, "nativeTopicTtsReplayByRow");
                    openingBinding = rows[openingRow];
                    if (openingBinding != null) break;
                    yield return null;
                }
                Require(openingBinding != null && bridge.PublishedRequestCount == requestsBeforeOpening + 1,
                    "Fresh native opening did not create exactly one TTS request and exact-row replay binding.");
                Type openingBindingType = openingBinding.GetType();
                string openingRequestId = (string)openingBindingType.GetField("RequestId", BindingFlags.Instance | BindingFlags.Public).GetValue(openingBinding);
                string openingToken = (string)openingBindingType.GetField("ConversationToken", BindingFlags.Instance | BindingFlags.Public).GetValue(openingBinding);
                string openingPerson = (string)openingBindingType.GetField("PersonId", BindingFlags.Instance | BindingFlags.Public).GetValue(openingBinding);
                string openingBoundPerson, openingBoundToken, openingText;
                Require(bridge.TryReadRequestBinding(openingRequestId, out openingBoundPerson, out openingBoundToken, out openingText) &&
                    openingPerson == actor.Resident.Id && openingBoundPerson == openingPerson &&
                    openingBoundToken == openingToken && openingText == openingRow.textLabel.Text,
                    "Fresh native opening request was not bound to its exact row, resident, and session.");

                int firstNew = list.Count;
                int beforeRequests = bridge.PublishedRequestCount;

                var topic = new TalkManager.ListItem { questionType = TalkManager.QuestionType.Work };
                var toneField = typeof(DaggerfallTalkWindow).GetField("selectedTalkTone", Private);
                var tone = toneField == null ? DaggerfallTalkWindow.TalkTone.Normal :
                    (DaggerfallTalkWindow.TalkTone)toneField.GetValue(talk);
                string question = game.TalkManager.GetQuestionText(topic, tone);
                string answer = game.TalkManager.GetAnswerText(topic);
                Require(!string.IsNullOrWhiteSpace(question) && !string.IsNullOrWhiteSpace(answer),
                    "Native Work topic returned no question or answer text.");
                typeof(DaggerfallTalkWindow).GetMethod("SetQuestionAnswerPairInConversationListbox", Private)
                    .Invoke(talk, new object[] { question, answer });
                ListBox.ListItem answerRow = list.Count == firstNew + 2 ? list.GetItem(firstNew + 1) : null;
                Require(answerRow != null && answerRow.textLabel != null && answerRow.textLabel.Text == answer,
                    "Native topic answer was not appended as its exact native transcript row.");

                float requestDeadline = Time.realtimeSinceStartup + 120f;
                object binding = null;
                while (Time.realtimeSinceStartup < requestDeadline)
                {
                    IDictionary rows = Read<IDictionary>(host, "nativeTopicTtsReplayByRow");
                    binding = rows[answerRow];
                    if (binding != null) break;
                    yield return null;
                }
                Require(binding != null && bridge.PublishedRequestCount == beforeRequests + 1,
                    "Native topic answer did not create exactly one TTS request and exact-row replay binding.");
                Type bindingType = binding.GetType();
                string requestId = (string)bindingType.GetField("RequestId", BindingFlags.Instance | BindingFlags.Public).GetValue(binding);
                string bindingToken = (string)bindingType.GetField("ConversationToken", BindingFlags.Instance | BindingFlags.Public).GetValue(binding);
                string bindingPersonId = (string)bindingType.GetField("PersonId", BindingFlags.Instance | BindingFlags.Public).GetValue(binding);
                string personId, token, text;
                Require(bridge.TryReadRequestBinding(requestId, out personId, out token, out text) &&
                    personId == actor.Resident.Id && personId == bindingPersonId && token == bindingToken && text == answer,
                    "Native topic TTS request was not bound to the exact resident, session, and answer text.");
                Require(!Read<bool>(host, "conversationSessionOpen") && !Read<bool>(host, "localDialogueEnabled"),
                    "Native topic TTS probe unexpectedly opened the AI conversation path.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_TOPIC_TTS_PASS: ai-free=true; opening-request=true; " +
                    "source=TalkManager.Work; exact-answer-row=true; one-topic-request=true; replay-row-bound=true; person=" + personId +
                    "; request=" + requestId + "; playback=not-tested.");
            }
            finally
            {
                DaggerfallUI.Instance.PopToHUD();
                game.PlayerMotor.transform.position = originalPosition;
            }
        }

        private static T Read<T>(object target, string name)
        {
            FieldInfo field = typeof(PeopleOfTheIliacBayMod).GetField(name, Private);
            if (field == null) throw new InvalidOperationException("Native topic TTS probe field is missing: " + name);
            return (T)field.GetValue(target);
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
