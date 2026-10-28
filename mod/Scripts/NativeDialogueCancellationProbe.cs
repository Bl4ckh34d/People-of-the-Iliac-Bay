using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Separate cancellation evidence. This is not five-step execution acceptance.
    internal static class NativeDialogueCancellationProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static IEnumerator RunFixture(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            Require(host && streets && GameManager.Instance.IsReady && character == "Hakon Ironhand" &&
                Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE") == "dialogue-cancel",
                "Dialogue cancellation requires the explicit owned fixture.");
            bool paidTravel=Environment.GetEnvironmentVariable("POTIB_GOAL_PAID_TRAVEL")=="1";
            if(paidTravel)yield return GoalSequenceProbe.HireActualSequenceCompanion(host,streets);
            else yield return NativeBiographyDialogueProbe.EnterActualFamilyHome(host, streets);
            var store = (WorldStateStore)Field(host, "store").GetValue(host);
            var client = (LocalDialogueClient)Field(host, "localDialogue").GetValue(host);
            ResidentActor actor = null;
            float until = Time.realtimeSinceStartup + 45f;
            while (!actor && Time.realtimeSinceStartup < until)
            {
                actor = UnityEngine.Object.FindObjectsOfType<ResidentActor>().Where(value => value && value.Person &&
                    value.Resident != null && value.Resident.IsAlive && value.Resident.AgeGroup == ResidentAgeGroup.Adult &&
                    !value.IsSleeping && !value.IsConversing && !value.Resident.IsQuestProtected &&
                    !streets.IsAlarmed(value.Resident.Id) && store.ActiveNpcTask(value.Resident.Id) == null &&
                    (paidTravel?value.Resident.Id==GoalSequenceProbe.SequenceCompanion &&
                        store.ReadPaidFollowForTravelTask(value.Resident.Id,DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())!=null:
                        store.ActiveNpcAction(value.Resident.Id) == null))
                    .OrderByDescending(value => store.ConversationDisposition(value.Resident.Id)).FirstOrDefault();
                if (!actor) yield return null;
            }
            Require(actor, "No awake actual resident offers a native Wait target.");
            string person = actor.Resident.Id;
            var game = GameManager.Instance;
            var paidFollow=paidTravel?store.ActiveNpcAction(person):null;
            long paidRevision=paidTravel?store.ReadNpcServiceContracts().Revision:0;
            int paidGold=game.PlayerEntity.GetGoldAmount();
            if (Vector3.Distance(actor.transform.position, game.PlayerMotor.transform.position) > 8f)
            {
                Vector3 contact = Vector3.zero;
                Require(TryNativeContact(streets, actor, out contact),
                    "Selected cancellation resident has no verified nearby native observer floor.");
                game.PlayerMotor.transform.position = contact + Vector3.up;
                Physics.SyncTransforms();
            }
            Require(Vector3.Distance(actor.transform.position, game.PlayerMotor.transform.position) <= 8f,
                "Actual cancellation task requires a nearby native conversation.");
            Require(streets.ConversationActionCandidates(person).Any(option => option.Kind == NpcActionKind.Wait),
                "Nearby awake home resident does not offer a native Wait target.");
            NpcActionOption laterMove=null;string laterBuilding=null;
            if(paidTravel)
            {
                laterMove=streets.ConversationActionCandidates(person).FirstOrDefault(option=>option.Kind==NpcActionKind.MoveTo &&
                    option.Target!=null && option.Target.Kind==NpcActionTargetKind.Building &&
                    !string.IsNullOrWhiteSpace(game.TalkManager.GetBuildingNameForBuildingKey(option.Target.BuildingKey)));
                Require(laterMove!=null,"Actually hired companion has no offered native building for a later walk.");
                laterBuilding=game.TalkManager.GetBuildingNameForBuildingKey(laterMove.Target.BuildingKey);
            }
            var priorTrace = client.ForegroundClassificationTrace;
            bool originalEnabled = (bool)Field(host, "localDialogueEnabled").GetValue(host);
            bool originalPrewarm = (bool)Field(host, "dialoguePrewarmEnabled").GetValue(host);
            Field(host, "localDialogueEnabled").SetValue(host, true);
            Field(host, "dialoguePrewarmEnabled").SetValue(host, false);
            client.ForegroundClassificationTrace = (request, response, choice) =>
            {
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CANCEL_ACTUAL_CLASSIFICATION: request=" + request +
                    "; response=" + response + "; choice=" + choice);
                if (priorTrace != null) priorTrace(request, response, choice);
            };
            try
            {
                DaggerfallTalkWindow talk = null;
                yield return ConversationMoodProbe.OpenTalk(host, GameManager.Instance, actor, value => talk = value);
                yield return Submit(host, client, talk,
                    paidTravel?"Please wait here for sixty game seconds, then walk to the exterior entrance of "+laterBuilding+".":
                        "Please wait here for sixty game seconds, then follow me for sixty-one game seconds.", "request");
                var task = store.ActiveNpcTask(person);
                Require(task != null && task.Steps.Count == 2 && task.Steps[0].Kind == NpcActionKind.Wait &&
                    task.Steps[1].Kind == (paidTravel?NpcActionKind.MoveTo:NpcActionKind.Follow),
                    "Normal model did not accept the two-step native Wait request; refusal remains a refusal.");
                string taskId = task.Id;
                Require(task.Steps[0].DurationSeconds == 60 && (paidTravel?
                    task.PaidFollowActionId==paidFollow.Id && task.Steps[1].Target.BuildingKey==laterMove.Target.BuildingKey:
                    task.Steps[1].DurationSeconds == 61),
                    "Real extraction did not retain the requested native durations.");
                yield return ConversationMoodProbe.CloseTalk(host);
                until = Time.realtimeSinceStartup + 10f;
                NpcActionJob active = null;
                while (Time.realtimeSinceStartup < until)
                {
                    task = store.ReadNpcTask(taskId);
                    active = store.ActiveNpcAction(person);
                    if (task != null && task.Phase == NpcTaskPhase.Running && active != null &&
                        active.Kind == NpcActionKind.Wait && active.Phase == NpcActionPhase.Executing) break;
                    yield return null;
                }
                Require(task != null && task.Phase == NpcTaskPhase.Running && active != null &&
                    active.Kind == NpcActionKind.Wait && active.Phase == NpcActionPhase.Executing,
                    "Accepted Wait did not physically start before cancellation.");
                string actionId = active.Id;
                yield return ConversationMoodProbe.OpenTalk(host, GameManager.Instance, actor, value => talk = value);
                yield return Submit(host, client, talk,
                    paidTravel?"Stop now. Cancel the waiting-and-walking task, including the later walk. Continue our already paid travel service afterward.":
                        "Stop now. Cancel my entire task, including the later following step. You can return to your own plans.", "cancel");
                task = store.ReadNpcTask(taskId);
                active = store.ReadNpcAction(actionId);
                var recovery = store.ActiveNpcAction(person);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CANCEL_NATIVE_STATE: task=" + (task == null ? "missing" : task.Phase.ToString()) +
                    "; wait=" + (active == null ? "missing" : active.Phase.ToString()) +
                    "; routineRecovery=" + (recovery != null && recovery.ReturningToRoutine) +
                    "; laterStepDispatched=" + (store.ReadNpcAction(taskId + ":step:1") != null)+
                    "; paidFollowResumed="+(paidTravel && recovery!=null && recovery.Id==paidFollow.Id));
                Require(task != null && task.Phase == NpcTaskPhase.Cancelled && active != null &&
                    active.Phase == NpcActionPhase.Cancelled && task.Steps.All(step => step.CompletedEffectId == null) &&
                    store.ActiveNpcTask(person) == null && store.ReadNpcAction(taskId + ":step:1") == null &&
                    (paidTravel?SamePaidFollow(store,recovery,paidFollow,paidRevision,paidGold):
                        recovery == null || recovery.Id == actionId && recovery.Phase == NpcActionPhase.Cancelled && recovery.ReturningToRoutine),
                    "Actual semantic cancel did not stop both the accepted task and active native Wait.");
                yield return ConversationMoodProbe.CloseTalk(host);
                for (int i = 0; i < 10; i++) yield return null;
                task = store.ReadNpcTask(taskId);
                active = store.ReadNpcAction(actionId);
                Require(task != null && task.Phase == NpcTaskPhase.Cancelled &&
                    active != null && active.Phase == NpcActionPhase.Cancelled &&
                    active.CompletedEffectId == null && task.Steps.All(step => step.CompletedEffectId == null) &&
                    store.ActiveNpcTask(person) == null && store.ReadNpcAction(taskId + ":step:1") == null,
                    "Cancelled task resumed after Talk closed.");
                if(paidTravel)Require(SamePaidFollow(store,store.ActiveNpcAction(person),paidFollow,paidRevision,paidGold),
                    "Task cancellation changed the continuing paid service or charged again.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_CANCEL_NATIVE_PASS: actualGemma=true; normalWillingness=true; " +
                    "acceptedWaitFollow="+(!paidTravel).ToString().ToLowerInvariant()+"; acceptedPaidWaitMove="+paidTravel.ToString().ToLowerInvariant()+"; actualWaitRunning=true; semanticCancel=true; nativeActionCancelled=true; " +
                    "taskCancelled=true; noCompletedEffects=true; laterStepNeverDispatched=true; noResumeAfterTalk=true; paidFollowResumed="+paidTravel.ToString().ToLowerInvariant()+"; " +
                    "terminalRoutineRecoveryAllowed=true; fiveStepExecution=false");
            }
            finally
            {
                client.ForegroundClassificationTrace = priorTrace;
                Field(host, "localDialogueEnabled").SetValue(host, originalEnabled);
                Field(host, "dialoguePrewarmEnabled").SetValue(host, originalPrewarm);
                client.Cancel(); DaggerfallUI.Instance.PopToHUD();
            }
        }

        private static IEnumerator Submit(PeopleOfTheIliacBayMod host, LocalDialogueClient client,
            DaggerfallTalkWindow talk, string message, string label)
        {
            var busy = Field(host, "conversationBusy");
            Require(!(bool)busy.GetValue(host) && !client.IsBusy, "Earlier cancellation turn is still pending.");
            var rows = (ListBox)typeof(DaggerfallTalkWindow).GetField("listboxConversation", Private).GetValue(talk);
            int before = rows.Count;
            typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText", Private).Invoke(host, new object[] { talk, message });
            float until = Time.realtimeSinceStartup + 180f;
            while (Time.realtimeSinceStartup < until && ((bool)busy.GetValue(host) || client.IsBusy || rows.Count <= before))
                yield return null;
            yield return null;
            Require(!(bool)busy.GetValue(host) && !client.IsBusy && rows.Count > before,
                "Actual cancellation dialogue did not complete: " + label);
            until = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < until && !NativeBiographyDialogueProbe.AllNewAnswersRevealed(rows, before)) yield return null;
            Require(NativeBiographyDialogueProbe.AllNewAnswersRevealed(rows, before),
                "Native cancellation answer reveal did not finish: " + label);
            var visible = new StringBuilder();
            for (int i = before; i < rows.Count; i++)
            {
                var row = rows.GetItem(i);
                if (row != null && row.textLabel != null) visible.Append(row.textLabel.Text).Append(' ');
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CANCEL_VISIBLE_TURN: case=" + label + "; " + visible);
        }
        private static FieldInfo Field(PeopleOfTheIliacBayMod host, string name)
        { return host.GetType().GetField(name, Private); }
        private static bool SamePaidFollow(WorldStateStore store,NpcActionJob current,NpcActionJob original,long revision,int gold)
        {
            return current!=null && current.Id==original.Id && !NpcActionExecutor.IsTerminal(current) &&
                current.FinishAt==original.FinishAt && NpcPaidCompanionAction.ContractId(current)==NpcPaidCompanionAction.ContractId(original) &&
                store.ReadNpcServiceContracts().Revision==revision && GameManager.Instance.PlayerEntity.GetGoldAmount()==gold;
        }
        private static bool TryNativeContact(StreetPopulation streets, ResidentActor actor, out Vector3 contact)
        {
            contact = Vector3.zero;
            var game = GameManager.Instance;
            if (!game.PlayerEnterExit.IsPlayerInsideBuilding)
            {
                var geometry = (TownGeometry)typeof(StreetPopulation).GetField("geometry", Private).GetValue(streets);
                var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                return geometry != null && directory != null && NativeMoveToWaitProbe.TryObserverGround(
                    geometry, directory, actor.transform.position - Vector3.up, out contact);
            }
            var room = game.PlayerEnterExit.Interior;
            var inside = (InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry", Private).GetValue(streets);
            if (!room || inside == null) return false;
            var local = room.transform.InverseTransformPoint(actor.transform.position - Vector3.up);
            foreach (var anchor in inside.LocalAnchors)
            {
                var point = room.transform.TransformPoint(anchor);
                float distance = Vector3.Distance(point, actor.transform.position - Vector3.up);
                if (distance < 2f || distance > 4f || inside.RouteBetween(local, anchor).Count < 2) continue;
                contact = point;
                return true;
            }
            return false;
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
