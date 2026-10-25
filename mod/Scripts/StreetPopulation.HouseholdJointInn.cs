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
        private sealed class JointInnBinding
        {
            internal HouseholdJointInnMotion Motion;
            internal ResidentActor First,Second;
            internal DaggerfallInterior Room;
            internal string DayId;
            internal float RetryAt;
            internal bool RecoveryLogged;
        }
        private readonly Dictionary<int,JointInnBinding> jointInnBindings=new Dictionary<int,JointInnBinding>();
        private readonly Dictionary<int,HouseholdJointProgress> jointInnFrames=new Dictionary<int,HouseholdJointProgress>();
        private readonly Dictionary<string,ResidentActor> jointInnEligible=new Dictionary<string,ResidentActor>(StringComparer.Ordinal);
        private void BeginHouseholdInnFrame(){jointInnFrames.Clear();jointInnEligible.Clear();}
        private bool DeferHouseholdInn(Plan plan,ResidentActor actor,double now)
        {
            if(!interior || worldStore==null || !worldStore.HasActiveHouseholdJoint(population.SettlementId,plan.Resident.Id))return false;
            var progress=worldStore.ReadHouseholdJointProgress(population.SettlementId,plan.Resident.HomeKey);
            if(progress==null || progress.Phase!=HouseholdJointPhase.Visiting || progress.Retired)return false;
            var source=worldStore.ReadHouseholdDay(progress.SettlementId,progress.HomeKey,progress.AbsoluteDay);
            if(source?.JointInn==null || source.JointInn.InnKey!=interior.EntryDoor.buildingKey || source.Id!=progress.HouseholdDayId ||
                HouseholdJointMovement.SourceFingerprint(source)!=progress.SourceFingerprint)return false;
            jointInnFrames[progress.HomeKey]=progress;
            if(actor && JointBodyFree(plan.Resident.Id,now))jointInnEligible[plan.Resident.Id]=actor;
            return true;
        }
        private void TickHouseholdInns(double now)
        {
            foreach(var progress in jointInnFrames.Values)
            {
                ResidentActor first,second;jointInnEligible.TryGetValue(progress.FirstResidentId,out first);jointInnEligible.TryGetValue(progress.SecondResidentId,out second);
                JointInnBinding binding;jointInnBindings.TryGetValue(progress.HomeKey,out binding);
                if(binding!=null && (binding.DayId!=progress.HouseholdDayId || binding.Room!=interior ||
                    first && binding.First!=first || second && binding.Second!=second))
                {if(binding.Motion!=null)binding.Motion.Release();jointInnBindings.Remove(progress.HomeKey);binding=null;}
                if(binding==null || binding.Motion==null && Time.unscaledTime>=binding.RetryAt)
                {
                    binding=new JointInnBinding{First=first,Second=second,Room=interior,DayId=progress.HouseholdDayId,RetryAt=Time.unscaledTime+2};
                    jointInnBindings[progress.HomeKey]=binding;
                    if(first && second && shopGeometry!=null)
                    {
                        var a=plans.Find(p=>p.Resident.Id==progress.FirstResidentId);var b=plans.Find(p=>p.Resident.Id==progress.SecondResidentId);
                        string reason;var routes=a!=null && b!=null?FamilyPointRoutes(a,b,FamilyBookings(a,b),progress.AbsoluteDay,out reason):null;
                        var source=worldStore.ReadHouseholdDay(progress.SettlementId,progress.HomeKey,progress.AbsoluteDay);
                        HouseholdJointInnMotion motion;
                        if(HouseholdJointInnMotion.TryCreate(worldStore,source,first,second,interior,indoorGeometry,shopGeometry,now,
                            (actor,physical,at)=>TryCompleteInnPurchaseAtCounter(actor,physical,at),out motion,routes))
                        {
                            binding.Motion=motion;physicalShopVisits[first.Resident.Id]=motion.FirstVisit;physicalShopVisits[second.Resident.Id]=motion.SecondVisit;
                        }
                    }
                }
                if(binding.Motion==null){HouseholdJointBodyMotion.Hold(first);HouseholdJointBodyMotion.Hold(second);continue;}
                try
                {
                    binding.Motion.Tick(now,first && JointBodyFree(progress.FirstResidentId,now),second && JointBodyFree(progress.SecondResidentId,now),GameManager.IsGamePaused);
                    if(binding.Motion.RequiresRecovery && !binding.RecoveryLogged)
                    {binding.RecoveryLogged=true;Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_INN_RECOVERY_REQUIRED: home="+progress.HomeKey+"; actual owner changed");}
                    if(binding.Motion.Complete)
                    {
                        RemoveDestinationActor(progress.FirstResidentId);RemoveDestinationActor(progress.SecondResidentId);
                        jointInnBindings.Remove(progress.HomeKey);
                    }
                }
                catch(Exception error){Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_INN_RECOVERY_REQUIRED: home="+progress.HomeKey+"; "+error.Message);}
            }
        }
        private void CaptureHouseholdInns(double now)
        {
            foreach(var binding in jointInnBindings.Values)
                if(binding.Motion!=null && !binding.Motion.Complete && !binding.Motion.RequiresRecovery && binding.Room==interior && binding.First && binding.Second &&
                    !(worldStore.ReadHouseholdJointSimulation(population.SettlementId,binding.First.Resident.HomeKey)?.Active??false) &&
                    binding.First.Resident.IsAlive && binding.Second.Resident.IsAlive && Reaction(binding.First.Resident.Id,now)==null &&
                    Reaction(binding.Second.Resident.Id,now)==null && JointBodyFree(binding.First.Resident.Id,now) && JointBodyFree(binding.Second.Resident.Id,now))
                {
                    try{binding.Motion.Capture(now);}
                    catch(ArgumentException error){WarnHouseholdCapture(binding,error);}
                    catch(InvalidOperationException error){WarnHouseholdCapture(binding,error);}
                }
        }
        private static void WarnHouseholdCapture(JointInnBinding binding,Exception error)
        {if(!binding.RecoveryLogged){binding.RecoveryLogged=true;Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_INN_CAPTURE_HELD: "+error.Message);}}
        private void ClearHouseholdInns()
        {
            foreach(var binding in jointInnBindings.Values)if(binding.Motion!=null)binding.Motion.Release();
            jointInnBindings.Clear();jointInnFrames.Clear();jointInnEligible.Clear();
        }
    }
}
