using System;
using System.Collections;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    internal static class BusinessIdentityProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static IEnumerator Run(LivingCitiesMod host,StreetPopulation streets,string character)
        {
            var game=GameManager.Instance;game.SaveLoadManager.EnumerateSaves();
            int slot=game.SaveLoadManager.FindSaveFolderByNames(character,"People - Shop entrance");
            if(slot<0) throw new InvalidOperationException("Business identity probe lacks shop fixture.");
            bool loaded=false;SaveLoadManager.OnLoadEventHandler handler=data=>loaded=true;
            SaveLoadManager.OnLoad+=handler;
            try
            {
                game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();float deadline=Time.realtimeSinceStartup+90;
                while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<deadline) yield return null;
                if(!loaded || !streets.Ready) throw new InvalidOperationException("Shop fixture load timed out.");
            }
            finally {SaveLoadManager.OnLoad-=handler;}
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();DaggerfallStaticDoors owner=null;StaticDoor entry=default(StaticDoor);
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>()) foreach(var door in doors.Doors)
            {
                BuildingSummary summary;
                if(door.doorType==DoorTypes.Building && directory.GetBuildingSummary(door.buildingKey,out summary) &&
                    summary.BuildingType==DFLocation.BuildingTypes.GeneralStore) {owner=doors;entry=door;break;}
            }
            if(!owner) throw new InvalidOperationException("No general store.");
            TownSmokeHarness.EnterInterior(owner.transform,entry);yield return new WaitForSecondsRealtime(1);
            DaggerfallUI.Instance.PopToHUD();var room=game.PlayerEnterExit.Interior;
            var store=(WorldStateStore)typeof(LivingCitiesMod).GetField("store",Private).GetValue(host);
            var owns=typeof(LivingCitiesMod).GetMethod("IsConversationAtOwnedPlace",Private);
            var business=typeof(LivingCitiesMod).GetMethod("VerifiedKeeperBusinessContext",Private);
            var identity=typeof(LivingCitiesMod).GetField("conversationPersonId",Private);
            var talk=game.TalkManager;StaticNPC keeper=null;
            foreach(var npc in room.GetComponentsInChildren<StaticNPC>())
            {
                FactionFile.FactionData faction;
                if(game.PlayerEntity.FactionData.GetFactionData(npc.Data.factionID,out faction) &&
                    faction.sgroup==(int)FactionFile.SocialGroups.Merchants) {keeper=npc;break;}
            }
            if(!keeper) throw new InvalidOperationException("No native shop service NPC.");
            var guestObject=new GameObject("Business identity test tenant");guestObject.transform.parent=room.transform;
            var guest=guestObject.AddComponent<StaticNPC>();var guestData=keeper.Data;
            guestData.hash+=100000;guestData.factionID=0;
            typeof(StaticNPC).GetField("npcData",Private).SetValue(guest,guestData);
            var person=store.RegisterNativePerson(guestData.mapID,guestData.buildingKey,guestData.hash,"Test Tenant",
                ResidentSex.Female,(int)guestData.race,0,guestData.billboardArchiveIndex,guestData.billboardRecordIndex);
            // Isolated fixture represents a saved on-site bed claim, not a new
            // housing survey or an extra live occupant in a player save.
            person.HomeKey=guestData.buildingKey;person.BedSlot=0;person.BedCapacity=1;
            try
            {
                typeof(TalkManager).GetField("currentNPCType",Private).SetValue(talk,TalkManager.NPCType.Static);
                typeof(TalkManager).GetField("lastTargetStaticNPC",Private).SetValue(talk,guest);
                identity.SetValue(host,person.Id);
                object[] args={null};
                if((bool)owns.Invoke(host,args) || !string.IsNullOrEmpty((string)business.Invoke(host,null)))
                    throw new InvalidOperationException("Bed-owning tenant was classified as shopkeeper.");
                var alarm=store.RecordViolence(person.SettlementId,person.Id,100);
                store.LocateViolence(alarm.Id,entry.buildingKey,0,0,0);person.IsAlive=false;
                store.ConfirmCorpse(alarm.Id,101,entry.buildingKey,0,0,0);
                store.InitializeCorpseHomeKey(alarm.Id);
                if(!store.GetAvailableCorpsePropertyKeyClaims(alarm.Id).Exists(k=>k.BuildingKey==entry.buildingKey))
                    throw new InvalidOperationException("Tenant lost assigned home access key.");
                var allowKey=(Func<string,int,int,bool>)Delegate.CreateDelegate(typeof(Func<string,int,int,bool>),streets,
                    typeof(StreetPopulation).GetMethod("AllowsCorpsePropertyKey",Private));
                if(store.GetAvailableCorpsePropertyKeyClaims(alarm.Id,allowKey).Count!=1)
                    throw new InvalidOperationException("Tenant home key was filtered as a workplace-only key.");
                person.HomeKey=-1;person.BedSlot=-1;person.BedCapacity=0;
                if(store.GetAvailableCorpsePropertyKeyClaims(alarm.Id,allowKey).Count!=0 ||
                    store.GetAvailableCorpsePropertyKeyClaims(alarm.Id).Count!=1)
                    throw new InvalidOperationException("Legacy non-keeper workplace key was exposed or ledger reset.");
                typeof(TalkManager).GetField("lastTargetStaticNPC",Private).SetValue(talk,keeper);
                identity.SetValue(host,"npc:"+keeper.Data.mapID+":"+keeper.Data.buildingKey+":"+keeper.Data.hash);
                args=new object[]{null};if(!(bool)owns.Invoke(host,args)) throw new InvalidOperationException("Real selling NPC lost keeper role.");
                Debug.Log("LIVING_CITIES_BUSINESS_IDENTITY_PASS: tenant keeps home key but not business ownership; selling NPC is keeper.");
                var mobileObject=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,room.transform);
                try
                {
                    var actor=mobileObject.AddComponent<ResidentActor>();
                    actor.Initialize(new Resident {Id="business-child-probe",FullName="Test Child",AgeGroup=ResidentAgeGroup.Child,AppearanceSeed=42},1,
                        game.StreamingWorld.GetCurrentCityNavigation());
                    typeof(TalkManager).GetField("lastTargetMobileNPC",Private).SetValue(talk,actor.Person);
                    if((bool)typeof(LivingCitiesMod).GetMethod("ConversationPartnerIsChild",Private).Invoke(host,null))
                        throw new InvalidOperationException("Stale mobile child replaced active static adult.");
                    typeof(LivingCitiesMod).GetField("conversationSessionOpen",Private).SetValue(host,false);
                    typeof(LivingCitiesMod).GetMethod("BeginGroundedConversation",Private).Invoke(host,null);
                    if((string)identity.GetValue(host)!="npc:"+keeper.Data.mapID+":"+keeper.Data.buildingKey+":"+keeper.Data.hash)
                        throw new InvalidOperationException("Static speaker inherited stale mobile identity.");
                    guestData.context=(StaticNPC.Context)999;
                    typeof(StaticNPC).GetField("npcData",Private).SetValue(guest,guestData);
                    typeof(TalkManager).GetField("lastTargetStaticNPC",Private).SetValue(talk,guest);
                    typeof(TalkManager).GetField("currentNPCType",Private).SetValue(talk,TalkManager.NPCType.Mobile);
                    typeof(LivingCitiesMod).GetField("conversationSessionOpen",Private).SetValue(host,false);
                    typeof(LivingCitiesMod).GetMethod("BeginGroundedConversation",Private).Invoke(host,null);
                    if((string)identity.GetValue(host)!=actor.Resident.Id ||
                        (string)typeof(LivingCitiesMod).GetMethod("PersistentMemoryIdentity",Private).Invoke(host,null)!=actor.Resident.Id)
                        throw new InvalidOperationException("Mobile speaker inherited stale static identity or memory.");
                    Debug.Log("LIVING_CITIES_ACTIVE_SPEAKER_PASS: static adult ignores stale mobile child; mobile memory ignores stale static NPC.");
                }
                finally {UnityEngine.Object.Destroy(mobileObject);}
            }
            finally {UnityEngine.Object.Destroy(guestObject);}
            var allows=typeof(StreetPopulation).GetMethod("AllowsPropertyKey",Private);
            int inns=0;
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>()) foreach(var door in doors.Doors)
            {
                BuildingSummary summary;
                if(door.doorType!=DoorTypes.Building || !directory.GetBuildingSummary(door.buildingKey,out summary) ||
                    summary.BuildingType!=DFLocation.BuildingTypes.Tavern) continue;
                if((bool)allows.Invoke(streets,new object[]{guestData.mapID,door.buildingKey}))
                    throw new InvalidOperationException("Inn accepted a property key.");
                inns++;
            }
            if(inns==0) throw new InvalidOperationException("No inn key exclusions tested.");
            Debug.Log("LIVING_CITIES_NO_INN_KEYS_PASS: local inn doors excluded="+inns+"; permanent home key retained by core tests.");
        }
    }
}
