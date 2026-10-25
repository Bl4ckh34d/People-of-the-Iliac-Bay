using System;
using System.Collections.Generic;
using System.Linq;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcDecisionFreshnessTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Decision freshness helper accepts current binding and same-request archive advance", Current));
            cases.Add(("Decision freshness helper rejects every changed binding dimension", ChangedBinding));
            cases.Add(("Decision freshness helper checks revisions and exact ordered options", ChangedOptions));
        }

        private static NpcDecisionRequest Request()
        {
            var unknown = ConversationContextFact<string>.Unknown();
            var snapshot = new ConversationContextSnapshot(Binding(), 100, unknown, unknown,
                ConversationContextFact<ConversationDialogueRole>.Unknown(), unknown, unknown,
                ConversationContextFact<ConversationNpcPosition>.Unknown(),
                ConversationContextFact<ConversationNpcActivity>.Unknown(), ConversationContextFact<bool>.Unknown(),
                ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(), unknown, 0, 1,
                null, new List<ConversationContextCommitment>(), new List<ConversationContextSourceHandle>());
            return new NpcDecisionRequest(Guid.NewGuid(), snapshot, 4, "route", "Please wait.", "Fixture context.",
                new[] { new NpcDecisionOption("wait", "Wait here", true), new NpcDecisionOption("social", "Conversation", false) });
        }

        private static ConversationContextBinding Binding(ConversationActorCategory category = ConversationActorCategory.Mobile,
            string person = "resident:1", string session = "session", long requestRevision = 1, long saveEpoch = 1,
            char archive = 'a', char head = 'b', string scene = "inn", long sceneRevision = 1, bool ephemeral = false)
            => new ConversationContextBinding(category, ephemeral ? null : person, ephemeral ? "speaker" : null,
                session, requestRevision, saveEpoch, new string(archive, 32), new string(head, 64), scene, sceneRevision);

        private static void Current()
        {
            var request = Request();
            Require(NpcDecisionResult.IsCurrent(request, request.Binding, request.SnapshotRevision, 4, request.Options));
            Require(NpcDecisionResult.IsCurrent(request, Binding(head: 'c'), request.SnapshotRevision, 4, request.Options));
        }

        private static void ChangedBinding()
        {
            var request = Request();
            var changed = new[] { Binding(category: ConversationActorCategory.Static), Binding(person: "resident:2"),
                Binding(session: "other"), Binding(requestRevision: 2), Binding(saveEpoch: 2), Binding(archive: 'd'),
                Binding(scene: "outside"), Binding(sceneRevision: 2), Binding(ephemeral: true), null };
            foreach (var binding in changed)
                Require(!NpcDecisionResult.IsCurrent(request, binding, request.SnapshotRevision, 4, request.Options));
        }

        private static void ChangedOptions()
        {
            var request = Request();
            Require(!NpcDecisionResult.IsCurrent(null, request.Binding, request.SnapshotRevision, 4, request.Options));
            Require(!NpcDecisionResult.IsCurrent(request, request.Binding, request.SnapshotRevision + 1, 4, request.Options));
            Require(!NpcDecisionResult.IsCurrent(request, request.Binding, request.SnapshotRevision, 5, request.Options));
            Require(!NpcDecisionResult.IsCurrent(request, request.Binding, request.SnapshotRevision, 4, null));
            Require(!NpcDecisionResult.IsCurrent(request, request.Binding, request.SnapshotRevision, 4, request.Options.Reverse()));
            Require(!NpcDecisionResult.IsCurrent(request, request.Binding, request.SnapshotRevision, 4,
                new[] { new NpcDecisionOption("wait", "Other meaning", true), request.Options[1] }));
            Require(!NpcDecisionResult.IsCurrent(request, request.Binding, request.SnapshotRevision, 4,
                new[] { new NpcDecisionOption("wait", "Wait here", false), request.Options[1] }));
        }

        private static void Require(bool value)
        { if (!value) throw new InvalidOperationException("Decision freshness comparison did not match expected binding."); }
    }
}
