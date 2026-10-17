using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LivingCities.Core
{
    // Category vocabulary selects native topic groups, never fabricated destinations.
    public static class ConversationBrowsing
    {
        private static readonly string[,] Categories = {
            { @"\b(taverns?|inns?|pubs?|lodging|accommodation)\b|\bplace to (stay|sleep)\b", @"\b(taverns?|inns?)\b" },
            { @"\b(temples?|shrines?|churches?|chapels?)\b", @"\btemples?\b" },
            { @"\b(guilds?|guildhalls?)\b", @"\bguilds?\b" },
            { @"\bshops?\b|\bplaces? to (shop|buy supplies)\b", @"\b(stores?|shops?|smiths?|alchemists?|armorers?|bookstores?)\b" },
            { @"\b(bookshops?|bookstores?|booksellers?)\b|\bbuy books\b", @"\bbookstores?\b" },
            { @"\b(alchemists?|potions?)\b", @"\balchemists?\b" },
            { @"\b(armorers?|armourers?|armo[u]?r)\b", @"\barmorers?\b" },
            { @"\bweapons?\b|\bweaponsmiths?\b", @"\bweapon\b" },
            { @"\b(clothes|clothing|tailors?)\b", @"\bclothing\b" },
            { @"\b(jewellers?|jewelers?|gems?)\b", @"\bgem\b" },
            { @"\b(pawnshops?|pawnbrokers?)\b", @"\bpawn\b" },
            { @"\b(general stores?|supplies)\b", @"\bgeneral stores?\b" },
            { @"\b(banks?|banking)\b", @"\bbanks?\b" },
            { @"\b(libraries|library)\b", @"\b(libraries|library)\b" }
        };

        public static List<int> RequestedGroups(string text, IList<ConversationTopic> topics)
        {
            var result = new List<int>();
            text = text ?? "";
            if (topics == null || !IsBrowseQuestion(text)) return result;
            if (topics.Any(t => !t.Group && ConversationKnowledge.NamesTopic(text, t))) return result;
            bool specificShop = false;
            for (int i = 4; i <= 11; i++) specificShop |= Match(text, Categories[i, 0]);
            foreach (var topic in topics.Where(t => t.Group))
            {
                bool match = ConversationKnowledge.NamesTopic(text, topic);
                for (int i = 0; !match && i < Categories.GetLength(0); i++)
                {
                    if (i == 3 && specificShop) continue;
                    match = Match(text, Categories[i, 0]) && Match(topic.Caption, Categories[i, 1]);
                }
                if (match) result.Add(topic.Id);
            }
            return result.Distinct().ToList();
        }

        public static bool IsBrowseQuestion(string text)
        {
            text = text ?? "";
            if (Match(text, @"\b(don't|do not|not)\s+(need|want|recommend|suggest|mark|pin|look)")) return false;
            if (Match(text, @"\byour\s+(inns?|taverns?|shops?|temples?|guilds?)\b")) return false;
            if (Match(text, @"\b(you|your)\b.{0,35}\b(own|live|work|working|going|plan|like|enjoy|stayed|visited)\b")) return false;
            return ConversationKnowledge.WantsMap(text) || Match(text,
                @"\b(where|nearby|nearest|recommend|suggest|looking for|find|any|know|need|looking to|tell me about|is there|are there)\b");
        }

        public static bool IsMapChoice(string text)
        {
            return ConversationKnowledge.WantsMap(text) && Match(text,
                @"\b(one|any|either|them|those|these|it|there|that place)\b");
        }

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

        public static string SelectReply(string prose, string factual, IEnumerable<string> requiredNames,
            IEnumerable<string> excludedNames, bool? marked)
        {
            if (string.IsNullOrWhiteSpace(prose)) return factual;
            Func<string,bool> named = name => ConversationKnowledge.NamesTopic(prose,new ConversationTopic(0,name,"",false));
            if (requiredNames.Any(name=>!named(name)) || excludedNames.Any(named)) return factual;
            bool claimsMarker=Match(prose,@"\b(marked|pinned|added to your map)\b");
            bool negative=Match(prose,@"\b(can't|cannot|couldn't|could not|unable|not|won't)\b");
            if (marked==true && (!claimsMarker || negative) ||
                marked==false && (!negative || !Match(prose,@"\b(map|mark|marker)\b")) ||
                marked!=true && claimsMarker) return factual;
            if (Match(prose,@"where we are|we are now|we're in|stone's throw|short walk|\b\d+\s*(minutes?|metres?|meters?|paces?)\b")) return factual;
            return prose;
        }

        private static bool Match(string text, string pattern)
        { return Regex.IsMatch(text ?? "", pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
    }
}
