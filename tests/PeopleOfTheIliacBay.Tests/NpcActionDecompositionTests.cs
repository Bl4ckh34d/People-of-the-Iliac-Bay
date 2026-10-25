using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcActionDecompositionTests
    {
        public static void Add(List<(string Name, System.Action Body)> cases)
        {
            cases.Add(("Semantic action decomposition returns unchanged single request or no action", new System.Action(() => {
                const string single = "  Could you buy two arrows for me?  ";
                var one = NpcActionDecomposition.Parse("SINGLE", single);
                Check.True(one != null);
                Check.Equal(NpcActionDecompositionKind.Single, one.Kind);
                Check.Equal(1, one.Clauses.Count);
                Check.Equal(single, one.Clauses[0]);

                var none = NpcActionDecomposition.Parse("NONE", "He said to buy arrows, but I will not.");
                Check.True(none != null);
                Check.Equal(NpcActionDecompositionKind.None, none.Kind);
                Check.Equal(0, none.Clauses.Count);
            })));

            cases.Add(("Semantic action chain uses exact ordered source spans and preserves acquired-item handover wording", new System.Action(() => {
                const string text = "Buy two arrows then give them to me.";
                var chain = NpcActionDecomposition.Parse("CHAIN|Buy two arrows|give them to me.", text);
                Check.True(chain != null);
                Check.Equal(NpcActionDecompositionKind.Chain, chain.Kind);
                Check.Equal(2, chain.Clauses.Count);
                Check.Equal("Buy two arrows then ", chain.Clauses[0]);
                Check.Equal("give them to me.", chain.Clauses[1]);
            })));

            cases.Add(("Semantic chain spans count Unicode scalars and retain source text exactly", new System.Action(() => {
                const string text = "😀 buy bread then give it to me";
                var chain = NpcActionDecomposition.Parse("CHAIN|😀 buy bread|give it to me", text);
                Check.True(chain != null);
                Check.Equal("😀 buy bread then ", chain.Clauses[0]);
                Check.Equal("give it to me", chain.Clauses[1]);
                var three = NpcActionDecomposition.Parse("CHAIN|Buy two arrows|wait by the gate|give them to me.", "😀 Buy two arrows, then wait by the gate, and finally give them to me.");
                Check.True(three != null);
                Check.Equal(3, three.Clauses.Count);
                Check.Equal("😀 Buy two arrows, then ", three.Clauses[0]);
                Check.Equal("wait by the gate, and finally ", three.Clauses[1]);
                Check.Equal("give them to me.", three.Clauses[2]);
            })));

            cases.Add(("Semantic chain rejects unaccounted commands and malformed ranges", new System.Action(() => {
                const string text = "Buy two arrows then give them to me.";
                foreach (string output in new[] {
                    "CHAIN|Buy two arrows|unknown", // anchor absent from source
                    "CHAIN|Could you buy|then give it to me|NONE", // the model must not count an output-status token as a third requested action
                    "CHAIN|give them to me.|Buy two arrows", // reordered anchors
                    "CHAIN|Buy two arrows", // only one action anchor
                    "CHAIN|Buy|two|arrows|then|give|them|to|me.|me.", // more than eight anchors
                    "CHAIN|Buy two arrows|give them to me.|",
                    "SINGLE|Buy two arrows then give them to me.",
                    "chain|Buy two arrows|give them to me." })
                    Check.True(NpcActionDecomposition.Parse(output, text) == null, output);
                Check.True(NpcActionDecomposition.Parse("CHAIN|a|b|c|d|e|f|g|h|i", "a b c d e f g h i") == null,
                    "the eighth-step contract rejects a ninth span");
            })));

            cases.Add(("Semantic action decomposition accepts only connector text between clauses", new System.Action(() => {
                const string text = "Buy bread, and then give it to me.";
                // The action boundary follows the connector; original wording remains exact.
                var chain = NpcActionDecomposition.Parse("CHAIN|Buy bread|give it to me.", text);
                Check.True(chain != null);
                Check.Equal("Buy bread, and then ", chain.Clauses[0]);
                Check.Equal("give it to me.", chain.Clauses[1]);

                Check.True(string.Concat(chain.Clauses) == text, "every source character must remain present");
            })));

        }
    }
}
