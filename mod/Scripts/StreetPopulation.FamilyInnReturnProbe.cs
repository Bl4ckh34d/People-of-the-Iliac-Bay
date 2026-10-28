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
        // Resume the actual seated checkpoint when present. Otherwise rebuild
        // only the normal unobserved trip, without repeating bank or outward walk.
        internal IEnumerator VerifyFamilyInnReturnDirect()
        {
            var game=GameManager.Instance;
            if(!NativeProbeText.IsTestCharacter(game.PlayerEntity.Name))
                throw new InvalidOperationException("Family return requires the owned disposable character.");
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            game.SaveLoadManager.EnumerateSaves();
            int checkpoint=game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name,FamilySeatedCheckpoint);
            bool fresh=Environment.GetEnvironmentVariable("POTIB_FAMILY_RETURN_FRESH")=="1";
            if(checkpoint>=0 && !game.IsPlayerInside && !fresh)
            {
                var host=GetComponent<PeopleOfTheIliacBayMod>();
                yield return ConversationMoodProbe.LoadSave(host,this,game.PlayerEntity.Name,FamilySeatedCheckpoint,true);
            }
            HouseholdDayPlan day=null;float deadline=Time.realtimeSinceStartup+30;
            if(game.IsPlayerInside)
            {
                while(day==null && Time.realtimeSinceStartup<deadline)
                {
                    if(Ready && interior && shopGeometry!=null)
                        foreach(var progress in worldStore.ReadActiveHouseholdJoints(population.SettlementId))
                        {
                            var source=worldStore.ReadHouseholdDay(progress.SettlementId,progress.HomeKey,progress.AbsoluteDay);
                            if(progress.Phase!=HouseholdJointPhase.Visiting || source?.JointInn==null ||
                                source.JointInn.InnKey!=interior.EntryDoor.buildingKey)continue;
                            var first=FindActor(source.First.ResidentId);var second=FindActor(source.Second.ResidentId);
                            if(first && second && first.IsSeated && second.IsSeated){day=source;break;}
                        }
                    if(day==null)yield return null;
                }
                if(day==null)throw new InvalidOperationException("Existing Inn checkpoint did not restore its actual seated family.");
                string payer=day.JointInn.PayerId,partner=payer==day.First.ResidentId?day.Second.ResidentId:day.First.ResidentId;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_RETURN_RESUME: seatedCheckpoint=true; home="+day.HomeKey+
                    "; bankRepeated=false; outwardWalkRepeated=false; playerTimeAdvanceBeforeResume=false");
                yield return VerifyObservedFamilyInnReturn(day,payer,partner,false);
                yield break;
            }
            int observedHome=-1;
            while(day==null && Time.realtimeSinceStartup<deadline)
            {
                if(Ready && EntranceSurveyReady && geometry!=null && geometry.UsesNativeNavigation)
                {
                    if(observedHome<0)observedHome=ObserveFamilyCashHousehold();
                    if(observedHome>=0)
                    {
                        nextHouseholdDay=population.Households.FindIndex(home=>home.BuildingKey==observedHome);
                        UpdateHouseholdDays(ActionNow());
                        var source=worldStore.ReadHouseholdDay(population.SettlementId,observedHome,(int)Math.Floor(ActionNow()/86400d));
                        if(source?.JointInn!=null && !source.JointSuppressed)day=source;
                    }
                }
                if(day==null)yield return new WaitForSecondsRealtime(.5f);
            }
            if(day==null)throw new InvalidOperationException("Actual natural family plan did not publish for direct Inn observation: "+familyCashSelectionStatus);
            var returnRoute=day.JointInn.First.Itinerary.ExitRoute;
            for(int index=1;index<returnRoute.Count;index++)
            {
                var a=returnRoute[index-1];var b=returnRoute[index];
                double dx=b.X-a.X,dz=b.Z-a.Z,squared=dx*dx+dz*dz;
                if(squared<0.000001)continue;
                foreach(bool reverse in new[]{false,true})
                {
                    var actualRoute=geometry.RouteBetweenPoints(reverse?b:a,reverse?a:b);double deviation=0;
                    foreach(var corner in actualRoute)
                    {
                        double t=Math.Max(0,Math.Min(1,((corner.X-a.X)*dx+(corner.Z-a.Z)*dz)/squared));
                        double x=corner.X-a.X-t*dx,z=corner.Z-a.Z-t*dz;deviation=Math.Max(deviation,Math.Sqrt(x*x+z*z));
                    }
                    if(deviation>0.4 || actualRoute.Count<2)
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_ROUTE_SOURCE: native="+geometry.UsesNativeNavigation+"; segment="+index+
                            "; reverse="+reverse+"; start="+a.X+","+a.Z+"; end="+b.X+","+b.Z+"; currentPoints="+actualRoute.Count+"; deviation="+deviation);
                }
            }
            string payerId=day.JointInn.PayerId,partnerId=payerId==day.First.ResidentId?day.Second.ResidentId:day.First.ResidentId;
            var wallet=worldStore.ReadNpcInventory(payerId);
            if(wallet==null || wallet.WalletGold<day.JointInn.ReserveGold)
                throw new InvalidOperationException("Existing family payer cannot fund the ordinary Inn; this return probe never repeats bank or credits wallets.");
            var progressAtHome=worldStore.ReadHouseholdJointProgress(day.SettlementId,day.HomeKey);
            if(progressAtHome==null || progressAtHome.Phase!=HouseholdJointPhase.Waiting)
                throw new InvalidOperationException("Direct return setup requires the actual normal waiting household, not an injected phase.");
            TownGeometry.Entrance inn;if(!geometry.Entrances.TryGetValue(day.JointInn.InnKey,out inn))
                throw new InvalidOperationException("Natural family Inn has no native surveyed entrance.");
            Vector3 observer;
            if(!NativeMoveToWaitProbe.TryObserverGround(geometry,directory,geometry.ScenePoint(new RoutePoint(inn.LocalDoor.x,inn.LocalDoor.z)),out observer))
                throw new InvalidOperationException("Natural family Inn has no clear native observer ground.");
            game.PlayerMotor.transform.position=observer+Vector3.up;Physics.SyncTransforms();
            worldStore.BeginHouseholdJointSimulation(day.Id,progressAtHome.Sequence,ActionNow());
            double arrival=day.AbsoluteDay*86400d+Math.Max(
                day.JointInn.First.DepartureSeconds+day.JointInn.First.Itinerary.Stops[0].WalkSeconds,
                day.JointInn.Second.DepartureSeconds+day.JointInn.Second.Itinerary.Stops[0].WalkSeconds)+.01;
            if(arrival<=ActionNow())throw new InvalidOperationException("Normal unobserved family Inn arrival is already past.");
            game.PlayerEntity.PreventEnemySpawns=true;
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(arrival-ActionNow()));
            var simulated=worldStore.AdvanceHouseholdJointSimulation(day.Id,ActionNow());
            if(simulated.Phase!=HouseholdJointPhase.AtInn)
                throw new InvalidOperationException("Normal unobserved family trip did not reach the actual Inn: "+simulated.Phase);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_RETURN_SETUP: home="+day.HomeKey+"; inn="+day.JointInn.InnKey+
                "; naturalPlan=true; existingWallets=true; ordinaryUnobservedSimulation=true; injectedPhase=false; bankRepeated=false; outwardWalkRepeated=false");
            yield return VerifyFundedFamilyInn(day,payerId,partnerId,false,!fresh);
        }
    }
}
