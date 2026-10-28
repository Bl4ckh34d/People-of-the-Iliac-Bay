using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Tests
{
    // Focused offline runner; invoke with --family-all for the existing family cases.
    // The ordinary runner remains unchanged for parent integration.
    internal static class FamilyFocusedRunner
    {
        private static int Main(string[] args)
        {
            var cases=new List<(string Name,Action Body)>();
            if(Array.IndexOf(args,"--traveler-action-away")>=0)TravelerSaveTests.AddBoundActionAbsenceCase(cases);
            else if(Array.IndexOf(args,"--paid-training-rejoin")>=0)NpcActionExecutorTests.AddPaidTrainingRejoin(cases);
            else FamilySimplificationTests.Add(cases);
            if(Array.IndexOf(args,"--family-all")>=0)
            {
                HouseholdDayPlanTests.Add(cases);HouseholdDayAppointmentTests.Add(cases);HouseholdAccessTests.Add(cases);
                HouseholdChildcareTests.Add(cases);HouseholdInnServiceTests.Add(cases);HouseholdInnVisitTests.Add(cases);
                HouseholdJointSimulationTests.Add(cases);HouseholdJointSimulationStoreTests.Add(cases);
                HouseholdJointMovementTests.Add(cases);HouseholdJointStoreTests.Add(cases);
            }
            int failed=0;
            foreach(var test in cases)
            {try{test.Body();Console.WriteLine("PASS "+test.Name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+test.Name+": "+error.Message);}}
            Console.WriteLine((cases.Count-failed)+"/"+cases.Count+" passed; "+failed+" failed");return failed==0?0:1;
        }
    }
}
