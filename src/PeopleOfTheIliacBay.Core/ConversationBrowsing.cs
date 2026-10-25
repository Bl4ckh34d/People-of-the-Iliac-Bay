using System;
using System.Collections.Generic;
using System.Linq;

namespace PeopleOfTheIliacBay.Core
{
    // Selection is restricted to remembered, currently eligible native destinations.
    public static class ConversationBrowsing
    {
        // Revalidate the remembered set at execution time; callers supply actual
        // native knowledge and current permission, not model-selected arbitrary IDs.
        public static int Pick(IEnumerable<int> remembered, IEnumerable<int> eligible, Func<int, int> choose)
        {
            if (remembered == null || eligible == null || choose == null) throw new ArgumentNullException();
            var allowed = new HashSet<int>(eligible);
            var choices = remembered.Where(allowed.Contains).Distinct().ToList();
            if (choices.Count == 0) return -1;
            int index = choose(choices.Count);
            if (index < 0 || index >= choices.Count) throw new ArgumentOutOfRangeException(nameof(choose));
            return choices[index];
        }

    }
}
