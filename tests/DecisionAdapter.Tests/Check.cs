using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.DFU.Tests
{
    internal static class Check
    {
        public static void True(bool value,string message="Assertion failed."){if(!value)throw new InvalidOperationException(message);}
        public static void Equal<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new InvalidOperationException("Expected "+expected+" but got "+actual);}
        public static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    }
}
