using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class InnChat
        {
            public ResidentActor A,B;
            public double EndsAt;
        }
        private readonly Dictionary<string,InnChat> innChats=new Dictionary<string,InnChat>();
        private readonly Dictionary<string,double> nextInnChat=new Dictionary<string,double>();
        private float nextInnChatScan;

        private bool InnChatEligible(ResidentActor actor)
        {
            PhysicalShopVisit visit;
            return actor && actor.gameObject.activeInHierarchy && actor.Resident.IsAlive &&
                actor.Resident.AgeGroup==ResidentAgeGroup.Adult && !actor.IsSleeping && !actor.IsConversing &&
                !IsAlarmed(actor.Resident.Id) && !bystanderPostures.ContainsKey(actor.Resident.Id) &&
                physicalShopVisits.TryGetValue(actor.Resident.Id,out visit) &&
                visit.HouseholdDayId==null &&
                visit.Snapshot.Phase==ShopVisitPhase.Browsing && actor.IsStandingStill;
        }
        private void EndInnChat(InnChat chat,double now)
        {
            foreach(var id in new List<string>(innChats.Keys)) if(innChats[id]==chat)
            {innChats.Remove(id);nextInnChat[id]=now+120;}
            foreach(var actor in new[]{chat.A,chat.B}) if(actor)
            {
                actor.ResumeFromSocialPause();
            }
        }
        private void UpdateInnChats(double now)
        {
            if(!shopGeometry.IsTavern || GameManager.IsGamePaused) return;
            foreach(var chat in new HashSet<InnChat>(innChats.Values))
                if(now>=chat.EndsAt || !InnChatEligible(chat.A) || !InnChatEligible(chat.B) ||
                    (chat.A.transform.position-chat.B.transform.position).sqrMagnitude>4)
                    EndInnChat(chat,now);
            if(Time.unscaledTime>=nextInnChatScan)
            {
                nextInnChatScan=Time.unscaledTime+1;
                var candidates=new List<ResidentActor>();
                foreach(var actor in actors.Values) if(InnChatEligible(actor)) candidates.Add(actor);
                candidates.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Resident.Id,b.Resident.Id));
                // Only rendered patrons in this room; no settlement/world scan.
                int limit=Math.Min(32,candidates.Count);
                for(int i=0;i<limit;i++)
                {
                    var a=candidates[i];double ready;
                    if(innChats.ContainsKey(a.Resident.Id) || nextInnChat.TryGetValue(a.Resident.Id,out ready) && now<ready) continue;
                    for(int j=i+1;j<limit;j++)
                    {
                        var b=candidates[j];
                        if(innChats.ContainsKey(b.Resident.Id) || nextInnChat.TryGetValue(b.Resident.Id,out ready) && now<ready) continue;
                        var delta=b.transform.position-a.transform.position;
                        if(Math.Abs(delta.y)>0.3f || delta.sqrMagnitude<1 || delta.sqrMagnitude>3.24f ||
                            !CivilianResponses.Sees(a.transform.position,b.transform.position,2)) continue;
                        var chat=new InnChat {A=a,B=b,EndsAt=now+60};
                        innChats[a.Resident.Id]=chat;innChats[b.Resident.Id]=chat;break;
                    }
                }
            }
            foreach(var chat in new HashSet<InnChat>(innChats.Values))
            {
                FaceInnPartner(chat.A,chat.B);FaceInnPartner(chat.B,chat.A);
            }
        }
        private static void FaceInnPartner(ResidentActor actor,ResidentActor partner)
        {
            actor.ResumeFromSocialPause();
            actor.Place(actor.transform.position-Vector3.up,partner.transform.position-actor.transform.position,true,waitAtOccupiedTarget:true);
        }
        internal ResidentActor InnChatPartner(string id)
        {
            InnChat chat;
            if(!innChats.TryGetValue(id,out chat) || !InnChatEligible(chat.A) || !InnChatEligible(chat.B)) return null;
            return chat.A.Resident.Id==id ? chat.B : chat.A;
        }
        private void ClearInnChats()
        {innChats.Clear();nextInnChat.Clear();nextInnChatScan=0;}
    }
}
