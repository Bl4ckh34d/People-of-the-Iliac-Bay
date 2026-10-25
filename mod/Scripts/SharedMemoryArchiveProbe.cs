using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit owned-player test only. Read phase runs in another process.
    internal static class SharedMemoryArchiveProbe
    {
        private const string Person="archive-probe:alice";
        private static WorldStateStore Store(PeopleOfTheIliacBayMod host)
        {return (WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);}
        private static void Say(PeopleOfTheIliacBayMod host,string words)
        {Store(host).RememberConversation(Person,100,"6 Morning Star 405","Black Wolf Inn",words,"I will remember.","native");}
        private static void Expect(PeopleOfTheIliacBayMod host,string present,string absent)
        {
            string recall=Store(host).RecallConversation(Person,"Mirabelle",100000,5000);
            string branchRecall=Store(host).RecallConversation(Person,present??absent??"",100000,5000);
            if(!recall.Contains("Mirabelle") || (present!=null && !branchRecall.Contains(present)) ||
                (absent!=null && branchRecall.Contains(absent)))
                throw new InvalidOperationException("Archive timeline mismatch: "+branchRecall);
            if(Store(host).RecallConversation("archive-probe:bob","",100,5000).Contains("Mirabelle"))
                throw new InvalidOperationException("Archive leaked between people.");
            var save=(WorldState)host.GetSaveData();
            string json=SaveLoadManager.Serialize(typeof(ConversationArchiveCheckpoint),save.ConversationArchive);
            if(save.ConversationMemories.Count!=0 || save.ConversationArchive==null || json.Length>240)
                throw new InvalidOperationException("Native save retained transcript or lost checkpoint.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ARCHIVE_CHECKPOINT: chars="+json.Length+"; head="+save.ConversationArchive.Head);
        }
        private static void ExpectOldParaphrasedFactAndRoles(PeopleOfTheIliacBayMod host)
        {
            // This is an archive/retrieval assertion only. It checks the context
            // that would reach the dialogue prompt, not a model's interpretation.
            string recall=Store(host).RecallConversation(Person,"What did I purchase?",100000,2000);
            if(!recall.Contains("Player said: \"I bought a silver brooch in Wayrest.\"") ||
                !recall.Contains("NPC said (native): \"I bought a brass amulet in Daggerfall.\""))
                throw new InvalidOperationException("Paraphrased old purchase fact or its speaker attribution was lost: "+recall);
            if(recall.Contains("Sentinel ruby"))
                throw new InvalidOperationException("Future-branch fact leaked into old-save retrieval: "+recall);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEMORY_RETRIEVAL_CONTEXT_PASS: paraphrase=What did I purchase?; older player/NPC claims remain separately attributed.");
        }
        public static IEnumerator Run(PeopleOfTheIliacBayMod host,string character,bool readOnly)
        {
            if(!character.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal)) throw new InvalidOperationException("Archive probe requires disposable character.");
            if(!readOnly)
            {
                yield return Load(character,"People - Gothway Garden");
                Say(host,"My sister is Mirabelle.");
                Store(host).RememberConversation(Person,110,"6 Morning Star 405","Black Wolf Inn",
                    "I bought a silver brooch in Wayrest.","I bought a brass amulet in Daggerfall.","native");
                for(int i=0;i<48;i++)
                    Store(host).RememberConversation(Person,200+i,"6 Morning Star 405","Black Wolf Inn",
                        "I saw bird number "+i+" by the town wall.","The bird found a warm place to rest.","native");
                yield return Save(character,"People - Memory base");
                Say(host,"My ruby is in Sentinel."); yield return Save(character,"People - Memory future");
                yield return Load(character,"People - Memory base"); Expect(host,null,"ruby");
                yield return AssertIndexedRecallParity(host,"base-before-branch","ruby");
                ExpectOldParaphrasedFactAndRoles(host);
                Say(host,"My emerald is in Wayrest."); yield return Save(character,"People - Memory branch");
                Expect(host,"emerald","ruby"); yield return AssertIndexedRecallParity(host,"branch-after-append","emerald");
            }
            yield return Load(character,"People - Memory future"); Expect(host,"ruby","emerald");
            yield return AssertIndexedRecallParity(host,"future-loaded","ruby");
            yield return Load(character,"People - Memory branch"); Expect(host,"emerald","ruby");
            yield return AssertIndexedRecallParity(host,"branch-loaded","emerald");
            yield return Load(character,"People - Memory base"); Expect(host,null,"ruby");
            yield return AssertIndexedRecallParity(host,"base-loaded-before-emerald","ruby");
            Expect(host,null,"emerald"); yield return AssertIndexedRecallParity(host,"base-loaded-after-emerald","emerald");
            ExpectOldParaphrasedFactAndRoles(host);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEMORY_ARCHIVE_PASS: phase="+(readOnly?"restart-read":"write")+"; disk save/load and same-date branch isolation passed.");
        }

        private static IEnumerator AssertIndexedRecallParity(PeopleOfTheIliacBayMod host,string phase,string branchQuery)
        {
            var store=Store(host);
            string[] queries={"Mirabelle","What did I purchase?",branchQuery};
            double[] cutoffs={115,100000};
            var before=new string[queries.Length*cutoffs.Length];
            for(int q=0;q<queries.Length;q++)for(int t=0;t<cutoffs.Length;t++)
                before[q*cutoffs.Length+t]=store.RecallConversation(Person,queries[q],cutoffs[t],2000);
            float deadline=Time.realtimeSinceStartup+5f;
            while(!store.IsConversationRecallIndexReady(Person)&&Time.realtimeSinceStartup<deadline)
            {
                store.AdvanceConversationRecallWarmup(Person,32,2);
                yield return null;
            }
            if(!store.IsConversationRecallIndexReady(Person))
                throw new InvalidOperationException("Native archive branch index did not become ready within 5 seconds: phase="+phase);
            for(int q=0;q<queries.Length;q++)for(int t=0;t<cutoffs.Length;t++)
            {
                string after=store.RecallConversation(Person,queries[q],cutoffs[t],2000);
                if(before[q*cutoffs.Length+t]!=after)
                    throw new InvalidOperationException("Indexed recall changed the public archive output: phase="+phase+
                        "; query="+queries[q]+"; cutoff="+cutoffs[t]+"; before="+before[q*cutoffs.Length+t]+"; after="+after);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEMORY_ARCHIVE_INDEX_PASS: phase="+phase+
                "; queries="+queries.Length+"; cutoffs="+cutoffs.Length+"; exactOutputParity=true; ready=true.");
        }
        private static IEnumerator Save(string character,string title)
        {
            bool done=false; SaveLoadManager.OnSaveEventHandler handler=data=>done=true;
            SaveLoadManager.OnSave+=handler;
            try
            {
                DaggerfallUI.Instance.PopToHUD(); yield return null;
                GameManager.Instance.SaveLoadManager.Save(character,title);
                float until=Time.realtimeSinceStartup+60;
                while(!done && Time.realtimeSinceStartup<until) yield return null;
                if(!done) throw new InvalidOperationException("Archive native save timed out: "+title);
                GameManager.Instance.SaveLoadManager.EnumerateSaves();
            }
            finally {SaveLoadManager.OnSave-=handler;}
        }
        private static IEnumerator Load(string character,string title)
        {
            var manager=GameManager.Instance.SaveLoadManager; manager.EnumerateSaves();
            int key=manager.FindSaveFolderByNames(character,title);
            if(key<0) throw new InvalidOperationException("Archive test save missing: "+title);
            bool done=false; SaveLoadManager.OnLoadEventHandler handler=data=>done=true;
            SaveLoadManager.OnLoad+=handler;
            try
            {
                manager.Load(key); DaggerfallUI.Instance.PopToHUD();
                float until=Time.realtimeSinceStartup+60;
                while(!done && Time.realtimeSinceStartup<until) yield return null;
                if(!done) throw new InvalidOperationException("Archive native load timed out: "+title);
                yield return null;
            }
            finally {SaveLoadManager.OnLoad-=handler;}
        }
    }
}
