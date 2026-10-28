using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Explicit disposable native fixture. Only setup positions are staged.
        // Approach, browsing, payment, UID transfer and return use production.
        internal IEnumerator RunMarketVisitNativeProbe()
        {
            var game=GameManager.Instance;
            if(game==null || !game.IsReady || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Market visit fixture requires a ready native exterior.");
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            float deadline=Time.realtimeSinceStartup+30;
            while((!Ready || !EntranceSurveyReady || !directory || geometry==null || !NativeNavigationWorld.Current ||
                !NativeNavigationWorld.Current.Ready || NativeNavigationWorld.Current.Scene!="city-"+directory.MapID) &&
                Time.realtimeSinceStartup<deadline)yield return null;
            if(!Ready || !directory || geometry==null || !NativeNavigationWorld.Current || !NativeNavigationWorld.Current.Ready)
                throw new InvalidOperationException("Market source/NavMesh readiness missing after30 seconds: ready="+Ready+
                    "; entrances="+EntranceSurveyReady+"; map="+(directory?directory.MapID:-1)+"; geometry="+(geometry!=null)+
                    "; nav="+(bool)NativeNavigationWorld.Current+"; navReady="+(NativeNavigationWorld.Current && NativeNavigationWorld.Current.Ready)+
                    "; navScene="+(NativeNavigationWorld.Current?NativeNavigationWorld.Current.Scene:"none"));
            var catalog=NativeMarketCatalog.For(directory);catalog.PrepareSlots();
            NativeMarketPost sellerPost=null,show=null;int slots=0;
            foreach(var post in catalog.Posts)
            {
                slots+=post.Slots.Count;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_POST: id="+post.Id+"; role="+post.Role+"; available="+post.Available+
                    "; native="+(post.Seller?post.Seller.Data.hash:0)+"; post="+post.PostWorldPosition.ToString("R")+
                    "; goods="+post.Goods.Count+"; display="+post.DisplayWorldPosition.ToString("R")+"; slots="+post.Slots.Count);
                foreach(var slot in post.Slots)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_SLOT: id="+slot.Id+"; payment="+slot.Payment+
                    "; ground="+slot.Ground.ToString("R")+"; target="+slot.Look.ToString("R"));
                if(sellerPost==null && post.Role!=MarketPostRole.Performer && post.Role!=MarketPostRole.Unknown &&
                    post.Available && post.Goods.Count>0 &&
                    post.Slots.Exists(s=>s.Payment) && post.Slots.Exists(s=>!s.Payment))sellerPost=post;
                if(post.Role==MarketPostRole.Performer && post.Available && post.Slots.Count>=4)show=post;
            }
            if(sellerPost==null)
                throw new InvalidOperationException("No actual serving commercial market post has goods and separate browse/payment slots: posts="+
                    catalog.Posts.Count+"; slots="+slots+". No substitute market geometry is allowed.");
            int requiredBodies=show==null?1:4;
            Plan actualBuyerPlan=null;double plannedTime=double.PositiveInfinity,actualDeparture=0;ActivitySnapshot plannedState=default(ActivitySnapshot);
            double current=ActionNow(),dayStart=Math.Floor(current/86400)*86400;
            int fixtureDay=(int)Math.Floor(current/86400);
            ResidentActor existingBuyer=null;int itinerarySources=0;
            foreach(var plan in plans)if(plan.Routine!=null && plan.Routine.Itinerary!=null)itinerarySources++;
            foreach(var body in actors.Values)
            {
                if(!body || !body.gameObject.activeInHierarchy || body.Resident==null || body.IsSleeping || body.IsConversing ||
                    body.Resident.AgeGroup!=ResidentAgeGroup.Adult || body.GetComponent<StaticNPC>())continue;
                var plan=plans.Find(value=>value.Resident.Id==body.Resident.Id);
                var inventory=worldStore.ReadNpcInventory(body.Resident.Id);
                if(plan==null || !ClosedShopEligible(plan,current) || Scheduled(plan,current).Activity!=Activity.Travel ||
                    inventory==null || inventory.WalletGold<10 || StableSeed.Combine(body.Resident.AppearanceSeed,(uint)fixtureDay,7841,1)%3!=0)continue;
                actualBuyerPlan=plan;plannedTime=current;plannedState=Scheduled(plan,current);existingBuyer=body;break;
            }
            // Noon has no ordinary outside residents in this real save. Select
            // one already published outing, not an invented daily plan or NPC.
            if(actualBuyerPlan==null)foreach(var plan in plans)
            {
                if(!plan.Resident.IsAlive || plan.Resident.AgeGroup!=ResidentAgeGroup.Adult || plan.Resident.NativeIdentity!=null ||
                    !ClosedShopEligible(plan,current) || RuntimeFamilyOwnsDay(plan,current) || plan.Routine==null ||
                    StableSeed.Combine(plan.Resident.AppearanceSeed,(uint)fixtureDay,7841,1)%3!=0)continue;
                foreach(var outing in ResidentRoutineDay.CopyOutings(plan.Routine))
                {
                    if(outing.Itinerary.Stops.Count==0)continue;
                    double sample=dayStart+outing.DepartureSeconds+Math.Min(15,outing.Itinerary.Stops[0].WalkSeconds*0.25);
                    if(sample<=current || sample>=plannedTime)continue;
                    var state=Scheduled(plan,sample);
                    if(state.Activity!=Activity.Travel)continue;
                    actualBuyerPlan=plan;plannedTime=sample;actualDeparture=outing.DepartureSeconds;plannedState=state;
                }
            }
            if(actualBuyerPlan==null)throw new InvalidOperationException("No current eligible travelling body or actual remaining published adult outing can supply this market buyer: plans="+
                plans.Count+"; itinerarySources="+itinerarySources+"; activeBodies="+actors.Count+"; outdoorRoutesPrepared="+OutdoorRoutesPrepared+
                ". The cold SAVE3 contains no saved routes; the existing actual SAVE9 acceptance checkpoint has250 itineraries. No calendar search or NPC injection was performed.");
            game.PlayerMotor.transform.position=(existingBuyer?existingBuyer.GroundPosition:geometry.ScenePoint(plannedState.Position))+Vector3.right*4+Vector3.up;
            if(plannedTime>current)clock.RaiseTime((float)(plannedTime-clock.ToSeconds()));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_VISIT_PLANNED_BUYER: resident="+actualBuyerPlan.Resident.Id+
                "; sourceDeparture="+actualDeparture+"; scheduledTravelAt="+plannedTime+"; sourcePosition="+plannedState.Position.X+","+plannedState.Position.Z+
                "; selectedExistingBody="+(bool)existingBuyer+"; actualScheduledTravel=true; observerOnly=true; noNpcInjection=true; noNpcFunding=true");
            var chosen=new List<ResidentActor>();ResidentActor buyer=null;
            deadline=Time.realtimeSinceStartup+30;float nextBuyerDiagnostic=Time.realtimeSinceStartup;
            while((chosen.Count<requiredBodies || buyer==null) && Time.realtimeSinceStartup<deadline)
            {
                chosen.Clear();buyer=null;
                foreach(var body in actors.Values)
                {
                    if(!body || !body.gameObject.activeInHierarchy || !body.Resident.IsAlive || body.IsSleeping || body.IsConversing ||
                        body.Resident.AgeGroup!=ResidentAgeGroup.Adult || body.GetComponent<StaticNPC>() || !body.GetComponent<CrowdMotion>() ||
                        !body.GetComponent<CrowdMotion>().HasPlacement)continue;
                    var plan=plans.Find(p=>p.Resident.Id==body.Resident.Id);
                    if(plan==null || !ClosedShopEligible(plan,ActionNow()))continue;
                    if(chosen.Count<requiredBodies)chosen.Add(body);
                    var inventory=worldStore.ReadNpcInventory(body.Resident.Id);
                    int day=(int)Math.Floor(ActionNow()/86400);
                    if(buyer==null && body.Resident.Id==actualBuyerPlan.Resident.Id && inventory!=null && inventory.WalletGold>=10 &&
                        StableSeed.Combine(body.Resident.AppearanceSeed,(uint)day,7841,1)%3==0)
                    {buyer=body;}
                }
                if(buyer!=null && !chosen.Contains(buyer)){if(chosen.Count==requiredBodies)chosen[0]=buyer;else chosen.Add(buyer);}
                if(Time.realtimeSinceStartup>=nextBuyerDiagnostic)
                {
                    var actual=FindActor(actualBuyerPlan.Resident.Id);var inventory=worldStore.ReadNpcInventory(actualBuyerPlan.Resident.Id);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_VISIT_BUYER_READINESS: resident="+actualBuyerPlan.Resident.Id+
                        "; activity="+Scheduled(actualBuyerPlan,ActionNow()).Activity+"; actor="+(bool)actual+
                        "; active="+(actual && actual.gameObject.activeInHierarchy)+"; sleeping="+(actual && actual.IsSleeping)+
                        "; eligible="+ClosedShopEligible(actualBuyerPlan,ActionNow())+"; wallet="+(inventory==null?-1:inventory.WalletGold)+
                        "; chosen="+chosen.Count+"; required="+requiredBodies);
                    nextBuyerDiagnostic=Time.realtimeSinceStartup+5;
                }
                if(chosen.Count<requiredBodies || buyer==null)yield return null;
                if(buyer!=null && chosen.Count==requiredBodies)break;
            }
            if(chosen.Count<requiredBodies || buyer==null)throw new InvalidOperationException("Market native fixture needs "+requiredBodies+
                " actual eligible resident bodies and a buyer with their own money.");
            bool enabledBefore=enabled;var playerBefore=game.PlayerMotor.transform.position;
            var originals=new Dictionary<ResidentActor,Vector3>();var previous=new Dictionary<string,MarketVisit>();
            var previousDays=new Dictionary<string,int>();
            foreach(var body in chosen)
            {
                originals.Add(body,body.transform.position);MarketVisit active;int day;
                if(marketVisits.TryGetValue(body.Resident.Id,out active))previous.Add(body.Resident.Id,active);
                if(marketVisitDays.TryGetValue(body.Resident.Id,out day))previousDays.Add(body.Resident.Id,day);
                EndMarketVisit(body.Resident.Id);
            }
            enabled=true;
            try
            {
                NativeMarketSlot browse=null;Vector3 approach=Vector3.zero;
                foreach(var slot in sellerPost.Slots)
                    if(!slot.Payment && TryMarketProbeApproach(slot,buyer.Resident.Id,out approach)){browse=slot;break;}
                if(browse==null)throw new InvalidOperationException("Selected market stand has no real clear approach to its goods table.");
                StageMarketProbeBody(buyer,approach);
                var observerAway=(approach-browse.Ground).normalized;
                game.PlayerMotor.transform.position=approach+observerAway*4+Vector3.up;
                var visit=StartMarketProbeVisit(buyer,browse,approach,30,catalog);
                int sellerHash=NativeExteriorNpcIdentity.Hash(sellerPost.Seller,directory.transform);
                string sellerId=NativeExteriorNpcIdentity.PersonId(directory.MapID,sellerHash);
                var seller=worldStore.NativePersonInSettlement(directory.MapID,sellerId);NativeVendorTradeStock stock;string reason;
                if(seller==null || !NativeVendorTradeAdapter.TryOpenMarketStock(worldStore,sellerPost.Seller,seller,out stock,out reason))
                    throw new InvalidOperationException("Selected market seller has no native owned stock.");
                var owner=buyer.GetComponent<NativeNpcInventoryOwner>();
                if(!owner || owner.Binding==null)throw new InvalidOperationException("Market buyer has no native ownership binding.");
                int buyerGold=worldStore.ReadNpcInventory(buyer.Resident.Id).WalletGold;
                int sellerGold=worldStore.ReadNpcInventory(sellerId).WalletGold;
                var before=new HashSet<ulong>();for(int index=0;index<owner.Binding.Items.Count;index++)before.Add(owner.Binding.Items.GetItem(index).UID);
                var stockCounts=new Dictionary<ulong,int>();for(int index=0;index<stock.Items.Count;index++)stockCounts.Add(stock.Items.GetItem(index).UID,stock.Items.GetItem(index).stackCount);
                bool browsed=false,counter=false;float travelled=0,maximumStep=0;Vector3 last=buyer.transform.position;
                deadline=Time.realtimeSinceStartup+180;
                while(marketVisits.ContainsKey(buyer.Resident.Id) && Time.realtimeSinceStartup<deadline)
                {
                    float step=Vector3.Distance(last,buyer.transform.position);maximumStep=Mathf.Max(maximumStep,step);travelled+=step;last=buyer.transform.position;
                    if(step>0.7f)throw new InvalidOperationException("Market body jumped during production movement: "+step);
                    if(!visit.Payment && visit.Dwell.ArrivedAt>=0 && !browsed)
                    {
                        browsed=true;var facing=visit.Slot.Look-(buyer.transform.position-Vector3.up);facing.y=0;
                        if(Vector3.Dot(buyer.transform.forward,facing.normalized)<0.8f)throw new InvalidOperationException("Native browser does not face displayed goods.");
                        visit.Dwell=new MarketDwell(300);
                        game.PlayerMotor.transform.position=buyer.transform.position-facing.normalized*1.5f;
                        float hold=Time.realtimeSinceStartup+2;
                        while(Time.realtimeSinceStartup<hold)
                        {
                            if(Vector3.Dot(buyer.transform.forward,facing.normalized)<0.8f)throw new InvalidOperationException("Browser turned from goods toward nearby player.");
                            yield return null;
                        }
                        visit.Dwell=new MarketDwell(1);last=buyer.transform.position;
                    }
                    if(visit.Payment && visit.Dwell.ArrivedAt>=0)counter=true;
                    yield return null;
                }
                if(marketVisits.ContainsKey(buyer.Resident.Id) || !browsed || !counter || visit.Receipt==null || travelled<3)
                    throw new InvalidOperationException("Physical browse/payment/return incomplete: browse="+browsed+"; counter="+counter+
                        "; receipt="+(visit.Receipt!=null)+"; travelled="+travelled+"; active="+marketVisits.ContainsKey(buyer.Resident.Id));
                var receipt=visit.Receipt;var item=owner.Binding.Items.GetItem(receipt.RecipientItemUid);
                int remaining=stock.Items.GetItem(receipt.SourceItemUid)==null?0:stock.Items.GetItem(receipt.SourceItemUid).stackCount;
                if(item==null || item.stackCount!=1 || before.Contains(item.UID) || item.UID==receipt.SourceItemUid || remaining!=stockCounts[receipt.SourceItemUid] ||
                    buyerGold-worldStore.ReadNpcInventory(buyer.Resident.Id).WalletGold!=receipt.Price ||
                    worldStore.ReadNpcInventory(sellerId).WalletGold-sellerGold!=receipt.Price ||
                    worldStore.NpcInventoryItemOwner(item.UID)!=buyer.Resident.Id ||
                    Vector3.Distance(buyer.transform.position-Vector3.up,approach)>0.65f)
                    throw new InvalidOperationException("Market purchase ownership, stock, exact wallets or physical return mismatch.");
                NativeNpcInventoryBinding restored;
                if(!NativeNpcInventoryBridge.TryOpen(worldStore,buyer.Resident.Id,null,0,out restored,out reason) ||
                    restored.Items.GetItem(item.UID)==null || restored.Items.GetItem(item.UID).GetType()!=item.GetType())
                    throw new InvalidOperationException("Purchased native item UID/type did not survive inventory serialization.");
                var memory=worldStore.Snapshot().SceneMemories.Find(m=>m.ObserverId==buyer.Resident.Id && m.EventId==receipt.TransactionId);
                if(memory==null || memory.Kind!=NpcSceneEventKind.Shopping || memory.Valence< -5 || memory.Valence>5 || memory.ResponsibleId!=sellerId ||
                    !memory.Observation.Contains(item.LongName) || !memory.Observation.Contains(seller.Name))
                    throw new InvalidOperationException("Actual market purchase lacks its valued item/seller memory.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_BUY_RETURN_PASS: buyer="+buyer.Resident.Id+"; seller="+sellerId+
                    "; source="+receipt.SourceItemUid+"; received="+receipt.RecipientItemUid+"; gold="+receipt.Price+
                    "; stockDelta=0; buyerWalletDelta="+(-receipt.Price)+"; sellerWalletDelta="+receipt.Price+
                    "; retainedOnReturn=true; serializedType=true; goodsFacing=true; nearbyPlayerIgnored=true; physicalTravel="+travelled+"; maximumStep="+maximumStep);

                if(show==null)
                {
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_STAGE_NOT_EXERCISED: reason=no-eligible-live-stage-with-four-viewing-slots; sourceAbsenceNotProven=true");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_VISIT_NATIVE_PASS: realPosts="+catalog.Posts.Count+"; sourceSlots="+slots+
                        "; sellerPost="+sellerPost.Id+"; stageExercised=false; spectators=0; actualGoods=true; nativeBuyerAndSeller=true; browsePayReturn=true; exactOwnedUid=true; exactGold=true");
                    yield break;
                }
                var viewing=new List<MarketVisit>();var stageSlots=new List<NativeMarketSlot>(show.Slots);
                for(int index=0;index<chosen.Count;index++)
                {
                    NativeMarketSlot selected=null;Vector3 start=Vector3.zero;
                    foreach(var slot in stageSlots)
                        if(TryMarketProbeApproach(slot,chosen[index].Resident.Id,out start) &&
                            catalog.TryClaim(slot,chosen[index].Resident.Id,start,ActionNow(),ActionNow()+3600)){selected=slot;break;}
                    if(selected==null)throw new InvalidOperationException("Actual stage cannot reserve four separate reachable viewing places.");
                    stageSlots.Remove(selected);StageMarketProbeBody(chosen[index],start);
                    viewing.Add(StartMarketProbeVisit(chosen[index],selected,start,1200,catalog));
                }
                deadline=Time.realtimeSinceStartup+75;bool all=false;
                while(!all && Time.realtimeSinceStartup<deadline)
                {
                    all=true;
                    for(int index=0;index<chosen.Count;index++)
                    {
                        if(viewing[index].Dwell.ArrivedAt<0)all=false;
                    }
                    yield return null;
                }
                if(!all)throw new InvalidOperationException("Stage spectators did not physically arrive.");
                float minimum=float.MaxValue;
                for(int index=0;index<chosen.Count;index++)
                {
                    var body=chosen[index];var direction=viewing[index].Slot.Look-(body.transform.position-Vector3.up);direction.y=0;
                    if(Vector3.Dot(body.transform.forward,direction.normalized)<0.8f)throw new InvalidOperationException("Spectator faces away from native performers.");
                    for(int other=index+1;other<chosen.Count;other++)minimum=Mathf.Min(minimum,Vector3.Distance(body.transform.position,chosen[other].transform.position));
                    string context;if(!TryMarketVisitContext(body,ActionNow(),out context)||!context.Contains("stage"))
                        throw new InvalidOperationException("Real stage activity is missing from spectator context.");
                }
                if(minimum<CrowdMotion.Separation-0.02f)throw new InvalidOperationException("Stage crowd overlaps: minimum="+minimum);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_VISIT_NATIVE_PASS: realPosts="+catalog.Posts.Count+"; sourceSlots="+slots+
                    "; stage="+show.Id+"; stageExercised=true; spectators=4; physicalArrival=true; minimumGap="+minimum+
                    "; stageFacing=true; actualGoods=true; nativeBuyerAndSeller=true; browsePayReturn=true; exactOwnedUid=true; exactGold=true");
            }
            finally
            {
                foreach(var body in chosen)if(body)
                {
                    EndMarketVisit(body.Resident.Id);marketVisitDays.Remove(body.Resident.Id);
                    MarketVisit active;int day;
                    if(previous.TryGetValue(body.Resident.Id,out active))marketVisits[body.Resident.Id]=active;
                    if(previousDays.TryGetValue(body.Resident.Id,out day))marketVisitDays[body.Resident.Id]=day;
                    StageMarketProbeBody(body,originals[body]-Vector3.up);
                }
                game.PlayerMotor.transform.position=playerBefore;enabled=enabledBefore;
            }
        }
        private MarketVisit StartMarketProbeVisit(ResidentActor actor,NativeMarketSlot slot,Vector3 resume,double duration,NativeMarketCatalog catalog)
        {
            double now=ActionNow();
            if(!catalog.TryClaim(slot,actor.Resident.Id,actor.transform.position-Vector3.up,now,now+7200,actor))
                throw new InvalidOperationException("Native market fixture cannot claim its real activity slot.");
            var visit=new MarketVisit {Slot=slot,StartedAt=now,EndsAt=now+7200,Day=(int)Math.Floor(now/86400),Limit=1,
                Dwell=new MarketDwell(duration),LocalResume=directory.transform.InverseTransformPoint(resume),
                ResumeDescription="my previous route",Reason="a controlled market stop during my normal outing"};
            visit.Seen.Add(slot.Post.Id);marketVisits[actor.Resident.Id]=visit;marketVisitDays[actor.Resident.Id]=visit.Day;return visit;
        }
        private static void StageMarketProbeBody(ResidentActor actor,Vector3 ground)
        {var native=actor.GetComponent<NativeResidentNavigation>();if(native)native.ResetPlacement();actor.GetComponent<CrowdMotion>().RestorePlacement(ground);}
        private static bool TryMarketProbeApproach(NativeMarketSlot slot,string person,out Vector3 ground)
        {
            ground=Vector3.zero;
            for(int attempt=0;attempt<12;attempt++)
            {
                float angle=attempt*Mathf.PI/6;var desired=slot.Ground+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*3;
                NavMeshHit sample;if(!NavMesh.SamplePosition(desired,out sample,0.4f,NavMesh.AllAreas) ||
                    !NativeMarketCatalog.ClearBody(sample.position,person) || Vector3.Distance(sample.position,slot.Ground)<2.5f)continue;
                var path=new NavMeshPath();
                if(!NavMesh.CalculatePath(sample.position,slot.Ground,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)continue;
                ground=sample.position;return true;
            }
            return false;
        }
    }
}
