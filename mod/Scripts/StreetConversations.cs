using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class StreetChat
        {
            public ResidentActor A,B;
            public StationaryParticipant Stationary;
            public Vector3 LocalA,LocalB,OriginalHeading;
            public Transform Scene;
            public double EndsAt;
            public string FirstId,OtherId,OtherName;
            public Transform OtherBody {get{return B?B.transform:Stationary!=null && Stationary.Npc?Stationary.Npc.transform:null;}}
        }
        private readonly Dictionary<string,StreetChat> streetChats=new Dictionary<string,StreetChat>();
        private readonly Dictionary<string,double> nextStreetChat=new Dictionary<string,double>();
        private double nextChatScan;
        private int chatCandidateCursor;
        private bool ChatEligible(ResidentActor actor,double now)
        {
            if(!actor || !actor.gameObject.activeInHierarchy || !actor.Resident.IsAlive || actor.IsSleeping || actor.IsConversing || actor.IsSeated ||
                actor.Resident.AgeGroup!=ResidentAgeGroup.Adult || IsAlarmed(actor.Resident.Id) || bystanderPostures.ContainsKey(actor.Resident.Id))return false;
            var plan=plans.Find(p=>p.Resident.Id==actor.Resident.Id);
            if(plan==null || !ClosedShopEligible(plan,now))return false;
            ClosedShopWait wait;
            if(closedShopWaits.TryGetValue(actor.Resident.Id,out wait) && wait.Group!=null && wait.Group.Members.Count>1)return false;
            var state=GetState(actor.Resident.Id,now);
            // Work, appointments, money errands, medical visits and shared family
            // movement keep their own priority. Ordinary travel and browsing can pause.
            if(state.Activity==Activity.Travel)
                return ActiveSchedule(plan).EvaluateSeconds(now+360).Activity!=Activity.Work;
            if(interior && state.Activity==Activity.Visit)
            {
                PhysicalShopVisit visit;
                return !physicalShopVisits.TryGetValue(actor.Resident.Id,out visit) ||
                    visit.Snapshot.Phase==ShopVisitPhase.Browsing || visit.Snapshot.Phase==ShopVisitPhase.Walking;
            }
            return false;
        }
        private bool StaticChatEligible(StationaryParticipant entry,double now)
        {
            if(!StaticObserverAwake(entry.Npc,entry.Person) || entry.Npc.IsChildNPC ||
                entry.Npc.GetComponent<QuestResourceBehaviour>() || entry.Npc.GetComponent<DaggerfallAction>() ||
                worldStore.NativeResponse(entry.Person.Id)!=null)return false;
            // A customer already at this counter takes priority over small talk.
            foreach(var actor in actors.Values)
            {
                if(!actor || !actor.gameObject.activeInHierarchy || Vector3.Distance(actor.transform.position,entry.Npc.transform.position)>3)continue;
                PhysicalShopVisit visit;
                if(worldStore.ReadNpcCashVisit(actor.Resident.Id)!=null ||
                    physicalShopVisits.TryGetValue(actor.Resident.Id,out visit) && visit.Snapshot.Phase==ShopVisitPhase.Haggling)return false;
            }
            return true;
        }
        private bool ChatReady(string id,double now)
        {double ready;return !streetChats.ContainsKey(id) && (!nextStreetChat.TryGetValue(id,out ready) || now>=ready);}
        private void PrepareStreetChats(double now)
        {
            if(GameManager.IsGamePaused || now<nextChatScan)return;nextChatScan=now+12;
            foreach(var chat in new HashSet<StreetChat>(streetChats.Values))
                if(now>=chat.EndsAt || !ChatEligible(chat.A,now) ||
                    (chat.Stationary==null?!ChatEligible(chat.B,now):!StaticChatEligible(chat.Stationary,now)))EndStreetChat(chat,now);
            var candidates=new List<ResidentActor>(actors.Values);
            // A rotating four-person budget avoids an all-pairs search every frame.
            for(int attempt=0;attempt<Math.Min(4,candidates.Count);attempt++)
            {
                if(chatCandidateCursor>=candidates.Count)chatCandidateCursor=0;
                var a=candidates[chatCandidateCursor++];
                if(!ChatEligible(a,now) || !ChatReady(a.Resident.Id,now))continue;
                uint window=(uint)(now/600);bool met=false;
                foreach(var b in candidates)
                {
                    if(a==b || !ChatEligible(b,now) || !ChatReady(b.Resident.Id,now) ||
                        !SocialInteractionSpacing.Contains(Vector3.Distance(a.transform.position,b.transform.position)) ||
                        !CivilianResponses.Sees(a.transform.position,b.transform.position,3) ||
                        !StreetChatTiming.ShouldMeet(a.Resident.AppearanceSeed,b.Resident.AppearanceSeed,window))continue;
                    double end=now+StreetChatTiming.Duration(a.Resident.AppearanceSeed,b.Resident.AppearanceSeed,window);
                    if(!ChatHasTime(a,now,end) || !ChatHasTime(b,now,end))continue;
                    BeginStreetChat(a,b,null,end);met=true;break;
                }
                if(met)continue;
                foreach(var entry in stationaryParticipants.Values)
                {
                    if(!StaticChatEligible(entry,now) || !ChatReady(entry.Person.Id,now) ||
                        !SocialInteractionSpacing.Contains(Vector3.Distance(a.transform.position,entry.Npc.transform.position)) ||
                        !CivilianResponses.Sees(a.transform.position,entry.Npc.transform.position,3))continue;
                    uint seed=unchecked((uint)entry.Person.NativeHash);
                    if(!StreetChatTiming.ShouldMeet(a.Resident.AppearanceSeed,seed,window))continue;
                    double end=now+Math.Min(180,StreetChatTiming.Duration(a.Resident.AppearanceSeed,seed,window));
                    if(!ChatHasTime(a,now,end))continue;
                    BeginStreetChat(a,null,entry,end);break;
                }
            }
        }
        private bool ChatHasTime(ResidentActor actor,double now,double end)
        {
            var before=GetState(actor.Resident.Id,now);var after=GetState(actor.Resident.Id,end);
            return after.Activity==before.Activity && after.BuildingKey==before.BuildingKey;
        }
        private void BeginStreetChat(ResidentActor a,ResidentActor b,StationaryParticipant stationary,double end)
        {
            Transform scene=interior?interior.transform:directory.transform;
            var other=b?b.transform:stationary.Npc.transform;
            var pose=stationary==null?null:stationary.Npc.GetComponent<DirectionalKeeperPose>();
            var chat=new StreetChat {A=a,B=b,Stationary=stationary,EndsAt=end,Scene=scene,
                FirstId=a.Resident.Id,OtherId=b?b.Resident.Id:stationary.Person.Id,OtherName=b?b.Person.NameNPC:stationary.Person.Name,
                LocalA=scene.InverseTransformPoint(a.transform.position-Vector3.up),
                LocalB=scene.InverseTransformPoint(other.position-Vector3.up),OriginalHeading=pose?pose.Heading:other.forward};
            streetChats[a.Resident.Id]=chat;streetChats[chat.OtherId]=chat;
            if(pose)pose.Face(a.transform.position-other.position);
            string eventId="street-chat:"+Guid.NewGuid().ToString("N");
            NativeSceneRecollection.Remember(worldStore,a.Resident.Id,eventId,NpcSceneEventKind.Conversation,
                "I stopped for a short conversation with "+chat.OtherName+" before continuing my plans.",1,chat.OtherId);
            NativeSceneRecollection.Remember(worldStore,chat.OtherId,eventId,NpcSceneEventKind.Conversation,
                "I had a short conversation with "+a.Person.NameNPC+(stationary==null?" before continuing my plans.":" here at my post."),1,a.Resident.Id);
        }
        private void EndStreetChat(StreetChat chat,double now)
        {
            foreach(string id in new[]{chat.FirstId,chat.OtherId})if(id!=null)
            {streetChats.Remove(id);nextStreetChat[id]=now+1200+StreetChatTiming.Duration(chat.A?chat.A.Resident.AppearanceSeed:0,1,(uint)(now/600))*3;}
            if(chat.Stationary!=null && chat.Stationary.Npc)
            {var pose=chat.Stationary.Npc.GetComponent<DirectionalKeeperPose>();if(pose)pose.Face(chat.OriginalHeading);}
            if(chat.A)chat.A.ResumeFromSocialPause();if(chat.B)chat.B.ResumeFromSocialPause();
        }
        private void ClearStreetChats()
        {foreach(var chat in new HashSet<StreetChat>(streetChats.Values))EndStreetChat(chat,0);streetChats.Clear();}
        internal bool TryStreetChatContext(string personId,double now,out string context)
        {
            context=null;StreetChat chat;
            if(personId==null || !streetChats.TryGetValue(personId,out chat) || now>=chat.EndsAt || !chat.A || !chat.OtherBody)return false;
            context="I paused for a brief conversation here with "+(personId==chat.A.Resident.Id?chat.OtherName:chat.A.Person.NameNPC)+
                ". I will continue my previous activity afterwards. No specific conversation topic has been established.";
            return true;
        }
        private bool PlaceStreetChat(ResidentActor actor,double now)
        {
            StreetChat chat;if(!streetChats.TryGetValue(actor.Resident.Id,out chat))return false;
            if(now>=chat.EndsAt || !ChatEligible(actor,now) || !chat.OtherBody)
            {EndStreetChat(chat,now);return false;}
            bool first=actor==chat.A;
            var ground=chat.Scene.TransformPoint(first?chat.LocalA:chat.LocalB);
            var other=first?chat.OtherBody:chat.A.transform;
            PhysicalShopVisit visit;if(physicalShopVisits.TryGetValue(actor.Resident.Id,out visit))visit.Pause(now);
            actor.Place(ground,other.position-(ground+Vector3.up),true,freeStanding:false);return true;
        }
    }
}
