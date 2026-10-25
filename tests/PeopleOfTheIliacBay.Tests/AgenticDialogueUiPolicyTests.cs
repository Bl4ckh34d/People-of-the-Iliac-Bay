using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class AgenticDialogueUiPolicyTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("LLM free-text alone suppresses native tone chooser", () => {
                Check.True(AgenticDialogueUiPolicy.SuppressNativeToneChooser(true, true));
            }));
            cases.Add(("Disabled or unavailable LLM restores native tone chooser", () => {
                Check.True(!AgenticDialogueUiPolicy.SuppressNativeToneChooser(false, true));
                Check.True(!AgenticDialogueUiPolicy.SuppressNativeToneChooser(true, false));
                Check.True(!AgenticDialogueUiPolicy.SuppressNativeToneChooser(false, false));
            }));
        }
    }
}
