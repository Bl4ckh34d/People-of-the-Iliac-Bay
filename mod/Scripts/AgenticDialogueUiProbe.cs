using System;
using System.Collections;
using System.IO;
using System.Reflection;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Native-window acceptance for combined LLM + D3 Talk presentation.
    // Runs only from the explicitly requested conversation-mood probe.
    internal static class AgenticDialogueUiProbe
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateMethod = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string RunFlag = "-peopleoftheiliacbay-conversation-mood-probe";

        internal static IEnumerator Verify(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk)
        {
            if (!host || talk == null || !talk.IsSetup || Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) < 0)
                throw new InvalidOperationException("Agentic dialogue UI probe requires an open Talk window and its opt-in run flag.");

            FieldInfo localDialogueEnabledField = HostField("localDialogueEnabled");
            FieldInfo routingSessionField = HostField("decisionRoutingSession");
            FieldInfo dialogueWasOpenField = HostField("dialogueWasOpen");
            FieldInfo conversationSessionOpenField = HostField("conversationSessionOpen");
            FieldInfo returningGreetingPendingField = HostField("returningGreetingPending");
            FieldInfo prewarmEnabledField = HostField("dialoguePrewarmEnabled");
            FieldInfo inputField = HostField("dialogueInput");
            FieldInfo inputWindowField = HostField("dialogueInputWindow");
            FieldInfo questionLabelHostField = HostField("dialogueQuestionLabel");
            FieldInfo mainPanelField = TalkField("mainPanel");
            FieldInfo nativeBackgroundField = TalkField("textureBackground");
            FieldInfo toneField = TalkField("selectedTalkTone");
            FieldInfo markerField = TalkField("panelTone");
            FieldInfo[] buttonFields = {
                TalkField("buttonCheckboxTonePolite"),
                TalkField("buttonCheckboxToneNormal"),
                TalkField("buttonCheckboxToneBlunt")
            };

            MethodInfo ensureInput = typeof(PeopleOfTheIliacBayMod).GetMethod("EnsureDialogueInput", PrivateMethod);
            MethodInfo updateDialogue = typeof(PeopleOfTheIliacBayMod).GetMethod("UpdateEmbeddedDialogue", PrivateMethod);
            MethodInfo updateBounds = typeof(PeopleOfTheIliacBayMod).GetMethod("UpdateDialogueInputBounds", PrivateMethod);
            MethodInfo setChooser = typeof(PeopleOfTheIliacBayMod).GetMethod("SetAgenticToneChooser", PrivateMethod);
            MethodInfo refreshQuestion = typeof(PeopleOfTheIliacBayMod).GetMethod("RefreshAgenticNativeQuestion",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo setCaret = typeof(TextBox).GetMethod("SetCursorPosition", PrivateMethod);
            if (ensureInput == null || updateDialogue == null || updateBounds == null || setChooser == null || setCaret == null)
                throw new InvalidOperationException("Agentic dialogue UI policy or input method is missing.");

            if (!ReferenceEquals(inputWindowField.GetValue(host), talk))
                ensureInput.Invoke(host, new object[] { talk });
            var input = inputField.GetValue(host) as WrappedDialogueInput;
            var questionLabel = questionLabelHostField.GetValue(host) as TextLabel;
            var mainPanel = mainPanelField.GetValue(talk) as Panel;
            var marker = markerField.GetValue(talk) as BaseScreenComponent;
            if (input == null || questionLabel == null || mainPanel == null || marker == null)
                throw new InvalidOperationException("Native Talk controls are missing from the probe window.");
            var buttons = new Button[buttonFields.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i] = buttonFields[i].GetValue(talk) as Button;
                if (buttons[i] == null) throw new InvalidOperationException("Native tone button is missing.");
            }

            bool oldLlmEnabled = Convert.ToBoolean(localDialogueEnabledField.GetValue(host));
            object oldSession = routingSessionField.GetValue(host);
            var probeSession = new LocalDecisionSession
            {
                ExpectedModelId = "probe-ui-only",
                Enabled = true
            };
            bool oldDialogueWasOpen = Convert.ToBoolean(dialogueWasOpenField.GetValue(host));
            bool oldConversationSessionOpen = Convert.ToBoolean(conversationSessionOpenField.GetValue(host));
            bool oldGreetingPending = Convert.ToBoolean(returningGreetingPendingField.GetValue(host));
            bool oldPrewarmEnabled = Convert.ToBoolean(prewarmEnabledField.GetValue(host));
            bool oldInputEnabled = input.Enabled;
            bool oldQuestionEnabled = questionLabel.Enabled;
            Vector2 oldInputPosition = input.Position;
            Vector2 oldInputSize = input.Size;
            string oldInputText = input.Text;
            int oldCaret = input.Caret;
            bool oldInputFocus = input.HasFocus();
            object oldTone = toneField.GetValue(talk);
            Texture2D nativeBackground = nativeBackgroundField.GetValue(talk) as Texture2D;
            bool[] oldButtonEnabled = new bool[buttons.Length];
            for (int i = 0; i < buttons.Length; i++) oldButtonEnabled[i] = buttons[i].Enabled;
            bool oldMarkerEnabled = marker.Enabled;

            try
            {
                routingSessionField.SetValue(host, probeSession);
                dialogueWasOpenField.SetValue(host, true);
                conversationSessionOpenField.SetValue(host, true);
                returningGreetingPendingField.SetValue(host, false);
                prewarmEnabledField.SetValue(host, false);

                // Establish the vanilla baseline and capture what exact state the
                // production policy must restore when either feature is disabled.
                localDialogueEnabledField.SetValue(host, false);
                updateDialogue.Invoke(host, null);
                Require(ReferenceEquals(mainPanel.BackgroundTexture, nativeBackground),
                    "Native mode did not retain its original Talk background.");
                Require(Mathf.Abs(input.Size.x - questionLabel.MaxWidth) < 0.01f,
                    "Native mode did not retain the original dialogue input width.");
                object vanillaTone = toneField.GetValue(talk);
                bool[] vanillaButtons = new bool[buttons.Length];
                for (int i = 0; i < buttons.Length; i++) vanillaButtons[i] = buttons[i].Enabled;
                bool vanillaMarker = marker.Enabled;

                // First disable LLM while D3 remains enabled.
                localDialogueEnabledField.SetValue(host, false);
                probeSession.Enabled = true;
                updateDialogue.Invoke(host, null);
                RequireVanilla(talk, mainPanel, nativeBackground, toneField, input, questionLabel, vanillaTone, buttons,
                    vanillaButtons, marker, vanillaMarker, "LLM off");

                // Disabling the optional planner must not restore controls that
                // conflict with LLM-owned conversational tone.
                localDialogueEnabledField.SetValue(host, true);
                probeSession.Enabled = false;
                updateDialogue.Invoke(host, null);
                Require(!ReferenceEquals(mainPanel.BackgroundTexture, nativeBackground),
                    "LLM mode with D3 off did not replace the Talk background.");
                Require((DaggerfallTalkWindow.TalkTone)toneField.GetValue(talk) == DaggerfallTalkWindow.TalkTone.Normal,
                    "LLM mode with D3 off left a non-neutral native tone selected.");
                for (int i = 0; i < buttons.Length; i++)
                    Require(!buttons[i].Enabled, "LLM mode with D3 off left a tone button enabled: " + buttonFields[i].Name + ".");
                Require(!marker.Enabled, "LLM mode with D3 off left the tone marker enabled.");

                // Exercise the actual production gate with a valid local-only
                // session. No transport request is sent by this UI-only probe.
                probeSession.Enabled = true;
                toneField.SetValue(talk, DaggerfallTalkWindow.TalkTone.Blunt);
                localDialogueEnabledField.SetValue(host, true);
                updateDialogue.Invoke(host, null);
                Require(!ReferenceEquals(mainPanel.BackgroundTexture, nativeBackground),
                    "Both enabled settings did not replace the Talk background.");
                Require((DaggerfallTalkWindow.TalkTone)toneField.GetValue(talk) == DaggerfallTalkWindow.TalkTone.Normal,
                    "Combined mode left a non-neutral native tone selected.");
                for (int i = 0; i < buttons.Length; i++)
                    Require(!buttons[i].Enabled, "Combined mode left a tone button enabled: " + buttonFields[i].Name + ".");
                Require(!marker.Enabled, "Combined mode left the tone marker enabled.");

                float expandedWidth = Mathf.Max(questionLabel.MaxWidth, 310f - questionLabel.Position.x);
                Require(Mathf.Abs(input.Size.x - expandedWidth) < 0.01f &&
                    Mathf.Abs(input.Position.x + input.Size.x - (questionLabel.Position.x + expandedWidth)) < 0.01f &&
                    input.Size.x > questionLabel.MaxWidth,
                    "Combined mode did not expand the existing dialogue input to the right inset.");
                string sample = new string('W', 220);
                input.Text = sample;
                setCaret.Invoke(input, new object[] { 0 });
                int expandedClickCaret = input.CaretAtPosition(new Vector2(input.Size.x - 3f, 1f));
                int expandedLineCount = input.LineCount;

                // Directly exercise the same layout method for the native-width
                // case; production UpdateEmbeddedDialogue restores this whenever
                // either gate is off, which was verified above.
                updateBounds.Invoke(host, new object[] { talk, false });
                Require(Mathf.Abs(input.Size.x - questionLabel.MaxWidth) < 0.01f,
                    "Vanilla mode did not restore the dialogue input width.");
                setCaret.Invoke(input, new object[] { 0 });
                int vanillaClickCaret = input.CaretAtPosition(new Vector2(input.Size.x - 3f, 1f));
                int vanillaLineCount = input.LineCount;
                Require(expandedLineCount < vanillaLineCount && expandedClickCaret > vanillaClickCaret,
                    "Expanded input width did not change wrapping and right-edge click hit-testing.");
                input.Text = oldInputText;
                setCaret.Invoke(input, new object[] { oldCaret });
                updateDialogue.Invoke(host, null);
                Require(input.Text == oldInputText && input.HasFocus() == oldInputFocus,
                    "Expanded input UI probe changed the player's draft or focus.");

                ScreenCapture.CaptureScreenshot(Path.Combine(Application.persistentDataPath,
                    "PeopleOfTheIliacBay-agentic-dialogue-tone-hidden.png"));
                yield return new WaitForEndOfFrame();
            }
            finally
            {
                localDialogueEnabledField.SetValue(host, oldLlmEnabled);
                routingSessionField.SetValue(host, oldSession);
                probeSession.Dispose();
                dialogueWasOpenField.SetValue(host, oldDialogueWasOpen);
                conversationSessionOpenField.SetValue(host, oldConversationSessionOpen);
                returningGreetingPendingField.SetValue(host, oldGreetingPending);
                prewarmEnabledField.SetValue(host, oldPrewarmEnabled);

                setChooser.Invoke(host, new object[] { talk, oldLlmEnabled });
                updateBounds.Invoke(host, new object[] { talk, agenticToneSuppressedFor(host, talk) });
                toneField.SetValue(talk, oldTone);
                if (refreshQuestion != null) refreshQuestion.Invoke(null, new object[] { talk });
                input.Text = oldInputText;
                setCaret.Invoke(input, new object[] { oldCaret });
                input.Position = oldInputPosition;
                input.Size = oldInputSize;
                input.Enabled = oldInputEnabled;
                questionLabel.Enabled = oldQuestionEnabled;
                for (int i = 0; i < buttons.Length; i++) buttons[i].Enabled = oldButtonEnabled[i];
                marker.Enabled = oldMarkerEnabled;
            }

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_AGENTIC_DIALOGUE_UI_PASS: production-settings-gate=true; llm-off-restores-native=true; d3-off-keeps-agentic=true; alternate-background=true; tone-chooser-hidden=true; neutral-tone=true; expanded-wrap-and-click=true; draft-focus-restored=true.");
        }

        private static bool agenticToneSuppressedFor(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk)
        {
            FieldInfo suppressed = HostField("agenticToneSuppressed");
            FieldInfo window = HostField("agenticToneWindow");
            return Convert.ToBoolean(suppressed.GetValue(host)) && ReferenceEquals(window.GetValue(host), talk);
        }

        private static void RequireVanilla(DaggerfallTalkWindow talk, Panel mainPanel, Texture2D nativeBackground,
            FieldInfo toneField,
            WrappedDialogueInput input, TextLabel questionLabel, object nativeTone, Button[] buttons, bool[] nativeButtons,
            BaseScreenComponent marker, bool nativeMarker, string caseName)
        {
            Require(ReferenceEquals(mainPanel.BackgroundTexture, nativeBackground), caseName + " changed the native Talk background.");
            Require(Equals(toneField.GetValue(talk), nativeTone), caseName + " changed the selected native tone.");
            Require(Mathf.Abs(input.Size.x - questionLabel.MaxWidth) < 0.01f, caseName + " left the expanded input width active.");
            Require(marker.Enabled == nativeMarker, caseName + " changed the tone marker state.");
            for (int i = 0; i < buttons.Length; i++)
                Require(buttons[i].Enabled == nativeButtons[i], caseName + " changed a native tone button state.");
        }

        private static FieldInfo HostField(string name)
        {
            FieldInfo field = typeof(PeopleOfTheIliacBayMod).GetField(name, PrivateInstance);
            if (field == null) throw new InvalidOperationException("Dialogue host field is missing: " + name + ".");
            return field;
        }

        private static FieldInfo TalkField(string name)
        {
            FieldInfo field = typeof(DaggerfallTalkWindow).GetField(name, PrivateInstance);
            if (field == null) throw new InvalidOperationException("Native Talk field is missing: " + name + ".");
            return field;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
