using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class StreetChat
        {
            public ResidentActor A,B;
            public Vector3 LocalA,LocalB;
            public double EndsAt;
        }
        private readonly Dictionary<string,StreetChat> streetChats=new Dictionary<string,StreetChat>();
        private readonly Dictionary<string,double> nextStreetChat=new Dictionary<string,double>();
        private double nextChatScan;
        private bool ChatEligible(ResidentActor actor,double now)
        {
            return actor && actor.gameObject.activeInHierarchy && actor.Resident.IsAlive &&
                actor.Resident.AgeGroup==ResidentAgeGroup.Adult && !actor.IsSleeping && !actor.IsConversing &&
                !IsAlarmed(actor.Resident.Id) && !bystanderPostures.ContainsKey(actor.Resident.Id) &&
                GetState(actor.Resident.Id,now).Activity==Activity.Travel;
        }
        private void PrepareStreetChats(double now)
        {
            if(GameManager.IsGamePaused) return;
            foreach(var chat in new HashSet<StreetChat>(streetChats.Values))
                if(now>=chat.EndsAt || !ChatEligible(chat.A,now) || !ChatEligible(chat.B,now)) EndStreetChat(chat,now);
            if(now<nextChatScan) return;nextChatScan=now+12;
            var candidates=new List<ResidentActor>(actors.Values);
            candidates.Sort((a,b)=>StringComparer.Ordinal.Compare(a ? a.Resident.Id : "",b ? b.Resident.Id : ""));
            for(int i=0;i<candidates.Count;i++)
            {
                var a=candidates[i];double ready;
                if(!ChatEligible(a,now) || streetChats.ContainsKey(a.Resident.Id) ||
                    nextStreetChat.TryGetValue(a.Resident.Id,out ready) && now<ready) continue;
                for(int j=i+1;j<candidates.Count;j++)
                {
                    var b=candidates[j];
                    if(!ChatEligible(b,now) || streetChats.ContainsKey(b.Resident.Id) ||
                        nextStreetChat.TryGetValue(b.Resident.Id,out ready) && now<ready) continue;
                    float distance=Vector3.Distance(a.transform.position,b.transform.position);
                    if(!SocialInteractionSpacing.Contains(distance) || !CivilianResponses.Sees(a.transform.position,b.transform.position,3)) continue;
                    uint window=(uint)(now/600);
                    if(!StreetChatTiming.ShouldMeet(a.Resident.AppearanceSeed,b.Resident.AppearanceSeed,window)) continue;
                    double end=now+StreetChatTiming.Duration(a.Resident.AppearanceSeed,b.Resident.AppearanceSeed,window);
                    if(GetState(a.Resident.Id,end).Activity!=Activity.Travel || GetState(b.Resident.Id,end).Activity!=Activity.Travel) continue;
                    var chat=new StreetChat {A=a,B=b,EndsAt=end,
                        LocalA=directory.transform.InverseTransformPoint(a.transform.position-Vector3.up),
                        LocalB=directory.transform.InverseTransformPoint(b.transform.position-Vector3.up)};
                    streetChats[a.Resident.Id]=chat;streetChats[b.Resident.Id]=chat;break;
                }
            }
        }
        private void EndStreetChat(StreetChat chat,double now)
        {
            foreach(var id in new List<string>(streetChats.Keys))
                if(streetChats[id]==chat) {streetChats.Remove(id);nextStreetChat[id]=now+1800;}
            foreach(var actor in new[]{chat.A,chat.B}) if(actor)
                actor.ResumeFromSocialPause();
        }
        private bool PlaceStreetChat(ResidentActor actor,double now)
        {
            StreetChat chat;if(!streetChats.TryGetValue(actor.Resident.Id,out chat)) return false;
            if(!ChatEligible(actor,now)) {EndStreetChat(chat,now);return false;}
            bool first=actor==chat.A;
            var ground=directory.transform.TransformPoint(first ? chat.LocalA : chat.LocalB);
            var other=first ? chat.B : chat.A;
            actor.Place(ground,other.transform.position-(ground+Vector3.up),true);return true;
        }
    }
}
