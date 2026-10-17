using System;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed class GateGuardDutySnapshot
    {
        public int GateIndex, Side;
        public bool ExteriorAssignment, GateOpen;
        public string GuardId;
        public SavedRoutePoint Target, Position;
        public GuardDuty Duty;
        public double ArrivesAt;
    }
    public sealed partial class GuardPopulation
    {
        private sealed class GateDutyPair
        {
            public DaggerfallCityGate Gate;
            public int GateIndex, Side;
            public SavedRoutePoint ReturnInside, Outside;
            public string GuardId;
            public bool WasClosed, RecoverInside;
            public double HoldInsideUntil;
            public SavedRoutePoint ObservedPost;
            public List<SavedRoutePoint> ReturnRoute;
            public PreparedWall Wall;
        }
        private sealed class GateDutyPost
        {
            public DaggerfallCityGate Gate;
            public int GateIndex, Side;
            public SavedRoutePoint Point;
            public string GuardId;
        }
        private readonly List<GateDutyPost> gateDutyPosts = new List<GateDutyPost>();
        private readonly List<GateDutyPair> gateDutyPairs = new List<GateDutyPair>();
        private static FieldInfo gateOpenField;
        private static bool gateOpenFieldResolved;
        private bool gateDutySurveyed;
        private bool gateApproachesDirty;
        private float nextGateDutyTick;
        private readonly Stack<System.Collections.IEnumerator> gateDutyWork=new Stack<System.Collections.IEnumerator>();
        private void InvalidateGateApproaches()
        {
            gateApproachesDirty=true;
            if(approachSurvey!=null) { StopCoroutine(approachSurvey); approachSurvey=null; }
        }
        private void CancelGateDutyWork()
        {
            while(gateDutyWork.Count>0) (gateDutyWork.Pop() as IDisposable)?.Dispose();
        }

        // Call after SetTown assigns directory, geometry, and Roster.
        public void InitializeGateGuardPosts()
        {
            CancelGateDutyWork();
            gateDutyPairs.Clear();
            gateDutyPosts.Clear();
            gateDutySurveyed = false;
            if (!OwnsExterior || geometry == null || Roster == null) return;
            var sites = geometry.SurveyGateGuardPosts();
            foreach (var site in sites)
            {
                AddGatePost(site.GateIndex, 0, site.InsideLeft);
                AddGatePost(site.GateIndex, 1, site.InsideRight);
                AddGatePair(site.GateIndex, 0, site.InsideReturnLeft, site.OutsideLeft);
                AddGatePair(site.GateIndex, 1, site.InsideReturnRight, site.OutsideRight);
            }
            BindExistingGateGuards();
            gateDutySurveyed = true;
            Debug.Log("LIVING_CITIES_GATE_POST_SURVEY: gates=" + sites.Count + "; pairedPosts=" + gateDutyPairs.Count);
        }

        // Call after Roster.Advance(now) in GuardPopulation.Update.
        public void UpdateGateGuardDuty(double now)
        {
            if (!OwnsExterior || Roster == null || geometry == null)
            { CancelGateDutyWork(); return; }
            if(GameManager.IsGamePaused) return;
            if (Time.unscaledTime < nextGateDutyTick) return;
            if (!gateDutySurveyed) InitializeGateGuardPosts();
            if (!gateDutySurveyed || gateDutyPairs.Count == 0) return;
            if (SettlementHaunting.StreetsDangerous(Roster.SettlementId,
                GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0), now))
            { CancelGateDutyWork(); return; }
            if(gateDutyWork.Count==0)
            {
                if(!BackgroundWork.TryStart()) return;
                gateDutyWork.Push(AdvanceGateDuty());
            }
            try
            {
                var step=gateDutyWork.Peek();
                if(!step.MoveNext())
                {
                    (gateDutyWork.Pop() as IDisposable)?.Dispose();
                    if(gateDutyWork.Count==0) nextGateDutyTick=Time.unscaledTime+1;
                }
                else if(step.Current is System.Collections.IEnumerator) gateDutyWork.Push((System.Collections.IEnumerator)step.Current);
                else nextGateDutyTick=Time.unscaledTime+(step.Current is float ? (float)step.Current : 0);
            }
            catch { CancelGateDutyWork(); throw; }
        }

        private System.Collections.IEnumerator AdvanceGateDuty()
        {
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var post in gateDutyPosts) if (post.GuardId != null) assigned.Add(post.GuardId);
            foreach (var pair in gateDutyPairs) if (pair.GuardId != null) assigned.Add(pair.GuardId);
            foreach (var post in gateDutyPosts)
            {
                if (post.GuardId != null && Roster.Guards.Exists(g => g.Id == post.GuardId && g.IsAlive)) continue;
                yield return 0.1f;
                double now=Now();
                post.GuardId = null;
                var gate = post.Gate;
                if (!gate || !gate.gameObject.activeInHierarchy) continue;
                GuardRecord guard=null;
                yield return SelectStaticGateGuard(post,assigned,g=>guard=g);
                if (guard == null) continue;
                post.GuardId = guard.Id; assigned.Add(guard.Id);
            }
            foreach (var pair in gateDutyPairs)
            {
                yield return 0.1f;
                double now=Now();
                if (!pair.Gate || !pair.Gate.gameObject.activeInHierarchy) continue;
                bool open = IsVanillaGateOpen(pair.Gate, now);
                var guard = pair.GuardId == null ? null : Roster.Guards.Find(g => g.Id == pair.GuardId);
                if (guard == null || !guard.IsAlive)
                {
                    pair.GuardId = null;
                    yield return SelectGateGuard(pair,assigned,g=>guard=g);
                    now=Now();open=IsVanillaGateOpen(pair.Gate,now);
                    if (guard == null) continue;
                    pair.GuardId = guard.Id; assigned.Add(guard.Id);
                }
                if(pair.ObservedPost!=null && !AtPoint(pair.ObservedPost,guard.Post)) InvalidateGateApproaches();
                pair.ObservedPost=new SavedRoutePoint { X=guard.Post.X,Z=guard.Post.Z };
                if (!open)
                {
                    if (AtPoint(guard.Post, pair.Outside)) pair.RecoverInside = true;
                    pair.WasClosed = true;
                    continue;
                }
                if (pair.WasClosed && AtPoint(guard.Post, pair.Outside)) pair.RecoverInside = true;
                pair.WasClosed = false;

                double dayStart=Math.Floor(now/86400)*86400;
                double daySeconds = now-dayStart;
                double duskAt=dayStart+DaggerfallDateTime.DuskHour*3600;
                double returnTravelSeconds=double.PositiveInfinity;
                if(pair.ReturnRoute==null)
                {
                    var prepared=new TownGeometry.RouteResult();
                    yield return ReadGateRoute(pair.Outside,pair.ReturnInside,prepared);
                    if(prepared.Points!=null && prepared.Points.Count>=2) pair.ReturnRoute=prepared.Points;
                }
                var returnRoute=pair.ReturnRoute;
                if(returnRoute!=null) returnTravelSeconds=RouteLength(returnRoute)*12/1.3;
                now=Now();
                if(!guard.IsAlive || pair.GuardId!=guard.Id || !IsVanillaGateOpen(pair.Gate,now)) continue;
                dayStart=Math.Floor(now/86400)*86400;
                daySeconds=now-dayStart;
                duskAt=dayStart+DaggerfallDateTime.DuskHour*3600;
                double recallAt=duskAt-returnTravelSeconds-60;
                if(guard.PostRecall==null || guard.PostRecall.WallDuty==null)
                {
                    yield return PrepareWallPatrol(pair,guard);
                    now=Now();
                    ArmGateRecall(guard,pair,now);
                    dayStart=Math.Floor(now/86400)*86400;daySeconds=now-dayStart;
                    duskAt=dayStart+DaggerfallDateTime.DuskHour*3600;
                }
                bool closingWindow=duskAt-now<=returnTravelSeconds+60;
                bool openingWindow=daySeconds>=DaggerfallDateTime.DawnHour*3600;
                bool shouldBeOutside=openingWindow && !closingWindow && !pair.RecoverInside && now>=pair.HoldInsideUntil;
                var target = shouldBeOutside ? pair.Outside : pair.ReturnInside;
                if (AtPoint(guard.Post, target) || guard.Duty != GuardDuty.Posted || guard.Interior != null ||
                    guard.Refuge != null || Roster.IsOnPatrol(guard.Id) || guard.IncidentId != null)
                    continue;
                var departurePost=guard.Post;var preparedMove=new TownGeometry.RouteResult();
                yield return ReadGateRoute(departurePost,target,preparedMove);
                var route=preparedMove.Points;now=Now();
                if(route==null || route.Count<2 || !guard.IsAlive || !AtPoint(guard.Post,departurePost) ||
                    guard.Duty!=GuardDuty.Posted || guard.Interior!=null || guard.Refuge!=null ||
                    guard.IncidentId!=null || Roster.IsOnPatrol(guard.Id) || !IsVanillaGateOpen(pair.Gate,now)) continue;
                double travelSeconds = RouteLength(route) * 12 / 1.3;
                if (now + travelSeconds > duskAt) continue;
                if(shouldBeOutside && now+travelSeconds+returnTravelSeconds+60>duskAt) continue;
                try
                {
                    Roster.MovePost(guard.Id, route, now, 1.3, 12);
                    ArmGateRecall(guard,pair,now);
                    InvalidateGateApproaches();
                    if (!shouldBeOutside && pair.RecoverInside)
                    { pair.RecoverInside = false; pair.HoldInsideUntil = now + 600; }
                    Debug.Log("LIVING_CITIES_GATE_GUARD_MOVE: " + pair.GuardId + "; side=" + pair.Side +
                        "; target=" + (shouldBeOutside ? "outside" : "inside"));
                }
                catch (ArgumentException) { /* Preserve identity and wait until a safe route is available. */ }
            }
            if (gateApproachesDirty)
            {
                gateApproachesDirty=false;
                if (approachSurvey!=null) StopCoroutine(approachSurvey);
                approachSurvey=StartCoroutine(SurveyApproaches(GameManagerNavigation()));
            }
        }

        private void AddGatePost(int gateIndex,int side,SavedRoutePoint point)
        {
            var gates = directory.GetComponentsInChildren<DaggerfallCityGate>();
            Array.Sort(gates, (a,b) => {
                var pa=directory.transform.InverseTransformPoint(a.transform.position);
                var pb=directory.transform.InverseTransformPoint(b.transform.position);
                int order=pa.x.CompareTo(pb.x); return order!=0 ? order : pa.z.CompareTo(pb.z);
            });
            if (gateIndex < 0 || gateIndex >= gates.Length) return;
            gateDutyPosts.Add(new GateDutyPost { Gate=gates[gateIndex],GateIndex=gateIndex,Side=side,Point=point });
        }
        private void AddGatePair(int gateIndex, int side, SavedRoutePoint inside, SavedRoutePoint outside)
        {
            var gates = directory.GetComponentsInChildren<DaggerfallCityGate>();
            Array.Sort(gates, (a,b) => {
                var pa=directory.transform.InverseTransformPoint(a.transform.position);
                var pb=directory.transform.InverseTransformPoint(b.transform.position);
                int order=pa.x.CompareTo(pb.x); return order!=0 ? order : pa.z.CompareTo(pb.z);
            });
            if (gateIndex < 0 || gateIndex >= gates.Length) return;
            gateDutyPairs.Add(new GateDutyPair { Gate=gates[gateIndex], GateIndex=gateIndex, Side=side,
                ReturnInside=inside, Outside=outside });
        }

        private void BindExistingGateGuards()
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in gateDutyPairs)
            {
                var guard = Roster.Guards.Find(g => g.IsAlive && !used.Contains(g.Id) && !Roster.IsOnPatrol(g.Id) &&
                    (AtPoint(g.Post, pair.ReturnInside) || AtPoint(g.Post, pair.Outside)));
                if (guard == null) continue;
                pair.GuardId = guard.Id; used.Add(guard.Id);
            }
            foreach (var post in gateDutyPosts)
            {
                var guard = Roster.Guards.Find(g=>g.IsAlive && !used.Contains(g.Id) && !Roster.IsOnPatrol(g.Id) && AtPoint(g.Post,post.Point));
                if(guard==null) continue;
                post.GuardId=guard.Id; used.Add(guard.Id);
            }
        }

        private System.Collections.IEnumerator SelectStaticGateGuard(GateDutyPost post,HashSet<string> assigned,Action<GuardRecord> complete)
        {
            if(EligibleGateGuards(assigned)<=Mathf.Max(4,Roster.Target/4)) yield break;
            foreach(var guard in GateCandidates(assigned,post.Point))
            {
                var source=guard.Post;var prepared=new TownGeometry.RouteResult();
                yield return ReadGateRoute(source,post.Point,prepared);
                if(prepared.Points==null || prepared.Points.Count<2 || !EligibleGateGuard(guard,assigned) || !AtPoint(source,guard.Post)) continue;
                bool moved=false;
                try {Roster.MovePost(guard.Id,prepared.Points,Now(),1.3,12);moved=true;}
                catch(ArgumentException) { }
                if(!moved) continue;
                InvalidateGateApproaches();complete(guard);yield break;
            }
        }

        private System.Collections.IEnumerator SelectGateGuard(GateDutyPair pair,HashSet<string> assigned,Action<GuardRecord> complete)
        {
            if(!IsVanillaGateOpen(pair.Gate,Now()) || EligibleGateGuards(assigned)<=Mathf.Max(4,Roster.Target/4)) yield break;
            if(pair.ReturnRoute==null)
            {
                var prepared=new TownGeometry.RouteResult();
                yield return ReadGateRoute(pair.Outside,pair.ReturnInside,prepared);
                if(prepared.Points==null || prepared.Points.Count<2) yield break;
                pair.ReturnRoute=prepared.Points;
            }
            double now=Now(),dayStart=Math.Floor(Now()/86400)*86400;
            double returnTravel=RouteLength(pair.ReturnRoute)*12/1.3;
            bool outside=now-dayStart>=DaggerfallDateTime.DawnHour*3600 &&
                dayStart+DaggerfallDateTime.DuskHour*3600-now>returnTravel+60;
            var target=outside ? pair.Outside : pair.ReturnInside;
            foreach(var guard in GateCandidates(assigned,target))
            {
                var source=guard.Post;var prepared=new TownGeometry.RouteResult();
                yield return ReadGateRoute(source,target,prepared);
                if(outside) yield return PrepareWallPatrol(pair,guard);
                now=Now();
                if(prepared.Points==null || prepared.Points.Count<2 || !EligibleGateGuard(guard,assigned) ||
                    !AtPoint(source,guard.Post) || !IsVanillaGateOpen(pair.Gate,now)) continue;
                double arrival=now+RouteLength(prepared.Points)*12/1.3;
                // A late preparation waits for a fresh cycle with an inside target.
                if(arrival>dayStart+DaggerfallDateTime.DuskHour*3600 ||
                    outside && arrival+returnTravel+60>dayStart+DaggerfallDateTime.DuskHour*3600) yield break;
                bool moved=false;
                try {Roster.MovePost(guard.Id,prepared.Points,now,1.3,12);moved=true;}
                catch(ArgumentException) { }
                if(!moved) continue;
                ArmGateRecall(guard,pair,now);
                InvalidateGateApproaches();complete(guard);yield break;
            }
        }

        private System.Collections.IEnumerator ReadGateRoute(SavedRoutePoint from,SavedRoutePoint to,TownGeometry.RouteResult result)
        {
            var surveyed=geometry;var roster=Roster;
            yield return surveyed.ReadGateRouteSteps(from,to,result,()=>OwnsExterior && ReferenceEquals(geometry,surveyed) && ReferenceEquals(Roster,roster));
            if(result.Points==null || result.Points.Count<2) yield break;
            if(Gap(result.Points[0],from)>0.05 || Gap(result.Points[result.Points.Count-1],to)>0.05)
            {result.Points.Clear();yield break;}
            result.Points[0]=new SavedRoutePoint {X=from.X,Z=from.Z};
            result.Points[result.Points.Count-1]=new SavedRoutePoint {X=to.X,Z=to.Z};
        }

        private int EligibleGateGuards(HashSet<string> assigned)
        { int count=0; foreach(var guard in Roster.Guards) if(EligibleGateGuard(guard,assigned)) count++; return count; }
        private List<GuardRecord> GateCandidates(HashSet<string> assigned,SavedRoutePoint target)
        {
            var candidates=Roster.Guards.FindAll(g=>EligibleGateGuard(g,assigned));
            candidates.Sort((a,b)=> { int order=Gap(a.Post,target).CompareTo(Gap(b.Post,target));
                return order!=0 ? order : string.CompareOrdinal(a.Id,b.Id); });
            return candidates;
        }
        private bool EligibleGateGuard(GuardRecord guard,HashSet<string> assigned)
        { return guard.IsAlive && guard.Duty==GuardDuty.Posted && guard.Interior==null && guard.Refuge==null &&
            guard.IncidentId==null && !Roster.IsOnPatrol(guard.Id) && !assigned.Contains(guard.Id); }

        private bool TryGateRoute(SavedRoutePoint from,SavedRoutePoint to,out List<SavedRoutePoint> route)
        {
            long routeStarted=RuntimeTiming.Start();
            try { route=geometry.GateRouteBetweenPoints(from,to); }
            finally {RuntimeTiming.End("guard-gate-route",routeStarted);}
            if (route.Count<2 || Gap(route[0],from)>0.05 || Gap(route[route.Count-1],to)>0.05) return false;
            route[0]=new SavedRoutePoint { X=from.X,Z=from.Z };
            route[route.Count-1]=new SavedRoutePoint { X=to.X,Z=to.Z };
            return true;
        }
        private bool TryGateReturnRoute(GateDutyPair pair,out List<SavedRoutePoint> route)
        {
            // These endpoints and surveyed geometry are fixed for this loaded town.
            // Rebuilding the same eight graph routes each second caused ~150 ms stalls.
            // InitializeGateGuardPosts discards pairs when the layout changes.
            route=pair.ReturnRoute;
            if(route!=null) return true;
            if(!TryGateRoute(pair.Outside,pair.ReturnInside,out route)) return false;
            pair.ReturnRoute=route;
            return true;
        }
        private static bool AtPoint(SavedRoutePoint a,SavedRoutePoint b) { return Gap(a,b)<0.05; }
        public bool IsGateGuard(string id)
        {
            foreach(var post in gateDutyPosts) if(post.GuardId==id) return true;
            foreach(var pair in gateDutyPairs) if(pair.GuardId==id) return true;
            return false;
        }
        public List<GateGuardDutySnapshot> GateGuardDutySnapshots(double now)
        {
            var result=new List<GateGuardDutySnapshot>();
            foreach(var post in gateDutyPosts)
                result.Add(CreateGateSnapshot(post.Gate,post.GateIndex,post.Side,false,post.GuardId,post.Point,now));
            foreach(var pair in gateDutyPairs)
            {
                var guard=pair.GuardId==null ? null : Roster.Guards.Find(g=>g.Id==pair.GuardId);
                result.Add(CreateGateSnapshot(pair.Gate,pair.GateIndex,pair.Side,true,pair.GuardId,
                    guard==null ? pair.ReturnInside : guard.Post,now));
            }
            return result;
        }
        private GateGuardDutySnapshot CreateGateSnapshot(DaggerfallCityGate gate,int gateIndex,int side,
            bool exterior,string guardId,SavedRoutePoint target,double now)
        {
            var guard=guardId==null || Roster==null ? null : Roster.Guards.Find(g=>g.Id==guardId);
            return new GateGuardDutySnapshot { GateIndex=gateIndex,Side=side,ExteriorAssignment=exterior,
                GateOpen=gate && IsVanillaGateOpen(gate,now),GuardId=guardId,Target=target,
                Position=guard==null ? target : Roster.Position(guard.Id,now),
                Duty=guard==null ? GuardDuty.Dead : guard.Duty,ArrivesAt=guard==null ? 0 : guard.ArrivesAt };
        }
        private static bool IsVanillaGateOpen(DaggerfallCityGate gate,double now)
        {
            if (!gateOpenFieldResolved)
            {
                gateOpenFieldResolved=true;
                gateOpenField=typeof(DaggerfallCityGate).GetField("isOpen",BindingFlags.Instance|BindingFlags.NonPublic);
            }
            if (gateOpenField==null)
                return !DaggerfallUnity.Instance.WorldTime.Now.IsNight;
            try { return (bool)gateOpenField.GetValue(gate); }
            catch (Exception) { return !DaggerfallUnity.Instance.WorldTime.Now.IsNight; }
        }
    }
}
