using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var cases=new List<(string Name,Action Body)>();
            NativeNpcCompanionExperienceTests.Add(cases);
            CombatUseCases.Add(cases);
            int failed=0;
            foreach(var item in cases)
                try {item.Body();Console.WriteLine("PASS "+item.Name);}
                catch(Exception error) {failed++;Console.WriteLine("FAIL "+item.Name+": "+error.Message);}
            Console.WriteLine((cases.Count-failed)+"/"+cases.Count+" passed");
            return failed==0?0:1;
        }
    }
    internal static class Check
    {
        internal static void True(bool value,string message=null)
        {if(!value)throw new InvalidOperationException(message??"Expected true.");}
        internal static void Equal<T>(T expected,T actual)
        {if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new InvalidOperationException("Expected "+expected+"; actual "+actual);}
    }
}
