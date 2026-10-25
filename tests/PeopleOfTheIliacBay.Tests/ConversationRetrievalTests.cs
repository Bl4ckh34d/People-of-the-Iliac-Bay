using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ConversationRetrievalTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            // Removing concept matching or morphology must lose these old facts.
            var corpus=new[] {
                new[] { "My horse is called Copper.", "What was my mount's name?", "Copper" },
                new[] { "My mare is called Juniper.", "What did I name my steed?", "Juniper" },
                new[] { "I hid the parcel in the tavern cellar.", "Where was the package hidden?", "tavern cellar" },
                new[] { "My occupation is a carpenter.", "What work do I do?", "carpenter" },
                new[] { "I purchased a silver brooch in Wayrest.", "What did I buy?", "silver brooch" },
                new[] { "I promised to return the borrowed emerald.", "What did I promise?", "borrowed emerald" },
                new[] { "The thieves took my amber pendant.", "What did the thief take?", "amber pendant" },
                new[] { "My home is in Glenpoint.", "Where do I live?", "Glenpoint" }
            };
            foreach(var item in corpus)
            {
                var row=item;
                cases.Add(("Hybrid recall corpus: "+row[1],()=>{
                    var store=WithOldFact(row[0]);
                    string recall=store.RecallConversation("alice",row[1],200000,1000);
                    Check.True(recall.Contains(row[2]),"Old paraphrased fact missing: "+row[2]);
                    Check.True(recall.Length<=1000);
                    Check.True(recall.Contains("6 Morning Star 405") && recall.Contains("Black Wolf Inn"));
                }));
            }
            cases.Add(("Implicit recall uses the latest shared topic to retrieve older detail",()=>{
                var store=WithOldFact("My horse is called Copper.");
                store.RememberConversation("alice",900,"spring","street","Do you remember my horse?","You mentioned a horse earlier.","model");
                string recall=store.RecallConversation("alice","What did I call it?",200000,1400);
                Check.True(recall.Contains("Copper"),"Implicit follow-up lost the earlier name.");
            }));
            cases.Add(("Recent topic expansion cannot override a new explicit topic",()=>{
                var store=WithOldFact("My sister is named Mirabelle.");
                store.RememberConversation("alice",900,"spring","street","My horse is called Copper.","A fine horse.","model");
                string recall=store.RecallConversation("alice","What is my sister called?",200000,800);
                Check.True(recall.Contains("Mirabelle"));
            }));
            cases.Add(("Implicit possessive recall uses the last recorded turn at a shared timestamp",()=>{
                var store=WithOldFact("My horse is called Copper.");
                store.RememberConversation("alice",900,"spring","street","My sister is named Mirabelle.","A lovely name.","model");
                store.RememberConversation("alice",900,"spring","street","Do you remember my horse?","You mentioned a horse earlier.","model");
                string recall=store.RecallConversation("alice","What was its name?",200000,1000);
                Check.True(recall.Contains("Copper"),"Same-timestamp follow-up retrieved the wrong topic.");
            }));
            cases.Add(("Long memory excerpts retain relevant NPC speech and both claim labels",()=>{
                var store=new WorldStateStore(12);
                string padding=new string('x',480)+". ";
                store.RememberConversation("alice",100,"6 Morning Star 405","Black Wolf Inn",
                    padding+"My brother is Marius.",padding+"My brother is Daric.","model");
                string recall=store.RecallConversation("alice","What is your brother called?",900,1100);
                Check.True(recall.Contains("Marius") && recall.Contains("Daric"),"Relevant evidence was prefix-truncated.");
                Check.True(recall.Contains("Player said: \"") && recall.Contains("NPC said (model): \""));
                Check.True(recall.Length<=1100);
            }));
            cases.Add(("Hybrid recall preserves contradictory dated claims without promoting them to facts",()=>{
                var store=WithOldFact("My horse is called Copper.");
                store.RememberConversation("alice",900,"7 Morning Star 405","Black Wolf Inn",
                    "I renamed my horse Ash.","My own horse is named Flint.","model");
                string recall=store.RecallConversation("alice","What is my mount called?",200000,1500);
                Check.True(recall.Contains("Copper") && recall.Contains("Ash") && recall.Contains("Flint"));
                Check.True(recall.Contains("6 Morning Star 405") && recall.Contains("7 Morning Star 405") && recall.Contains("not proof"));
            }));
            cases.Add(("Hybrid retrieval derives only from the current person's eligible save branch",()=>{
                var store=WithOldFact("My horse is called Copper.");
                var checkpoint=store.Snapshot();
                store.RememberConversation("alice",900,"winter","inn","My horse is called UnsavedSecret.","Indeed.","model");
                store.RecallConversation("alice","mount",200000);
                store.Restore(checkpoint);
                store.RememberConversation("bob",900,"winter","inn","My horse is called PrivateSecret.","Indeed.","model");
                store.RememberConversation("alice",300000,"future","inn","My horse is called FutureSecret.","Indeed.","model");
                string recall=store.RecallConversation("alice","What is my mount called?",200000,1000);
                Check.True(recall.Contains("Copper"));
                Check.True(!recall.Contains("UnsavedSecret") && !recall.Contains("PrivateSecret") && !recall.Contains("FutureSecret"));
                var other=new WorldStateStore(77);
                Check.True(!other.RecallConversation("alice","mount",200000).Contains("Copper"));
            }));
            cases.Add(("Minimum recall budget retains an intact role-labeled record",()=>{
                var store=WithOldFact("My horse is called Copper.");
                string recall=store.RecallConversation("alice","mount",200000,256);
                Check.True(recall.Length<=256 && recall.Contains("Copper") && recall.Contains("Player said:"));
                Check.True(recall.Contains("NPC said (model):"));
                Check.True(recall.EndsWith("\"",StringComparison.Ordinal),"Excerpt ends inside a claim.");
            }));
        }

        private static WorldStateStore WithOldFact(string player)
        {
            var store=new WorldStateStore(12);
            store.RememberConversation("alice",100,"6 Morning Star 405","Black Wolf Inn",player,"I will remember.","model");
            for(int i=0;i<80;i++) store.RememberConversation("alice",200+i,"spring","street",
                "Today I saw bird number "+i,"Interesting weather today.","model");
            return store;
        }
    }
}
