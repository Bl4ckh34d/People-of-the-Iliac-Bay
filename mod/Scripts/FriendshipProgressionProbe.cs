using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Native acceptance for IMM-10. This exercises the existing typed tone seam;
    // it does not classify free-text meaning or prove semantic classification.
    internal static class FriendshipProgressionProbe
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;
        private const string BaselineSave = "People - Gothway Garden";
        private const string FixtureCharacter = "PeopleOfTheIliacBayTest-9a5f197b";

        private sealed class Evidence
        {
            internal string PersonId;
            internal int HomeKey;
            internal double BaselineClock;
            internal double TestStartClock;
            internal double IntervalFactor;
            internal double Interval0Seconds;
            internal double Interval1Seconds;
            internal double Interval2Seconds;
            internal int StartContactMinute;
            internal string BaselineAttitude;
            internal string CheckpointTitle;
            internal double FractionalCredit;
            internal int EarnedAtCheckpoint;
            internal int StageAtCheckpoint;
            internal int ReopenedEncounters;
            internal int FinalOffset;
        }

        private sealed class HomeDayFixture
        {
            internal Household Home;
            internal Resident Resident;
            internal double IntervalFactor;
            internal double Interval0Seconds;
            internal double Interval1Seconds;
            internal double Interval2Seconds;
            internal int StartContactMinute;
        }

        internal static IEnumerator RunFixture(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            RequireOwnedFixture(character, fixtureSave);
            if (!host || !streets || !GameManager.Instance || !GameManager.Instance.IsReady || !streets.Ready)
                throw new InvalidOperationException("Friendship progression probe requires the ready owned native fixture.");

            Evidence evidence = new Evidence();
            Exception testFailure = null;
            Exception restoreFailure = null;
            IEnumerator test = DriveFlattened(RunCases(host, streets, character, evidence));
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try { more = test.MoveNext(); if (more) current = test.Current; }
                    catch (Exception error) { testFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally { Dispose(test, ref testFailure); }

            IEnumerator restore = DriveFlattened(RestoreBaseline(host, streets, character, evidence));
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try { more = restore.MoveNext(); if (more) current = restore.Current; }
                    catch (Exception error) { restoreFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally { Dispose(restore, ref restoreFailure); }

            if (restoreFailure != null)
            {
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_FRIENDSHIP_BASELINE_RESTORE_FAIL: " + restoreFailure);
                throw new InvalidOperationException("Friendship probe could not restore the exact baseline fixture.", restoreFailure);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FRIENDSHIP_BASELINE_RESTORED: character=" + character +
                "; save=" + BaselineSave + "; npc=" + (evidence.PersonId ?? "unselected") +
                "; fixture-attitude-restored=" + (evidence.PersonId != null) + "; clock-restored=true.");
            if (testFailure != null)
                throw new InvalidOperationException("Friendship progression probe failed after baseline restore.", testFailure);

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FRIENDSHIP_PROGRESSION_PASS: npc=" + evidence.PersonId +
                "; native-talk=begin-close-reopen; typed-tone=polite; initial-encounter-cap=5; shared-credit=true; " +
                "repeated-utterance=no-gain; elapsed-alone=no-gain; nominal-refill=12h-fractional,24h,30h,36h; " +
                "disk-save-reload=true; checkpoint-credit=" + evidence.FractionalCredit.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; checkpoint-stage=" + evidence.StageAtCheckpoint + "; final-offset=" + evidence.FinalOffset +
                "; interval-factor=" + FormatNumber(evidence.IntervalFactor) +
                "; base-interval-hours=24,30,36; personal-interval-hours=" + IntervalProfileDescription(evidence.Interval0Seconds,evidence.Interval1Seconds,evidence.Interval2Seconds) +
                "; stage2-hard-cap-hours=36; nominal-contact-offset-hours=0,12,24,27,57,93; effective-contact-offset-hours=" + ContactOffsetsHoursDescription(evidence.Interval0Seconds,evidence.Interval1Seconds,evidence.Interval2Seconds) +
                "; home-only-contact-window=" + ContactWindowDescription(evidence.TestStartClock,
                    evidence.Interval0Seconds,evidence.Interval1Seconds,evidence.Interval2Seconds) +
                "; chosen-start-minute=" + evidence.StartContactMinute + "; test-start=" + evidence.TestStartClock +
                "; semantic-classification=not-tested.");
        }

        private static IEnumerator RunCases(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, Evidence evidence)
        {
            GameManager game = GameManager.Instance;
            SaveLoadManager saves = game.SaveLoadManager;
            saves.EnumerateSaves();
            int baselineSlot = saves.FindSaveFolderByNames(character, BaselineSave);
            if (baselineSlot < 0) throw new InvalidOperationException("Exact Gothway Garden baseline save is missing; refusing friendship probe.");

            evidence.BaselineClock = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            WorldState initialBaseline = (WorldState)host.GetSaveData();
            RuntimeOptions options = RequireField(typeof(PeopleOfTheIliacBayMod), "options").GetValue(host) as RuntimeOptions;
            if (options == null) throw new InvalidOperationException("Friendship probe cannot read the active resident wake/sleep window.");
            HomeDayFixture fixture = SelectHomeDayFixture(initialBaseline,
                game.StreamingWorld.GetCurrentBuildingDirectory().MapID, options.WakeHour, options.SleepHour);
            evidence.PersonId = fixture.Resident.Id;
            evidence.HomeKey = fixture.Home.BuildingKey;
            evidence.IntervalFactor = fixture.IntervalFactor;
            evidence.Interval0Seconds = fixture.Interval0Seconds;
            evidence.Interval1Seconds = fixture.Interval1Seconds;
            evidence.Interval2Seconds = fixture.Interval2Seconds;
            evidence.StartContactMinute = fixture.StartContactMinute;
            evidence.BaselineAttitude = SerializeAttitude(FindAttitude(initialBaseline, evidence.PersonId));
            Require(evidence.BaselineAttitude == null, "Selected HomeDay fixture already has baseline friendship state.");
            ResidentActor actor = null;
            AdvanceToNextHomeContactWindow(fixture.StartContactMinute);
            yield return EnterHomeAndWaitForResident(game, streets, fixture, value => actor = value);
            // The resident is first observed in the exact native home interior.
            // All later clock contacts stay in this room and use this identity.
            yield return WaitForHomeManagedAdult(streets, evidence.PersonId, evidence.HomeKey, value => actor = value);
            double nativeWindowStart = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            if (!ContactWindowIsAwake(nativeWindowStart, evidence.Interval0Seconds, evidence.Interval1Seconds,
                evidence.Interval2Seconds, options.WakeHour, options.SleepHour))
            {
                AdvanceToNextHomeContactWindow(fixture.StartContactMinute);
                yield return WaitForHomeManagedAdult(streets, evidence.PersonId, evidence.HomeKey, value => actor = value);
            }
            WorldState selectionState = (WorldState)host.GetSaveData();
            Require(FindAttitude(selectionState, evidence.PersonId) == null,
                "Selected HomeDay fixture acquired friendship state while the player entered its home.");
            evidence.TestStartClock = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            Require(ContactWindowIsAwake(evidence.TestStartClock, evidence.Interval0Seconds, evidence.Interval1Seconds,
                evidence.Interval2Seconds, options.WakeHour, options.SleepHour),
                "Native home entry elapsed enough game time to move the scaled contact sequence outside the awake window.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FRIENDSHIP_HOME_FIXTURE_SELECTED: npc=" + evidence.PersonId +
                "; baseline-clock=" + evidence.BaselineClock + "; first-contact-clock=" + evidence.TestStartClock +
                "; home=" + evidence.HomeKey + "; routine=HomeDay; native-interior=true; interval-factor=" + FormatNumber(evidence.IntervalFactor) +
                "; base-interval-hours=24,30,36; personal-interval-hours=" + IntervalProfileDescription(evidence.Interval0Seconds,evidence.Interval1Seconds,evidence.Interval2Seconds) +
                "; stage2-hard-cap-hours=36; nominal-contact-offset-hours=0,12,24,27,57,93; effective-contact-offset-hours=" + ContactOffsetsHoursDescription(evidence.Interval0Seconds,evidence.Interval1Seconds,evidence.Interval2Seconds) +
                "; contact-window=" + ContactWindowDescription(evidence.TestStartClock, evidence.Interval0Seconds,evidence.Interval1Seconds,evidence.Interval2Seconds) +
                "; chosen-start-minute=" + evidence.StartContactMinute + ".");

            MethodInfo begin = RequireMethod(typeof(PeopleOfTheIliacBayMod), "BeginGroundedConversation", 0);
            MethodInfo beginMemory = RequireMethod(typeof(PeopleOfTheIliacBayMod), "BeginDialogueMemory", 1);
            MethodInfo apply = RequireMethod(typeof(PeopleOfTheIliacBayMod), "ApplyConversationDisposition", 2);
            FieldInfo personField = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationPersonId");
            FieldInfo sessionField = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationSessionOpen");
            FieldInfo memoryWindowField = RequireField(typeof(PeopleOfTheIliacBayMod), "memoryWindow");

            DaggerfallTalkWindow talk = null;
            yield return OpenTalk(host, game, streets, actor, evidence.HomeKey, begin, beginMemory, sessionField, memoryWindowField,
                personField, value => talk = value);
            RequireCurrentPerson(host, personField, evidence.PersonId, talk);
            Require(FindCurrentAttitude(host, evidence.PersonId).PositiveThisEncounter == 0,
                "First native encounter did not begin with zero positive points.");

            const string repeated = "IMM10-native-repeat-control";
            Apply(host, apply, evidence.PersonId, repeated, 1);
            Apply(host, apply, evidence.PersonId, repeated, 0);
            for (int index = 0; index < 4; index++)
                Apply(host, apply, evidence.PersonId, "IMM10-native-initial-" + index, 1);
            Apply(host, apply, evidence.PersonId, "IMM10-native-sixth-same-encounter", 0);
            ConversationAttitude initial = FindCurrentAttitude(host, evidence.PersonId);
            Require(initial.Offset == 5 && initial.PositiveEarned == 5 && initial.PositiveCredit == 0 &&
                initial.PositiveThisEncounter == 5, "Initial native encounter did not stop at five positive points.");
            int firstEncounterCount = initial.EncounterCount;

            yield return CloseTalk(host, sessionField, memoryWindowField);
            actor = FindHomeManagedAdult(streets, evidence.PersonId, evidence.HomeKey);
            if (!actor) throw new InvalidOperationException("The original registered HomeDay NPC did not remain available after Talk closed.");
            yield return OpenTalk(host, game, streets, actor, evidence.HomeKey, begin, beginMemory, sessionField, memoryWindowField,
                personField, value => talk = value);
            RequireCurrentPerson(host, personField, evidence.PersonId, talk);
            ConversationAttitude reopened = FindCurrentAttitude(host, evidence.PersonId);
            Require(reopened.EncounterCount == firstEncounterCount + 1 && reopened.PositiveThisEncounter == 0 &&
                reopened.PositiveCredit == 0 && reopened.PositiveEarned == 5 && reopened.Offset == 5,
                "Close/reopen reset shared credit or failed to record a fresh native encounter.");
            Apply(host, apply, evidence.PersonId, "IMM10-native-reopen-no-credit", 0);
            Require(FindCurrentAttitude(host, evidence.PersonId).Offset == 5,
                "Reopened native encounter gained disposition without shared positive credit.");
            evidence.ReopenedEncounters = FindCurrentAttitude(host, evidence.PersonId).EncounterCount;
            yield return CloseTalk(host, sessionField, memoryWindowField);

            // Advancing only the game clock must not mutate stored credit.
            double creditBeforeTwelveHourAdvance = FindCurrentAttitude(host, evidence.PersonId).PositiveCredit;
            AdvanceInterval(evidence, evidence.Interval0Seconds / 2d);
            ConversationAttitude elapsedOnly = FindCurrentAttitude(host, evidence.PersonId);
            Require(Math.Abs(elapsedOnly.PositiveCredit - creditBeforeTwelveHourAdvance) < 0.000001 &&
                elapsedOnly.Offset == 5 && elapsedOnly.PositiveEarned == 5 && elapsedOnly.PositiveCredit < 1,
                "Elapsed game time alone changed stored positive credit.");
            yield return WaitForHomeManagedAdult(streets, evidence.PersonId, evidence.HomeKey, value => actor = value);
            yield return OpenTalk(host, game, streets, actor, evidence.HomeKey, begin, beginMemory, sessionField, memoryWindowField,
                personField, value => talk = value);
            Apply(host, apply, evidence.PersonId, "IMM10-native-twelve-hour-refill-observe", 1);
            ConversationAttitude afterFirstTwelveHourPoint = FindCurrentAttitude(host, evidence.PersonId);
            Require(afterFirstTwelveHourPoint.Offset == 6 && afterFirstTwelveHourPoint.PositiveEarned == 6 &&
                afterFirstTwelveHourPoint.PositiveProgressionStage == 0 && afterFirstTwelveHourPoint.PositiveStageProgress == 1 &&
                afterFirstTwelveHourPoint.PositiveCredit >= 1.49 && afterFirstTwelveHourPoint.PositiveCredit <= 1.51,
                "The scaled 12-hour stage-zero interval did not expose 1.5 credit after one point was spent from the 2.5-point refill.");
            Apply(host, apply, evidence.PersonId, "IMM10-native-twelve-hour-0", 1);
            ConversationAttitude afterTwelve = FindCurrentAttitude(host, evidence.PersonId);
            Require(afterTwelve.Offset == 7 && afterTwelve.PositiveEarned == 7 &&
                afterTwelve.PositiveCredit >= 0.49 && afterTwelve.PositiveCredit <= 0.51 &&
                afterTwelve.PositiveProgressionStage == 0 && afterTwelve.PositiveStageProgress == 2,
                "Twelve game hours did not refill fractional credit at the 24-hour rate.");
            yield return CloseTalk(host, sessionField, memoryWindowField);

            // Reach stage one at 24 hours, then retain half credit under its
            // 30-hour refill interval for a non-zero-stage disk checkpoint.
            AdvanceInterval(evidence, evidence.Interval0Seconds / 2d);
            Require(FindCurrentAttitude(host, evidence.PersonId).Offset == 7,
                "Elapsed-only advance to the 24-hour mark granted disposition.");
            yield return WaitForHomeManagedAdult(streets, evidence.PersonId, evidence.HomeKey, value => actor = value);
            yield return OpenTalk(host, game, streets, actor, evidence.HomeKey, begin, beginMemory, sessionField, memoryWindowField,
                personField, value => talk = value);
            for (int index = 0; index < 3; index++) Apply(host, apply, evidence.PersonId, "IMM10-native-stage-zero-" + index, 1);
            ConversationAttitude atStageOne = FindCurrentAttitude(host, evidence.PersonId);
            Require(atStageOne.Offset == 10 && atStageOne.PositiveEarned == 10 && atStageOne.PositiveProgressionStage == 1,
                "The 24-hour refill did not advance friendship to progression stage one.");
            yield return CloseTalk(host, sessionField, memoryWindowField);

            AdvanceInterval(evidence, evidence.Interval1Seconds / 10d);
            yield return WaitForHomeManagedAdult(streets, evidence.PersonId, evidence.HomeKey, value => actor = value);
            yield return OpenTalk(host, game, streets, actor, evidence.HomeKey, begin, beginMemory, sessionField, memoryWindowField,
                personField, value => talk = value);
            Apply(host, apply, evidence.PersonId, "IMM10-native-stage-one-fraction", 0);
            ConversationAttitude checkpoint = FindCurrentAttitude(host, evidence.PersonId);
            Require(checkpoint.Offset == 10 && checkpoint.PositiveEarned == 10 && checkpoint.PositiveProgressionStage == 1 &&
                checkpoint.PositiveCredit >= 0.49 && checkpoint.PositiveCredit <= 0.51,
                "Three additional game hours did not preserve half credit at progression stage one.");
            evidence.FractionalCredit = checkpoint.PositiveCredit;
            evidence.EarnedAtCheckpoint = checkpoint.PositiveEarned;
            evidence.StageAtCheckpoint = checkpoint.PositiveProgressionStage;
            LogAttitudeSnapshot("before-disk-save", evidence.PersonId, checkpoint);
            yield return CloseTalk(host, sessionField, memoryWindowField);
            yield return SaveCheckpoint(game, character, evidence);
            yield return LoadSave(host, streets, character, evidence.CheckpointTitle);
            ConversationAttitude afterDiskLoad = FindCurrentAttitude(host, evidence.PersonId);
            LogAttitudeSnapshot("after-disk-load", evidence.PersonId, afterDiskLoad);
            Require(afterDiskLoad != null && afterDiskLoad.Offset == 10 && afterDiskLoad.PositiveEarned == 10 &&
                afterDiskLoad.PositiveProgressionStage == 1 && Math.Abs(afterDiskLoad.PositiveCredit - evidence.FractionalCredit) < 0.0001,
                "Actual native disk reload changed fractional friendship credit, earned points, or stage.");
            yield return WaitForHomeManagedAdult(streets, evidence.PersonId, evidence.HomeKey, value => actor = value);
            ConversationAttitude beforeReopen = FindCurrentAttitude(host, evidence.PersonId);
            LogAttitudeSnapshot("before-post-reload-talk", evidence.PersonId, beforeReopen);
            double loadedCreditBeforeReopen = beforeReopen.PositiveCredit;
            yield return OpenTalk(host, game, streets, actor, evidence.HomeKey, begin, beginMemory, sessionField, memoryWindowField,
                personField, value => talk = value);
            ConversationAttitude afterReopen = FindCurrentAttitude(host, evidence.PersonId);
            LogAttitudeSnapshot("after-post-reload-talk-before-apply", evidence.PersonId, afterReopen);
            Apply(host, apply, evidence.PersonId, "IMM10-native-after-reload-no-reset", 0);
            ConversationAttitude afterReopenNoDelta = FindCurrentAttitude(host, evidence.PersonId);
            LogAttitudeSnapshot("after-post-reload-no-delta-apply", evidence.PersonId, afterReopenNoDelta);
            Require(afterReopenNoDelta.Offset == 10 &&
                afterReopenNoDelta.PositiveProgressionStage == 1 &&
                // Disk round-trip accuracy is checked above; this check isolates
                // reopening from serialization by comparing the actual loaded credit.
                afterReopenNoDelta.PositiveCredit >= loadedCreditBeforeReopen,
                "Reopened post-reload Talk reset offset or fractional credit.");
            yield return CloseTalk(host, sessionField, memoryWindowField);

            // The fractional checkpoint uses stage one's longer refill interval.
            AdvanceInterval(evidence, evidence.Interval1Seconds);
            Require(FindCurrentAttitude(host, evidence.PersonId).Offset == 10,
                "Elapsed-only advance at progression stage one granted disposition.");
            yield return WaitForHomeManagedAdult(streets, evidence.PersonId, evidence.HomeKey, value => actor = value);
            yield return OpenTalk(host, game, streets, actor, evidence.HomeKey, begin, beginMemory, sessionField, memoryWindowField,
                personField, value => talk = value);
            for (int index = 0; index < 5; index++) Apply(host, apply, evidence.PersonId, "IMM10-native-stage-one-" + index, 1);
            ConversationAttitude stageTwo = FindCurrentAttitude(host, evidence.PersonId);
            Require(stageTwo.Offset == 15 && stageTwo.PositiveEarned == 15 && stageTwo.PositiveProgressionStage == 2,
                "The 30-hour refill did not advance friendship to progression stage two.");
            yield return CloseTalk(host, sessionField, memoryWindowField);

            AdvanceInterval(evidence, evidence.Interval2Seconds);
            Require(FindCurrentAttitude(host, evidence.PersonId).Offset == 15,
                "Elapsed-only advance at progression stage two granted disposition.");
            yield return WaitForHomeManagedAdult(streets, evidence.PersonId, evidence.HomeKey, value => actor = value);
            yield return OpenTalk(host, game, streets, actor, evidence.HomeKey, begin, beginMemory, sessionField, memoryWindowField,
                personField, value => talk = value);
            for (int index = 0; index < 5; index++) Apply(host, apply, evidence.PersonId, "IMM10-native-stage-two-" + index, 1);
            ConversationAttitude stageThree = FindCurrentAttitude(host, evidence.PersonId);
            Require(stageThree.Offset == 20 && stageThree.PositiveEarned == 20 && stageThree.PositiveProgressionStage == 3,
                "The 36-hour refill did not advance friendship to progression stage three.");
            evidence.FinalOffset = stageThree.Offset;
            yield return CloseTalk(host, sessionField, memoryWindowField);
        }

        private static IEnumerator RestoreBaseline(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, Evidence evidence)
        {
            GameManager game = GameManager.Instance;
            if (game == null || !game.IsReady) throw new InvalidOperationException("Cannot restore friendship baseline after player shutdown.");
            DaggerfallUI.Instance.PopToHUD();
            bool loaded = false;
            double savedBaselineClock = double.NaN;
            SaveLoadManager.OnLoadEventHandler onLoad = data =>
            {
                savedBaselineClock = data.dateAndTime.gameTime;
                loaded = true;
            };
            SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.EnumerateSaves();
                int slot = game.SaveLoadManager.FindSaveFolderByNames(character, BaselineSave);
                if (slot < 0) throw new InvalidOperationException("Exact baseline save disappeared before friendship cleanup.");
                game.SaveLoadManager.Load(slot);
                DaggerfallUI.Instance.PopToHUD();
                float deadline = Time.realtimeSinceStartup + 90f;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Native friendship baseline reload timed out.");
            }
            finally { SaveLoadManager.OnLoad -= onLoad; }

            double restoredClock = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            if (double.IsNaN(savedBaselineClock) || Math.Abs(restoredClock - savedBaselineClock) > 2.0)
                throw new InvalidOperationException("Exact baseline reload did not restore game clock. before=" +
                    savedBaselineClock + "; after=" + restoredClock);
            if (evidence.PersonId != null)
            {
                WorldState restored = (WorldState)host.GetSaveData();
                string restoredAttitude = SerializeAttitude(FindAttitude(restored, evidence.PersonId));
                if (!string.Equals(restoredAttitude, evidence.BaselineAttitude, StringComparison.Ordinal))
                    throw new InvalidOperationException("Exact baseline reload did not remove friendship probe state for the selected NPC.");
            }
            if (!string.IsNullOrEmpty(evidence.CheckpointTitle))
            {
                game.SaveLoadManager.EnumerateSaves();
                int checkpoint = game.SaveLoadManager.FindSaveFolderByNames(character, evidence.CheckpointTitle);
                if (checkpoint >= 0)
                {
                    game.SaveLoadManager.DeleteSaveFolder(checkpoint);
                    game.SaveLoadManager.EnumerateSaves();
                    if (game.SaveLoadManager.FindSaveFolderByNames(character, evidence.CheckpointTitle) >= 0)
                        throw new InvalidOperationException("Owned temporary friendship checkpoint remained after cleanup.");
                }
            }
        }

        private static IEnumerator OpenTalk(PeopleOfTheIliacBayMod host, GameManager game, StreetPopulation streets,
            ResidentActor actor, int homeKey,
            MethodInfo begin, MethodInfo beginMemory, FieldInfo sessionField, FieldInfo memoryWindowField,
            FieldInfo personField, Action<DaggerfallTalkWindow> completed)
        {
            if (!actor || actor.Resident == null || !actor.Resident.IsAlive || actor.Person == null ||
                streets.FindActor(actor.Resident.Id) != actor || FindHomeManagedAdult(streets, actor.Resident.Id, homeKey) != actor)
                throw new InvalidOperationException("Selected exact registered HomeDay NPC is no longer active, awake and present at its home for native Talk.");
            DaggerfallUI.Instance.PopToHUD();
            game.TalkManager.TalkToMobileNPC(actor.Person);
            float deadline = Time.realtimeSinceStartup + 25f;
            DaggerfallTalkWindow talk = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                if (talk != null && talk.IsSetup && game.TalkManager.CurrentNPCType == TalkManager.NPCType.Mobile &&
                    game.TalkManager.MobileNPC == actor.Person) break;
                yield return null;
            }
            if (talk == null || !talk.IsSetup || game.TalkManager.CurrentNPCType != TalkManager.NPCType.Mobile ||
                game.TalkManager.MobileNPC != actor.Person)
                throw new InvalidOperationException("Native Talk did not open for the selected managed NPC.");

            if (!(bool)sessionField.GetValue(host)) begin.Invoke(host, null);
            if (memoryWindowField.GetValue(host) != talk)
                beginMemory.Invoke(host, new object[] { talk });
            string expected = actor.Resident.Id;
            if (!string.Equals(Convert.ToString(personField.GetValue(host)), expected, StringComparison.Ordinal))
                throw new InvalidOperationException("BeginGroundedConversation did not bind the selected stable NPC identity.");
            long revision = Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod), "conversationContextRequestRevision")
                .GetValue(host)) + 1;
            var context = RequireMethod(typeof(PeopleOfTheIliacBayMod), "CaptureConversationContext", 3)
                .Invoke(host, new object[] { talk, "Inspect the current home conversation context.", revision }) as ConversationContextSnapshot;
            bool outdoors;
            Require(context != null && context.Binding.PersonId == expected &&
                context.NpcOutdoors.TryGetVerified(out outdoors) && !outdoors &&
                context.Weather.Status == ConversationContextFactStatus.Unknown,
                "Registered managed home resident did not receive verified indoor context without weather.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FRIENDSHIP_HOME_CONTEXT_PASS: npc=" + expected +
                "; home=" + homeKey + "; registered=true; outdoors=false; weather=Unknown.");
            completed(talk);
        }

        private static IEnumerator CloseTalk(PeopleOfTheIliacBayMod host, FieldInfo sessionField,
            FieldInfo memoryWindowField)
        {
            DaggerfallUI.Instance.PopToHUD();
            float deadline = Time.realtimeSinceStartup + 15f;
            while ((DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow ||
                (bool)sessionField.GetValue(host) || memoryWindowField.GetValue(host) != null) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            if (DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow ||
                (bool)sessionField.GetValue(host) || memoryWindowField.GetValue(host) != null)
                throw new InvalidOperationException("Native Talk close did not end the friendship conversation session.");
        }

        private static IEnumerator SaveCheckpoint(GameManager game, string character, Evidence evidence)
        {
            evidence.CheckpointTitle = "People - IMM10 Friendship " + Guid.NewGuid().ToString("N").Substring(0, 8);
            game.SaveLoadManager.EnumerateSaves();
            if (game.SaveLoadManager.FindSaveFolderByNames(character, evidence.CheckpointTitle) >= 0)
                throw new InvalidOperationException("Generated friendship checkpoint title already exists; refusing to overwrite it.");
            bool saved = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnSave += onSave;
            try
            {
                game.SaveLoadManager.Save(character, evidence.CheckpointTitle);
                float deadline = Time.realtimeSinceStartup + 60f;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Native friendship disk save timed out.");
                game.SaveLoadManager.EnumerateSaves();
                int slot = game.SaveLoadManager.FindSaveFolderByNames(character, evidence.CheckpointTitle);
                if (slot < 0) throw new InvalidOperationException("Friendship checkpoint was not found after native save.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FRIENDSHIP_DISK_CHECKPOINT: title=" + evidence.CheckpointTitle +
                    "; folder=" + game.SaveLoadManager.GetSaveFolder(slot) + "; stage=" + evidence.StageAtCheckpoint +
                    "; credit=" + evidence.FractionalCredit.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ".");
            }
            finally { SaveLoadManager.OnSave -= onSave; }
        }

        private static IEnumerator LoadSave(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string title)
        {
            GameManager game = GameManager.Instance;
            bool loaded = false;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.EnumerateSaves();
                int slot = game.SaveLoadManager.FindSaveFolderByNames(character, title);
                if (slot < 0) throw new InvalidOperationException("Exact friendship checkpoint is missing: " + title);
                game.SaveLoadManager.Load(slot);
                DaggerfallUI.Instance.PopToHUD();
                float deadline = Time.realtimeSinceStartup + 90f;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Native friendship disk reload timed out.");
            }
            finally { SaveLoadManager.OnLoad -= onLoad; }
            yield return null;
        }

        private static void Apply(PeopleOfTheIliacBayMod host, MethodInfo method, string personId,
            string utterance, int expectedDelta)
        {
            int before = FindCurrentAttitude(host, personId).Offset;
            method.Invoke(host, new object[] { "polite", utterance });
            int actual = FindCurrentAttitude(host, personId).Offset - before;
            Require(actual == expectedDelta, "Existing typed polite tone application returned unexpected offset change for " + utterance +
                ". expected=" + expectedDelta + "; actual=" + actual);
        }

        private static void AdvanceGameClock(double seconds)
        {
            if (seconds <= 0 || seconds > 36 * 3600 + 1)
                throw new InvalidOperationException("Friendship probe game-clock step is outside its explicit 36-hour hard cap.");
            double appliedSeconds = Math.Ceiling(seconds);
            if (appliedSeconds > 36 * 3600 + 1)
                throw new InvalidOperationException("Rounded friendship probe game-clock step exceeded the 36-hour hard cap tolerance.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FRIENDSHIP_CLOCK_STEP: requestedSeconds=" +
                seconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                "; appliedWholeGameSeconds=" + appliedSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + ".");
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)appliedSeconds);
        }

        private static void AdvanceInterval(Evidence evidence, double seconds)
        {
            if (evidence == null || evidence.IntervalFactor < 0.85 || evidence.IntervalFactor > 1.15 ||
                seconds <= 0 || seconds > evidence.Interval2Seconds + 1 || seconds > 36 * 3600d + 1)
                throw new InvalidOperationException("Friendship probe direct friendship-interval step is outside its explicit bound.");
            AdvanceGameClock(seconds);
        }

        private static HomeDayFixture SelectHomeDayFixture(WorldState baseline, int settlementId, int wakeHour, int sleepHour)
        {
            SettlementPopulation town = baseline == null || baseline.Settlements == null ? null :
                baseline.Settlements.Find(value => value != null && value.SettlementId == settlementId);
            TownRoutineState savedTown = baseline == null || baseline.Routines == null ? null :
                baseline.Routines.Find(value => value != null && value.SettlementId == settlementId);
            BuildingDirectory directory = GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
            if (town == null || town.Households == null || savedTown == null || savedTown.Residents == null || !directory)
                throw new InvalidOperationException("Baseline lacks current-settlement household or routine data for a native HomeDay fixture.");

            foreach (Household home in town.Households)
            {
                if (home == null || home.Residents == null || !HasNativeHomeDoor(directory, home.BuildingKey)) continue;
                foreach (Resident resident in home.Residents)
                {
                    if (resident == null || !resident.IsAlive || resident.IsQuestProtected || resident.AgeGroup != ResidentAgeGroup.Adult ||
                        FindAttitude(baseline, resident.Id) != null || HasFixtureOverlay(baseline, resident.Id)) continue;
                    ResidentRoutine routine = savedTown.Residents.Find(value => value != null && value.ResidentId == resident.Id);
                    if (routine != null && (routine.DestinationKey >= 0 ||
                        (routine.Itinerary != null && routine.Itinerary.Stops != null && routine.Itinerary.Stops.Count > 0) ||
                        (routine.Route != null && routine.Route.Count >= 2))) continue;
                    double interval0 = ConversationEffects.FriendshipIntervalSeconds(resident.Id, 0);
                    double interval1 = ConversationEffects.FriendshipIntervalSeconds(resident.Id, 1);
                    double interval2 = ConversationEffects.FriendshipIntervalSeconds(resident.Id, 2);
                    if (!ValidIntervalProfile(resident.Id, interval0, interval1, interval2)) continue;
                    double factor = interval0 / 86400d;
                    int startMinute;
                    if (!TryChooseHomeContactMinute(wakeHour, sleepHour, interval0, interval1, interval2, out startMinute)) continue;
                    return new HomeDayFixture { Home = home, Resident = resident,
                        IntervalFactor = factor, Interval0Seconds = interval0, Interval1Seconds = interval1,
                        Interval2Seconds = interval2, StartContactMinute = startMinute };
                }
            }
            throw new InvalidOperationException("No living unprotected adult with baseline HomeDay routine, no friendship or task/action/appointment overlay, a native home door, and a feasible awake contact phase for their stable friendship interval factor is available.");
        }

        private static bool ValidIntervalProfile(string personId, double interval0, double interval1, double interval2)
        {
            double stage3 = ConversationEffects.FriendshipIntervalSeconds(personId, 3);
            double factor = interval0 / 86400d;
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor < 0.85 || factor > 1.15 ||
                !FiniteInterval(interval0) || !FiniteInterval(interval1) || !FiniteInterval(interval2) || !FiniteInterval(stage3)) return false;
            double expected0 = 86400d * factor;
            double expected1 = 108000d * factor;
            double expected2 = Math.Min(36 * 3600d, 129600d * factor);
            return Math.Abs(interval0 - expected0) < 0.001 && Math.Abs(interval1 - expected1) < 0.001 &&
                Math.Abs(interval2 - expected2) < 0.001 && interval2 <= 36 * 3600d + 0.001 &&
                Math.Abs(stage3 - interval2) < 0.001;
        }

        private static bool FiniteInterval(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0; }

        private static bool HasFixtureOverlay(WorldState baseline, string personId)
        {
            return baseline == null ||
                (baseline.NpcActions != null && baseline.NpcActions.Exists(value => value != null && value.ResidentId == personId)) ||
                (baseline.NpcTasks != null && baseline.NpcTasks.Exists(value => value != null && value.ResidentId == personId)) ||
                (baseline.RoutineRecoveries != null && baseline.RoutineRecoveries.Exists(value => value != null && value.ResidentId == personId)) ||
                (baseline.Appointments != null && baseline.Appointments.Exists(value => value != null && value.ResidentId == personId)) ||
                (baseline.RecentProactiveContacts != null && baseline.RecentProactiveContacts.Exists(value => value != null && value.ResidentId == personId)) ||
                (baseline.DialogueComplaints != null && baseline.DialogueComplaints.Exists(value => value != null && value.PersonId == personId));
        }

        private static bool HasNativeHomeDoor(BuildingDirectory directory, int homeKey)
        {
            foreach (DaggerfallStaticDoors owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                if (owner.Doors != null)
                    foreach (StaticDoor door in owner.Doors)
                        if (door.doorType == DoorTypes.Building && door.buildingKey == homeKey) return true;
            return false;
        }

        private static IEnumerator EnterHomeAndWaitForResident(GameManager game, StreetPopulation streets,
            HomeDayFixture fixture, Action<ResidentActor> assign)
        {
            BuildingDirectory directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            DaggerfallStaticDoors owner = null;
            StaticDoor entry = default(StaticDoor);
            foreach (DaggerfallStaticDoors candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                if (!candidate || candidate.Doors == null) continue;
                foreach (StaticDoor door in candidate.Doors)
                    if (door.doorType == DoorTypes.Building && door.buildingKey == fixture.Home.BuildingKey)
                    { owner = candidate; entry = door; break; }
                if (owner) break;
            }
            if (!owner) throw new InvalidOperationException("Selected HomeDay resident has no exact native home door.");
            DaggerfallUI.Instance.PopToHUD();
            TownSmokeHarness.EnterInterior(owner.transform, entry);
            float deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (game.PlayerEnterExit.IsPlayerInsideBuilding &&
                    game.PlayerEnterExit.BuildingDiscoveryData.buildingKey == fixture.Home.BuildingKey && streets.Ready)
                {
                    ResidentActor actor = FindHomeManagedAdult(streets, fixture.Resident.Id, fixture.Home.BuildingKey);
                    if (actor)
                    {
                        assign(actor);
                        yield break;
                    }
                }
                yield return null;
            }
            throw new InvalidOperationException("Native home interior did not materialize the exact awake registered HomeDay resident within 60 seconds.");
        }

        private static ResidentActor FindHomeManagedAdult(StreetPopulation streets, string personId, int homeKey)
        {
            if (!streets || !GameManager.Instance || !GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding ||
                GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.buildingKey != homeKey) return null;
            ResidentActor actor = streets.FindActor(personId);
            if (!actor || !actor.gameObject.activeInHierarchy || actor.Resident == null || !actor.Resident.IsAlive ||
                actor.Resident.AgeGroup != ResidentAgeGroup.Adult || actor.IsSleeping || actor.IsConversing ||
                actor.transform.parent != GameManager.Instance.PlayerEnterExit.Interior.transform) return null;
            ActivitySnapshot live = streets.GetState(personId, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            return live.Activity == Activity.Home && live.BuildingKey == homeKey ? actor : null;
        }

        private static IEnumerator WaitForHomeManagedAdult(StreetPopulation streets, string personId, int homeKey,
            Action<ResidentActor> assign)
        {
            float deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                ResidentActor actor = FindHomeManagedAdult(streets, personId, homeKey);
                if (actor) { assign(actor); yield break; }
                yield return new WaitForSecondsRealtime(0.25f);
            }
            throw new InvalidOperationException("Original registered HomeDay resident " + personId +
                " did not remain active, awake and at the exact home interior within 60 seconds.");
        }

        private static void AdvanceToNextHomeContactWindow(int startMinute)
        {
            if (startMinute < 0 || startMinute >= 24 * 60)
                throw new InvalidOperationException("Friendship probe selected an invalid awake HomeDay start minute.");
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            double today = Math.Floor(now / 86400d) * 86400d;
            double target = today + startMinute * 60d;
            if (target <= now + 1) target += 86400;
            AdvanceGameClock(target - now);
            double actual = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            if (Math.Abs(actual - target) > 1)
                throw new InvalidOperationException("Could not align explicit fixture time with the next awake home contact window. expected=" + target + "; actual=" + actual);
        }

        private static bool TryChooseHomeContactMinute(int wakeHour, int sleepHour,
            double interval0, double interval1, double interval2, out int selected)
        {
            int wake = wakeHour * 60;
            int sleep = sleepHour * 60;
            double[] offsetsSeconds = ContactOffsetsSeconds(interval0, interval1, interval2);
            int preferred = wake + 3 * 60 + 15;
            selected = -1;
            int bestDistance = int.MaxValue;
            for (int start = 0; start < 24 * 60; start++)
            {
                bool valid = true;
                foreach (double offsetSeconds in offsetsSeconds)
                {
                    double contact = ModuloMinutes(start + offsetSeconds / 60d);
                    if (contact < wake + 5 || contact >= sleep - 5) { valid = false; break; }
                }
                if (!valid) continue;
                int distance = Math.Abs(start - preferred);
                if (distance < bestDistance) { selected = start; bestDistance = distance; }
            }
            return selected >= 0;
        }

        private static bool ContactWindowIsAwake(double startClock, double interval0, double interval1,
            double interval2, int wakeHour, int sleepHour)
        {
            if (double.IsNaN(startClock) || double.IsInfinity(startClock)) return false;
            double startMinute = (startClock % 86400d) / 60d;
            double[] offsetsSeconds = ContactOffsetsSeconds(interval0, interval1, interval2);
            foreach (double offsetSeconds in offsetsSeconds)
            {
                double contact = ModuloMinutes(startMinute + offsetSeconds / 60d);
                if (contact < wakeHour * 60 + 5 || contact >= sleepHour * 60 - 5) return false;
            }
            return true;
        }

        private static double ModuloMinutes(double value)
        {
            double result = value % (24 * 60);
            return result < 0 ? result + 24 * 60 : result;
        }

        private static double[] ContactOffsetsSeconds(double interval0, double interval1, double interval2)
        {
            return new[] { 0d, interval0 / 2d, interval0, interval0 + interval1 / 10d,
                interval0 + 1.1d * interval1, interval0 + 1.1d * interval1 + interval2 };
        }

        private static string ContactWindowDescription(double startClock, double interval0, double interval1,
            double interval2)
        {
            double start = (Math.Floor(startClock) % 86400d) / 60d;
            var values = new List<string>();
            foreach (double offsetSeconds in ContactOffsetsSeconds(interval0, interval1, interval2))
                values.Add(FormatContactMinute(ModuloMinutes(start + offsetSeconds / 60d)));
            return string.Join(",", values.ToArray());
        }

        private static string IntervalProfileDescription(double interval0, double interval1, double interval2)
        {
            return FormatNumber(interval0 / 3600d) + "," + FormatNumber(interval1 / 3600d) + "," +
                FormatNumber(interval2 / 3600d);
        }

        private static string ContactOffsetsHoursDescription(double interval0, double interval1, double interval2)
        {
            var values = new List<string>();
            foreach (double seconds in ContactOffsetsSeconds(interval0, interval1, interval2))
                values.Add(FormatNumber(seconds / 3600d));
            return string.Join(",", values.ToArray());
        }

        private static string FormatContactMinute(double minute)
        {
            int totalSeconds = (int)Math.Round(ModuloMinutes(minute) * 60d);
            totalSeconds %= 24 * 3600;
            int hour = totalSeconds / 3600;
            int minutePart = (totalSeconds / 60) % 60;
            int second = totalSeconds % 60;
            return hour.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                minutePart.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                second.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static ConversationAttitude FindCurrentAttitude(PeopleOfTheIliacBayMod host, string personId)
        { return FindAttitude((WorldState)host.GetSaveData(), personId); }

        private static ConversationAttitude FindAttitude(WorldState state, string personId)
        {
            return state == null || state.ConversationAttitudes == null ? null :
                state.ConversationAttitudes.Find(value => value != null && value.PersonId == personId);
        }

        private static string SerializeAttitude(ConversationAttitude attitude)
        { return attitude == null ? null : SaveLoadManager.Serialize(typeof(ConversationAttitude), attitude); }

        private static void LogAttitudeSnapshot(string point, string personId, ConversationAttitude value)
        {
            if (value == null)
            {
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FRIENDSHIP_ATTITUDE_SNAPSHOT: point=" + point +
                    "; npc=" + personId + "; value=null.");
                return;
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FRIENDSHIP_ATTITUDE_SNAPSHOT: point=" + point +
                "; npc=" + personId + "; offset=" + value.Offset + "; earned=" + value.PositiveEarned +
                "; credit=" + value.PositiveCredit.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                "; stage=" + value.PositiveProgressionStage + "; stageProgress=" + value.PositiveStageProgress +
                "; encounter=" + value.EncounterCount + "; thisEncounter=" + value.PositiveThisEncounter + ".");
        }

        private static void RequireCurrentPerson(PeopleOfTheIliacBayMod host, FieldInfo personField,
            string personId, DaggerfallTalkWindow talk)
        {
            Require(talk != null && DaggerfallUI.UIManager.TopWindow == talk,
                "Native Talk window is not the current UI window.");
            Require(string.Equals(Convert.ToString(personField.GetValue(host)), personId, StringComparison.Ordinal),
                "Native conversation changed stable person identity.");
            Require(FindCurrentAttitude(host, personId) != null,
                "Native conversation did not create a persisted conversation attitude record.");
        }

        private static MethodInfo RequireMethod(Type type, string name, int parameterCount)
        {
            foreach (MethodInfo method in type.GetMethods(Members))
                if (method.Name == name && method.GetParameters().Length == parameterCount) return method;
            throw new InvalidOperationException("Required friendship runtime method is missing: " + name);
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, Members);
            if (field == null) throw new InvalidOperationException("Required friendship runtime field is missing: " + name);
            return field;
        }

        private static void RequireOwnedFixture(string character, string fixtureSave)
        {
            if (!string.Equals(character, FixtureCharacter, StringComparison.Ordinal) ||
                !string.Equals(fixtureSave, BaselineSave, StringComparison.Ordinal))
                throw new InvalidOperationException("Friendship progression probe requires the owned " +
                    FixtureCharacter + " / " + BaselineSave + " fixture.");
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        // Unity normally drives nested IEnumerator values itself. That means an
        // exception from a nested native save/load or dialogue coroutine can
        // escape this fixture's catch and skip restoration. Flatten only plain
        // enumerators; let Unity handle actual yield instructions as usual.
        private static IEnumerator DriveFlattened(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            Exception failure = null;
            try
            {
                while (stack.Count > 0)
                {
                    IEnumerator iterator = stack.Peek();
                    bool more;
                    object current = null;
                    try { more = iterator.MoveNext(); if (more) current = iterator.Current; }
                    catch (Exception error) { failure = error; break; }
                    if (!more)
                    {
                        stack.Pop();
                        Dispose(iterator, ref failure);
                        if (failure != null) break;
                        continue;
                    }

                    IEnumerator child = current as IEnumerator;
                    if (child != null && !(current is YieldInstruction) && !(current is CustomYieldInstruction))
                    { stack.Push(child); continue; }
                    yield return current;
                }
            }
            finally { while (stack.Count > 0) Dispose(stack.Pop(), ref failure); }
            if (failure != null)
                throw new InvalidOperationException("Nested friendship progression coroutine failed.", failure);
        }

        private static void Dispose(IEnumerator iterator, ref Exception failure)
        {
            IDisposable disposable = iterator as IDisposable;
            if (disposable == null) return;
            try { disposable.Dispose(); }
            catch (Exception error) { if (failure == null) failure = error; }
        }
    }
}
