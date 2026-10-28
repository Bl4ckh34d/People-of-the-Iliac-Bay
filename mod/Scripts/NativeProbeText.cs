using System;
using System.Globalization;
using FullSerializer;

namespace PeopleOfTheIliacBay.DFU
{
    // Exact machine-format checks for opt-in test fixtures. No dialogue interpretation.
    internal static class NativeProbeText
    {
        internal static bool IsTestCharacter(string value)
        {
            if(value==TownSmokeHarness.CanonicalFixtureCharacter)return true;
            const string prefix="PeopleOfTheIliacBayTest-";
            if(value==null || value.Length!=prefix.Length+8 || !value.StartsWith(prefix,StringComparison.Ordinal))return false;
            for(int i=prefix.Length;i<value.Length;i++)
                if(!(value[i]>='0' && value[i]<='9') && !(value[i]>='a' && value[i]<='f'))return false;
            return true;
        }
        internal static bool ContainsNumber(string text,int number)
        {
            string expected=number.ToString(CultureInfo.InvariantCulture);
            for(int i=0;i<(text??"").Length;)
            {
                if(text[i]<'0' || text[i]>'9'){i++;continue;}
                int start=i;
                while(i<text.Length && text[i]>='0' && text[i]<='9')i++;
                if(text.Substring(start,i-start)==expected)return true;
            }
            return false;
        }
        internal static string WithoutJsonProperty(string json,string property)
        {
            fsData data;var result=fsJsonParser.Parse(json,out data);
            if(result.Failed)throw new InvalidOperationException("Invalid fixture JSON.");
            RemoveProperty(data,property);
            return fsJsonPrinter.CompressedJson(data);
        }
        private static void RemoveProperty(fsData data,string property)
        {
            if(data.IsDictionary)
            {
                data.AsDictionary.Remove(property);
                foreach(var item in data.AsDictionary.Values)RemoveProperty(item,property);
            }
            else if(data.IsList)foreach(var item in data.AsList)RemoveProperty(item,property);
        }
    }
}
