using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class LegacyRunner
    {
        internal static int Run()
        {
            var tests=new List<(string Name,Action Body)>();
            AdultDailyItineraryTests.Add(tests);AdultDailyRoutineTests.Add(tests);RoutineRecoveryPlannerTests.Add(tests);
            RoutineRecoveryPointTests.Add(tests);RoutineRecoveryAcceptanceTests.Add(tests);
            RoutineRecoveryStoreTests.Add(tests);
            RoutineRecoveryLifecycleTests.Add(tests);
            NpcRoutineRecoveryTransitionTests.Add(tests);
            HouseholdDayPlanTests.Add(tests);
            HouseholdDayAppointmentTests.Add(tests);
            HouseholdRoutineRecoveryTests.Add(tests);
            int failed=0;foreach(var test in tests)try{test.Body();}catch(Exception error){failed++;Console.WriteLine("FAIL "+test.Name+": "+error.Message);}
            Console.WriteLine((tests.Count-failed)+"/"+tests.Count+" linked routine tests passed");return failed;
        }
    }
    internal static class Check
    {
        internal static void True(bool value,string message=null){if(!value)throw new Exception(message??"Expected true");}
        internal static void Equal<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception("Expected "+expected+"; got "+actual);}
        internal static void Near(double expected,double actual,double tolerance=0.000001){True(Math.Abs(expected-actual)<=tolerance,"Expected "+expected+"; got "+actual);}
        internal static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    }
}
