using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class Check
    {
        internal static void True(bool value,string message="Assertion failed") {if(!value)throw new Exception(message);}
        internal static void Equal<T>(T expected,T actual) {True(EqualityComparer<T>.Default.Equals(expected,actual),"Expected "+expected+", actual "+actual);}
        internal static void Near(double expected,double actual) {True(Math.Abs(expected-actual)<.001,"Expected "+expected+", actual "+actual);}
        internal static void Throws<T>(Action action) where T:Exception {try {action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    }
}
