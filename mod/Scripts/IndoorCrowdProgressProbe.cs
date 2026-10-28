using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Observe real saved patrons at normal clock speed. Dwell and seated time are
    // not stalls; only a physically walking visit with no body progress is tracked.
    public static class IndoorCrowdProgressProbe
    {
        private sealed class Sample
        {
            public Vector3 Position,Target;public float ProgressAt,Worst,MissingAt,BestDistance;
            public bool HasBody,WasWalking,HadSeat,SawLeaving;public int Leg=-1,CompletedLegs;
        }
        private static readonly FieldInfo ProgressField=typeof(PhysicalShopVisit).GetField("progress",BindingFlags.Instance|BindingFlags.NonPublic);
        private static readonly FieldInfo JourneyStartField=typeof(ShopVisitJourney).GetField("start",BindingFlags.Instance|BindingFlags.NonPublic);
        private const float ObserveSeconds=180;
        // Standalone crowd fixture: the catch-up regression keeps its original inn
        // and assertions. This changes only observer location/time, using an
        // existing native door and the production-generated saved daily visits.
        public static IEnumerator RunExistingVenue(StreetPopulation streets,int buildingKey=131841)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-indoor-crowd-probe")<0)
                throw new InvalidOperationException("Existing-venue crowd fixture requires explicit crowd flag.");
            var game=GameManager.Instance;
            if(!NativeProbeText.IsTestCharacter(game.PlayerEntity.Name))
                throw new InvalidOperationException("Existing-venue crowd fixture requires a disposable character.");
            game.PlayerEntity.GodMode=true;
            if(game.PlayerEnterExit.IsPlayerInside)
            {
                game.PlayerEnterExit.TransitionExterior();float until=Time.realtimeSinceStartup+90;
                while((game.PlayerEnterExit.IsPlayerInside || !streets.Ready) && Time.realtimeSinceStartup<until)yield return null;
                if(game.PlayerEnterExit.IsPlayerInside || !streets.Ready)throw new InvalidOperationException("Crowd venue exterior transition timed out.");
            }
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            DaggerfallStaticDoors doorOwner=null;StaticDoor entry=default(StaticDoor);
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                foreach(var door in owner.Doors ?? new StaticDoor[0])
                    if(door.doorType==DoorTypes.Building && door.buildingKey==buildingKey){doorOwner=owner;entry=door;break;}
                if(doorOwner)break;
            }
            if(!doorOwner)throw new InvalidOperationException("Existing crowd venue native door missing: "+buildingKey);
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            double morning=(Math.Floor(clock.ToSeconds()/86400d)+1)*86400d+9*3600;
            game.PauseGame(true);clock.RaiseTime((float)(morning-clock.ToSeconds()));yield return null;game.PauseGame(false);
            TownSmokeHarness.EnterInterior(doorOwner.transform,entry);DaggerfallUI.Instance.PopToHUD();
            var field=typeof(StreetPopulation).GetField("shopJourneys",BindingFlags.Instance|BindingFlags.NonPublic);
            var interiorField=typeof(StreetPopulation).GetField("interior",BindingFlags.Instance|BindingFlags.NonPublic);
            float deadline=Time.realtimeSinceStartup+90;
            while(Time.realtimeSinceStartup<deadline)
            {
                var room=game.PlayerEnterExit.Interior;
                var saved=(Dictionary<string,ShopVisitJourney>)field.GetValue(streets);
                if(room && room.EntryDoor.buildingKey==buildingKey && ReferenceEquals(interiorField.GetValue(streets),room) && streets.Ready && saved.Count>0)break;
                yield return null;
            }
            var current=game.PlayerEnterExit.Interior;
            if(!current || current.EntryDoor.buildingKey!=buildingKey || !ReferenceEquals(interiorField.GetValue(streets),current) || ((Dictionary<string,ShopVisitJourney>)field.GetValue(streets)).Count==0)
                throw new InvalidOperationException("Existing crowd venue has no ready saved visits: "+buildingKey);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_CROWD_VENUE: building="+buildingKey+"; existing native door and saved schedules; gameTime="+clock.ToSeconds());
            yield return Run(streets);
        }
        public static IEnumerator Run(StreetPopulation streets)
        {
            var game=GameManager.Instance;var clock=DaggerfallUnity.Instance.WorldTime.Now;
            var journeys=(Dictionary<string,ShopVisitJourney>)typeof(StreetPopulation).GetField("shopJourneys",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(streets);
            double start=clock.ToSeconds(),chosen=-1;int bestScore=-1,predictedPeak=0,predictedChanges=0,predictedSeats=0,predictedDepartures=0;
            var planned=new List<ShopVisitJourney>();var starts=new Dictionary<ShopVisitJourney,double>();
            foreach(var journey in journeys.Values)if(journey!=null)
            {planned.Add(journey);starts[journey]=(double)JourneyStartField.GetValue(journey);}
            // Score every bounded candidate, rather than accepting the first two
            // transitions. Only saved schedules determine the observed population.
            double window=ObserveSeconds*DaggerfallUnity.Instance.WorldTime.TimeScale;
            double lastStart=(Math.Floor(start/86400d)+1)*86400d-window;
            for(int offset=0;start+offset<=lastStart;offset+=15)
            {
                // Yield bounded search slices so even a whole-day scan does not
                // monopolize the native player frame. Snapshot saved journeys;
                // ordinary live simulation can continue between slices.
                if(offset>0 && offset%240==0)yield return null;
                int changing=0,seats=0,departures=0,peak=0;
                foreach(var journey in planned)
                {
                    foreach(var occupancy in journey.Occupancies())
                        if(occupancy.End>=start+offset && occupancy.End<start+offset+window)
                        {
                            changing++;
                            for(int leg=0;leg<journey.LegCount;leg++)if(journey.SeatId(leg)!=null)
                            {
                                var route=journey.LegRoute(leg);var end=route[route.Count-1];
                                if(Math.Abs(end.X-occupancy.Position.X)+Math.Abs(end.Y-occupancy.Position.Y)+Math.Abs(end.Z-occupancy.Position.Z)<0.01){seats++;break;}
                            }
                        }
                    if(journey.PlannedEndAt>=start+offset && journey.PlannedEndAt<start+offset+window)departures++;
                }
                for(double at=start+offset;at<start+offset+window;at+=5)
                {
                    int walking=0;foreach(var journey in planned)
                    {
                        // Evaluate clamps pre-arrival samples to the first route
                        // point. Those future visitors are not walking here yet.
                        if(at<starts[journey])continue;
                        var phase=journey.Evaluate(at).Phase;if(phase==ShopVisitPhase.Walking||phase==ShopVisitPhase.Leaving)walking++;
                    }
                    peak=Math.Max(peak,walking);
                }
                int score=Math.Min(peak,4)*1000+Math.Min(seats,2)*200+Math.Min(departures,2)*200+changing;
                if(peak>=3 && changing>=4 && seats>=1 && departures>=1)score+=100000;
                if(score>bestScore){bestScore=score;chosen=start+offset;predictedPeak=peak;predictedChanges=changing;predictedSeats=seats;predictedDepartures=departures;}
            }
            if(chosen<0)throw new InvalidOperationException("No busy saved visit interval for crowd probe.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_CROWD_PLAN: building="+game.PlayerEnterExit.Interior.EntryDoor.buildingKey+
                "; scannedUntil="+lastStart+"; chosen="+chosen+"; predictedPeakWalking="+predictedPeak+
                "; transitions="+predictedChanges+"; seatEndings="+predictedSeats+"; departures="+predictedDepartures);
            if(predictedPeak<3 || predictedChanges<4 || predictedSeats<1 || predictedDepartures<1)
                throw new InvalidOperationException("Dense crowd coverage unavailable in remaining saved day: peakWalking="+predictedPeak+
                    "; transitions="+predictedChanges+"; seatEndings="+predictedSeats+"; departures="+predictedDepartures);
            if(chosen>clock.ToSeconds())
            {
                game.PauseGame(true);clock.RaiseTime((float)(chosen-clock.ToSeconds()));yield return null;game.PauseGame(false);
                for(int i=0;i<12;i++)yield return null;
            }
            // The arrival camera stands in the exit. Move only the observer to a
            // surveyed main-floor anchor farthest from the saved walking paths.
            var geometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(streets);
            var room=game.PlayerEnterExit.Interior;
            Vector3 observer=Vector3.zero;float best=-1;
            foreach(var local in geometry.LocalAnchors)
            {
                var world=room.transform.TransformPoint(local);
                if(Math.Abs(world.y-(game.PlayerMotor.transform.position.y-1))>0.5f || !geometry.EntryGround.HasValue || Vector3.Distance(world,geometry.EntryGround.Value)<3)continue;
                float clearance=10;
                foreach(var journey in journeys.Values)for(int leg=0;leg<journey.LegCount;leg++)
                {
                    var route=journey.LegRoute(leg);
                    for(int i=1;i<route.Count;i++)
                    {
                        var a=new Vector3((float)route[i-1].X,(float)route[i-1].Y,(float)route[i-1].Z);
                        var b=new Vector3((float)route[i].X,(float)route[i].Y,(float)route[i].Z);
                        var delta=b-a;float fraction=delta.sqrMagnitude<0.0001f?0:Mathf.Clamp01(Vector3.Dot(local-a,delta)/delta.sqrMagnitude);
                        clearance=Math.Min(clearance,Vector3.Distance(local,a+delta*fraction));
                    }
                }
                foreach(var body in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if(body.gameObject.activeInHierarchy)clearance=Math.Min(clearance,Vector3.Distance(world,body.transform.position-Vector3.up));
                if(clearance>best){best=clearance;observer=world;}
            }
            if(best<0.8f)throw new InvalidOperationException("No unobstructive crowd observer position.");
            game.PlayerMotor.transform.position=observer+Vector3.up;Physics.SyncTransforms();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_CROWD_WINDOW: gameTime="+clock.ToSeconds()+"; observer="+observer+"; pathClearance="+best+"; predictedPeakWalking="+predictedPeak+"; score="+bestScore+"; realSeconds="+ObserveSeconds);
            var samples=new Dictionary<string,Sample>();var moved=new HashSet<string>();
            var visits=(Dictionary<string,PhysicalShopVisit>)typeof(StreetPopulation).GetField("physicalShopVisits",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(streets);
            float finish=Time.realtimeSinceStartup+ObserveSeconds;int peakWalking=0,seatEgress=0,departed=0;float concurrentSeconds=0,crossingSeconds=0,lastSample=Time.realtimeSinceStartup;
            while(Time.realtimeSinceStartup<finish)
            {
                float now=Time.realtimeSinceStartup;int walkingNow=0;
                var crossingPositions=new List<Vector3>();var crossingDirections=new List<Vector3>();
                foreach(var pair in visits)
                {
                    var actor=streets.FindActor(pair.Key);
                    Sample sample;
                    if(!samples.TryGetValue(pair.Key,out sample))samples.Add(pair.Key,sample=new Sample {ProgressAt=now});
                    bool walking=pair.Value.Snapshot.Phase==ShopVisitPhase.Walking || pair.Value.Snapshot.Phase==ShopVisitPhase.Leaving;
                    var physical=(ShopPhysicalProgress)ProgressField.GetValue(pair.Value);
                    if(!actor || !actor.gameObject.activeInHierarchy)
                    {
                        if(!walking){sample.MissingAt=0;continue;}
                        if(sample.MissingAt==0)sample.MissingAt=now;
                        if(now-sample.MissingAt>10)throw new InvalidOperationException("Active walking visit lost its body: "+pair.Key);
                        sample.HasBody=false;continue;
                    }
                    sample.MissingAt=0;
                    var local=room.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
                    var point=pair.Value.Snapshot.Position;
                    var target=point==null?local:new Vector3((float)point.X,(float)point.Y,(float)point.Z);
                    float distance=Vector3.Distance(local,target);
                    bool legChanged=sample.Leg>=0 && physical.Leg>sample.Leg;
                    if(legChanged){sample.CompletedLegs++;moved.Add(pair.Key);sample.ProgressAt=now;}
                    if(sample.HadSeat && pair.Value.SelectedSeatId==null){seatEgress++;sample.HadSeat=false;}
                    if(pair.Value.SelectedSeatId!=null && actor.IsSeated)sample.HadSeat=true;
                    if(pair.Value.Snapshot.Phase==ShopVisitPhase.Leaving)sample.SawLeaving=true;
                    if(!sample.HasBody || sample.Leg!=physical.Leg || !sample.WasWalking)
                    {sample.HasBody=true;sample.Position=actor.transform.position;sample.ProgressAt=now;sample.BestDistance=distance;sample.Target=target;}
                    sample.Leg=physical.Leg;
                    if(!walking || actor.IsConversing)
                    {sample.Position=actor.transform.position;sample.ProgressAt=now;sample.WasWalking=false;continue;}
                    walkingNow++;sample.WasWalking=true;
                    var displacement=actor.transform.position-sample.Position;displacement.y=0;
                    if(displacement.magnitude>=0.05f)
                    {crossingPositions.Add(actor.transform.position);crossingDirections.Add(displacement.normalized);}
                    sample.Position=actor.transform.position;
                    // Target changes alone grant no progress. A repeated lateral
                    // oscillation cannot reset the clock by accumulating distance.
                    if(Vector3.Distance(sample.Target,target)>0.25f)
                    {sample.Target=target;sample.BestDistance=distance;}
                    else if(sample.BestDistance-distance>=0.25f)
                    {moved.Add(pair.Key);sample.BestDistance=distance;sample.ProgressAt=now;}
                    sample.Worst=Math.Max(sample.Worst,now-sample.ProgressAt);
                }
                peakWalking=Math.Max(peakWalking,walkingNow);
                if(walkingNow>=3)concurrentSeconds+=now-lastSample;
                bool crossing=false;
                for(int i=0;i<crossingPositions.Count;i++)for(int j=i+1;j<crossingPositions.Count;j++)
                    if(Vector3.Distance(crossingPositions[i],crossingPositions[j])<2.2f && Vector3.Dot(crossingDirections[i],crossingDirections[j])<0.7f)crossing=true;
                if(crossing)crossingSeconds+=now-lastSample;
                lastSample=now;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            int stalled=0;
            foreach(var pair in samples)
            {
                PhysicalShopVisit visit;visits.TryGetValue(pair.Key,out visit);
                if(visit==null && pair.Value.SawLeaving)departed++;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_CROWD_PERSON: "+pair.Key+"; worstWalkingStall="+pair.Value.Worst+"; completedLegs="+pair.Value.CompletedLegs+"; lastFailure="+(visit==null?"departed":visit.LastFailure));
                if(pair.Value.Worst>45)stalled++;
            }
            string coverage="patrons="+samples.Count+"; moving="+moved.Count+"; peakWalking="+peakWalking+"; concurrent3Seconds="+concurrentSeconds+"; crossingSeconds="+crossingSeconds+"; seatEgress="+seatEgress+"; departed="+departed+"; stalledOver45s="+stalled;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_CROWD_COVERAGE: "+coverage);
            if(samples.Count<6 || moved.Count<4 || peakWalking<3 || concurrentSeconds<3 || crossingSeconds<0.5f || seatEgress<1 || departed<1 || stalled>0)
                throw new InvalidOperationException("Dense continuous inn progress failed or lacked coverage: "+coverage);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_CROWD_PASS: "+ObserveSeconds+" real seconds at normal clock; "+coverage+"; target progress and completed legs measured; dwell/seated/chat excluded.");
        }
    }
}
