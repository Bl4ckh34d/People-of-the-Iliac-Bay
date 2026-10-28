using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class MarketVisit
        {
            internal NativeMarketSlot Slot;
            internal MarketDwell Dwell;
            internal readonly HashSet<string> Seen=new HashSet<string>(StringComparer.Ordinal);
            internal Vector3 LocalResume;
            internal double StartedAt,EndsAt,NextCheck;
            internal int Day,Limit;
            internal bool Returning,Remembered;
            internal string Reason,Purchase,ResumeDescription;
            internal MarketPurchase Choice;
            internal bool Payment,PurchaseResolved;
            internal NativeVendorPurchaseReceipt Receipt;
        }
        private readonly Dictionary<string,MarketVisit> marketVisits=new Dictionary<string,MarketVisit>(StringComparer.Ordinal);
        private readonly Dictionary<string,int> marketVisitDays=new Dictionary<string,int>(StringComparer.Ordinal);

        private ActivitySnapshot MarketVisitState(Plan plan,double now,ActivitySnapshot source)
        {
            MarketVisit visit;
            if(!marketVisits.TryGetValue(plan.Resident.Id,out visit))return source;
            if(!ClosedShopEligible(plan,now) || (int)Math.Floor(now/86400)!=visit.Day)
            {EndMarketVisit(plan.Resident.Id);return source;}
            var point=visit.Returning?visit.LocalResume:visit.Slot.LocalGround;
            return new ActivitySnapshot(Activity.Travel,-1,new RoutePoint(point.x,point.z),0,new RoutePoint(point.x,point.z));
        }
        private bool PlaceMarketVisit(Plan plan,ResidentActor actor,ActivitySnapshot state,double now)
        {
            if(!actor || !directory || geometry==null || interior || worldStore==null || actor.IsConversing || GameManager.IsGamePaused)return false;
            string id=plan.Resident.Id;MarketVisit visit;
            var catalog=NativeMarketCatalog.For(directory);
            if(!marketVisits.TryGetValue(id,out visit))
            {
                int day=(int)Math.Floor(now/86400),lastDay;
                if(state.Activity!=Activity.Travel || marketVisitDays.TryGetValue(id,out lastDay)&&lastDay==day ||
                    !ClosedShopEligible(plan,now) || plan.Resident.AgeGroup!=ResidentAgeGroup.Adult ||
                    StableSeed.Combine(plan.Resident.AppearanceSeed,(uint)day,7619,0)%5>=3)return false;
                ClosedShopWait wait;bool closed=closedShopWaits.TryGetValue(id,out wait) && wait.Schedule==null &&
                    wait.Pastime!=ClosedShopPastime.Conversation && now<wait.ReturnAt;
                if(closedShopWaits.ContainsKey(id) && !closed)return false;
                if(!BackgroundWork.TryStart())return false;
                catalog.PrepareSlots();
                var resume=closed?new RoutePoint(wait.LocalDoor.x,wait.LocalDoor.z):state.TravelDestination??state.Position;
                visit=new MarketVisit {StartedAt=now,Day=day,Limit=2+(int)(plan.Resident.AppearanceSeed%3),
                    EndsAt=closed?wait.ReturnAt:Math.Min(now+1800,day*86400d+sleepHour*3600),
                    LocalResume=new Vector3((float)resume.X,0,(float)resume.Z),
                    Reason=closed?GameManager.Instance.TalkManager.GetBuildingNameForBuildingKey(wait.Building)+
                        " was closed, so I am passing the wait at the market before returning":"a short market stop during my ordinary outing",
                    ResumeDescription=closed?GameManager.Instance.TalkManager.GetBuildingNameForBuildingKey(wait.Building):"my previous route"};
                if(visit.EndsAt-now<180 || !ChooseMarketSlot(plan,actor,visit,catalog,now,true))return false;
                marketVisits.Add(id,visit);marketVisitDays[id]=day;
            }
            if(!ClosedShopEligible(plan,now)){EndMarketVisit(id);return false;}
            if(!visit.Returning && (now>=visit.EndsAt || !visit.Slot.Post.Available))BeginMarketReturn(visit,catalog,id);
            if(!visit.Returning && !visit.Payment && Time.unscaledTime>=visit.NextCheck &&
                !NativeMarketCatalog.ClearBody(visit.Slot.Ground,id))
            {
                visit.NextCheck=Time.unscaledTime+1;
                foreach(var alternative in visit.Slot.Post.Slots)
                {
                    if(alternative.Payment || alternative==visit.Slot ||
                        !catalog.TryClaim(alternative,id,actor.transform.position-Vector3.up,now,visit.EndsAt,actor))continue;
                    visit.Slot=alternative;visit.Dwell.Advance(now,false);break;
                }
            }
            Vector3 target=visit.Returning?geometry.ScenePoint(new RoutePoint(visit.LocalResume.x,visit.LocalResume.z)):visit.Slot.Ground;
            Vector3 actual=actor.transform.position-Vector3.up;bool arrived=Vector3.Distance(actual,target)<=0.4f;
            if(visit.Returning)
            {
                if(arrived){EndMarketVisit(id);return false;}
                actor.Place(target,target-actual,navigationDestination:target);return true;
            }
            var crowd=actor.GetComponent<CrowdMotion>();if(crowd)crowd.ResumeFromPause();
            // Occupied browsing/show attention always keeps the native activity
            // target. freeStanding would turn the NPC toward a passing player.
            actor.Place(target,visit.Slot.Look-actual,standing:arrived,waitAtOccupiedTarget:true,navigationDestination:target);
            actual=actor.transform.position-Vector3.up;arrived=Vector3.Distance(actual,target)<=0.4f;
            bool complete=visit.Dwell.Advance(now,arrived);
            if(arrived && !visit.Remembered && !visit.Payment)
            {
                visit.Remembered=true;
                var post=visit.Slot.Post;bool show=post.Role==MarketPostRole.Performer;
                NativeSceneRecollection.Remember(worldStore,id,"market:"+visit.Day+":"+post.Id,
                    show?NpcSceneEventKind.Company:NpcSceneEventKind.Shopping,
                    "During "+visit.Reason+", I "+(show?"watched the performers at the actual stage":"looked at "+MarketGoodsDescription(post)+" on this market table")+
                    ". I intend to return to my previous route afterwards. No purchase is implied.",
                    (int)(StableSeed.Combine(plan.Resident.AppearanceSeed,(uint)visit.Day,7717,(uint)visit.Seen.Count)%3)-1,id);
            }
            if(!complete || Time.unscaledTime<visit.NextCheck)return true;
            visit.NextCheck=Time.unscaledTime+1;
            if(visit.Payment)CompleteMarketPurchase(plan,actor,visit,now);
            else if(!visit.PurchaseResolved && BeginMarketPurchase(plan,actor,visit,catalog,now))return true;
            if(visit.Seen.Count>=visit.Limit || visit.Slot.Post.Role==MarketPostRole.Performer ||
                now+120>=visit.EndsAt || !ChooseMarketSlot(plan,actor,visit,catalog,now,false))BeginMarketReturn(visit,catalog,id);
            return true;
        }
        private bool ChooseMarketSlot(Plan plan,ResidentActor actor,MarketVisit visit,NativeMarketCatalog catalog,double now,bool first)
        {
            var choices=new List<MarketVisitSlot>();var live=new Dictionary<string,NativeMarketSlot>(StringComparer.Ordinal);
            foreach(var post in catalog.Posts)
            {
                if(!post.Available || visit.Seen.Contains(post.Id))continue;
                foreach(var slot in post.Slots)
                {
                    if(slot.Payment || Vector3.Distance(actor.transform.position-Vector3.up,slot.Ground)>(first?16:35))continue;
                    var local=slot.LocalGround;choices.Add(new MarketVisitSlot(slot.Id,post.Id,local.x,local.z));live.Add(slot.Id,slot);
                }
            }
            var actual=actor.transform.position-Vector3.up;
            var localActual=directory.transform.InverseTransformPoint(actual);
            while(choices.Count>0)
            {
                var nearest=MarketActivity.Nearest(choices,localActual.x,localActual.z,null,candidate=>true);
                if(nearest==null)break;choices.Remove(nearest);var slot=live[nearest.Id];
                if(!catalog.TryClaim(slot,plan.Resident.Id,actual,now,visit.EndsAt,actor))continue;
                visit.Slot=slot;visit.Seen.Add(slot.Post.Id);visit.Remembered=false;
                visit.Payment=false;visit.PurchaseResolved=false;visit.Choice=null;
                visit.Dwell=new MarketDwell(MarketActivity.DwellSeconds(plan.Resident.AppearanceSeed+(uint)visit.Seen.Count,
                    visit.Day,slot.Post.Role==MarketPostRole.Performer));
                return true;
            }
            return false;
        }
        private void BeginMarketReturn(MarketVisit visit,NativeMarketCatalog catalog,string id)
        {visit.Returning=true;catalog.Release(id);}
        private void EndMarketVisit(string id)
        {
            marketVisits.Remove(id);if(directory)NativeMarketCatalog.For(directory).Release(id);
        }
        private bool HasMarketActivity(string id,double now)
        {
            MarketVisit visit;var actor=FindActor(id);
            return actor && actor.gameObject.activeInHierarchy && marketVisits.TryGetValue(id,out visit) &&
                !visit.Returning && now>=visit.StartedAt && now<visit.EndsAt;
        }
        private void ResetMarketVisits()
        {
            if(directory){var catalog=directory.GetComponent<NativeMarketCatalog>();if(catalog)foreach(string id in marketVisits.Keys)catalog.Release(id);}
            marketVisits.Clear();marketVisitDays.Clear();
        }
        internal bool TryMarketVisitContext(ResidentActor actor,double now,out string context)
        {
            context=null;MarketVisit visit;
            if(!actor || !marketVisits.TryGetValue(actor.Resident.Id,out visit) || IsAlarmed(actor.Resident.Id) ||
                !visit.Returning && !visit.Slot.Post.Available)return false;
            bool arrived=Vector3.Distance(actor.transform.position-Vector3.up,visit.Slot.Ground)<=0.4f;
            string destination=visit.Payment && visit.Choice!=null?visit.Choice.SellerName+" to discuss buying "+visit.Choice.ItemName:
                visit.Slot.Post.Role==MarketPostRole.Performer?"a viewing place in front of the stage":MarketGoodsDescription(visit.Slot.Post)+" on the next market table";
            string activity=visit.Returning?"walking back toward "+visit.ResumeDescription+" after the market stop":
                !arrived?"walking toward "+destination:
                visit.Payment && visit.Choice!=null?"standing opposite "+visit.Choice.SellerName+" to discuss buying "+visit.Choice.ItemName:
                visit.Slot.Post.Role==MarketPostRole.Performer?"watching the performers from a place in front of the stage":
                "looking at "+MarketGoodsDescription(visit.Slot.Post)+" on the market table";
            context="Currently "+activity+"; "+visit.Reason+". "+
                (visit.Purchase??"I have not bought anything during this visit.")+" I resume my original outing afterwards; I am still outdoors.";
            return true;
        }
        private static string MarketGoodsDescription(NativeMarketPost post)
        {
            var kinds=new List<string>();
            foreach(var evidence in post.Goods)
            {
                string name=evidence.Kind==MarketGoodsKind.Fish?"fish":evidence.Kind==MarketGoodsKind.Meat?"meat":
                    evidence.Kind==MarketGoodsKind.Bread?"bread":evidence.Kind==MarketGoodsKind.Produce?"fruit":
                    evidence.Kind==MarketGoodsKind.Weapons?"weapons":"household goods";
                if(!kinds.Contains(name))kinds.Add(name);
            }
            return string.Join(", ",kinds.ToArray());
        }
    }
}
