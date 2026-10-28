using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private float nextStationaryLife;
        private bool stationaryTransitionsRegistered;
        private readonly List<NativeStationaryLife> stationaryLives=new List<NativeStationaryLife>();
        private readonly Dictionary<string,NativeStationaryCorpse> stationaryCorpses=new Dictionary<string,NativeStationaryCorpse>(StringComparer.Ordinal);
        private string StationarySceneKey()
        {
            var game=GameManager.Instance;
            return game.PlayerEnterExit.IsPlayerInsideDungeon?"scene:dungeon:"+game.PlayerGPS.CurrentLocation.MapTableData.MapId:
                game.PlayerEnterExit.IsPlayerInsideBuilding?"scene:building:"+game.PlayerEnterExit.BuildingDiscoveryData.buildingKey:"scene:exterior";
        }
        internal NativeStationaryLife EnsureStationaryLife(StaticNPC npc)
        {
            var game=GameManager.Instance;
            if(!npc || !npc.gameObject.activeInHierarchy || saveFault || !options.Enabled || store==null ||
                game==null || !game.IsReady || !game.PlayerGPS.HasCurrentLocation)return null;
            var existing=npc.GetComponent<NativeStationaryLife>();
            if(existing && existing.Current(store))return existing;
            if(existing){existing.Release();DestroyImmediate(existing);}
            int map=game.PlayerGPS.CurrentMapID;var data=npc.Data;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            store.EnsureNativeLocationIdentity(map);
            NativePerson person=null;var actor=npc.GetComponent<ResidentActor>();
            if(actor && actor.Resident!=null)person=actor.Resident.NativeIdentity;
            int town,hash;string castle;
            if(person==null && NativeExteriorNpcIdentity.TryRead(npc,out town,out hash))
                person=store.RegisterNativePerson(town,-1,hash,npc.DisplayName,data.gender==Genders.Female?ResidentSex.Female:ResidentSex.Male,
                    (int)data.race,data.factionID,data.billboardArchiveIndex,data.billboardRecordIndex);
            var questResource=npc.GetComponent<QuestResourceBehaviour>();var questPerson=questResource?questResource.TargetResource as Person:null;
            bool authoredPerson=!questResource || questPerson!=null && questPerson.IsQuestor || QuestMachine.Instance.IsIndividualNPC(data.factionID);
            if(person==null && game.PlayerEnterExit.IsPlayerInsideBuilding && data.context==StaticNPC.Context.Building && authoredPerson)
                person=store.RegisterNativePerson(map,game.PlayerEnterExit.BuildingDiscoveryData.buildingKey,data.hash,npc.DisplayName,
                    data.gender==Genders.Female?ResidentSex.Female:ResidentSex.Male,(int)data.race,data.factionID,data.billboardArchiveIndex,data.billboardRecordIndex);
            if(person==null && TryNativeCastleLayoutKey(npc,out castle))
                person=store.RegisterNativeLayoutPerson(map,castle,data.hash,npc.DisplayName,data.gender==Genders.Female?ResidentSex.Female:ResidentSex.Male,
                    (int)data.race,data.factionID,data.billboardArchiveIndex,data.billboardRecordIndex);
            if(person==null)
            {
                string key=StationarySceneKey();var resource=npc.GetComponent<QuestResourceBehaviour>();
                var block=npc.GetComponentInParent<DaggerfallRDBBlock>();
                if(resource && resource.QuestUID!=0 && resource.TargetSymbol!=null && !string.IsNullOrEmpty(resource.TargetSymbol.Original))
                    key+=":quest:"+resource.QuestUID+":"+resource.TargetSymbol.Original;
                else if(block)key+=":block:"+block.RDBPosition.x.ToString("R",CultureInfo.InvariantCulture)+":"+block.RDBPosition.y.ToString("R",CultureInfo.InvariantCulture)+":seed:"+data.nameSeed;
                else
                {
                    Vector3 point=directory?directory.transform.InverseTransformPoint(npc.transform.position):npc.transform.position;
                    key+=":point:"+Mathf.RoundToInt(point.x*10)+":"+Mathf.RoundToInt(point.z*10);
                }
                person=store.RegisterNativeLayoutPerson(map,key,data.hash,npc.DisplayName,data.gender==Genders.Female?ResidentSex.Female:ResidentSex.Male,
                    (int)data.race,data.factionID,data.billboardArchiveIndex,data.billboardRecordIndex);
            }
            if(!person.IsAlive || person.RetiredBedroomFlat){npc.gameObject.SetActive(false);return null;}
            var nativeBody=npc.GetComponent<DaggerfallEntityBehaviour>();
            if(directory && !game.PlayerEnterExit.IsPlayerInsideDungeon && !(nativeBody && nativeBody.Entity is EnemyEntity && npc.GetComponent<EnemyDeath>()) && !store.HasNpcInventory(person.Id))
            {
                var catalog=NativeMarketCatalog.For(directory);var post=catalog==null?null:catalog.Find(npc);string reason;
                if(post!=null && !NativeMarketStock.EnsureInitialInventory(store,person,post,out reason))throw new InvalidOperationException(reason);
            }
            existing=npc.gameObject.AddComponent<NativeStationaryLife>();existing.Bind(this,store,person);stationaryLives.Add(existing);return existing;
        }
        private void TickStationaryLife()
        {
            var game=GameManager.Instance;
            if(saveFault || !options.Enabled || store==null || game==null || !game.IsReady || !game.IsPlayingGame() || !game.PlayerGPS.HasCurrentLocation || Time.unscaledTime<nextStationaryLife)return;
            nextStationaryLife=Time.unscaledTime+1;
            if(!stationaryTransitionsRegistered){PlayerEnterExit.OnPreTransition+=BeforeStationaryTransition;stationaryTransitionsRegistered=true;}
            stationaryLives.RemoveAll(life=>!life);
            foreach(var npc in UnityEngine.Object.FindObjectsOfType<StaticNPC>())EnsureStationaryLife(npc);
            RefreshStationaryCorpses();
        }
        internal void ObserveStationaryPlayerHit(NativeStationaryLife life)
        {if(streets)streets.ObserveStationaryPlayerHit(store,life.Person,life.transform.position);}
        internal void RecordStationaryDeath(NativeStationaryLife life,bool playerHit)
        {
            var game=GameManager.Instance;bool dungeon=game.PlayerEnterExit.IsPlayerInsideDungeon;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            Transform root=game.PlayerEnterExit.IsPlayerInsideBuilding?game.PlayerEnterExit.Interior.transform:
                dungeon?game.PlayerEnterExit.Dungeon.transform:directory?directory.transform:transform;
            var pose=life.GetComponent<SleepingPose>();var billboard=life.GetComponent<DaggerfallBillboard>();
            Vector3 ground=pose && pose.Active?life.transform.position:life.transform.position-Vector3.up*(billboard?billboard.Summary.Size.y*0.5f:1);
            var point=root.InverseTransformPoint(ground);int building=game.PlayerEnterExit.IsPlayerInsideBuilding?game.PlayerEnterExit.BuildingDiscoveryData.buildingKey:-1;
            var alarm=store.RecordNativeStationaryDeath(life.Person.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),building,point.x,point.y,point.z,playerHit);
            if(playerHit && (!streets || !streets.ReportStationaryPlayerDeath(store,alarm)))
            {game.PlayerEntity.TallyCrimeGuildRequirements(false,5);game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Murder;if(!dungeon)game.PlayerEntity.SpawnCityGuards(true);}
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIONARY_DEATH: person="+life.Person.Id+"; nativeQuest="+(life.GetComponent<QuestResourceBehaviour>()!=null)+"; oneInventory=true");
        }
        private void CaptureStationaryLife(){foreach(var life in stationaryLives)if(life)life.Capture();foreach(var corpse in stationaryCorpses.Values)if(corpse)corpse.Capture();}
        private void BeforeStationaryTransition(PlayerEnterExit.TransitionEventArgs args){CaptureStationaryLife();}
        private void ResetStationaryLife()
        {
            if(stationaryTransitionsRegistered)PlayerEnterExit.OnPreTransition-=BeforeStationaryTransition;stationaryTransitionsRegistered=false;
            foreach(var life in stationaryLives)if(life){life.Release();DestroyImmediate(life);}stationaryLives.Clear();
            foreach(var corpse in stationaryCorpses.Values)if(corpse)DestroyImmediate(corpse.gameObject);stationaryCorpses.Clear();nextStationaryLife=0;
        }
        private void RefreshStationaryCorpses()
        {
            var game=GameManager.Instance;int map=game.PlayerGPS.CurrentMapID;bool dungeon=game.PlayerEnterExit.IsPlayerInsideDungeon;
            var wanted=new HashSet<string>();
            if(!streets || !streets.OwnsStationaryCorpses(store,map))
            {
                var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                Transform root=game.PlayerEnterExit.IsPlayerInsideBuilding?game.PlayerEnterExit.Interior.transform:dungeon?game.PlayerEnterExit.Dungeon.transform:directory?directory.transform:transform;
                int building=game.PlayerEnterExit.IsPlayerInsideBuilding?game.PlayerEnterExit.BuildingDiscoveryData.buildingKey:-1;
                foreach(var alarm in store.ObserveCorpses(map,building,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()))
                {
                    var person=store.NativePersonInSettlement(map,alarm.VictimId);var body=alarm.Corpse;
                    if(person==null || !body.HasPosition || body.BuildingKey!=building ||
                        dungeon && (person.NativeLayoutKey==null || !(person.NativeLayoutKey.StartsWith(StationarySceneKey()+":",StringComparison.Ordinal) ||
                            game.IsPlayerInsideCastle && person.NativeLayoutKey.StartsWith("castle:"+game.PlayerGPS.CurrentLocation.Exterior.ExteriorData.LocationId+":",StringComparison.Ordinal))) ||
                        !dungeon && person.NativeLayoutKey!=null && person.NativeLayoutKey.StartsWith("scene:dungeon:",StringComparison.Ordinal))continue;
                    wanted.Add(alarm.Id);NativeStationaryCorpse existing;
                    if(stationaryCorpses.TryGetValue(alarm.Id,out existing) && existing)continue;
                    var view=GameObjectHelper.CreateDaggerfallBillboardGameObject(380,1,root);view.transform.position=root.TransformPoint(new Vector3((float)body.X,(float)body.Y,(float)body.Z));
                    view.GetComponent<DaggerfallBillboard>().AlignToBase();existing=view.AddComponent<NativeStationaryCorpse>();existing.Bind(store,person.Id);stationaryCorpses[alarm.Id]=existing;
                }
            }
            foreach(string id in new List<string>(stationaryCorpses.Keys))if(!wanted.Contains(id)){var body=stationaryCorpses[id];if(body){body.Capture();Destroy(body.gameObject);}stationaryCorpses.Remove(id);}
        }
    }
}
