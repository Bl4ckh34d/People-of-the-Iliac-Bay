using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Controlled clock, real authored person, actual town/home geometry.
        // No synthetic household, bed, inventory, path or replacement actor.
        internal IEnumerator VerifyMarketCommute()
        {
            var game=GameManager.Instance;var clock=DaggerfallUnity.Instance.WorldTime.Now;
            string morningPerson=Environment.GetEnvironmentVariable("POTIB_MARKET_MORNING_PERSON");
            bool morningOnly=!string.IsNullOrEmpty(morningPerson);
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            float deadline=Time.realtimeSinceStartup+30,nextReadiness=Time.realtimeSinceStartup;
            NativeMarketCommuter selected=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                double shortest=double.PositiveInfinity;
                foreach(var body in marketCommuters.Values)
                    if(body && body.Role!=MarketPostRole.Performer && body.Role!=MarketPostRole.Unknown &&
                        (!morningOnly || body.Person.Id==morningPerson) &&
                        body.Commute!=null && body.Commute.Count>=2)
                    {double length=MarketRouteLength(body.Commute);if(length>0 && length<shortest){selected=body;shortest=length;}}
                if(Time.realtimeSinceStartup>=nextReadiness)
                {Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_COMMUTE_READINESS: "+MarketCommuteReadiness());nextReadiness=Time.realtimeSinceStartup+5;}
                if(selected)break;yield return null;
            }
            if(!selected)throw new InvalidOperationException("Market commute readiness missing after30 seconds; requestedMorningPerson="+morningPerson+"; "+MarketCommuteReadiness());
            var npc=selected.Npc;string id=selected.Person.Id;
            var actor=selected.GetComponent<ResidentActor>();
            var owner=selected.GetComponent<NativeNpcInventoryOwner>();
            if(!actor || !actor.IsStationary || owner==null || owner.Binding==null)
                throw new InvalidOperationException("Native market body lacks its shared stationary entity/inventory binding.");
            var items=owner.Binding.Items;
            int nativeHash=selected.Person.NativeHash;
            Vector3 previous=selected.transform.position;double homeMetres=0;float maxStep=0;
            float walkBound,nextWalkProgress;
            if(morningOnly)
            {
                // Reconstruct ordinary unobserved night state through the existing
                // production clock catch-up. This does not claim another evening walk.
                MarketProbeRaiseTo(Math.Max(21,sleepHour)*3600+30);
                deadline=Time.realtimeSinceStartup+30;
                do {yield return null;} while((!selected.InsideHome || selected.AtPost || selected.gameObject.activeInHierarchy) && Time.realtimeSinceStartup<deadline);
                if(!selected.InsideHome || selected.AtPost || selected.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Ordinary unobserved market home setup missing after30 seconds; person="+id+"; home="+selected.InsideHome+"; atPost="+selected.AtPost+"; active="+selected.gameObject.activeInHierarchy+"; phase="+selected.Phase);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_MORNING_SETUP: person="+id+"; ordinaryOffscreenReconstruction=true; eveningWalkNewlyClaimed=false; priorEveningEvidence=market-commute22-player.log; home="+selected.InsideHome+"; nativePhase="+selected.Phase);
            }
            else
            {
            int workDay=(int)Math.Floor(clock.ToSeconds()/86400d);
            game.PlayerMotor.transform.position=selected.transform.position-selected.Offset+Vector3.right*4+Vector3.up;
            double closes=MarketWorkday.ClosingMinute(unchecked((uint)nativeHash),workDay,false)*60d;
            // Native climate/scene updates may consume several real seconds.
            // First observe actual work; a clock-skip reconstruction at home
            // cannot count as a newly witnessed evening walk.
            MarketProbeRaiseTo(closes-120);
            deadline=Time.realtimeSinceStartup+30;
            do {yield return null;} while((!selected.gameObject.activeInHierarchy || selected.InsideHome || !selected.AtPost || selected.Phase!="work") &&
                Time.realtimeSinceStartup<deadline && clock.ToSeconds()%86400<closes);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_COMMUTE_PHASE: phase=observed-work; person="+id+"; second="+clock.ToSeconds()%86400+
                "; closes="+closes+"; active="+selected.gameObject.activeInHierarchy+"; atPost="+selected.AtPost+"; home="+selected.InsideHome+
                "; nativePhase="+selected.Phase+"; actualClosing="+selected.ClosingMinute+"; routeMetres="+MarketRouteLength(selected.Commute));
            if(!selected.gameObject.activeInHierarchy || selected.InsideHome || !selected.AtPost || selected.Phase!="work")
                throw new InvalidOperationException("Market evening cannot be observed: seller was not actually working before closing; second="+
                    clock.ToSeconds()%86400+"; closes="+closes+"; home="+selected.InsideHome+"; phase="+selected.Phase);
            previous=selected.transform.position;
            double remainingWork=Math.Max(0,selected.ClosingMinute*60d-clock.ToSeconds()%86400);
            float closingBound=(float)(remainingWork/Math.Max(0.1,DaggerfallUnity.Instance.WorldTime.TimeScale)+30);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_COMMUTE_BOUND: phase=remaining-work; seconds="+closingBound+
                "; gameSeconds="+remainingWork+"; timeScale="+DaggerfallUnity.Instance.WorldTime.TimeScale);
            deadline=Time.realtimeSinceStartup+closingBound;
            while(selected.AtPost && !selected.InsideHome && Time.realtimeSinceStartup<deadline)yield return null;
            if(selected.AtPost)throw new InvalidOperationException("Observed market work did not end at its actual closing boundary.");
            walkBound=(float)(MarketRouteLength(selected.Commute)/ResidentMovement.WalkSpeed+30);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_COMMUTE_BOUND: phase=evening-walk; routeMetres="+MarketRouteLength(selected.Commute)+
                "; nativeSpeed="+ResidentMovement.WalkSpeed+"; seconds="+walkBound+"; marginSeconds=30; readiness=false");
            deadline=Time.realtimeSinceStartup+walkBound;nextWalkProgress=Time.realtimeSinceStartup;
            while(Time.realtimeSinceStartup<deadline && !selected.InsideHome)
            {
                yield return null;
                float step=Vector3.Distance(previous,selected.transform.position);
                if(selected.gameObject.activeInHierarchy){homeMetres+=step;maxStep=Mathf.Max(maxStep,step);}
                previous=selected.transform.position;
                game.PlayerMotor.transform.position=previous-selected.Offset+Vector3.right*4+Vector3.up;
                if(Time.realtimeSinceStartup>=nextWalkProgress)
                {Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_COMMUTE_PROGRESS: phase=evening-walk; metres="+homeMetres+
                    "; remainingGroundGap="+Vector3.Distance(previous-selected.Offset,selected.Commute[0])+"; nativePhase="+selected.Phase+
                    "; maxStep="+maxStep);nextWalkProgress=Time.realtimeSinceStartup+15;}
            }
            if(!selected.InsideHome || homeMetres<Math.Min(3,MarketRouteLength(selected.Commute)*0.25) || maxStep>1)
                throw new InvalidOperationException("Market evening walk failed: home="+selected.InsideHome+"; metres="+homeMetres+"; maxStep="+maxStep);
            }
            DaggerfallStaticDoors homeOwner=null;StaticDoor homeDoor=default(StaticDoor);
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var door in doors.Doors)
                    if(door.buildingKey==selected.Person.HomeKey && door.doorType==DoorTypes.Building)
                    {homeOwner=doors;homeDoor=door;break;}
            if(!homeOwner)throw new InvalidOperationException("Assigned market home has no real native building door.");
            TownSmokeHarness.EnterInterior(homeOwner.transform,homeDoor);
            deadline=Time.realtimeSinceStartup+30;
            while((!interior || interior.EntryDoor.buildingKey!=selected.Person.HomeKey || indoorGeometry==null) && Time.realtimeSinceStartup<deadline)yield return null;
            if(!interior || interior.EntryDoor.buildingKey!=selected.Person.HomeKey)
                throw new InvalidOperationException("Could not enter the seller's actual assigned home.");
            if(!morningOnly)MarketProbeRaiseTo(Math.Max(21,sleepHour)*3600+30);
            var bedRecords=new List<int>(indoorBeds.Keys);bedRecords.Sort();
            if(selected.Person.BedSlot<0 || selected.Person.BedSlot>=bedRecords.Count)
                throw new InvalidOperationException("Market worker has no actual assigned bed in its home.");
            var assignedBed=indoorBeds[bedRecords[selected.Person.BedSlot]];
            deadline=Time.realtimeSinceStartup+120;SleepingPose sleeping=null;bool reachedBed=false;
            while(Time.realtimeSinceStartup<deadline)
            {
                sleeping=selected.GetComponent<SleepingPose>();Vector3 beside;
                Vector3 standing=sleeping && sleeping.Active?sleeping.NativeStandingPosition:selected.transform.position;
                reachedBed=selected.Phase=="sleep" && selected.InsideHome && selected.gameObject.activeInHierarchy &&
                    indoorGeometry.StandBesideBed(assignedBed,out beside,true) &&
                    Vector3.Distance(standing-selected.Offset,beside)<=0.65f;
                if(reachedBed)break;yield return null;
            }
            if(!reachedBed)
                throw new InvalidOperationException("Original market body did not physically reach and sleep in its assigned home bed.");
            string context;
            if(!TryMarketCommuterContext(npc,clock.ToSeconds(),out context) || !context.Contains("resting at my assigned bed"))
                throw new InvalidOperationException("Market sleep context does not describe the observed bed state.");
            var entity=selected.GetComponent<DaggerfallEntityBehaviour>().Entity;
            int injured=Math.Max(1,entity.MaxHealth-20),wallet=worldStore.ReadNpcInventory(id).WalletGold;
            int nativeRate=NativeNpcRestTreatment.RecoveryRatePerGameHour(entity);
            var sleepEffects=selected.GetComponent<DaggerfallWorkshop.Game.MagicAndEffects.EntityEffectManager>();
            string healthPreflight="person="+id+"; home="+selected.Person.HomeKey+"; bed="+selected.Person.BedSlot+
                "; health="+entity.CurrentHealth+"; maxHealth="+entity.MaxHealth+"; injuryTarget="+injured+"; nativeHourlyRate="+nativeRate+
                "; expectedHourGain="+Math.Min(nativeRate,entity.MaxHealth-injured)+"; poison="+(sleepEffects?sleepEffects.PoisonCount:-1)+
                "; disease="+(sleepEffects?sleepEffects.DiseaseCount:-1)+"; realBedReached="+reachedBed;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_SLEEP_PREFLIGHT: "+healthPreflight);
            if(entity.MaxHealth<=1 || injured>=entity.MaxHealth || nativeRate<=0 || !sleepEffects || sleepEffects.PoisonCount!=0 || sleepEffects.DiseaseCount!=0)
                throw new InvalidOperationException("Actual market sleeper does not meet native recovery prerequisites: "+healthPreflight);
            entity.SetHealth(injured);selected.GetComponent<NativeNpcVitalsBinding>().Capture();
            ObserveMarketHomeSleep(selected,clock.ToSeconds());
            double sleepStarted=clock.ToSeconds();float recoveryStarted=Time.unscaledTime;HomeSleepClock observedSleep=null;
            deadline=Time.realtimeSinceStartup+5;
            do {yield return null;} while((Time.unscaledTime-recoveryStarted<0.5f || !homeSleepClocks.TryGetValue(id,out observedSleep) ||
                observedSleep.CheckedAt<sleepStarted || !observedSleep.Eligible || !observedSleep.AtHomeAsleep) && Time.realtimeSinceStartup<deadline);
            homeSleepClocks.TryGetValue(id,out observedSleep);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_SLEEP_CLOCK_PREFLIGHT: elapsedRealSeconds="+(Time.unscaledTime-recoveryStarted)+
                "; checkedAt="+(observedSleep==null?-1:observedSleep.CheckedAt)+"; requiredFrom="+sleepStarted+
                "; eligible="+(observedSleep!=null && observedSleep.Eligible)+"; asleep="+(observedSleep!=null && observedSleep.AtHomeAsleep));
            if(Time.unscaledTime-recoveryStarted<0.5f || observedSleep==null || observedSleep.CheckedAt<sleepStarted || !observedSleep.Eligible || !observedSleep.AtHomeAsleep)
                throw new InvalidOperationException("Actual market sleeper did not enter the ordinary recovery observation before the hour skip.");
            clock.RaiseTime(3600);
            double sleepThrough=clock.ToSeconds();
            deadline=Time.realtimeSinceStartup+5;
            // The shared production observer deliberately checks at 0.5-second
            // intervals. Two rendered frames do not establish that it ran.
            do {yield return null;} while(entity.CurrentHealth<=injured &&
                (!homeSleepClocks.TryGetValue(id,out observedSleep) || observedSleep.CheckedAt<sleepThrough) &&
                Time.realtimeSinceStartup<deadline);
            homeSleepClocks.TryGetValue(id,out observedSleep);
            string sleepValues="person="+id+"; health="+entity.CurrentHealth+"; injured="+injured+"; maxHealth="+entity.MaxHealth+
                "; nativeHourlyRate="+NativeNpcRestTreatment.RecoveryRatePerGameHour(entity)+"; wallet="+worldStore.ReadNpcInventory(id).WalletGold+
                "; expectedWallet="+wallet+"; checkedAt="+(observedSleep==null?-1:observedSleep.CheckedAt)+"; requiredThrough="+sleepThrough+
                "; eligible="+(observedSleep!=null && observedSleep.Eligible)+"; asleep="+(observedSleep!=null && observedSleep.AtHomeAsleep)+
                "; accumulatedSeconds="+(observedSleep==null?-1:observedSleep.Seconds)+"; poison="+(sleepEffects?sleepEffects.PoisonCount:-1)+
                "; disease="+(sleepEffects?sleepEffects.DiseaseCount:-1)+"; active="+selected.gameObject.activeInHierarchy+"; phase="+selected.Phase;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_SLEEP_RECOVERY_VALUES: "+sleepValues);
            if(entity.CurrentHealth<=injured || worldStore.ReadNpcInventory(id).WalletGold!=wallet)
                throw new InvalidOperationException("Market home sleep did not reuse ordinary free native health recovery: "+sleepValues);
            int nextDay=(int)Math.Floor(clock.ToSeconds()/86400d)+1;
            double arrival=MarketWorkday.ArrivalMinute(unchecked((uint)nativeHash),nextDay)*60d;
            double travel=MarketRouteLength(selected.Commute)/ResidentMovement.WalkSpeed*DaggerfallUnity.Instance.WorldTime.TimeScale;
            double departure=arrival-travel-selected.HomeExitSeconds;
            double preDeparture=5*Math.Max(0.1,DaggerfallUnity.Instance.WorldTime.TimeScale);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_MORNING_ROUTE_PREFLIGHT: person="+id+"; routeMetres="+MarketRouteLength(selected.Commute)+
                "; nativeSpeed="+ResidentMovement.WalkSpeed+"; timeScale="+DaggerfallUnity.Instance.WorldTime.TimeScale+
                "; arrival="+arrival+"; departure="+departure+"; homeExitGameSeconds="+selected.HomeExitSeconds+
                "; beforeDepartureRealSeconds=5; walkBudget="+(MarketRouteLength(selected.Commute)/ResidentMovement.WalkSpeed+30)+"; outerBudget=900");
            MarketProbeRaiseTo(Math.Max(0,departure-preDeparture));
            yield return null;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_COMMUTE_PHASE: phase=observed-home-before-departure; person="+id+
                "; second="+clock.ToSeconds()%86400+"; departure="+(arrival-travel-selected.HomeExitSeconds)+
                "; active="+selected.gameObject.activeInHierarchy+"; home="+selected.InsideHome+"; nativePhase="+selected.Phase);
            if(!selected.gameObject.activeInHierarchy || !selected.InsideHome || clock.ToSeconds()%86400>=departure)
                throw new InvalidOperationException("Morning clock-skip crossed departure before the actual home body could be observed.");
            deadline=Time.realtimeSinceStartup+120;
            while(selected.gameObject.activeInHierarchy && Time.realtimeSinceStartup<deadline)yield return null;
            if(selected.gameObject.activeInHierarchy)throw new InvalidOperationException("Market sleeper did not wake and walk out of the actual home.");
            game.PlayerEnterExit.TransitionExterior(true);
            deadline=Time.realtimeSinceStartup+30;
            while((game.PlayerEnterExit.IsPlayerInside || interior) && Time.realtimeSinceStartup<deadline)yield return null;
            if(game.PlayerEnterExit.IsPlayerInside)throw new InvalidOperationException("Could not follow the market worker outside.");
            while((!selected.gameObject.activeInHierarchy || selected.transform.parent!=directory.transform) && Time.realtimeSinceStartup<deadline)yield return null;
            previous=selected.transform.position;double returnMetres=0;maxStep=0;
            walkBound=(float)(MarketRouteLength(selected.Commute)/ResidentMovement.WalkSpeed+30);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_COMMUTE_BOUND: phase=morning-walk; routeMetres="+MarketRouteLength(selected.Commute)+
                "; nativeSpeed="+ResidentMovement.WalkSpeed+"; seconds="+walkBound+"; marginSeconds=30; readiness=false");
            deadline=Time.realtimeSinceStartup+walkBound;nextWalkProgress=Time.realtimeSinceStartup;
            while(Time.realtimeSinceStartup<deadline && !selected.AtPost)
            {
                yield return null;float step=Vector3.Distance(previous,selected.transform.position);
                if(selected.gameObject.activeInHierarchy){returnMetres+=step;maxStep=Mathf.Max(maxStep,step);}
                previous=selected.transform.position;
                game.PlayerMotor.transform.position=previous-selected.Offset+Vector3.right*4+Vector3.up;
                if(Time.realtimeSinceStartup>=nextWalkProgress)
                {Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MARKET_COMMUTE_PROGRESS: phase=morning-walk; metres="+returnMetres+
                    "; atPost="+selected.AtPost+"; nativePhase="+selected.Phase+"; maxStep="+maxStep);
                    nextWalkProgress=Time.realtimeSinceStartup+15;}
            }
            if(!selected.AtPost || returnMetres<3 || maxStep>1 || clock.ToSeconds()%86400>390*60+60)
                throw new InvalidOperationException("Market morning return failed: atPost="+selected.AtPost+"; metres="+returnMetres+"; maxStep="+maxStep+"; daySecond="+clock.ToSeconds()%86400);
            if(selected.Npc!=npc || selected.Person.Id!=id || selected.Person.NativeHash!=nativeHash ||
                !ReferenceEquals(items,selected.GetComponent<NativeNpcInventoryOwner>().Binding.Items) ||
                selected.GetComponents<ResidentActor>().Length!=1 || selected.GetComponent<MobilePersonNPC>())
                throw new InvalidOperationException("Market commute replaced or duplicated its native body, identity or inventory.");
            Debug.Log((morningOnly?"PEOPLE_OF_THE_ILIAC_BAY_MARKET_MORNING_PASS: person=":"PEOPLE_OF_THE_ILIAC_BAY_MARKET_COMMUTE_PASS: person=")+id+"; home="+selected.Person.HomeKey+
                "; bed="+selected.Person.BedSlot+"; eveningMetres="+homeMetres+"; morningMetres="+returnMetres+
                "; eveningWalkNewlyClaimed="+!morningOnly+"; sameStaticBody=true; sameInventory=true; realBed=true; sleepContext=true; sleepArtRequired=false; sharedHomeSleepHeal=true; nativeMobileDuplicate=false; maxMorningStep="+maxStep);
        }

        private string MarketCommuteReadiness()
        {
            var text=new StringBuilder("ready=").Append(Ready).Append("; map=").Append(directory?directory.MapID:-1)
                .Append("; interior=").Append(interior?interior.EntryDoor.buildingKey:-1).Append("; commuters=").Append(marketCommuters.Count)
                .Append("; surveyedHomes=").Append(marketHomes==null?-1:marketHomes.Count).Append("; entrances=").Append(geometry==null?0:geometry.Entrances.Count);
            if(!directory || worldStore==null)return text.ToString();
            var catalog=NativeMarketCatalog.For(directory);text.Append("; catalogPosts=").Append(catalog.Posts.Count);
            int count=0;
            foreach(var post in catalog.Posts)
            {
                if(!post.Seller || count++>=8)continue;
                int hash=NativeExteriorNpcIdentity.Hash(post.Seller,directory.transform);
                var person=worldStore.NativePersonInSettlement(directory.MapID,NativeExteriorNpcIdentity.PersonId(directory.MapID,hash));
                NativeMarketCommuter commuter;marketCommuters.TryGetValue(NativeExteriorNpcIdentity.PersonId(directory.MapID,hash),out commuter);
                text.Append(" | seller=").Append(post.Seller.DisplayName).Append("; role=").Append(post.Role)
                    .Append("; active=").Append(post.Seller.gameObject.activeInHierarchy).Append("; registered=").Append(person!=null)
                    .Append("; home=").Append(person==null?-1:person.HomeKey).Append("; bed=").Append(person==null?-1:person.BedSlot)
                    .Append("; routePoints=").Append(commuter && commuter.Commute!=null?commuter.Commute.Count:0);
            }
            return text.ToString();
        }
        private static void MarketProbeRaiseTo(double second)
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((second-clock.ToSeconds()%86400+86400)%86400));
        }
    }
}
