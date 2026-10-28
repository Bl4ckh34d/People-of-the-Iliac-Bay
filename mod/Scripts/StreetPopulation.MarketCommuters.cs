using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private readonly Dictionary<string,NativeMarketCommuter> marketCommuters=new Dictionary<string,NativeMarketCommuter>();
        private List<HomeProfile> marketHomes;
        private bool marketTransitionsRegistered;
        private sealed class MarketSleepPlan
        {
            internal Plan Plan;
            internal int Day,Closing,Wake,Sleep;
            internal double TimeScale;
        }
        private readonly Dictionary<string,MarketSleepPlan> marketSleepPlans=new Dictionary<string,MarketSleepPlan>();

        private void TickMarketCommuters(double now)
        {
            if(!Ready || !directory || population==null || worldStore==null || geometry==null)return;
            if(!marketTransitionsRegistered)
            {PlayerEnterExit.OnPreTransition+=PrepareMarketTransition;marketTransitionsRegistered=true;}
            var catalog=NativeMarketCatalog.For(directory);
            foreach(var post in catalog.Posts)
            {
                if(!post.Seller)continue;
                var data=post.Seller.Data;FactionFile.FactionData faction;
                if(post.Seller.GetComponent<QuestResourceBehaviour>() || post.Seller.GetComponent<DaggerfallAction>() ||
                    QuestMachine.Instance.HasFactionListener(data.factionID) || QuestMachine.Instance.ActiveQuestor(data)!=null ||
                    GameManager.Instance.PlayerEntity.FactionData.GetFactionData(data.factionID,out faction) &&
                    faction.type==(int)FactionFile.FactionTypes.Individual)continue;
                int hash=NativeExteriorNpcIdentity.Anchor(post.Seller,directory.transform);
                string id=NativeExteriorNpcIdentity.PersonId(directory.MapID,hash);
                var person=worldStore.NativePersonInSettlement(directory.MapID,id);
                if(person==null)person=worldStore.RegisterNativePerson(directory.MapID,-1,hash,post.Seller.DisplayName,
                    data.gender==DaggerfallWorkshop.Game.Entity.Genders.Female?ResidentSex.Female:ResidentSex.Male,
                    (int)data.race,data.factionID,data.billboardArchiveIndex,data.billboardRecordIndex);
                if(!person.IsAlive || person.RetiredBedroomFlat)continue;
                if(person.HomeKey<0)
                {
                    if(marketHomes==null)marketHomes=HousingSurvey.Read(directory);
                    bool housed=worldStore.AssignExteriorNativeBed(person.Id,marketHomes);
                    // A new actual home assignment can follow the generic static
                    // inventory binding. Issue its real key once at that event;
                    // ordinary re-entry must never replenish a lost saved key.
                    var existingOwner=post.Seller.GetComponent<NativeNpcInventoryOwner>();
                    if(housed && existingOwner && existingOwner.Binding!=null)
                    {
                        string reason;
                        if(!NativeNpcStartingBelongings.TryEnsureVerifiedHomeKey(worldStore,worldStore.ResolveResident(person.Id),existingOwner.Binding.Items,out reason))
                            throw new InvalidOperationException(reason);
                        existingOwner.Capture();
                    }
                }
                if(person.HomeKey<0 || person.BedSlot<0)continue;
                var home=population.Households.Find(h=>h.BuildingKey==person.HomeKey);
                var member=home==null?null:home.Residents.Find(r=>r.Id==person.Id);
                if(member==null)continue;
                var actor=post.Seller.GetComponent<ResidentActor>();
                // Native scene caching disables exterior roots while an inn or
                // another home is loaded. Keep an existing exact actor lease;
                // only the scene where this body is active may initialize it.
                if(!post.Seller.gameObject.activeInHierarchy &&
                    (!actor || !actor.IsStationary || actor.Resident==null || actor.Resident.Id!=person.Id))continue;
                if(!actor || actor.Resident==null)
                {
                    string reason;
                    if(!NativeMarketStock.EnsureInitialInventory(worldStore,person,post,out reason))continue;
                    if(!actor)actor=post.Seller.gameObject.AddComponent<ResidentActor>();
                }
                if(post.Seller.gameObject.activeInHierarchy)actor.InitializeStationary(member,worldStore);
                NativeMarketCommuter commuter;
                if(!marketCommuters.TryGetValue(id,out commuter) || !commuter)
                {
                    commuter=post.Seller.GetComponent<NativeMarketCommuter>();
                    if(!commuter)commuter=post.Seller.gameObject.AddComponent<NativeMarketCommuter>();
                    var billboard=post.Seller.GetComponent<DaggerfallBillboard>();
                    commuter.Npc=post.Seller;commuter.Person=person;commuter.Directory=directory;
                    commuter.OriginalParent=post.Seller.transform.parent;commuter.OriginalLocal=post.Seller.transform.localPosition;
                    commuter.OriginalRotation=post.Seller.transform.localRotation;commuter.OriginalActive=post.Seller.gameObject.activeSelf;
                    commuter.Offset=Vector3.up*(billboard?billboard.Summary.Size.y*0.5f:1);
                    commuter.PostLocal=directory.transform.InverseTransformPoint(post.PostWorldPosition-commuter.Offset);
                    commuter.DirectoryOrigin=directory.transform.position;
                    commuter.PostId=post.Id;commuter.Role=post.Role;commuter.Phase="work";commuter.AtPost=true;marketCommuters[id]=commuter;
                    // This root survives native interior disposal. All visible coordinates
                    // still belong to the current native exterior or the assigned home.
                    commuter.transform.SetParent(transform,true);
                    BindStationaryObserver(commuter.Npc,person);
                }
                commuter.Person=person;
            }
            foreach(var commuter in marketCommuters.Values)
                if(commuter && commuter.Npc)
                {
                    BindStationaryObserver(commuter.Npc,commuter.Person);TickMarketCommuter(commuter,catalog,now);
                    ObserveMarketHomeSleep(commuter,now);
                }
        }

        private void TickMarketCommuter(NativeMarketCommuter body,NativeMarketCatalog catalog,double now)
        {
            var person=body.Person;
            if(!person.IsAlive || person.RetiredBedroomFlat){body.gameObject.SetActive(false);return;}
            Vector3 originShift=directory.transform.position-body.DirectoryOrigin;
            if(originShift.sqrMagnitude>0.0001f)
            {
                if(body.Commute!=null)for(int i=0;i<body.Commute.Count;i++)body.Commute[i]+=originShift;
                body.DirectoryOrigin=directory.transform.position;body.Route=null;
            }
            int day=(int)Math.Floor(now/86400);double second=now-day*86400d;
            uint seed=unchecked((uint)person.NativeHash);
            if(body.Day!=day || now<body.LastNow)
            {body.Day=day;body.LeftWork=false;body.ClosingMinute=MarketWorkday.ClosingMinute(seed,day,false);body.Initialized=false;body.Route=null;}
            bool skipped=ShopVisitJourney.NeedsCatchup(body.LastNow,now,Time.unscaledDeltaTime,DaggerfallUnity.Instance.WorldTime.TimeScale);
            body.LastNow=now;
            if(skipped){body.Initialized=false;body.Route=null;}
            bool homeLoaded=interior && interior.EntryDoor.buildingKey==person.HomeKey;
            bool outside=!GameManager.Instance.PlayerEnterExit.IsPlayerInside;
            Transform scene=outside?directory.transform:homeLoaded?interior.transform:transform;
            if(body.transform.parent!=scene)body.transform.SetParent(scene,true);
            int sceneKey=outside?-1:interior?interior.EntryDoor.buildingKey:-2;
            bool changedScene=body.SceneKey!=sceneKey;
            if(changedScene){body.SceneKey=sceneKey;body.Initialized=false;body.Route=null;}
            bool talking=MarketBusy(body,now);
            var alarm=ReactionsEnabled?worldStore.NativeResponse(person.Id):null;
            if(alarm!=null && GameManager.Instance.PlayerEntity.CrimeCommitted==DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None &&
                !worldStore.HasPersistentMurderFear(person.Id) && worldStore.ClearNativeResponse(person.Id,now))alarm=null;
            if(alarm!=null)
            {
                var reaction=alarm.People.Find(p=>p.ResidentId==person.Id);
                if(reaction!=null && reaction.HasPosition)
                {
                    var pose=body.GetComponent<SleepingPose>();if(pose)pose.Wake();
                    bool inScene=reaction.BuildingKey==(interior?interior.EntryDoor.buildingKey:-1);
                    if(inScene && body.ResponseIncident!=alarm.Id && reaction.Escape==null && body.gameObject.activeInHierarchy)
                    {
                        // Older static witness producers stored the billboard pivot.
                        // Shared movement consumes ground coordinates, as mobile bodies do.
                        var local=(interior?interior.transform:directory.transform).InverseTransformPoint(body.transform.position-body.Offset);
                        reaction.X=local.x;reaction.Y=local.y;reaction.Z=local.z;
                    }
                    body.ResponseIncident=alarm.Id;
                    body.gameObject.SetActive(inScene && (outside || interior));
                    MarketPhase(body,"flee",now,"Danger interrupted my activity at the market or on my home journey.");
                    if(inScene && (reaction.Reaction==CivilianReaction.Flee || !talking))MarketFlee(body,reaction,alarm,now);
                    return;
                }
            }
            body.ResponseIncident=null;
            // Accepted work and recovery own their existing movement adapters.
            // Alarm handling above still has priority over ordinary market work.
            if(worldStore.ActiveNpcAction(person.Id)!=null || worldStore.ActiveNpcTask(person.Id)!=null ||
                worldStore.ReadNpcTreatmentVisit(person.Id)!=null || HasActiveNpcHealthCare(person.Id))return;
            if(talking)return;
            // Morning sales do not keep an empty stand open that evening.
            // Only the actual closing-time customer reservation extends the shift.
            if(second>=17*3600+30*60 && catalog.HasCustomer(body.PostId,now))
                body.ClosingMinute=MarketWorkday.ClosingMinute(seed,day,true);
            double arrival=MarketWorkday.ArrivalMinute(seed,day)*60d;
            TownGeometry.Entrance door;
            if(!geometry.Entrances.TryGetValue(person.HomeKey,out door))return;
            Vector3 post=geometry.ScenePoint(new RoutePoint(body.PostLocal.x,body.PostLocal.z));
            Vector3 homeDoor=geometry.ScenePoint(new RoutePoint(door.LocalDoor.x,door.LocalDoor.z));
            if(body.Commute==null || body.Commute.Count<2)
            {
                if(!outside)return;
                if(Time.unscaledTime<body.RetryAt)return;
                body.RetryAt=Time.unscaledTime+5;
                body.Commute=MarketOutdoorRoute(homeDoor,post);
                body.Routine=new ResidentRoutine{ResidentId=person.Id};
                foreach(var point in body.Commute)
                {var local=directory.transform.InverseTransformPoint(point);body.Routine.Route.Add(new SavedRoutePoint{X=local.x,Z=local.z});}
            }
            var commute=body.Commute;
            double travel=MarketRouteLength(commute)/Math.Max(0.1,ResidentMovement.WalkSpeed)*DaggerfallUnity.Instance.WorldTime.TimeScale;
            double departure=Math.Max(0,arrival-travel-(homeLoaded?body.HomeExitSeconds:0));
            bool workTime=second>=departure && !body.LeftWork && second<body.ClosingMinute*60d;
            if(!body.LeftWork && second>=body.ClosingMinute*60d &&
                MarketWorkday.MayClose(now,body.ClosingMinute,talking,false))body.LeftWork=true;
            if(!body.Initialized)
            {
                body.InsideHome=!workTime;body.AtPost=workTime && second>=arrival;body.Initialized=true;
                // Scene materialization and deliberate clock jumps reconstruct
                // current state; they are not newly witnessed journey events.
                body.Phase=workTime?(body.AtPost?"work":"to-work"):
                    second>=sleepHour*3600d || second<departure?"sleep":"home";
                if(outside && workTime)
                {
                    body.transform.position=(body.AtPost?post:MarketAlong(commute,(second-departure)/Math.Max(1,arrival-departure)))+body.Offset;
                    body.Route=null;
                }
                else if(homeLoaded && !workTime && indoorGeometry!=null && indoorGeometry.EntryGround.HasValue)
                {
                    var pose=body.GetComponent<SleepingPose>();
                    if(!pose || !pose.Active)body.transform.position=indoorGeometry.EntryGround.Value+body.Offset;
                    body.Route=null;
                }
            }
            if(outside)
            {
                var pose=body.GetComponent<SleepingPose>();if(pose)pose.Wake();
                if(workTime)
                {
                    body.gameObject.SetActive(true);
                    if(body.InsideHome){body.transform.position=homeDoor+body.Offset;body.InsideHome=false;body.Route=null;}
                    if(MarketMove(body,post,body.Route??MarketOutdoorRoute(body.transform.position-body.Offset,post),(float)ResidentMovement.WalkSpeed))
                    {body.AtPost=true;MarketPhase(body,"work",now,"I returned to my market post and began work.");}
                    else MarketPhase(body,"to-work",now,"I left home and walked back to my market post.");
                }
                else if(!body.InsideHome)
                {
                    body.gameObject.SetActive(true);body.AtPost=false;
                    MarketPhase(body,"to-home",now,"I finished work at my market post and walked home.");
                    if(MarketMove(body,homeDoor,body.Route??MarketOutdoorRoute(body.transform.position-body.Offset,homeDoor),(float)ResidentMovement.WalkSpeed))
                    {body.InsideHome=true;body.Route=null;body.gameObject.SetActive(false);MarketPhase(body,"home",now,"I reached my home after work.");}
                }
                else body.gameObject.SetActive(false);
                return;
            }
            if(!homeLoaded){body.gameObject.SetActive(false);return;}
            if(indoorGeometry==null || !indoorGeometry.EntryGround.HasValue)return;
            // Entering a home during work does not pull its seller off the post.
            // A body already at home can still walk out when morning comes.
            if(workTime && !body.InsideHome){body.gameObject.SetActive(false);return;}
            body.gameObject.SetActive(true);
            if(workTime)
            {
                var pose=body.GetComponent<SleepingPose>();Vector3 ground;
                if(pose && pose.Active){pose.WakeOnFloor(indoorGeometry,out ground);body.Route=null;}
                var route=body.Route??MarketIndoorRoute(body.transform.position-body.Offset,indoorGeometry.EntryGround.Value);
                MarketPhase(body,"to-work",now,"I woke and left my bed to return to my market post.");
                if(MarketMove(body,indoorGeometry.EntryGround.Value,route,(float)ResidentMovement.WalkSpeed))
                {body.InsideHome=false;body.gameObject.SetActive(false);}
                return;
            }
            body.InsideHome=true;
            var records=new List<int>(indoorBeds.Keys);records.Sort();
            if(person.BedSlot>=records.Count)return;
            var bed=indoorBeds[records[person.BedSlot]];Vector3 beside;
            if(!indoorGeometry.StandBesideBed(bed,out beside,true))return;
            if(body.HomeExitSeconds<=0)
                body.HomeExitSeconds=MarketRouteLength(MarketIndoorRoute(beside,indoorGeometry.EntryGround.Value))/
                    ResidentMovement.WalkSpeed*DaggerfallUnity.Instance.WorldTime.TimeScale;
            var sleeping=body.GetComponent<SleepingPose>();
            bool sleep=second>=sleepHour*3600d || second<departure;
            if(sleeping && sleeping.Active && sleep)return;
            if(sleeping && sleeping.Active){sleeping.WakeOnFloor(indoorGeometry,out beside);body.Route=null;}
            if(MarketMove(body,beside,body.Route??MarketIndoorRoute(body.transform.position-body.Offset,beside),(float)ResidentMovement.WalkSpeed))
            {
                if(sleep)
                {
                    SleepingArt.Atlas art=null;
                    try {if(SleepEnabled && sleepingArt!=null)art=sleepingArt.ForNative(person);}
                    catch(Exception) { /* Sleep at the reached bed remains valid without its optional art. */ }
                    if(art!=null){if(!sleeping)sleeping=body.gameObject.AddComponent<SleepingPose>();sleeping.BeginNative(interior.transform,bed,art);}
                    MarketPhase(body,"sleep",now,"I went to sleep in my assigned bed at home.");
                }
                else MarketPhase(body,"home",now,"I am home after work, beside my assigned bed.");
            }
            else MarketPhase(body,"to-bed",now,"I came home and walked to my assigned bed.");
        }

        private static bool MarketTalking(StaticNPC npc)
        {
            var game=GameManager.Instance;
            return game.TalkManager.CurrentNPCType==TalkManager.NPCType.Static && game.TalkManager.StaticNPC==npc &&
                DaggerfallUI.UIManager!=null && DaggerfallUI.UIManager.ContainsWindow(DaggerfallUI.Instance.TalkWindow);
        }
        private bool MarketBusy(NativeMarketCommuter body,double now)
        {
            StreetChat chat;
            return MarketTalking(body.Npc) || PersonalTradeWindow.Active!=null && PersonalTradeWindow.Active.Portrait!=null &&
                PersonalTradeWindow.Active.Portrait.Speaker==body.Npc || NativeVendorTradeAdapter.ActivePersonId==body.Person.Id ||
                streetChats.TryGetValue(body.Person.Id,out chat) && now<chat.EndsAt && chat.OtherBody && chat.Scene==body.transform.parent;
        }
        private void ObserveMarketHomeSleep(NativeMarketCommuter body,double now)
        {
            var actor=body.GetComponent<ResidentActor>();
            if(!actor || actor.Resident==null || body.Routine==null || body.Routine.Route.Count<2 || !body.Person.IsAlive)return;
            if(MarketBusy(body,now))
            {
                HomeSleepClock paused;
                if(homeSleepClocks.TryGetValue(body.Person.Id,out paused))
                {paused.CheckedAt=now;paused.Seconds=0;paused.AtHomeAsleep=false;paused.Eligible=false;}
                return;
            }
            int arrival=MarketWorkday.ArrivalMinute(unchecked((uint)body.Person.NativeHash),body.Day);
            double scale=DaggerfallUnity.Instance.WorldTime.TimeScale;
            double travel=MarketRouteLength(body.Commute)/ResidentMovement.WalkSpeed*scale;
            int wake=Math.Max(0,(int)Math.Floor((arrival*60-travel-body.HomeExitSeconds)/60));
            int sleep=Math.Min(1439,Math.Max(sleepHour*60,(int)Math.Ceiling((body.ClosingMinute*60+travel)/60)+1));
            MarketSleepPlan cached;
            if(!marketSleepPlans.TryGetValue(body.Person.Id,out cached) || cached.Day!=body.Day || cached.Closing!=body.ClosingMinute ||
                cached.Wake!=wake || cached.Sleep!=sleep || cached.TimeScale!=scale || !ReferenceEquals(cached.Plan.Resident,actor.Resident))
            {
                var outward=new List<RoutePoint>();
                foreach(var point in body.Routine.Route)outward.Add(new RoutePoint(point.X,point.Z));
                var inward=new List<RoutePoint>(outward);inward.Reverse();
                DailySchedule schedule;
                try
                {
                    schedule=DailySchedule.ExteriorWorkday(body.Person.HomeKey,outward,inward,outward[outward.Count-1],arrival,
                        body.ClosingMinute,ResidentMovement.WalkSpeed,scale,wake,sleep);
                }
                catch(ArgumentException){return;}
                cached=new MarketSleepPlan {Day=body.Day,Closing=body.ClosingMinute,Wake=wake,Sleep=sleep,TimeScale=scale,
                    Plan=new Plan {Resident=actor.Resident,Routine=body.Routine,Schedule=schedule,HauntedSchedule=schedule}};
                marketSleepPlans[body.Person.Id]=cached;
            }
            // The ordinary shared clock owns Medical/Endurance, condition and
            // interruption guards, native SetHealth and persisted vital capture.
            ObserveHomeSleep(cached.Plan,actor,now);
        }
        private List<Vector3> MarketOutdoorRoute(Vector3 from,Vector3 to)
        {
            var a=directory.transform.InverseTransformPoint(from);var b=directory.transform.InverseTransformPoint(to);
            var result=new List<Vector3>();
            foreach(var point in geometry.RouteBetweenPoints(new SavedRoutePoint{X=a.x,Z=a.z},new SavedRoutePoint{X=b.x,Z=b.z}))
                result.Add(geometry.ScenePoint(new RoutePoint(point.X,point.Z)));
            return result;
        }
        private List<Vector3> MarketIndoorRoute(Vector3 from,Vector3 to)
        {
            var result=new List<Vector3>();
            foreach(var point in indoorGeometry.RouteBetween(interior.transform.InverseTransformPoint(from),interior.transform.InverseTransformPoint(to)))
                result.Add(interior.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z)));
            return result;
        }
        private static double MarketRouteLength(List<Vector3> route)
        {double result=0;for(int i=1;i<route.Count;i++)result+=Vector3.Distance(route[i-1],route[i]);return result;}
        private static Vector3 MarketAlong(List<Vector3> route,double fraction)
        {
            if(route.Count==0)return Vector3.zero;
            double left=MarketRouteLength(route)*Math.Max(0,Math.Min(1,fraction));
            for(int i=1;i<route.Count;i++)
            {float length=Vector3.Distance(route[i-1],route[i]);if(length>0 && left<=length)return Vector3.Lerp(route[i-1],route[i],(float)(left/length));left-=length;}
            return route[route.Count-1];
        }
        private bool MarketMove(NativeMarketCommuter body,Vector3 goal,List<Vector3> route,float speed)
        {
            Vector3 ground=body.transform.position-body.Offset;
            if(Vector3.Distance(ground,goal)<=0.4f){body.Route=null;return true;}
            if(body.Route==null || Vector3.Distance(body.RouteGoal,goal)>0.4f)
            {body.Route=route;body.RouteGoal=goal;body.Next=0;}
            if(body.Route==null || body.Route.Count<2)return false;
            while(body.Next<body.Route.Count && Vector3.Distance(ground,body.Route[body.Next])<=0.4f)body.Next++;
            if(body.Next>=body.Route.Count){body.Route=null;return Vector3.Distance(ground,goal)<=0.65f;}
            Vector3 next=Vector3.MoveTowards(ground,body.Route[body.Next],speed*Time.deltaTime);
            if(interior)InteriorGeometry.OpenOrdinaryDoors(ground,next);
            body.transform.position=next+body.Offset;return false;
        }
        private void MarketFlee(NativeMarketCommuter body,AlarmPerson reaction,CivilianIncident alarm,double now)
        {
            var flightActor=body.GetComponent<ResidentActor>();
            if(reaction.Reaction==CivilianReaction.Flee && flightActor)flightActor.ReleaseFlightPause();
            Transform origin=interior?interior.transform:directory.transform;
            if(reaction.Escape==null && interior && reaction.Reaction==CivilianReaction.Flee)
            {
                var guards=GetComponent<GuardPopulation>();
                var outside=guards?guards.WitnessRoute(interior?interior.EntryDoor.buildingKey:-1,now):null;
                var inside=indoorGeometry.RouteToExit(body.transform.position-body.Offset);
                if(outside!=null && outside.Count>=2 && inside.Count>=2)
                    reaction.Escape=CivilianEscape.Create(reaction,now,inside,outside,ResidentMovement.FleeSpeed(unchecked((uint)body.Person.NativeHash)));
            }
            if(reaction.Escape==null)
            {
                var actor=body.GetComponent<ResidentActor>();
                if(actor && body.Routine!=null)
                {
                    Vector3? danger=null;
                    if(alarm!=null && (alarm.ThreatId!=null || alarm.KillerThreatId!=null))
                    {
                        Vector3 current;var guards=GetComponent<GuardPopulation>();
                        danger=guards && guards.TryHostileThreatPosition(alarm.KillerThreatId??alarm.ThreatId,out current)?current:
                            alarm.HasLocation && alarm.BuildingKey==-1?directory.transform.TransformPoint(new Vector3((float)alarm.X,(float)alarm.Y,(float)alarm.Z)):
                            actor.GroundPosition;
                    }
                    Vector3 shelter=Vector3.zero;int shelterBuilding=-1;
                    bool hasShelter=!interior && reaction.Reaction==CivilianReaction.Flee && !reaction.ReachedRefuge &&
                        TryShelterTarget(actor,reaction,alarm,danger??GameManager.Instance.PlayerMotor.transform.position,now,out shelter,out shelterBuilding);
                    bool wasSheltered=reaction.ReachedRefuge;
                    CivilianResponses.Move(actor,reaction,body.Routine,origin,geometry,interior?indoorGeometry:null,danger,
                        hasShelter?(Vector3?)shelter:null,shelterBuilding,ShelterAccessible(actor.Resident.HomeKey,actor.Resident.HomeKey,alarm,now));
                    if(!wasSheltered && reaction.ReachedRefuge)ObserveResidentShelterEntry(actor,reaction,now);
                    if(reaction.ReachedRefuge && !reaction.RefugeSealed && alarm!=null)
                    {
                        worldStore.RecordShelterArrival(alarm.Id,body.Person.Id,now);
                        NpcNativeSafetyGoalLifecycle.TryFulfillAtRefuge(worldStore,body.Person.Id,alarm,reaction,now,body.Person.IsAlive,false);
                    }
                }
                body.Route=null;body.AtPost=false;return;
            }
            Vector3 target=interior?origin.TransformPoint(new Vector3((float)reaction.X,(float)reaction.Y,(float)reaction.Z)):
                geometry.ScenePoint(new RoutePoint(reaction.X,reaction.Z));
            // Existing escape ledger owns route/time. Apply the same physical
            // position as mobile witnesses, retaining this stationary body.
            if(interior)InteriorGeometry.OpenOrdinaryDoors(body.transform.position-body.Offset,target);
            body.transform.position=target+body.Offset;body.Route=null;body.AtPost=false;
        }
        private void MarketPhase(NativeMarketCommuter body,string phase,double now,string fact)
        {
            if(body.Phase==phase)return;
            bool first=body.Phase==null;
            body.Phase=phase;
            if(first)return;
            NativeSceneRecollection.Remember(worldStore,body.Person.Id,"market:"+body.Day+":"+phase+":"+((long)now).ToString(CultureInfo.InvariantCulture),
                NpcSceneEventKind.Journey,fact,0);
        }
        internal bool TryMarketCommuterContext(StaticNPC npc,double now,out string context,Func<int,string> buildingName=null)
        {
            context=null;var body=npc?npc.GetComponent<NativeMarketCommuter>():null;
            if(!body || body.Person==null)return false;
            string place=body.Phase=="work"?"at my assigned market post":body.Phase=="sleep"?"resting at my assigned bed":
                body.Phase=="to-home"?"walking from my market post to my home":body.Phase=="to-work"?"walking back to my market post":
                body.Phase=="flee"?"fleeing danger, with work interrupted":body.Phase=="to-bed"?"walking to my assigned bed at home":"at home after work";
            string address=buildingName==null?"building "+body.Person.HomeKey:buildingName(body.Person.HomeKey);
            context="My identity is "+body.Person.Name+". My market role is "+body.Role+". My market post is "+body.PostId+". I am "+place+" now. My actual home is "+
                address+" and my assigned bed slot is "+body.Person.BedSlot+". Today's planned market arrival is "+
                MarketWorkday.ArrivalMinute(unchecked((uint)body.Person.NativeHash),body.Day)/60+":"+
                (MarketWorkday.ArrivalMinute(unchecked((uint)body.Person.NativeHash),body.Day)%60).ToString("00",CultureInfo.InvariantCulture)+
                "; work closes at "+body.ClosingMinute/60+":"+(body.ClosingMinute%60).ToString("00",CultureInfo.InvariantCulture)+
                ". Actual browsing or buying customers can extend work within 17:45–18:15; passersby are not customers. An active conversation finishes before I leave.";
            return true;
        }
        internal ResidentActor FindMarketCommuterActor(string id)
        {
            NativeMarketCommuter body;
            return marketCommuters.TryGetValue(id,out body) && body?body.GetComponent<ResidentActor>():null;
        }
        internal bool TryMarketCommuterState(string id,out ActivitySnapshot state)
        {
            state=default(ActivitySnapshot);NativeMarketCommuter body;
            if(!marketCommuters.TryGetValue(id,out body) || !body)return false;
            bool inside=body.InsideHome && !body.AtPost;
            var local=(inside && interior && interior.EntryDoor.buildingKey==body.Person.HomeKey?interior.transform:directory.transform)
                .InverseTransformPoint(body.transform.position-body.Offset);
            Activity activity=body.Phase=="work"?Activity.Work:body.Phase=="sleep"?Activity.Sleep:
                body.Phase=="home"?Activity.Home:Activity.Travel;
            state=new ActivitySnapshot(activity,inside?body.Person.HomeKey:-1,new RoutePoint(local.x,local.z),body.AtPost?1:0);
            return true;
        }
        private void ObserveMarketInteriorViolence(CivilianIncident alarm,Vector3 victim,double now)
        {
            if(!interior || alarm==null || alarm.BuildingKey!=interior.EntryDoor.buildingKey)return;
            foreach(var body in marketCommuters.Values)
            {
                if(!body || !body.Npc || !body.gameObject.activeInHierarchy || body.transform.parent!=interior.transform || !body.Person.IsAlive)continue;
                var pose=body.GetComponent<SleepingPose>();
                var witness=CivilianResponses.Observe(worldStore,alarm,body.Person.Id,body.transform,victim,pose && pose.Active,now,interior.EntryDoor.buildingKey);
                if(witness==null || witness.HasPosition)continue;
                var local=interior.transform.InverseTransformPoint(body.transform.position-body.Offset);
                witness.StaticWitness=true;witness.HasPosition=true;witness.BuildingKey=interior.EntryDoor.buildingKey;
                witness.X=local.x;witness.Y=local.y;witness.Z=local.z;
            }
        }
        private void ReleaseMarketCommuters()
        {
            if(marketTransitionsRegistered)PlayerEnterExit.OnPreTransition-=PrepareMarketTransition;
            marketTransitionsRegistered=false;
            foreach(var body in marketCommuters.Values)
            {
                if(!body)continue;var pose=body.GetComponent<SleepingPose>();if(pose)pose.Wake();
                MarketSleepPlan sleepPlan;
                var sleepingActor=body.GetComponent<ResidentActor>();
                if(sleepingActor && marketSleepPlans.TryGetValue(body.Person.Id,out sleepPlan))
                    ObserveHomeSleep(sleepPlan.Plan,sleepingActor,lastPopulationSeconds>=0?lastPopulationSeconds:ActionNow(),true);
                var actor=body.GetComponent<ResidentActor>();if(actor)actor.ReleaseStationary();
                if(body.OriginalParent){body.transform.SetParent(body.OriginalParent,false);body.transform.localPosition=body.OriginalLocal;body.transform.localRotation=body.OriginalRotation;}
                body.gameObject.SetActive(body.OriginalActive && body.Person!=null && body.Person.IsAlive && !body.Person.RetiredBedroomFlat);
                // A restore can bind the authored object again in this frame.
                // Do not let a deferred marker destruction erase its new owner.
                DestroyImmediate(body);
            }
            marketCommuters.Clear();marketSleepPlans.Clear();marketHomes=null;
        }
        private void PrepareMarketTransition(PlayerEnterExit.TransitionEventArgs args)
        {
            // Native interior destruction must not destroy its original market
            // resident. The next tick attaches it to the actual observed scene,
            // preserving ordinary scene-relative combat and observer contracts.
            foreach(var body in marketCommuters.Values)
                if(body)body.transform.SetParent(transform,true);
        }
    }
}
