using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    internal static class BedroomReplacementProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            var game=GameManager.Instance;game.SaveLoadManager.EnumerateSaves();
            int slot=game.SaveLoadManager.FindSaveFolderByNames(character,"People - Shop entrance");
            if(slot<0) throw new InvalidOperationException("Bedroom replacement fixture missing.");
            bool loaded=false;SaveLoadManager.OnLoadEventHandler handler=data=>loaded=true;SaveLoadManager.OnLoad+=handler;
            try
            {
                game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();float deadline=Time.realtimeSinceStartup+90;
                while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<deadline) yield return null;
                if(!loaded || !streets.Ready) throw new InvalidOperationException("Bedroom fixture load timed out.");
            }
            finally {SaveLoadManager.OnLoad-=handler;}
            game.PlayerEntity.GodMode=true;
            var store=(WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store",Private).GetValue(host);
            var rentals=(NativeTavernRentals)typeof(PeopleOfTheIliacBayMod).GetField("nativeRentals",Private).GetValue(host);
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var doors=new List<KeyValuePair<Transform,StaticDoor>>();
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>()) foreach(var door in owner.Doors)
                if(door.doorType==DoorTypes.Building) doors.Add(new KeyValuePair<Transform,StaticDoor>(owner.transform,door));
            int innRemoved=0,hallKept=0,shopRemoved=0;bool shopOwner=false,ownerSleeping=false;
            foreach(var entry in doors)
            {
                BuildingSummary building;if(!directory.GetBuildingSummary(entry.Value.buildingKey,out building)) continue;
                bool inn=building.BuildingType==DFLocation.BuildingTypes.Tavern;
                bool shop=RMBLayout.IsShop(building.BuildingType);
                if(!inn && !shop || inn && innRemoved>0 || shop && shopRemoved>0 && shopOwner) continue;
                if(game.IsPlayerInside) {game.PlayerEnterExit.TransitionExterior();yield return new WaitForSecondsRealtime(.5f);}
                TownSmokeHarness.EnterInterior(entry.Key,entry.Value);yield return new WaitForSecondsRealtime(2);
                DaggerfallUI.Instance.PopToHUD();var room=game.PlayerEnterExit.Interior;
                if(inn) rentals.Refresh(store,directory);
                foreach(var npc in room.GetComponentsInChildren<StaticNPC>(true))
                {
                    var person=store.NativePeopleFor(directory.MapID).Find(p=>p.WorkKey==entry.Value.buildingKey && p.NativeHash==npc.Data.hash);
                    bool bedroom=inn && rentals.BedFor(room,npc.Data.hash)!=null;
                    if(inn && !bedroom)
                    {
                        if(person!=null && person.RetiredBedroomFlat) throw new InvalidOperationException("Main-hall person was retired.");
                        hallKept++;continue;
                    }
                    if(person!=null && person.RetiredBedroomFlat)
                    {
                        if(npc.gameObject.activeSelf || !person.IsAlive || person.HomeKey>=0 || store.InnLodgerHome(person.Id)!=null ||
                            store.NativeLodgingBed(person.Id)!=null) throw new InvalidOperationException("Retired flat still owns a body, bed or home.");
                        if(inn) innRemoved++;else shopRemoved++;
                    }
                    else if(inn && bedroom && BedroomFlatReplacement.Eligible(npc,building.BuildingType) && person!=null && person.IsAlive)
                        throw new InvalidOperationException("Ordinary guest-bedroom flat remained active.");
                    if(shop && NativeBusinessServices.IsKeeper(npc,building.BuildingType) && person!=null && person.IsAlive && person.HomeKey==entry.Value.buildingKey)
                    {
                        shopOwner=true;
                        var family=store.CurrentPopulation(directory.MapID).Households.Find(h=>h.HeadId==person.Id);
                        if(family!=null && family.Residents.Count==2)
                        {
                            var partner=family.Residents.Find(r=>r.Id!=person.Id);
                            if(partner.Sex!=ResidentSex.Female || partner.PartnerId!=person.Id ||
                                partner.FullName==null || !partner.FullName.EndsWith(" "+family.Surname,StringComparison.Ordinal))
                                throw new InvalidOperationException("Shop partner identity is inconsistent.");
                        }
                        var clock=DaggerfallUnity.Instance.WorldTime.Now;
                        clock.RaiseTime((float)((3600-clock.ToSeconds()%86400+86400)%86400));
                        DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                        yield return new WaitForSecondsRealtime(2);
                        var sleeping=npc.GetComponent<SleepingPose>();
                        if(!sleeping || !sleeping.Active) throw new InvalidOperationException("Keeper did not sleep in the shop at night.");
                        var beds=BedGeometry.Read(room);beds.Sort((a,b)=>a.RecordIndex.CompareTo(b.RecordIndex));
                        if(person.BedSlot<0 || person.BedSlot>=beds.Count ||
                            Vector3.Distance(npc.transform.position,room.transform.TransformPoint(beds[person.BedSlot].LocalCenter)+Vector3.up*.18f)>.05f)
                            throw new InvalidOperationException("Keeper slept outside the assigned bed.");
                        ownerSleeping=true;
                    }
                }
                if(innRemoved>0 && shopRemoved>0 && shopOwner) break;
            }
            if(innRemoved==0 || hallKept==0 || shopRemoved==0 || !shopOwner || !ownerSleeping)
                throw new InvalidOperationException("Bedroom fixture lacks acceptance evidence: inn="+innRemoved+"; hall="+hallKept+"; shop="+shopRemoved+"; keeper="+shopOwner);
            var json=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
            var restored=new WorldStateStore(1,PeopleOfTheIliacBayMod.ConversationArchiveRoot());
            restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),json));
            int retired=0;foreach(var person in restored.NativePeopleFor(directory.MapID)) if(person.RetiredBedroomFlat) retired++;
            if(retired<innRemoved+shopRemoved) throw new InvalidOperationException("Bedroom retirement did not survive native serialization.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BEDROOM_REPLACEMENT_PASS: inn="+innRemoved+"; mainHall="+hallKept+"; shop="+shopRemoved+"; owner sleeps on-site; native serialize/restore; no fake deaths or lingering native reservations.");
        }
    }
}
