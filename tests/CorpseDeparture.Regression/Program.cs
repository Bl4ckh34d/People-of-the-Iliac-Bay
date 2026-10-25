using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var cases=new List<(string Name,Action Body)>();OutdoorCorpseTests.Add(cases);GuardSearchRecruitmentTests.Add(cases);CorpseSuspectEvidenceTests.Add(cases);int failed=0;
            foreach(var test in cases){try{test.Body();Console.WriteLine("PASS "+test.Name);}
                catch(Exception error){failed++;Console.WriteLine("FAIL "+test.Name+": "+error);}}
            Console.WriteLine((cases.Count-failed)+"/"+cases.Count+" passed");return failed==0?0:1;
        }
    }
    internal static class Check
    {
        public static void True(bool value,string message="Condition was false"){if(!value)throw new Exception(message);}
        public static void Equal<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception("Expected "+expected+"; got "+actual);}
        public static void Near(double expected,double actual,double tolerance=0.000001){if(double.IsNaN(actual)||Math.Abs(expected-actual)>tolerance)throw new Exception("Expected "+expected+"; got "+actual);}
        public static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    }
}
