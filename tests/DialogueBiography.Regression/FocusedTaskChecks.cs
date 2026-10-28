using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class FocusedTaskChecks
    {
        internal static int Run()
        {
            var cases=new List<(string Name,Action Body)>();
            NpcTaskTests.Add(cases);NpcTaskFailureFeedbackTests.Add(cases);
            NpcTaskDependencyAcceptanceTests.Add(cases);NpcTaskIndexTests.Add(cases);ConversationTests.Add(cases);
            foreach(var test in cases)
                try {test.Body();}
                catch(Exception error){throw new Exception(test.Name+": "+error.Message,error);}
            return cases.Count;
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
