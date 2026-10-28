using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit disposable-fixture check. Ordinary production allocation, routes,
    // shift changes and bed reservations supply every guard destination.
    internal static partial class GuardShiftProbe
    {
        internal static IEnumerator Run()
        {
            var game=GameManager.Instance;
            if(!NativeProbeText.IsTestCharacter(game.PlayerEntity.Name))throw new InvalidOperationException("Guard probe requires an owned fixture.");
            bool previous=game.PlayerEntity.GlobalVars.GetGlobalVar(0);
            bool haunted=Environment.GetEnvironmentVariable("POTIB_GUARD_HAUNTING")=="1";
            game.PlayerEntity.GlobalVars.SetGlobalVar(0,!haunted);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_PROBE_CURSE: controlledFixture=true; originalLifted="+previous+"; testingLifted="+!haunted);
            try {if(haunted)yield return RunHaunted();else yield return RunUnhaunted();}
            finally {game.PlayerEntity.GlobalVars.SetGlobalVar(0,previous);}
        }

        internal static IEnumerator RunWallLaw(){return RunFocused("wall-law");}
        internal static IEnumerator RunWallNonPlayer(){return RunFocused("wall-npc");}
        internal static IEnumerator RunNightWallLaw(){return RunFocused("wall-law-night");}
        internal static IEnumerator RunSleep(){return RunFocused("guard-sleep");}
        internal static IEnumerator RunShiftChange(){return RunFocused("guard-shift-change");}

        private static IEnumerator RunFocused(string mode)
        {
            var game=GameManager.Instance;
            if(!NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Focused guard probe requires an owned outdoor fixture.");
            bool previous=game.PlayerEntity.GlobalVars.GetGlobalVar(0);
            game.PlayerEntity.GlobalVars.SetGlobalVar(0,true);
            try
            {
                var controller=UnityEngine.Object.FindObjectOfType<GuardPopulation>();
                if(!controller)throw new InvalidOperationException("Guard population controller is absent.");
                controller.DayPatrols=true;controller.NightPatrols=true;game.PlayerEntity.GodMode=true;
                float deadline=Time.realtimeSinceStartup+30;
                while(controller.Roster==null && Time.realtimeSinceStartup<deadline)yield return null;
                if(controller.Roster==null)throw new InvalidOperationException("Focused guard probe has no ordinary roster.");
                if(mode=="wall-law-night")yield return VerifyOneNightWall(controller,deadline);
                else if(mode=="wall-law" || mode=="wall-npc")
                {
                    {
                        while(!controller.Roster.Guards.Exists(g=>g.IsAlive && GuardShifts.OnDuty(g,Now()) &&
                            g.Refuge!=null && g.Refuge.WallRoute!=null && g.Refuge.Evaluate(Now()).Phase==RefugePhase.WallPosted) &&
                            Time.realtimeSinceStartup<deadline)yield return null;
                        if(!controller.Roster.Guards.Exists(g=>g.IsAlive && GuardShifts.OnDuty(g,Now()) &&
                            g.Refuge!=null && g.Refuge.WallRoute!=null && g.Refuge.Evaluate(Now()).Phase==RefugePhase.WallPosted))
                            throw new InvalidOperationException("No actual current-shift wall post reached for focused arrest check.");
                        if(mode=="wall-npc")yield return GuardWallTrespassProbe.RunNonPlayer(controller);
                        else yield return GuardWallTrespassProbe.Run(controller);
                    }
                    if(mode=="wall-law")Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_LAW_PASS: shift="+GuardShifts.At(Now())+
                        "; actualReachedPost=true; unchangedFixtureClock=true; fullGarrisonPrerequisite=false");
                }
                else if(mode=="guard-sleep")
                    yield return VerifySleep(controller,game.StreamingWorld.GetCurrentBuildingDirectory(),Mathf.Max(0,deadline-Time.realtimeSinceStartup),true);
                else
                {
                    if(GuardShifts.At(Now())!=GuardShift.Day)
                        throw new InvalidOperationException("Focused shift-change requires its prepared actual day save.");
                    var dayIds=new HashSet<string>();
                    foreach(var guard in controller.Roster.Guards)if(GuardShifts.OnDuty(guard,Now()))dayIds.Add(guard.Id);
                    var geometry=Field<TownGeometry>(controller,"geometry");
                    foreach(var shift in new[]{GuardShift.Night,GuardShift.Day})
                    {
                        RaiseHour(shift==GuardShift.Night?20:7);deadline=Time.realtimeSinceStartup+30;
                        GuardActor body=null;GuardRecord replacement=null;
                        while(Time.realtimeSinceStartup<deadline)
                        {
                            if(replacement==null)replacement=controller.Roster.Guards.Find(g=>GuardShifts.OnDuty(g,Now()) && g.Duty==GuardDuty.Posted &&
                                g.Refuge==null && g.Interior==null && controller.Roster.IsOnPatrol(g.Id));
                            if(replacement!=null)
                            {
                                var point=controller.Roster.Position(replacement.Id,Now());
                                game.PlayerMotor.transform.position=geometry.ScenePoint(new RoutePoint(point.X,point.Z))+Vector3.up+Vector3.forward*4;
                                yield return null;body=Find(replacement.Id);
                                // Native torch budgeting updates every 0.25 s.
                                // Keep this one real replacement and the same
                                // deadline while its actual light catches up.
                                var torch=body?body.GetComponentInChildren<Light>():null;
                                if(body && (shift!=GuardShift.Night || torch && torch.enabled))break;
                            }
                            yield return null;
                        }
                        if(!body || replacement==null)throw new InvalidOperationException("Focused shift change has no actual replacement patrol body within30seconds: "+shift);
                        if((shift==GuardShift.Night)==dayIds.Contains(replacement.Id))
                            throw new InvalidOperationException("Actual replacement body has the wrong day/night identity.");
                        if(shift==GuardShift.Night)
                        {
                            if(!body.GetComponentInChildren<Light>() || !body.GetComponentInChildren<Light>().enabled)
                                throw new InvalidOperationException("Actual replacement night patrol lacks its lit torch.");
                            foreach(var post in controller.GateGuardDutySnapshots(Now()))
                                if(post.GateOpen)throw new InvalidOperationException("Native gate remained open at20:00.");
                        }
                    }
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_SHIFT_CHANGE_PASS: differentDayNightIdentities=true; actualReplacementBodies=true; nightTorch=true; nativeNightGatesClosed=true; dayReturn=true; fullGarrisonPrerequisite=false");
                }
            }
            finally{game.PlayerEntity.GlobalVars.SetGlobalVar(0,previous);}
        }

        private static IEnumerator RunUnhaunted()
        {
            var game = GameManager.Instance;
            if (!NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Guard shift probe requires an owned outdoor fixture.");
            var controller = UnityEngine.Object.FindObjectOfType<GuardPopulation>();
            if (!controller) throw new InvalidOperationException("Guard population controller is absent.");
            controller.DayPatrols = true; controller.NightPatrols = true;
            game.PlayerEntity.GodMode = true;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            RaiseHour(12);
            yield return WaitForShift(controller, GuardShift.Day);
            var roster = controller.Roster;
            var geometry = Field<TownGeometry>(controller,"geometry");
            var streets = UnityEngine.Object.FindObjectOfType<StreetPopulation>();
            var population = Field<SettlementPopulation>(streets,"population");
            int residents = 0;
            foreach (var home in population.Households) residents += home.Residents.Count;
            int gates = controller.GateGuardDutySnapshots(Now()).Count / 4;
            int expected = GuardRoster.TargetWithWallSlots(GuardRoster.TargetCount(
                SettlementClassification.FromLocationType((int)directory.LocationData.MapTableData.LocationType),residents),gates,controller.WallWatchPostCount);
            if (roster.Target != expected) throw new InvalidOperationException("Native garrison quota differs from census and gate geometry: expected="+expected+"; actual="+roster.Target);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_SHIFT_CENSUS: residents="+residents+"; gates="+gates+"; target="+expected);
            yield return SurveyWallAccess(directory,geometry,roster);
            yield return GuardWallTrespassProbe.Run(controller);

            yield return VerifySleep(controller,directory);
            yield return VerifyNightShiftAndReturns(controller,directory,geometry);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_SHIFT_PASS: dayAndNightPatrols=true; fixedPosts=true; nativeBedSleep=true; wakeAndReturn=true; nightTorch=true; censusQuota=true");
        }

        private static IEnumerator VerifySleep(GuardPopulation controller,BuildingDirectory directory,float readinessSeconds=150,bool preparedOnly=false)
        {
            var game=GameManager.Instance;var roster=controller.Roster;
            float deadline = Time.realtimeSinceStartup + readinessSeconds;
            GuardRecord sleeper = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                sleeper = roster.Guards.Find(g => g.IsAlive && !GuardShifts.OnDuty(g,Now()) &&
                    g.Refuge != null && (g.Refuge.RestPending || g.Refuge.RestBedRecord.HasValue));
                if (sleeper != null) break;
                yield return null;
            }
            if (sleeper == null) throw new InvalidOperationException("No off-duty guard obtained a reachable quarter through ordinary rest planning.");
            int sleepingBuilding = sleeper.Refuge.BuildingKey;
            while (sleeper.Refuge.Evaluate(Now()).Phase != RefugePhase.Sheltered)
            {
                if(preparedOnly && Time.realtimeSinceStartup>=deadline)
                    throw new InvalidOperationException("Prepared guard sleep scene has no actually reached quarter within30seconds.");
                if (Now() >= sleeper.Refuge.ReleasedAt.Value) throw new InvalidOperationException("Guard rest never reached its reserved bed.");
                if(!preparedOnly)DaggerfallUnity.Instance.WorldTime.Now.RaiseTime(30);
                yield return null;
            }
            DaggerfallStaticDoors selectedOwner = null; StaticDoor selected = default(StaticDoor);
            foreach (var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach (var door in owner.Doors ?? new StaticDoor[0])
                    if (door.doorType == DoorTypes.Building && door.buildingKey == sleepingBuilding)
                    { selectedOwner = owner; selected = door; break; }
            if (!selectedOwner) throw new InvalidOperationException("Reserved wall bed has no native building door.");
            TownSmokeHarness.EnterInterior(selectedOwner.transform,selected);
            if(!preparedOnly)deadline = Time.realtimeSinceStartup + 30;
            GuardActor body = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                body = Find(sleeper.Id);
                if (body && body.IsSleeping) break;
                yield return null;
            }
            var interior = game.PlayerEnterExit.Interior;
            if (!interior || !body || !body.IsSleeping) throw new InvalidOperationException("Guard did not render the matching CityWatch sleeping pose in its real bed.");
            // Native concealment updates restore visibility every frame. Check only
            // after two complete rendered frames, not in the initial BeginGuard tick.
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            var bed = BedGeometry.Read(interior).Find(b => b.RecordIndex == sleeper.Refuge.RestBedRecord.Value);
            if (bed == null)
                foreach (var furnished in interior.GetComponentsInChildren<QuarterBed>())
                    if (furnished.Anchor.RecordIndex == sleeper.Refuge.RestBedRecord.Value) { bed = furnished.Anchor; break; }
            if (bed == null || Vector3.Distance(body.transform.position,interior.transform.TransformPoint(bed.LocalCenter)+Vector3.up*.18f) > .03f)
                throw new InvalidOperationException("Sleeping guard is not aligned with the actual mattress.");
            if (body.GetComponentInChildren<MobileUnit>().GetComponent<Renderer>().enabled)
                throw new InvalidOperationException("Standing CityWatch sprite remains visible while sleeping.");
            int visible=0,blankets=0;
            foreach (var renderer in body.GetComponentsInChildren<Renderer>())
                if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                { visible++; if (renderer.gameObject.name == "Sleeping blanket") blankets++; }
            if (visible != 1 || blankets != 1)
                throw new InvalidOperationException("Sleeping guard must have exactly one visible blanket and no standing sprite: visible="+visible+"; blankets="+blankets);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_SLEEP_PASS: guard="+sleeper.Id+"; building="+sleepingBuilding+
                "; nativeBed="+bed.RecordIndex+"; art=399/15; reservedBedAlignment=true; renderedFrames=2; visibleBlankets="+blankets+"; visibleRenderers="+visible);
            if(preparedOnly)yield break;
            var copy = GuardRoster.CopyValidated(roster);
            if (copy.Guards.Find(g => g.Id == sleeper.Id).Refuge.RestBedRecord != bed.RecordIndex)
                throw new InvalidOperationException("Reserved bed identity did not survive roster serialization validation.");
            double release = sleeper.Refuge.ReleasedAt.Value;
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)Math.Max(0,release + 1 - Now()));
            yield return new WaitForSecondsRealtime(.5f);
            if (body && body.IsSleeping) throw new InvalidOperationException("Guard remained asleep after scheduled departure for its next shift.");
            if (sleeper.Refuge == null || sleeper.Refuge.Evaluate(Now()).Phase != RefugePhase.Exiting)
                throw new InvalidOperationException("Waking guard skipped the physical interior return route.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_WAKE_PASS: guard="+sleeper.Id+"; physicalInteriorExit=true; earlyReturn=true");
            game.PlayerEnterExit.TransitionExterior();
            yield return new WaitForSecondsRealtime(2);
        }

        private static IEnumerator VerifyNightShiftAndReturns(GuardPopulation controller,BuildingDirectory directory,TownGeometry geometry)
        {
            var game=GameManager.Instance;
            RaiseHour(20);
            yield return WaitForShift(controller, GuardShift.Night);
            yield return GuardWallTrespassProbe.Run(controller);
            var night = controller.Roster.Guards.Find(g => g.IsAlive && GuardShifts.OnDuty(g,Now()) && controller.Roster.IsOnPatrol(g.Id));
            var nightPoint = controller.Roster.Position(night.Id,Now());
            game.PlayerMotor.transform.position = geometry.ScenePoint(new RoutePoint(nightPoint.X,nightPoint.Z)) + Vector3.up + Vector3.forward*4;
            yield return new WaitForSecondsRealtime(1.5f);
            var nightBody = Find(night.Id);
            if (!nightBody || !nightBody.GetComponentInChildren<Light>() || !nightBody.GetComponentInChildren<Light>().enabled)
                throw new InvalidOperationException("Night patrol has no lit carried torch near the observer.");
            foreach (var post in controller.GateGuardDutySnapshots(Now()))
                if (post.GateOpen) throw new InvalidOperationException("Native gate remained open at20:00.");
            yield return VerifyWallReturn(controller,directory);
            RaiseHour(7);
            yield return WaitForShift(controller, GuardShift.Day);
            yield return VerifyPhysicalGateRecall(controller);
        }

        private static IEnumerator WaitForShift(GuardPopulation controller, GuardShift shift,bool requireAllWallArrivals=true)
        {
            float hardDeadline=Time.realtimeSinceStartup+900;
            float deadline = Time.realtimeSinceStartup + (requireAllWallArrivals?420:180);
            float nextReport=Time.realtimeSinceStartup+15;
            string lastCounts="roster not ready";
            while (Time.realtimeSinceStartup < deadline)
            {
                var roster = controller.Roster;
                if (roster != null && controller.GateDutySurveyComplete)
                {
                    int activePairs = 0, fixedPosts = 0, rest = 0, staff = 0, wallPosts = 0;
                    foreach (var pair in roster.Patrols)
                        if (!pair.InterruptedAt.HasValue && roster.Guards.Exists(g => g.Id == pair.Plan.LeaderId && g.Shift == shift)) activePairs++;
                    foreach (var guard in roster.Guards)
                    {
                        if (GuardShifts.OnDuty(guard,Now()))
                        {
                            staff++;
                            if(requireAllWallArrivals && guard.Refuge!=null && guard.Refuge.WallRoute!=null)
                                deadline=Math.Min(hardDeadline,Math.Max(deadline,Time.realtimeSinceStartup+
                                    (float)WallArrivalRemaining(guard.Refuge,Now())+30));
                            if(guard.Refuge!=null && guard.Refuge.WallRoute!=null && guard.Refuge.Evaluate(Now()).Phase==RefugePhase.WallPosted)wallPosts++;
                            if (guard.Duty == GuardDuty.Posted && guard.Refuge==null && !roster.IsOnPatrol(guard.Id) && !controller.IsGateGuard(guard.Id)) fixedPosts++;
                        }
                        if (guard.Refuge != null && guard.Refuge.RestBedRecord.HasValue) rest++;
                    }
                    int gatesArrived = 0;
                    var gates = controller.GateGuardDutySnapshots(Now());
                    foreach (var gate in gates)
                    {
                        var guard = roster.Guards.Find(g => g.Id == gate.GuardId);
                        if (guard != null && GuardShifts.OnDuty(guard,Now()) && guard.Duty == GuardDuty.Posted &&
                            guard.Refuge == null && Math.Abs(gate.Position.X-gate.Target.X)<.4 && Math.Abs(gate.Position.Z-gate.Target.Z)<.4)
                            gatesArrived++;
                    }
                    int wantedPairs = GuardShifts.PatrolPairTarget(Math.Max(3,Field<int>(controller,"baseGuardTarget")),controller.MaximumPatrolPairs);
                    int wantedWall=controller.WallWatchPostCount;
                    lastCounts="shift="+shift+"; onDuty="+staff+"; patrolPairs="+activePairs+"/"+wantedPairs+
                        "; fixedTownPosts="+fixedPosts+"; arrivedGatePosts="+gatesArrived+"/"+gates.Count+
                        "; postedWallArchers="+wallPosts+"/"+wantedWall+"; reservedBeds="+rest;
                    if(Time.realtimeSinceStartup>=nextReport)
                    {
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_SHIFT_WAIT: "+lastCounts);
                        foreach(var guard in roster.Guards)
                            if(requireAllWallArrivals && guard.Refuge!=null && guard.Refuge.WallRoute!=null)
                                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_WATCH_PROGRESS: guard="+guard.Id+"; phase="+guard.Refuge.Evaluate(Now()).Phase+
                                    "; plannedArrivalRealSeconds="+WallArrivalRemaining(guard.Refuge,Now()));
                        foreach(var gate in gates)
                        {
                            var assigned=roster.Guards.Find(g=>g.Id==gate.GuardId);
                            if(assigned==null || !GuardShifts.OnDuty(assigned,Now()) || assigned.Duty!=GuardDuty.Posted || assigned.Refuge!=null)
                                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GATE_SHIFT_PENDING: gate="+gate.GateIndex+"; exterior="+gate.ExteriorAssignment+
                                    "; guard="+gate.GuardId+"; duty="+gate.Duty+"; arrivalRemaining="+(gate.ArrivesAt-Now())+
                                    "; refuge="+(assigned!=null && assigned.Refuge!=null ? assigned.Refuge.Evaluate(Now()).Phase.ToString() : "none"));
                        }
                        nextReport=Time.realtimeSinceStartup+15;
                    }
                    int expectedFixed=staff-2*wantedPairs-gates.Count-wantedWall;
                    if (GuardShifts.At(Now()) == shift && activePairs == wantedPairs && fixedPosts == expectedFixed && fixedPosts > 0 && gatesArrived == gates.Count && wantedWall>=16 && (!requireAllWallArrivals || wallPosts==wantedWall))
                    {
                        yield return VerifyGateBodies(controller,gates);
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_SHIFT_COUNTS: shift="+shift+"; roster="+roster.Guards.Count+
                            "; onDuty="+staff+"; patrolPairs="+activePairs+"; fixedTownPosts="+fixedPosts+"; gatePosts="+gatesArrived+
                            "; gateSlots="+gates.Count+"; wallArchers="+wallPosts+"; reservedBeds="+rest);
                        yield break;
                    }
                }
                yield return null;
            }
            throw new InvalidOperationException("Ordinary guard shift did not settle: "+lastCounts);
        }

        private static double WallArrivalRemaining(GuardRefugeJourney plan,double now)
        {
            double length=0;
            foreach(var route in new[]{plan.Exterior,plan.Interior,plan.WallRoute})
                for(int i=1;i<route.Count;i++)
                {var a=route[i-1];var b=route[i];double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;length+=Math.Sqrt(x*x+y*y+z*z);}
            return Math.Max(0,(plan.StartsAt+length*plan.SecondsPerMetre-now)/DaggerfallUnity.Instance.WorldTime.TimeScale);
        }

        private static IEnumerator RunHaunted()
        {
            var game=GameManager.Instance;
            if(game.PlayerEnterExit.IsPlayerInside)throw new InvalidOperationException("Haunting shelter test requires the outdoor fixture.");
            game.PlayerEntity.GodMode=true;
            var controller=UnityEngine.Object.FindObjectOfType<GuardPopulation>();
            RaiseHour(12);
            yield return WaitForShift(controller,GuardShift.Day);
            int wallPosts=controller.WallWatchPostCount;
            var interrupted=controller.Roster.Guards.Find(g=>g.IsAlive && g.Duty==GuardDuty.Posted && g.Refuge==null &&
                !controller.IsGateGuard(g.Id) && !controller.Roster.IsOnPatrol(g.Id));
            if(interrupted==null)throw new InvalidOperationException("Haunting override fixture needs an actual posted guard.");
            var origin=controller.Roster.Position(interrupted.Id,Now());
            controller.Roster.Dispatch("haunting-shelter-controlled-incident",Now(),new Dictionary<string,List<SavedRoutePoint>>{
                {interrupted.Id,new List<SavedRoutePoint>{origin,origin}}},1,1.3,DaggerfallUnity.Instance.WorldTime.TimeScale);
            controller.Roster.Engage(interrupted.Id,Now());
            RaiseHour(18);
            float deadline=Time.realtimeSinceStartup+900,nextReport=0;
            int alive=0,inside=0;
            while(Time.realtimeSinceStartup<deadline)
            {
                alive=inside=0;
                foreach(var guard in controller.Roster.Guards)
                {
                    if(!guard.IsAlive)continue;
                    alive++;
                    if(guard.Refuge!=null && guard.Refuge.HauntingShelter && guard.Refuge.WallRoute==null &&
                        guard.Refuge.Evaluate(Now()).Phase==RefugePhase.Sheltered)inside++;
                }
                if(alive>0 && alive==inside)break;
                if(Time.realtimeSinceStartup>=nextReport)
                {
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_HAUNTING_WAIT: physicallyInside="+inside+"; alive="+alive);
                    nextReport=Time.realtimeSinceStartup+20;
                }
                yield return null;
            }
            if(inside!=alive)throw new InvalidOperationException("Haunting shelter failed to bring every guard inside actual quarters: "+inside+"/"+alive);
            RaiseHour(20);yield return new WaitForSecondsRealtime(2);
            foreach(var guard in controller.Roster.Guards)
                if(guard.IsAlive && (!GuardActor.RefugeIndoors(guard,Now()) || guard.Refuge.WallRoute!=null || controller.Roster.IsOnPatrol(guard.Id)))
                    throw new InvalidOperationException("Haunted night retained an exterior guard or wall archer.");
            foreach(var post in controller.GateGuardDutySnapshots(Now()))
                if(!string.IsNullOrEmpty(post.GuardId))throw new InvalidOperationException("Haunted night retained a gate assignment.");
            var selected=controller.Roster.Guards.Find(g=>g.IsAlive && g.Refuge!=null && g.Refuge.HauntingShelter);
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            DaggerfallStaticDoors doorOwner=null;StaticDoor entrance=default(StaticDoor);float lowest=float.MaxValue;
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var raw in owner.Doors ?? new StaticDoor[0])
                    if(raw.doorType==DoorTypes.Building && raw.buildingKey==selected.Refuge.BuildingKey)
                    {
                        var door=raw;door.ownerPosition=owner.transform.position;door.ownerRotation=owner.transform.rotation;
                        float height=DaggerfallStaticDoors.GetDoorPosition(door).y;
                        if(height<lowest){lowest=height;doorOwner=owner;entrance=raw;}
                    }
            if(!doorOwner)throw new InvalidOperationException("Haunting quarter has no actual native door.");
            TownSmokeHarness.EnterInterior(doorOwner.transform,entrance);
            yield return new WaitForSecondsRealtime(2);
            var body=Find(selected.Id);
            if(!body || !body.IsInterior)throw new InvalidOperationException("Sheltered guard lacks its real body inside the native wall quarter.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_HAUNTING_SHELTER_PASS: allGuards="+alive+"; inside="+inside+
                "; exteriorPatrols=0; wallArchersOutside=0; interruptedEngagementGuard="+interrupted.Id+
                "; actualInteriorBody=true; building="+selected.Refuge.BuildingKey);
            game.PlayerEnterExit.TransitionExterior();yield return new WaitForSecondsRealtime(1);
            RaiseHour(5);yield return new WaitForSecondsRealtime(1);
            foreach(var guard in controller.Roster.Guards)
                if(guard.IsAlive && (!GuardActor.RefugeIndoors(guard,Now()) || !guard.Refuge.HauntingShelter))
                    throw new InvalidOperationException("Guard left haunting shelter before the 06:00 duty boundary.");
            RaiseHour(6);yield return new WaitForSecondsRealtime(1);
            foreach(var guard in controller.Roster.Guards)
                if(guard.IsAlive && guard.Refuge!=null && guard.Refuge.HauntingShelter)
                    throw new InvalidOperationException("Guard haunting order remained active after 06:00.");
            game.PlayerEntity.GlobalVars.SetGlobalVar(0,true);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_HAUNTING_PASS: unresolvedAllInside=true; heldThrough05=true; dawnOrderReleased=true; normalShiftCoverage=separateUnhauntedProbe; verifiedWallPosts="+wallPosts);
        }

        private static IEnumerator VerifyGateBodies(GuardPopulation controller,List<GateGuardDutySnapshot> gates)
        {
            var geometry=Field<TownGeometry>(controller,"geometry");
            int previousGate=-1,verified=0;
            foreach(var post in gates)
            {
                if(post.GateIndex!=previousGate)
                {
                    GameManager.Instance.PlayerMotor.transform.position=geometry.ScenePoint(new RoutePoint(post.Target.X,post.Target.Z))+Vector3.up+Vector3.forward*3;
                    yield return new WaitForSecondsRealtime(1);
                    previousGate=post.GateIndex;
                }
                var actor=Find(post.GuardId);
                if(!actor || actor.IsInterior || Vector3.Distance(actor.GroundPosition,geometry.ScenePoint(new RoutePoint(post.Target.X,post.Target.Z)))>.2)
                    throw new InvalidOperationException("Settled gate assignment lacks its real body at the surveyed post: "+post.GuardId);
                verified++;
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_GATE_BODIES_PASS: actualBodiesAtPosts="+verified);
        }

        private static IEnumerator VerifyPhysicalGateRecall(GuardPopulation controller)
        {
            var roster=controller.Roster;
            var guard=roster.Guards.Find(g=>GuardShifts.OnDuty(g,Now()) && g.PostRecall!=null && g.Duty==GuardDuty.Posted);
            if(guard==null)throw new InvalidOperationException("No outside gate guard has an armed pre-close physical recall.");
            var recall=guard.PostRecall;var geometry=Field<TownGeometry>(controller,"geometry");
            var from=geometry.ScenePoint(new RoutePoint(recall.Route[0].X,recall.Route[0].Z));
            var last=recall.Route[recall.Route.Count-1];
            var inside=geometry.ScenePoint(new RoutePoint(last.X,last.Z));
            GameManager.Instance.PlayerMotor.transform.position=inside+Vector3.up+Vector3.forward*3;
            yield return new WaitForSecondsRealtime(1);
            var body=Find(guard.Id);
            if(!body || Vector3.Distance(body.GroundPosition,from)>.2)throw new InvalidOperationException("Outside recall guard has not physically reached its starting post.");
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)Math.Max(0,recall.DepartsAt-1-Now()));
            yield return null;
            Vector3 previous=body.GroundPosition;float sampledAt=Time.realtimeSinceStartup,deadline=Time.realtimeSinceStartup+60;
            double walked=0;
            while(Vector3.Distance(body.GroundPosition,inside)>.55f && Time.realtimeSinceStartup<deadline)
            {
                yield return null;
                if(!body || Find(guard.Id)!=body)throw new InvalidOperationException("Pre-close recall lost its physical gate guard.");
                float step=Vector3.Distance(previous,body.GroundPosition);
                double quantum=1.3/DaggerfallUnity.Instance.WorldTime.TimeScale;
                if(step>1.8f*(Time.realtimeSinceStartup-sampledAt)+quantum+.05)
                    throw new InvalidOperationException("Pre-close guard return skipped physical movement.");
                walked+=step;previous=body.GroundPosition;sampledAt=Time.realtimeSinceStartup;
            }
            if(Vector3.Distance(body.GroundPosition,inside)>.55f || Now()>=recall.ClosesAt)
                throw new InvalidOperationException("Outside guard failed to physically reach inside staging before native closure.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_PHYSICAL_RECALL_PASS: guard="+guard.Id+"; metres="+walked+
                "; sameBody=true; beforeNativeClosure=true; remainingGameSeconds="+(recall.ClosesAt-Now()));
        }

        private static IEnumerator VerifyWallReturn(GuardPopulation controller,BuildingDirectory directory)
        {
            var game=GameManager.Instance;
            var guard=controller.Roster.Guards.Find(g=>GuardShifts.OnDuty(g,Now()) && g.Refuge!=null && g.Refuge.WallRoute!=null &&
                g.Refuge.Evaluate(Now()).Phase==RefugePhase.WallPosted);
            if(guard==null)throw new InvalidOperationException("No on-duty wall archer reached an actual elevated post.");
            var plan=guard.Refuge;
            if(!guard.HasBow || plan.UpperDoorIndex==plan.LowerDoorIndex)throw new InvalidOperationException("Wall role lacks a bow or distinct native door identities.");
            var top=plan.WallRoute[plan.WallRoute.Count-1];
            var geometry=Field<TownGeometry>(controller,"geometry");
            game.PlayerMotor.transform.position=geometry.ScenePoint(new RoutePoint(top.X,top.Z))+Vector3.up;
            yield return new WaitForSecondsRealtime(.5f);
            var body=Find(guard.Id);
            var surveyedPost=directory.transform.TransformPoint(new Vector3((float)top.X,(float)top.Y,(float)top.Z));
            if(!body || body.IsInterior || Vector3.Distance(body.GroundPosition,surveyedPost)>.15)
                throw new InvalidOperationException("Wall guard was projected to terrain instead of its elevated surveyed post.");
            var entity=body.GetComponent<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>();
            bool nativeBow=false;
            for(int i=0;i<entity.Entity.Items.Count;i++)
            {
                var item=entity.Entity.Items.GetItem(i);
                if(item.ItemGroup==DaggerfallWorkshop.Game.Items.ItemGroups.Weapons && item.TemplateIndex==(int)DaggerfallWorkshop.Game.Items.Weapons.Long_Bow)nativeBow=true;
            }
            var equipped=entity.Entity.ItemEquipTable.GetItem(DaggerfallWorkshop.Game.Items.EquipSlots.RightHand);
            if(!nativeBow || equipped==null || equipped.TemplateIndex!=(int)DaggerfallWorkshop.Game.Items.Weapons.Long_Bow)
                throw new InvalidOperationException("Wall archer has no equipped native bow.");
            DaggerfallStaticDoors ownerFound=null;StaticDoor upper=default(StaticDoor),lower=default(StaticDoor);
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var door in owner.Doors ?? new StaticDoor[0])
                    if(door.buildingKey==plan.BuildingKey && door.doorType==DoorTypes.Building)
                    {
                        if(door.doorIndex==plan.UpperDoorIndex){upper=door;ownerFound=owner;}
                        if(door.doorIndex==plan.LowerDoorIndex)lower=door;
                    }
            if(!ownerFound)throw new InvalidOperationException("Wall route upper native door disappeared.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_ARCHER_POST_PASS: guard="+guard.Id+"; building="+plan.BuildingKey+
                "; lowerDoor="+plan.LowerDoorIndex+"; upperDoor="+plan.UpperDoorIndex+"; nativeBow=true; elevatedBody=true");
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)Math.Max(0,plan.ReleasedAt.Value+.1-Now()));
            yield return null;
            if(plan.Evaluate(Now()).Phase!=RefugePhase.WallReturning)throw new InvalidOperationException("Wall relief skipped its upper walkway return.");
            float returnDeadline=Time.realtimeSinceStartup+180;
            Vector3 previous=body.GroundPosition;float sampledAt=Time.realtimeSinceStartup;double upperWalk=0;
            while(plan.Evaluate(Now()).Phase==RefugePhase.WallReturning && Time.realtimeSinceStartup<returnDeadline)
            {
                yield return null;
                if(plan.Evaluate(Now()).Phase!=RefugePhase.WallReturning)break;
                if(!body || !body.gameObject.activeInHierarchy || Find(guard.Id)!=body)
                    throw new InvalidOperationException("Wall return replaced or lost its visible upper-walk body.");
                float step=Vector3.Distance(previous,body.GroundPosition);
                if(step>1.8f*(Time.realtimeSinceStartup-sampledAt)+1/plan.SecondsPerMetre+.05)
                    throw new InvalidOperationException("Wall return body skipped physical upper-walk movement.");
                upperWalk+=step;previous=body.GroundPosition;sampledAt=Time.realtimeSinceStartup;
            }
            if(plan.Evaluate(Now()).Phase!=RefugePhase.Exiting)throw new InvalidOperationException("Wall relief skipped the upper door interior transition.");
            TownSmokeHarness.EnterInterior(ownerFound.transform,upper);
            yield return new WaitForSecondsRealtime(.5f);
            body=Find(guard.Id);
            if(!body || !body.IsInterior)throw new InvalidOperationException("Returning wall guard is missing in the real upper-door interior.");
            double startY=body.transform.position.y;
            previous=body.GroundPosition;sampledAt=Time.realtimeSinceStartup;
            while(plan.Evaluate(Now()).Phase==RefugePhase.Exiting && Time.realtimeSinceStartup<returnDeadline)
            {
                var at=plan.Evaluate(Now()).Position;
                if(at.Y<plan.Interior[plan.Interior.Count-1].Y-2)break;
                yield return null;
                if(!body || Find(guard.Id)!=body)throw new InvalidOperationException("Wall stair descent lost its visible interior body.");
                if(Vector3.Distance(previous,body.GroundPosition)>1.8f*(Time.realtimeSinceStartup-sampledAt)+1/plan.SecondsPerMetre+.05)
                    throw new InvalidOperationException("Wall relief body skipped physical interior stair movement.");
                previous=body.GroundPosition;sampledAt=Time.realtimeSinceStartup;
            }
            yield return null;
            if(body && body.transform.position.y>=startY-1)throw new InvalidOperationException("Wall guard did not descend the real interior stair route.");
            var lowerPoint=plan.Interior[0];
            game.PlayerMotor.transform.position=game.PlayerEnterExit.Interior.transform.TransformPoint(new Vector3((float)lowerPoint.X,(float)lowerPoint.Y,(float)lowerPoint.Z))+Vector3.up;
            game.PlayerEnterExit.TransitionExterior();yield return new WaitForSecondsRealtime(.5f);
            var expected=lower;expected.ownerPosition=ownerFound.transform.position;expected.ownerRotation=ownerFound.transform.rotation;
            if(Math.Abs(game.PlayerMotor.transform.position.y-DaggerfallStaticDoors.GetDoorPosition(expected).y)>3)
                throw new InvalidOperationException("Native wall exit did not select the actual lower exterior door.");
            returnDeadline=Time.realtimeSinceStartup+420;
            while(guard.Refuge!=null && Time.realtimeSinceStartup<returnDeadline)yield return null;
            if(guard.Refuge!=null)throw new InvalidOperationException("Wall relief did not complete its surveyed street return.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_RETURN_PASS: guard="+guard.Id+"; upperWalkMetres="+upperWalk+
                "; sameUpperBody=true; boundedFrameMovement=true; nativeUpperDoor=true; interiorDescent=true; nativeLowerExit=true; originalPost=true");
        }

        private static IEnumerator SurveyWallAccess(BuildingDirectory directory,TownGeometry geometry,GuardRoster roster)
        {
            var blocks = new HashSet<DaggerfallRMBBlock>();
            int doors = 0, elevatedDoors = 0, surfaces = 0, sampled = 0, reachable = 0, unreadableMeshes = 0;
            foreach (var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach (var door in owner.Doors ?? new StaticDoor[0])
                {
                    BuildingSummary summary;
                    if (door.doorType != DoorTypes.Building || !directory.GetBuildingSummary(door.buildingKey,out summary) ||
                        summary.BuildingType != DFLocation.BuildingTypes.Town23) continue;
                    doors++;
                    var worldDoor = door; worldDoor.ownerPosition=owner.transform.position; worldDoor.ownerRotation=owner.transform.rotation;
                    var doorPoint = DaggerfallStaticDoors.GetDoorPosition(worldDoor);
                    var localDoor = directory.transform.InverseTransformPoint(doorPoint);
                    float doorRise = doorPoint.y-geometry.ScenePoint(new RoutePoint(localDoor.x,localDoor.z)).y;
                    if (doorRise > 3)
                    {
                        elevatedDoors++;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_UPPER_DOOR: building="+door.buildingKey+"; model="+summary.ModelID+
                            "; rise="+doorRise+"; point="+doorPoint);
                    }
                    var block = owner.GetComponentInParent<DaggerfallRMBBlock>();
                    if (block) blocks.Add(block);
                }
            NavMeshHit ground;
            var posted = roster.Guards.Find(g => g.IsAlive && g.Duty == GuardDuty.Posted && g.Refuge == null);
            if (posted == null || !NavMesh.SamplePosition(geometry.ScenePoint(new RoutePoint(posted.Post.X,posted.Post.Z)),out ground,.75f,NavMesh.AllAreas))
                throw new InvalidOperationException("Wall access audit needs a real reachable street post.");
            var unique = new List<Vector3>();
            foreach (var block in blocks)
                foreach (var collider in block.GetComponentsInChildren<MeshCollider>())
                {
                    if (!collider.enabled || !collider.sharedMesh) continue;
                    if (!collider.sharedMesh.isReadable) { unreadableMeshes++; continue; }
                    var mesh = collider.sharedMesh; var vertices = mesh.vertices; var indices = mesh.triangles;
                    for (int i=0;i+2<indices.Length;i+=3)
                    {
                        if (i%384==0) yield return null;
                        var a=collider.transform.TransformPoint(vertices[indices[i]]);
                        var b=collider.transform.TransformPoint(vertices[indices[i+1]]);
                        var c=collider.transform.TransformPoint(vertices[indices[i+2]]);
                        if (Vector3.Cross(b-a,c-a).normalized.y < .65f) continue;
                        var center=(a+b+c)/3;var local=directory.transform.InverseTransformPoint(center);
                        if (center.y-geometry.ScenePoint(new RoutePoint(local.x,local.z)).y < 3) continue;
                        surfaces++;
                        if (unique.Exists(p=>Vector3.Distance(p,center)<1)) continue;
                        unique.Add(center);
                        NavMeshHit wall;
                        if (!NavMesh.SamplePosition(center,out wall,.5f,NavMesh.AllAreas) || Math.Abs(wall.position.y-center.y)>.5f) continue;
                        sampled++;
                        var path=new NavMeshPath();
                        if (!NavMesh.CalculatePath(ground.position,wall.position,NavMesh.AllAreas,path) ||
                            path.status!=NavMeshPathStatus.PathComplete || path.corners.Length<2) continue;
                        reachable++;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_ACCESS_ROUTE: block="+block.name+"; surface="+center+
                            "; corners="+path.corners.Length+"; rise="+(wall.position.y-ground.position.y));
                    }
                }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_ACCESS_AUDIT: nativeQuarterDoors="+doors+"; wallBlocks="+blocks.Count+
                "; elevatedExteriorDoors="+elevatedDoors+"; unreadableDecorativeMeshes="+unreadableMeshes+
                "; upperTriangles="+surfaces+"; distinctSurfaces="+unique.Count+"; navMeshUpperSurfaces="+sampled+"; streetReachableUpperSurfaces="+reachable);
        }
        private static void RaiseHour(int hour)
        { var clock=DaggerfallUnity.Instance.WorldTime.Now; clock.RaiseTime((float)((hour*3600d-clock.ToSeconds()%86400+86400)%86400)); }
        private static double Now() { var time=DaggerfallUnity.Instance.WorldTime.Now; return (double)time.ToSeconds()+time.Second-Math.Floor(time.Second); }
        private static GuardActor Find(string id)
        { foreach(var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>())if(actor.GuardId==id && actor.gameObject.activeInHierarchy)return actor;return null; }
        private static T Field<T>(object owner,string name)
        { return (T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner); }
    }
}
