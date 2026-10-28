using System;
using System.Collections.Generic;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal sealed class CompanionFastTravelMember
    {internal string ActionId;internal ResidentActor Actor;internal bool OriginalOwner;}
    internal sealed class CompanionFastTravelQuote
    {
        internal WorldStateStore Store;
        internal readonly List<CompanionFastTravelMember> Members=new List<CompanionFastTravelMember>();
    }
    public sealed partial class RoadTravelActors
    {
        internal static RoadTravelActors FastTravelOwner {get;private set;}
        private readonly Dictionary<string,CompanionFastTravelMember> fastTravelFollowers=new Dictionary<string,CompanionFastTravelMember>();
        private CompanionFastTravelQuote fastTravelTransit;
        private bool fastTravelArrived;
        private int fastTravelDestination;
        private float nextFastTravelPositionSave;

        private void InstallFastTravel()
        {
            FastTravelOwner=this;
            DaggerfallTravelPopUp.OnPreFastTravel+=BeforeNativeFastTravel;
            DaggerfallTravelPopUp.OnPostFastTravel+=AfterNativeFastTravel;
            UIWindowFactory.RegisterCustomUIWindow(UIWindowType.TravelPopUp,typeof(NativeCompanionTravelPopUp));
        }
        private void UninstallFastTravel()
        {
            DaggerfallTravelPopUp.OnPreFastTravel-=BeforeNativeFastTravel;
            DaggerfallTravelPopUp.OnPostFastTravel-=AfterNativeFastTravel;
            if(FastTravelOwner==this)FastTravelOwner=null;
        }
        private ResidentActor FindFastTravelFollower(string id)
        {CompanionFastTravelMember member;return id!=null && fastTravelFollowers.TryGetValue(id,out member)?member.Actor:null;}
        internal CompanionFastTravelQuote CaptureFastTravelQuote()
        {
            var quote=new CompanionFastTravelQuote();var game=GameManager.Instance;
            if(source==null || enabledForWorld==null || !enabledForWorld() || !game || game.IsPlayerInside)return quote;
            quote.Store=source();if(quote.Store==null)return quote;double now=DungeonNow();
            foreach(var job in quote.Store.PendingNpcActions())
            {
                if(!NpcCompanionFastTravel.CanFollow(quote.Store,job,now))continue;
                var actor=streets?streets.FindActor(job.ResidentId):null;
                if(!actor)actor=FindPaidCompanion(job.ResidentId);
                if(!actor)actor=Find(job.ResidentId);
                if(!actor || !actor.gameObject.activeInHierarchy || actor.Resident==null || !actor.Resident.IsAlive || actor.Resident.IsQuestProtected ||
                    !ReferenceEquals(actor.CompanionStateStore,quote.Store) || actor.IsConversing || actor.IsSleeping || actor.IsSeated ||
                    streets && streets.QuestOwnsResident!=null && streets.QuestOwnsResident(job.ResidentId) ||
                    Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)>32)continue;
                quote.Members.Add(new CompanionFastTravelMember {ActionId=job.Id,Actor=actor,OriginalOwner=job.FastTravelFollowOwner});
            }
            quote.Members.Sort((a,b)=>string.CompareOrdinal(a.Actor.Resident.Id,b.Actor.Resident.Id));return quote;
        }
        internal bool FastTravelQuoteMatches(CompanionFastTravelQuote quote)
        {
            if(quote==null)return false;var currentQuote=CaptureFastTravelQuote();
            if(!ReferenceEquals(quote.Store,currentQuote.Store) || quote.Members.Count!=currentQuote.Members.Count)return false;
            for(int index=0;index<quote.Members.Count;index++)
                if(quote.Members[index].Actor!=currentQuote.Members[index].Actor || quote.Members[index].ActionId!=currentQuote.Members[index].ActionId)return false;
            return true;
        }
        internal bool PrepareFastTravelQuote(CompanionFastTravelQuote quote)
        {
            if(fastTravelTransit!=null || !FastTravelQuoteMatches(quote))return false;
            try
            {
                foreach(var member in quote.Members)member.Actor.CaptureState();
                if(!FastTravelQuoteMatches(quote))return false;
                foreach(var member in quote.Members)
                {
                    var job=quote.Store.ReadNpcAction(member.ActionId);
                    if(!quote.Store.TrySetFastTravelFollowOwner(job.Id,job.Revision,DungeonNow(),true))
                    {AbortFastTravelQuote(quote);return false;}
                }
                return true;
            }
            catch(Exception error)
            {AbortFastTravelQuote(quote);Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_NOT_READY: "+error.Message);return false;}
        }
        internal void AbortFastTravelQuote(CompanionFastTravelQuote quote)
        {
            if(quote==null || quote.Store==null || source==null || !ReferenceEquals(source(),quote.Store))return;
            foreach(var member in quote.Members)
            {
                if(member.OriginalOwner)continue;var job=quote.Store.ReadNpcAction(member.ActionId);
                if(job!=null && job.FastTravelFollowOwner)quote.Store.TrySetFastTravelFollowOwner(job.Id,job.Revision,DungeonNow(),false);
            }
        }
        private void BeforeNativeFastTravel(DaggerfallTravelPopUp window)
        {
            var partyWindow=window as NativeCompanionTravelPopUp;
            var quote=partyWindow==null?null:partyWindow.ConfirmedQuote;
            if(quote==null || quote.Members.Count==0)return;
            // Every rejectable check/capture happened before the native debit.
            // This event only transfers those exact already-confirmed bodies.
            partyWindow.RecordNativeDeparture();fastTravelTransit=quote;fastTravelArrived=false;current=quote.Store;
            var reader=DaggerfallUnity.Instance.ContentReader;DaggerfallWorkshop.Utility.ContentReader.MapSummary destination;
            fastTravelDestination=reader.HasLocation(window.EndPos.X,window.EndPos.Y,out destination)?destination.MapID:-1;
            foreach(var member in quote.Members)
            {
                var actor=member.Actor;string id=actor.Resident.Id;
                var inventoryOwner=actor.GetComponent<NativeNpcInventoryOwner>();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_MEMBER: person="+id+"; action="+member.ActionId+
                    "; body="+actor.GetInstanceID()+"; inventoryOwner="+(inventoryOwner?inventoryOwner.GetInstanceID():0));
                if(streets)streets.ReleaseTransferredActor(actor);
                actors.Remove(id);paidCompanions.Remove(id);paidActions.Remove(id);retiredPaidCompanions.Remove(id);fastTravelFollowers.Remove(id);
                actor.TransferOwner(transform,navigation);
                var native=actor.GetComponent<NativeResidentNavigation>();if(native)native.ResetPlacement();
                actor.gameObject.SetActive(false);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_DEPART: companions="+quote.Members.Count+"; destination="+fastTravelDestination+
                "; nativeFare="+partyWindow.ConfirmedTotal+"; purseBefore="+partyWindow.GoldBeforeTravel+
                "; purseAfterNativeDebit="+GameManager.Instance.PlayerEntity.GetGoldAmount()+"; extraDebit=false; sameNativeBodies=true");
        }
        private void AfterNativeFastTravel(){if(fastTravelTransit!=null)fastTravelArrived=true;}

        private bool AdvanceFastTravelArrival(WorldStateStore store,double now)
        {
            if(fastTravelTransit==null)return false;
            var game=GameManager.Instance;var world=game.StreamingWorld;
            if(!fastTravelArrived || world.IsInit || !world.IsReady || world.IsRepositioningPlayer || game.IsPlayerInside || !game.PlayerGPS.HasCurrentLocation)return true;
            int map=game.PlayerGPS.CurrentLocation.MapTableData.MapId;
            if(fastTravelDestination>=0 && map!=fastTravelDestination)return true;
            foreach(var member in new List<CompanionFastTravelMember>(fastTravelTransit.Members))
            {
                if(!member.Actor || member.Actor.Resident==null || !member.Actor.Resident.IsAlive)
                {fastTravelTransit.Members.Remove(member);continue;}
                Vector3 ground;if(!FastTravelLanding(member.Actor,out ground))return true;
                var actor=member.Actor;var native=actor.GetComponent<NativeResidentNavigation>();if(native)native.ResetPlacement();
                actor.GetComponent<CrowdMotion>().RestorePlacement(ground);actor.gameObject.SetActive(true);
                var job=store.ReadNpcAction(member.ActionId);
                if(job!=null && job.PaidTravelHandoff!=null)
                {
                    if(now>=job.FinishAt)store.TryReleaseExpiredPaidTravel(job.Id,job.Revision,now);
                    else store.TryResumePaidTravel(job.Id,job.Revision,now);
                    job=store.ReadNpcAction(member.ActionId);
                }
                SaveFastTravelPosition(store,job,ground,now,map);
                job=store.ReadNpcAction(member.ActionId);
                bool active=NpcCompanionFastTravel.CanFollow(store,job,now);
                if(!active)EndFastTravelFollow(store,member,now);
                fastTravelFollowers[actor.Resident.Id]=member;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_ARRIVE: person="+actor.Resident.Id+"; action="+member.ActionId+
                    "; map="+map+"; body="+actor.GetInstanceID()+"; following="+active+"; originalDeadline="+(job==null?0:job.DeadlineAt)+
                    "; originalFinish="+(job==null?0:job.FinishAt)+"; contractExtended=false; sameInventoryOwner=true");
                fastTravelTransit.Members.Remove(member);
            }
            fastTravelTransit=null;fastTravelArrived=false;return false;
        }
        private bool FastTravelLanding(ResidentActor actor,out Vector3 ground)
        {
            var player=GameManager.Instance.PlayerMotor.FindGroundPosition();
            int rings=Math.Max(3,fastTravelTransit==null?3:(fastTravelTransit.Members.Count+7)/8+1);
            for(int ring=0;ring<rings;ring++)for(int direction=0;direction<8;direction++)
            {
                float angle=direction*Mathf.PI*0.25f;
                var offset=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(2.5f+ring*0.7f);
                if(ClearFastTravelGround(player+offset,actor,out ground))return true;
            }
            ground=Vector3.zero;return false;
        }
        private static bool ClearFastTravelGround(Vector3 expected,ResidentActor actor,out Vector3 ground)
        {
            foreach(var hit in Physics.RaycastAll(expected+Vector3.up*0.6f,Vector3.down,1.2f,~0,QueryTriggerInteraction.Ignore))
            {
                if(!hit.collider || hit.normal.y<0.7f || NativeNavigationGeometry.IsDynamicNavigationBody(hit.collider))continue;
                bool blocked=false;
                foreach(var collider in Physics.OverlapCapsule(hit.point+Vector3.up*0.78f,hit.point+Vector3.up*1.6f,CrowdMotion.BodyRadius,~0,QueryTriggerInteraction.Collide))
                    if(collider && (!actor || !collider.transform.IsChildOf(actor.transform)) && (!collider.isTrigger || collider.GetComponentInParent<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>() || collider.GetComponentInParent<PlayerMotor>()))
                    {blocked=true;break;}
                if(blocked)continue;ground=hit.point;return true;
            }
            ground=Vector3.zero;return false;
        }
        private static bool SavedFastTravelLanding(NpcActionJob job,out Vector3 ground)
        {
            ground=Vector3.zero;var game=GameManager.Instance;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();var position=job==null?null:job.LastPosition;
            if(!directory || position==null || position.Kind!=NpcActionTargetKind.WorldPoint ||
                position.SettlementId!=directory.MapID || position.WorldSpaceId!="settlement:"+directory.MapID)return false;
            var expected=directory.transform.TransformPoint(new Vector3((float)position.X,(float)position.Y,(float)position.Z));
            // Reload resumes the saved body position. A blocked or distant
            // point stays pending; it must not become a new arrival by the player.
            return Vector3.Distance(expected,game.PlayerMotor.transform.position)<=140 && ClearFastTravelGround(expected,null,out ground);
        }
        private void UpdateFastTravelFollowers(WorldStateStore store,double now)
        {
            var game=GameManager.Instance;if(game.IsPlayerInside)return;
            if(game.StreamingWorld.IsInit)
            {
                foreach(var member in fastTravelFollowers.Values)if(member.Actor)
                {member.Actor.CaptureState();member.Actor.gameObject.SetActive(false);Destroy(member.Actor.gameObject);}
                fastTravelFollowers.Clear();return;
            }
            if(!game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer)return;
            foreach(var job in store.PendingNpcActions())
            {
                if(!job.FastTravelFollowOwner || !NpcCompanionFastTravel.CanFollow(store,job,now) || fastTravelFollowers.ContainsKey(job.ResidentId) ||
                    fastTravelTransit!=null && fastTravelTransit.Members.Exists(member=>member.ActionId==job.Id) ||
                    streets && streets.FindActor(job.ResidentId) || paidCompanions.ContainsKey(job.ResidentId) || OwnsDungeonFollow(job.ResidentId) ||
                    !game.PlayerGPS.HasCurrentLocation || job.LastPosition==null || job.LastPosition.SettlementId!=game.PlayerGPS.CurrentLocation.MapTableData.MapId)continue;
                Vector3 ground;if(!SavedFastTravelLanding(job,out ground))continue;
                var body=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,transform);
                var actor=body.AddComponent<ResidentActor>();actor.Initialize(store.ResolveResident(job.ResidentId),0,navigation,inventoryStore:store);
                actor.GetComponent<CrowdMotion>().RestorePlacement(ground);
                fastTravelFollowers[job.ResidentId]=new CompanionFastTravelMember {ActionId=job.Id,Actor=actor,OriginalOwner=true};
            }
            foreach(var pair in new List<KeyValuePair<string,CompanionFastTravelMember>>(fastTravelFollowers))
            {
                var member=pair.Value;var actor=member.Actor;var job=store.ReadNpcAction(member.ActionId);
                if(!actor){fastTravelFollowers.Remove(pair.Key);continue;}
                if(!NpcCompanionFastTravel.CanFollow(store,job,now))
                {
                    EndFastTravelFollow(store,member,now);
                    if(game.StreamingWorld.IsInit || Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)>165)
                    {actor.CaptureState();actor.gameObject.SetActive(false);Destroy(actor.gameObject);fastTravelFollowers.Remove(pair.Key);continue;}
                    actor.HoldPhysicalPosition();continue;
                }
                FollowPlayer(actor);
                if(Time.unscaledTime>=nextFastTravelPositionSave && game.PlayerGPS.HasCurrentLocation)
                    SaveFastTravelPosition(store,job,actor.transform.position-Vector3.up,now,game.PlayerGPS.CurrentLocation.MapTableData.MapId);
            }
            if(Time.unscaledTime>=nextFastTravelPositionSave)nextFastTravelPositionSave=Time.unscaledTime+0.5f;
        }
        private static void SaveFastTravelPosition(WorldStateStore store,NpcActionJob job,Vector3 ground,double now,int map)
        {
            var directory=GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
            if(job==null || !directory || directory.MapID!=map)return;
            var local=directory.transform.InverseTransformPoint(ground);
            store.SaveNpcActionPosition(job.Id,job.Revision,new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=map,
                WorldSpaceId="settlement:"+map,X=local.x,Y=local.y,Z=local.z},false,now);
        }
        private void EndFastTravelFollow(WorldStateStore store,CompanionFastTravelMember member,double now)
        {
            var job=store.ReadNpcAction(member.ActionId);if(job==null)return;
            if(!NpcActionExecutor.IsTerminal(job))
            {
                store.ObserveNpcAction(job.Id,job.Revision,now,NpcActionObservation.Interrupted("The agreed Follow period ended during travel."));
                DaggerfallUI.AddHUDText(member.Actor.Resident.FullName+": Our agreed time has ended. Ask me again if you want me to continue.");
                job=store.ReadNpcAction(member.ActionId);
            }
            if(job!=null && NpcActionExecutor.IsTerminal(job) && NpcPaidCompanionAction.IsPaidFollowJob(job))
            {string reason;StreetPopulation.SettlePaidCompanionAction(store,job,NpcServiceEndReason.NaturalExpiry,out reason);}
        }
        private void ClearFastTravelFollowers()
        {
            foreach(var member in fastTravelFollowers.Values)if(member.Actor)
            {member.Actor.CaptureState();member.Actor.gameObject.SetActive(false);Destroy(member.Actor.gameObject);}
            fastTravelFollowers.Clear();
            if(fastTravelTransit!=null)foreach(var member in fastTravelTransit.Members)if(member.Actor)
            {member.Actor.CaptureState();Destroy(member.Actor.gameObject);}
            fastTravelTransit=null;fastTravelArrived=false;
        }
    }
}
