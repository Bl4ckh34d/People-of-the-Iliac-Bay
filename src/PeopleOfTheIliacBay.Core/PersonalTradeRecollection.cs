using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public static class PersonalTradeRecollection
    {
        public const string Cancelled="The player opened personal trade with me, then left without a deal. No items or gold changed hands in that session. The reason for leaving is unknown.";
        public static string Completed(IEnumerable<string> bought,IEnumerable<string> sold,int netGoldToNpc,int expectedNetGoldToNpc)
        {
            string money=netGoldToNpc>0?"The player paid me "+netGoldToNpc.ToString(CultureInfo.InvariantCulture)+" g":
                netGoldToNpc<0?"I paid the player "+(-(long)netGoldToNpc).ToString(CultureInfo.InvariantCulture)+" g":"No gold changed hands";
            string assessment=netGoldToNpc==expectedNetGoldToNpc?"a fair price matching my expectation":
                netGoldToNpc>expectedNetGoldToNpc?"better for me than my expected price":"worse for me than my expected price, but I accepted it";
            return "Completed personal trade. Player bought: "+Items(bought)+". Player sold: "+Items(sold)+". "+money+
                ". Overall deal: "+assessment+".";
        }
        private static string Items(IEnumerable<string> values)
        {
            var items=new List<string>();int omitted=0,length=0;
            foreach(string value in values)
            {
                string text=value??"item";
                if(text.Length>70)text=text.Substring(0,70);
                if(length+text.Length>145){omitted++;continue;}
                items.Add(text);length+=text.Length+2;
            }
            if(omitted>0)items.Add("and "+omitted.ToString(CultureInfo.InvariantCulture)+" further item lines");
            return items.Count==0?"nothing":string.Join(", ",items.ToArray());
        }
    }
}
