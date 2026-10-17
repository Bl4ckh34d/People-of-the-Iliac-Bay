using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Explicit owned-player test only. Read phase runs in another process.
    internal static class SharedMemoryArchiveProbe
    {
        private const string Person="archive-probe:alice";
        private static WorldStateStore Store(LivingCitiesMod host)
        {return (WorldStateStore)typeof(LivingCitiesMod).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);}
        private static void Say(LivingCitiesMod host,string words)
        {Store(host).RememberConversation(Person,100,"6 Morning Star 405","Black Wolf Inn",words,"I will remember.","native");}
        private static void Expect(LivingCitiesMod host,string present,string absent)
        {
            string recall=Store(host).RecallConversation(Person,"",100,5000);
            if(!recall.Contains("Mirabelle") || (present!=null && !recall.Contains(present)) || (absent!=null && recall.Contains(absent)))
                throw new InvalidOperationException("Archive timeline mismatch: "+recall);
            if(Store(host).RecallConversation("archive-probe:bob","",100,5000).Contains("Mirabelle"))
                throw new InvalidOperationException("Archive leaked between people.");
            var save=(WorldState)host.GetSaveData();
            string json=SaveLoadManager.Serialize(typeof(ConversationArchiveCheckpoint),save.ConversationArchive);
            if(save.ConversationMemories.Count!=0 || save.ConversationArchive==null || json.Length>240)
                throw new InvalidOperationException("Native save retained transcript or lost checkpoint.");
            Debug.Log("LIVING_CITIES_ARCHIVE_CHECKPOINT: chars="+json.Length+"; head="+save.ConversationArchive.Head);
        }
        public static IEnumerator Run(LivingCitiesMod host,string character,bool readOnly)
        {
            if(!character.StartsWith("LivingCitiesTest-",StringComparison.Ordinal)) throw new InvalidOperationException("Archive probe requires disposable character.");
            if(!readOnly)
            {
                yield return Load(character,"People - Gothway Garden");
                Say(host,"My sister is Mirabelle."); yield return Save(character,"People - Memory base");
                Say(host,"My ruby is in Sentinel."); yield return Save(character,"People - Memory future");
                yield return Load(character,"People - Memory base"); Expect(host,null,"ruby");
                Say(host,"My emerald is in Wayrest."); yield return Save(character,"People - Memory branch");
                Expect(host,"emerald","ruby");
            }
            yield return Load(character,"People - Memory future"); Expect(host,"ruby","emerald");
            yield return Load(character,"People - Memory branch"); Expect(host,"emerald","ruby");
            yield return Load(character,"People - Memory base"); Expect(host,null,"ruby"); Expect(host,null,"emerald");
            Debug.Log("LIVING_CITIES_MEMORY_ARCHIVE_PASS: phase="+(readOnly?"restart-read":"write")+"; disk save/load and same-date branch isolation passed.");
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
