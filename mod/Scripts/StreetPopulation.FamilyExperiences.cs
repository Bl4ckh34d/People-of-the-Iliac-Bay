using System;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private float nextFamilyExperienceSharing;
        private int nextFamilyExperienceHome;
        private void TickFamilyExperienceSharing(double now)
        {
            if(worldStore==null || population==null || population.Households.Count==0 ||
                Time.unscaledTime<nextFamilyExperienceSharing)return;
            nextFamilyExperienceSharing=Time.unscaledTime+0.5f;
            var loaded=interior?population.Households.Find(x=>x.BuildingKey==interior.EntryDoor.buildingKey):null;
            if(loaded!=null)ShareAvailableFamilyExperiences(loaded,now);
            // Bounded work; households without recent direct harm produce no records.
            for(int n=0;n<Math.Min(4,population.Households.Count);n++)
            {
                nextFamilyExperienceHome%=population.Households.Count;
                var home=population.Households[nextFamilyExperienceHome++];
                if(home!=loaded)ShareAvailableFamilyExperiences(home,now);
            }
        }
        private void ShareAvailableFamilyExperiences(Household home,double now)
        {
            foreach(var reporter in home.Residents)foreach(var recipient in home.Residents)
            {
                if(reporter==recipient || !FamilyConversationAvailable(reporter,home,now) ||
                    !FamilyConversationAvailable(recipient,home,now))continue;
                if(HouseholdHomeLoaded(population.SettlementId,home.BuildingKey))
                {
                    ResidentActor first,second;
                    if(!actors.TryGetValue(reporter.Id,out first) || !actors.TryGetValue(recipient.Id,out second) ||
                        !first || !second || first.transform.parent!=interior.transform || second.transform.parent!=interior.transform ||
                        !CivilianResponses.Sees(first.transform.position,second.transform.position,6))continue;
                }
                worldStore.ShareFamilyPlayerExperiences(reporter.Id,recipient.Id,population.SettlementId,home.BuildingKey,now);
            }
        }
        private bool FamilyConversationAvailable(Resident person,Household home,double now)
        {
            if(!person.IsAlive || person.HomeKey!=home.BuildingKey || !plans.Exists(x=>x.Resident.Id==person.Id) ||
                pendingHouseholdInitialization.Contains(person.Id) || simulatedFamilyFrame.Contains(person.Id) ||
                !HouseholdParentAvailable(person.Id,now))return false;
            var current=GetState(person.Id,now);
            if(current.Activity!=Activity.Home || current.BuildingKey!=home.BuildingKey)return false;
            ResidentActor actor;
            if(actors.TryGetValue(person.Id,out actor) && actor && actor.gameObject.activeInHierarchy)
                return interior && interior.EntryDoor.buildingKey==home.BuildingKey && actor.transform.parent==interior.transform &&
                    !actor.IsSleeping && !actor.IsConversing;
            // Offscreen uses the existing resolved routine, including actual entry,
            // absence, health, action and family movement owners checked above.
            return !HouseholdHomeLoaded(population.SettlementId,home.BuildingKey);
        }
    }
}
