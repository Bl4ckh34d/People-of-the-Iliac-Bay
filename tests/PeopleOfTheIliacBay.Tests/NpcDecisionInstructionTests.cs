using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcDecisionInstructionTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Decision instructions preserve legacy purpose and trace protocol version", LegacyPurposeCompatibility));
            cases.Add(("Decision instructions and user text preserve bounded multiline content", MultilineContent));
            cases.Add(("Decision instruction and text bounds reject overflow and unsupported controls", BoundsAndControls));
        }

        private static void LegacyPurposeCompatibility()
        {
            var request = Legacy("legacy-purpose", "turn", "context");
            Check.Equal("legacy-purpose", request.Purpose);
            Check.Equal("legacy-purpose", request.Instructions);
            Check.Equal("npc-decision-v2", request.ProtocolVersion);
            Check.Equal("npc-decision-v2", NpcDecisionRequest.CurrentProtocolVersion);
        }

        private static void MultilineContent()
        {
            string instructions = "Line one\r\nLine two\tindented " + new string('i', 7000);
            string originalTurn = "Please wait.\r\nThen follow me.\tThanks.";
            string context = "History:\n- I said hello.\r\n- NPC replied.\tSnapshot remains attributed.";
            var request = Explicit("short-purpose", instructions, originalTurn, context);
            Check.Equal(instructions, request.Instructions);
            Check.True(request.Instructions.Length > 128, "Instructions must not be truncated to purpose metadata bounds.");
            Check.Equal(originalTurn, request.OriginalTurn);
            Check.Equal(context, request.Context);
        }

        private static void BoundsAndControls()
        {
            var atLimits = Explicit(new string('p', 128), new string('i', 8192), new string('t', 8192), new string('c', 16384));
            Check.Equal(128, atLimits.Purpose.Length);
            Check.Equal(8192, atLimits.Instructions.Length);
            Check.Equal(8192, atLimits.OriginalTurn.Length);
            Check.Equal(16384, atLimits.Context.Length);
            Check.Throws<ArgumentException>(() => Explicit("purpose", new string('i', 8193), "turn", "context"));
            Check.Throws<ArgumentException>(() => Explicit(new string('p', 129), "instructions", "turn", "context"));
            Check.Throws<ArgumentException>(() => Explicit("purpose", "instructions", new string('t', 8193), "context"));
            Check.Throws<ArgumentException>(() => Explicit("purpose", "instructions", "turn", new string('c', 16385)));
            foreach (char forbidden in new[] { '\0', '\u000b', '\u001f', '\u007f', '\u0085' })
            {
                Check.Throws<ArgumentException>(() => Explicit("purpose", "before" + forbidden + "after", "turn", "context"));
                Check.Throws<ArgumentException>(() => Explicit("purpose", "instructions", "before" + forbidden + "after", "context"));
                Check.Throws<ArgumentException>(() => Explicit("purpose", "instructions", "turn", "before" + forbidden + "after"));
            }
            Check.Throws<ArgumentException>(() => Explicit("bad\nmetadata", "instructions", "turn", "context"));
            Check.Throws<ArgumentException>(() => Explicit("purpose", "\n\t\r", "turn", "context"));
        }

        private static NpcDecisionRequest Legacy(string purpose, string turn, string context)
            => new NpcDecisionRequest(Guid.NewGuid(), Snapshot(), 2, purpose, turn, context, Options());

        private static NpcDecisionRequest Explicit(string purpose, string instructions, string turn, string context)
            => new NpcDecisionRequest(Guid.NewGuid(), Snapshot(), 2, purpose, instructions, turn, context, Options());

        private static ConversationContextSnapshot Snapshot()
        {
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, "resident:7", null,
                "session", 1, 2, new string('a', 32), new string('b', 64), "scene", 3);
            var empty = ConversationContextFact<string>.Unknown();
            return new ConversationContextSnapshot(binding, 10, empty, empty,
                ConversationContextFact<ConversationDialogueRole>.Unknown(), empty, empty,
                ConversationContextFact<ConversationNpcPosition>.Unknown(),
                ConversationContextFact<ConversationNpcActivity>.Unknown(), ConversationContextFact<bool>.Unknown(),
                ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(), empty, 0, 1,
                null, new List<ConversationContextCommitment>(), new List<ConversationContextSourceHandle>());
        }

        private static NpcDecisionOption[] Options() => new[] { new NpcDecisionOption("wait", "Wait here", true) };
    }
}
