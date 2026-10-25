using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class RoutineRecoveryReloadProbe
    {
        public sealed class Result
        { public WorldStateStore Store; public ResidentActor Actor; public DaggerfallInterior Room; public InteriorGeometry Geometry; }
        private sealed class InventorySnapshot
        { public System.Collections.Generic.List<NpcInventoryItem> Items; public int WalletGold; }
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;

        public static IEnumerator Run(StreetPopulation streets,string resident,string actionId,Result result)
        {
            var game=GameManager.Instance;string character=game.PlayerEntity.Name;
            Require(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-routine-recovery-physical-probe")>=0 &&
                NativeProbeText.IsTestCharacter(character),
                "Recovery reload requires the explicitly owned disposable fixture.");
            var store=Store(streets);var actor=streets.FindActor(resident);
            Require(actor && !store.HasRoutineRecovery(resident),"Reload checkpoint must precede doorway commitment.");
            bool paused=GameManager.IsGamePaused,saved=false,loaded=false;
            SaveLoadManager.OnSaveEventHandler onSave=ignored=>saved=true;
            SaveLoadManager.OnLoadEventHandler onLoad=ignored=>loaded=true;
            SaveLoadManager.OnSave+=onSave;SaveLoadManager.OnLoad+=onLoad;
            try
            {
                game.PauseGame(true);streets.CaptureNpcActionPositions();
                var owner=actor.GetComponent<NativeNpcInventoryOwner>();
                Require(owner && owner.Binding!=null,"Recovering resident has no native inventory binding before disk save.");
                actor.CaptureInventory();
                var npcState=store.ReadNpcInventory(resident);
                Require(npcState!=null,"Recovering resident has no persisted native inventory before disk save.");
                var beforeNpc=CaptureInventory(owner.Binding.Items,npcState.WalletGold);
                AssertSameItems(beforeNpc.Items,npcState.Items,"pre-save NPC ledger");
                var beforePlayer=CaptureInventory(game.PlayerEntity.Items,game.PlayerEntity.GetGoldAmount());
                var job=store.ReadNpcAction(actionId);
                Require(job!=null && NpcActionExecutor.IsTerminal(job) && job.ReturningToRoutine &&
                    job.LastPosition.Kind==NpcActionTargetKind.IndoorPoint,"Checkpoint lost terminal recovery ownership.");
                string effect=job.CompletedEffectId;
                Vector3 expected=actor.transform.position;
                const string title="People - recovery mid-return";
                DaggerfallUI.AddHUDText("Testing recovery: saving and reloading during the return walk.",4f);
                game.SaveLoadManager.Save(character,title);
                float until=Time.realtimeSinceStartup+30;
                while(!saved && Time.realtimeSinceStartup<until)yield return null;
                Require(saved,"Recovery disk save timed out.");
                game.SaveLoadManager.EnumerateSaves();int slot=game.SaveLoadManager.FindSaveFolderByNames(character,title);
                Require(slot>=0,"Recovery disk slot was not created.");
                game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();
                until=Time.realtimeSinceStartup+120;
                while(Time.realtimeSinceStartup<until)
                {
                    if(loaded && streets.Ready)
                    {
                        actor=streets.FindActor(resident);
                        if(actor && actor.gameObject.activeInHierarchy)break;
                        game.PauseGame(false);
                    }
                    yield return null;
                }
                Require(loaded && streets.Ready && actor && actor.gameObject.activeInHierarchy,
                    "Recovery disk load did not reconstruct its actual actor.");
                game.PauseGame(true);game.PlayerEntity.GodMode=true;store=Store(streets);
                var restored=store.ReadNpcAction(actionId);
                Require(restored!=null && NpcActionExecutor.IsTerminal(restored) && restored.ReturningToRoutine &&
                    restored.CompletedEffectId==effect && restored.LastPosition.Kind==NpcActionTargetKind.IndoorPoint &&
                    Vector3.Distance(actor.transform.position,expected)<0.75f && !store.HasRoutineRecovery(resident),
                    "Disk reload changed the completed effect, return ownership, or physical return position.");
                var restoredOwner=actor.GetComponent<NativeNpcInventoryOwner>();
                Require(restoredOwner && restoredOwner.Binding!=null,"Reloaded resident has no native inventory binding.");
                var restoredNpcState=store.ReadNpcInventory(resident);
                Require(restoredNpcState!=null,"Reloaded resident has no native inventory ledger.");
                var afterNpc=CaptureInventory(restoredOwner.Binding.Items,restoredNpcState.WalletGold);
                AssertSameInventory(beforeNpc,afterNpc,"NPC after real disk reload");
                AssertSameItems(afterNpc.Items,restoredNpcState.Items,"reloaded NPC ledger");
                var afterPlayer=CaptureInventory(game.PlayerEntity.Items,game.PlayerEntity.GetGoldAmount());
                AssertSameInventory(beforePlayer,afterPlayer,"player after real disk reload");
                int bodies=0;
                foreach(var body in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if(body.gameObject.activeInHierarchy && body.Resident.Id==resident)bodies++;
                Require(bodies==1,"Disk reload duplicated the recovering resident.");
                result.Store=store;result.Actor=actor;result.Room=game.PlayerEnterExit.Interior;
                result.Geometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",Private).GetValue(streets);
                Require(result.Room && result.Geometry!=null && result.Geometry.EntryGround.HasValue,
                    "Reload did not restore the surveyed source room.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_RELOAD_PASS: real-disk-save-load; terminal-effect-preserved; returning=true; bodyGap="+
                    Vector3.Distance(actor.transform.position,expected).ToString("F3")+"; one-body; doorway-not-committed"+
                    "; npcItems="+afterNpc.Items.Count+"; npcWallet="+afterNpc.WalletGold+
                    "; playerItems="+afterPlayer.Items.Count+"; playerGold="+afterPlayer.WalletGold+
                    "; exactNativeUidPayload=true; wallets-preserved=true");
            }
            finally
            {SaveLoadManager.OnSave-=onSave;SaveLoadManager.OnLoad-=onLoad;game.PauseGame(paused);}
        }
        private static WorldStateStore Store(StreetPopulation streets)
        {return (WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);}
        private static InventorySnapshot CaptureInventory(DaggerfallWorkshop.Game.Items.ItemCollection items,int walletGold)
        {return new InventorySnapshot{Items=NativeNpcInventoryBridge.CaptureItems(items),WalletGold=walletGold};}
        private static void AssertSameInventory(InventorySnapshot expected,InventorySnapshot actual,string label)
        {
            Require(expected!=null && actual!=null,label+" inventory snapshot is missing.");
            Require(expected.WalletGold==actual.WalletGold,label+" wallet changed from "+expected.WalletGold+" to "+actual.WalletGold+".");
            AssertSameItems(expected.Items,actual.Items,label);
        }
        private static void AssertSameItems(System.Collections.Generic.IList<NpcInventoryItem> expected,
            System.Collections.Generic.IList<NpcInventoryItem> actual,string label)
        {
            Require(expected!=null && actual!=null && expected.Count==actual.Count,label+" item count changed.");
            var byUid=new System.Collections.Generic.Dictionary<ulong,string>();
            foreach(var item in expected)
            {
                Require(item!=null && item.Uid>0 && !byUid.ContainsKey(item.Uid),label+" expected inventory has an invalid or duplicate UID.");
                byUid.Add(item.Uid,item.NativePayload);
            }
            foreach(var item in actual)
            {
                string payload;
                Require(item!=null && byUid.TryGetValue(item.Uid,out payload) && payload==item.NativePayload,
                    label+" native item UID or exact payload changed: "+(item==null?"<null>":item.Uid.ToString()));
                byUid.Remove(item.Uid);
            }
            Require(byUid.Count==0,label+" lost saved native items.");
        }
        private static void Require(bool condition,string reason)
        {if(!condition)throw new InvalidOperationException(reason);}
    }
}
