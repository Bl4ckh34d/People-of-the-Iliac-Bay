using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        /// <summary>Checks saved admission provenance against retained source cursors and work owners.</summary>
        private static void ValidateNpcOperationAdmissions(WorldState validated, int sourceVersion)
        {
            var cursors = new Dictionary<string, NpcConversationCommandCursor>(StringComparer.Ordinal);
            foreach (var cursor in validated.NpcConversationCommandCursors)
                cursors.Add(cursor.PersonId, cursor);
            var conversationSources = new HashSet<string>(StringComparer.Ordinal);
            var tasks = new Dictionary<string, NpcTaskPlan>(StringComparer.Ordinal);
            foreach (var task in validated.NpcTasks) tasks.Add(task.Id, task);

            foreach (var task in validated.NpcTasks)
            {
                var source = task.Admission;
                if (source == null) continue; // Already accepted legacy rows may continue after migration.
                Require(sourceVersion >= 48, "Legacy task has future admission metadata.");
                NpcOperationAdmission.CopyValidated(source);
                if (source.Kind == NpcOperationAdmissionKind.Conversation)
                {
                    Require(source.SourceId == task.ResidentId &&
                        task.Id == NpcOperationAdmission.ConversationTaskId(validated.NpcFrameworkScopeId,
                            task.ResidentId, source.SourceRevision) &&
                        !task.Steps.Exists(step => step.Kind == NpcActionKind.CastSpell) &&
                        AcceptedConversationSource(cursors, conversationSources, task.ResidentId, source.SourceRevision),
                        "Conversation task admission lacks its accepted source.");
                }
                else if (source.Kind == NpcOperationAdmissionKind.HealthCare)
                {
                    Require(task.Id == NpcHealthCare.MakeTaskId(new NpcHealthCareRecord {
                            Id = source.SourceId, Revision = source.SourceRevision }) &&
                        task.ProposalId == task.Id && NpcHealthCare.IsSupportedCareTaskPlan(task),
                        "Health-care task admission does not match its episode and attempt.");
                }
                else throw new ArgumentException("Task has an invalid admission source kind.");
            }

            foreach (var action in validated.NpcActions)
            {
                var source = action.Admission;
                if (source == null) continue;
                Require(sourceVersion >= 48, "Legacy action has future admission metadata.");
                NpcOperationAdmission.CopyValidated(source);
                if (source.Kind == NpcOperationAdmissionKind.Conversation)
                {
                    string expectedId = action.Kind == NpcActionKind.ChangeSchedule
                        ? NpcOperationAdmission.ConversationDepartureId(validated.NpcFrameworkScopeId,
                            action.ResidentId, source.SourceRevision)
                        : NpcOperationAdmission.ConversationActionId(validated.NpcFrameworkScopeId,
                            action.ResidentId, source.SourceRevision);
                    Require(source.SourceId == action.ResidentId && action.Id == expectedId &&
                        AcceptedConversationSource(cursors, conversationSources, action.ResidentId, source.SourceRevision),
                        "Conversation action admission lacks its accepted source.");
                }
                else if (source.Kind == NpcOperationAdmissionKind.TaskStep)
                {
                    NpcTaskPlan task;
                    Require(tasks.TryGetValue(source.SourceId, out task) && task.ResidentId == action.ResidentId &&
                        source.StepIndex < task.Steps.Count && source.StepIndex <= task.CurrentStepIndex &&
                        source.SourceRevision <= task.Revision &&
                        action.Id == task.Id + ":step:" + source.StepIndex.ToString(CultureInfo.InvariantCulture) &&
                        action.Kind == task.Steps[source.StepIndex].Kind &&
                        (action.Kind != NpcActionKind.CastSpell ||
                            task.Admission != null && task.Admission.Kind == NpcOperationAdmissionKind.HealthCare &&
                            NpcMagicHealingSpec.Same(action.MagicHealing,task.Steps[source.StepIndex].MagicHealing)),
                        "Task-step action admission lacks its retained task source.");
                }
                else throw new ArgumentException("Action has an invalid admission source kind.");
            }
        }

        private static bool AcceptedConversationSource(Dictionary<string, NpcConversationCommandCursor> cursors,
            HashSet<string> seen, string personId, long ordinal)
        {
            NpcConversationCommandCursor cursor;
            return cursors.TryGetValue(personId, out cursor) && cursor.LastAcceptedOrdinal >= ordinal &&
                seen.Add(personId + "\0" + ordinal.ToString(CultureInfo.InvariantCulture));
        }
    }
}
