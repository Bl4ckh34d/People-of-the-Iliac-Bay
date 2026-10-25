using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Runs only after SharedMemoryArchiveProbe has loaded its real base save.
    // Retrieval and model-answer checks have separate markers so archive
    // correctness cannot be mistaken for model comprehension.
    internal static class SharedMemoryRecallModelProbe
    {
        private const string Person="archive-probe:alice";
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;

        public static IEnumerator Run(PeopleOfTheIliacBayMod host)
        {
            var hostType=typeof(PeopleOfTheIliacBayMod);
            var store=(WorldStateStore)hostType.GetField("store",Private).GetValue(host);
            const string question="Which item did I say I purchased?";
            string context=store.RecallConversation(Person,question,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),2000);
            int playerClaim=context.IndexOf("Player said: \"I bought a silver brooch in Wayrest.\"",StringComparison.Ordinal);
            int npcClaim=context.IndexOf("NPC said (native): \"I bought a brass amulet in Daggerfall.\"",StringComparison.Ordinal);
            if(playerClaim<0 || npcClaim<0)
                throw new InvalidOperationException("Restored archive did not retrieve both paraphrased purchase claims with speaker labels: "+context);
            if(context.Contains("Sentinel ruby") || context.Contains("emerald") ||
                store.RecallConversation("archive-probe:bob",question,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),2000).Contains("silver brooch"))
                throw new InvalidOperationException("Model probe context contains a future-branch or another-person claim.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEMORY_RETRIEVAL_CONTEXT_PASS: restored old-save paraphrase; Player and NPC claims separately labeled; future and other-person facts excluded.");

            var client=(LocalDialogueClient)hostType.GetField("localDialogue",Private).GetValue(host);
            var connection=(LocalDialogueConnection)hostType.GetMethod("NewDialogueConnection",Private).Invoke(host,null);
            var request=FreeTextDialogue.Create(question,"Alice","Breton","female","local patron",0,
                "You are Alice. The player is Neris. "+context);
            bool done=false;
            string answer=null;
            if(client==null || !client.Request(connection,request,value=>{answer=value;done=true;}))
                throw new InvalidOperationException("Local dialogue client rejected the archive recall request.");
            float deadline=Time.realtimeSinceStartup+60f;
            while(!done && Time.realtimeSinceStartup<deadline) yield return null;
            if(!done) throw new InvalidOperationException("Local model did not finish the restored-archive recall within 60 seconds.");
            if(answer==LocalDialogueClient.Unavailable || answer==LocalDialogueClient.ContextLimit || string.IsNullOrWhiteSpace(answer))
                throw new InvalidOperationException("Local model did not produce an answer from restored archive context: "+answer);
            int expected=answer.IndexOf("silver brooch",StringComparison.OrdinalIgnoreCase);
            if(expected<0)
                throw new InvalidOperationException("Live model did not recall the player's paraphrased purchase claim: "+answer);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEMORY_MODEL_ANSWER_PASS: query="+question+"; player purchase claim recalled; answer="+answer.Replace('\r',' ').Replace('\n',' '));
        }
    }
}
