using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class FamilyThreatTests
    {
        public static void Add(List<(string Name, System.Action Body)> cases)
        {
            
            
            cases.Add(("Direct family threat forces actual disposition to negative one hundred", () => {
                var store = new WorldStateStore(1);
                int actual;
                store.SetConversationBaseline("native:100", 100, 100, 12);
                store.SetConversationThreatDisposition("native:100", 100, 101);
                Check.Equal(-200, store.ConversationDisposition("native:100"));
                Check.True(store.TryGetActualConversationDisposition("native:100", out actual));
                Check.Equal(-100, actual);

                store.SetConversationBaseline("native:-40", -40, 100, 12);
                store.SetConversationThreatDisposition("native:-40", -40, 101);
                Check.Equal(-60, store.ConversationDisposition("native:-40"));
                Check.True(store.TryGetActualConversationDisposition("native:-40", out actual));
                Check.Equal(-100, actual);
            }));
            cases.Add(("Terminal family-threat refusal survives save restore and blocks only that NPC", (System.Action)(() => {
                var store = new WorldStateStore(1);
                store.SetConversationBaseline("npc:threatened", 100, 100, 12);
                store.SetConversationThreatDisposition("npc:threatened", 100, 101);
                var restored = new WorldStateStore(2);
                restored.Restore(store.Snapshot());
                Check.True(restored.TryGetActualConversationDisposition("npc:threatened", out int actual));
                Check.Equal(-100, actual);
                Check.True(!ConversationAccess.CanStart(false, false, false, actual <= -100));
                Check.True(ConversationAccess.CanStart(false, false, false,
                    restored.TryGetActualConversationDisposition("npc:unrelated", out int other) && other <= -100));
            })));
        }
    }
}
