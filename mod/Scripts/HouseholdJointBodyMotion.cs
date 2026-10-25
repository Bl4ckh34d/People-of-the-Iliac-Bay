using System;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // A pair owns one physical movement boundary. Higher-priority owners decide
    // eligibility before this call; individual schedules must not place either
    // participant afterward. Never feed planned positions into Observe.
    internal sealed class HouseholdJointBodyMotion
    {
        private readonly HouseholdJointMovement movement;
        private readonly ResidentActor first,second;
        private readonly TownGeometry geometry;
        private readonly Transform origin;
        private readonly Action<HouseholdJointProgress> commit;
        private bool rejected;
        internal HouseholdJointBodyMotion(HouseholdJointMovement movement,ResidentActor first,ResidentActor second,
            TownGeometry geometry,Transform origin,Action<HouseholdJointProgress> commit)
        {
            this.movement=movement??throw new ArgumentNullException(nameof(movement));
            this.first=first;this.second=second;this.geometry=geometry??throw new ArgumentNullException(nameof(geometry));
            this.origin=origin?origin:throw new ArgumentNullException(nameof(origin));
            this.commit=commit??throw new ArgumentNullException(nameof(commit));
            var p=movement.Snapshot();
            if(first && first.Resident.Id!=p.FirstResidentId || second && second.Resident.Id!=p.SecondResidentId || first && first==second)
                throw new ArgumentException("Shared physical bodies do not match their saved participants.");
        }
        internal bool Tick(double now,bool firstAvailable,bool secondAvailable,bool paused=false)
        {
            firstAvailable=firstAvailable && Available(first);secondAvailable=secondAvailable && Available(second);
            var p=movement.Snapshot();
            if(double.IsNaN(now) || double.IsInfinity(now) || now<p.ObservedAt)
                throw new ArgumentOutOfRangeException(nameof(now));
            if(p.Sequence==int.MaxValue){if(firstAvailable)Hold(first);if(secondAvailable)Hold(second);return false;}
            if(rejected || paused || !firstAvailable || !secondAvailable || movement.RequiresRecovery ||
                p.Phase!=HouseholdJointPhase.Outbound && p.Phase!=HouseholdJointPhase.Returning)
            {if(firstAvailable)Hold(first);if(secondAvailable)Hold(second);return false;}
            var beforeA=Point(first);var beforeB=Point(second);
            // An external owner can move a participant while this pair pauses.
            // That requires a physical recovery/rebind, not a scheduled snap or
            // a fabricated catch-up observation on the pair's old route.
            if(Distance(beforeA,p.First.Position)>0.001 || Distance(beforeB,p.Second.Position)>0.001)
            {rejected=true;Hold(first);Hold(second);return false;}
            var targets=movement.RejoinTargets(true,true);
            NativePairCollision.Refresh(first,second);
            try
            {
                if(targets.MoveFirst)Move(first,targets.First,candidate=>Allowed(now,candidate,beforeB));else Hold(first);
                var observedFirst=Point(first);
                if(targets.MoveSecond)Move(second,targets.Second,candidate=>Allowed(now,observedFirst,candidate));else Hold(second);
                if(!Available(first) || !Available(second)){rejected=true;return false;}
                var observedProgress=movement.PreviewObservation(now,Point(first),Point(second));
                commit(observedProgress);
                movement.Observe(now,Point(first),Point(second));return true;
            }
            catch(Exception)
            {
                // A rejected store commit must not leave unstored body poses
                // behind for scene teardown to lose. Restore the paired anchor
                // synchronously before latching this executor closed.
                if(first)first.GetComponent<CrowdMotion>().RestorePlacement(geometry.ScenePoint(new RoutePoint(beforeA.X,beforeA.Z)));
                if(second)second.GetComponent<CrowdMotion>().RestorePlacement(geometry.ScenePoint(new RoutePoint(beforeB.X,beforeB.Z)));
                var firstNavigation=first?first.GetComponent<NativeResidentNavigation>():null;
                var secondNavigation=second?second.GetComponent<NativeResidentNavigation>():null;
                if(firstNavigation)firstNavigation.Pause();
                if(secondNavigation)secondNavigation.Pause();
                rejected=true;
                Hold(first);Hold(second);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_BODY_REJECTED: firstSegment="+p.First.Segment+
                    "; beforeFirst="+Coordinates(beforeA)+"; actualFirst="+Coordinates(Point(first))+"; targetFirst="+Coordinates(targets.First)+
                    "; secondSegment="+p.Second.Segment+"; beforeSecond="+Coordinates(beforeB)+"; actualSecond="+Coordinates(Point(second))+
                    "; targetSecond="+Coordinates(targets.Second));
                throw;
            }
        }
        private bool Available(ResidentActor actor)
        {return actor && actor.gameObject.activeInHierarchy && actor.transform.parent==origin && actor.Resident.IsAlive &&
            !actor.Resident.IsQuestProtected && !actor.IsConversing && !actor.IsSleeping && !actor.IsSeated &&
            actor.GetComponent<CrowdMotion>() && actor.GetComponent<CrowdMotion>().HasPlacement;}
        private bool Allowed(double now,SavedRoutePoint firstPoint,SavedRoutePoint secondPoint)
        {
            try{movement.PreviewObservation(now,firstPoint,secondPoint);return true;}
            catch(ArgumentException){return false;}
        }
        private void Move(ResidentActor actor,SavedRoutePoint target,Func<SavedRoutePoint,bool> allowed)
        {
            var destination=geometry.ScenePoint(new RoutePoint(target.X,target.Z));
            var currentGround=actor.transform.position-Vector3.up;
            bool native=NativeNavigationWorld.Current && NativeNavigationWorld.Current.Ready &&
                Vector3.Distance(currentGround,destination)>1.25f;
            actor.ResumeFromSocialPause();actor.Place(native?currentGround:destination,destination-currentGround,comfortWalking:false,
                placementAllowed:candidate=>{var local=origin.InverseTransformPoint(candidate);return allowed(new SavedRoutePoint{X=local.x,Z=local.z});},
                navigationDestination:native?(Vector3?)destination:null,preferNative:native);
        }
        internal static void Hold(ResidentActor actor)
        {
            if(!actor || !actor.gameObject.activeInHierarchy || actor.IsConversing || actor.IsSleeping || actor.IsSeated ||
                !actor.GetComponent<CrowdMotion>() || !actor.GetComponent<CrowdMotion>().HasPlacement)return;
            var native=actor.GetComponent<NativeResidentNavigation>();if(native)native.Hold();
            actor.HoldPhysicalPosition();
        }
        private SavedRoutePoint Point(ResidentActor actor)
        {var p=origin.InverseTransformPoint(actor.transform.position-Vector3.up);return new SavedRoutePoint{X=p.x,Z=p.z};}
        private static double Distance(SavedRoutePoint a,SavedRoutePoint b)
        {double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        private static string Coordinates(SavedRoutePoint p)
        {return p.X.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+p.Z.ToString("R",System.Globalization.CultureInfo.InvariantCulture);}
    }
}
