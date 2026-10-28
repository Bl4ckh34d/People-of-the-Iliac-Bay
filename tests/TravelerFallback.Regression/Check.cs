using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class Check
    {
        internal static void True(bool value,string message=null){if(!value)throw new Exception(message??"Condition was false");}
        internal static void Equal<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception("Expected "+expected+"; got "+actual);}
        internal static void Near(double expected,double actual,double tolerance=0.000001){True(Math.Abs(expected-actual)<=tolerance,"Expected "+expected+"; got "+actual);}
        internal static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    }
}
