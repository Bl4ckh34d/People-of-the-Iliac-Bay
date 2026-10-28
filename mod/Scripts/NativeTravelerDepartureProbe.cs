using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        internal IEnumerator VerifyTravelerDepartureRetention()
        {
            var game=GameManager.Instance;var clock=DaggerfallUnity.Instance.WorldTime.Now;
            if(!NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Departure probe requires an owned outdoor fixture.");
            float deadline=Time.realtimeSinceStartup+90;
            while((!Ready || !EntranceSurveyReady) && Time.realtimeSinceStartup<deadline)yield return null;
            if(!Ready || !EntranceSurveyReady)throw new InvalidOperationException("Departure fixture geometry not ready.");
            yield return TravelerIdentityAdapter.PrepareOrigins(directory.MapID,()=>directory);
            var accesses=geometry.SurveyTravelerRoutes(197386);
            if(accesses.Count==0)throw new InvalidOperationException("Departure fixture needs a real open-gate Inn route.");
            var proposal=TravelerIdentityAdapter.Create(42,directory.MapID,197386,(int)(clock.ToSeconds()/86400),876,accesses[0]);
            proposal.Visit.DesiredArrival=clock.ToSeconds();
            worldStore.RegisterTraveler(proposal.Person);worldStore.RegisterTravelerVisit(proposal.Visit);
            var journey=worldStore.TravelerJourneyFor(proposal.Visit.Id);
            // A controlled missed check-in uses the real refusal transaction; no
            // room, payment or successful Inn visit is fabricated by this fixture.
            clock.RaiseTime((float)(journey.CheckOutAt+1-clock.ToSeconds()));
            worldStore.TryAdmitTraveler(proposal.Visit.Id,clock.ToSeconds());
            if(worldStore.TravelerVisitsFor(directory.MapID).Find(v=>v.Id==proposal.Visit.Id).Admission!=TravelerAdmission.Refused)
                throw new InvalidOperationException("Missed check-in must produce a recorded refusal.");
            journey=worldStore.TravelerJourneyFor(proposal.Visit.Id);
            clock.RaiseTime((float)Math.Max(0,journey.LeaveInnAt+1-clock.ToSeconds()));
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            game.PlayerMotor.CancelMovement=true;
            var start=geometry.ScenePoint(journey.Evaluate(clock.ToSeconds()).Position);
            game.PlayerMotor.transform.position=start+Vector3.up+Vector3.right*3;Physics.SyncTransforms();
            deadline=Time.realtimeSinceStartup+12;
            ResidentActor body=null;
            while(!body && Time.realtimeSinceStartup<deadline)
            {body=FindActor(proposal.Person.Id);yield return null;}
            if(!body)throw new InvalidOperationException("Refused traveler did not produce an ordinary departure body.");
            yield return null;
            Vector3 before=body.transform.position;
            clock.RaiseTime((float)(journey.ExitAt+2-clock.ToSeconds()));
            float until=Time.realtimeSinceStartup+1;
            while(Time.realtimeSinceStartup<until)yield return null;
            var retained=FindActor(proposal.Person.Id);
            if(!body || retained!=body || !body.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Observed traveler vanished at planned ExitAt before reaching the physical exit.");
            if(Vector3.Distance(before,body.transform.position)>6)
                throw new InvalidOperationException("Delayed departure snapped to the planned exit.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_DEPARTURE_RETAINED: sameBody=true; scheduledExitPassed=true; distance="+
                Vector3.Distance(before,body.transform.position));
            clock.RaiseTime((float)((18*3600+60-clock.ToSeconds()%86400+86400)%86400));
            yield return new WaitForSecondsRealtime(1);
            if(FindActor(proposal.Person.Id)!=body)throw new InvalidOperationException("Delayed traveler vanished at native gate closure.");
            clock.RaiseTime((float)((6*3600-clock.ToSeconds()%86400+86400)%86400));
            before=body.transform.position;
            until=Time.realtimeSinceStartup+8;
            while(Vector3.Distance(before,body.transform.position)<0.2f && Time.realtimeSinceStartup<until)yield return null;
            if(Vector3.Distance(before,body.transform.position)<0.2f)
                throw new InvalidOperationException("Delayed traveler does not resume when native gates reopen.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_DEPARTURE_PASS: retainedPastExit=true; retainedAcrossClosure=true; resumedAtDawn=true; controlledRefusal=true; fullExitWalk=false");
        }
    }
}
