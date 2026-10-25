using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class Program
    {
        static int Main()
        {
            var cases=new List<(string Name,Action Body)>();DailyOutingDistributionTests.Add(cases);HouseholdAccessTests.Add(cases);ResidentRoutineDayTests.Add(cases);AdultDailyRoutineTests.Add(cases);int failed=0;
            foreach(var test in cases)try{test.Body();Console.WriteLine("PASS "+test.Name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+test.Name+": "+e.Message);}
            Console.WriteLine((cases.Count-failed)+"/"+cases.Count+" passed");return failed==0?0:1;
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
