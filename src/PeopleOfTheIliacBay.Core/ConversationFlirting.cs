using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class ConversationFlirting
    {
        // Reception belongs to this pair, not to a phrase, frame or random reload.
        // Returns existing bounded tone categories; it never changes faction reputation.
        public static string Tone(string npcId, string playerId, string npcGender, string playerGender,
            bool child, int personalOffset)
        {
            if(child || !KnownGender(npcGender) || !KnownGender(playerGender) ||
                string.IsNullOrEmpty(npcId) || string.IsNullOrEmpty(playerId)) return "neutral";
            if(string.Equals(npcGender,playerGender,StringComparison.OrdinalIgnoreCase) || personalOffset<=-24) return "rude";
            uint hash=2166136261;
            unchecked { foreach(char c in "flirt-v1:"+npcId+"\n"+playerId) {hash^=c;hash*=16777619;} }
            return (StableSeed.Combine(hash,0,0,0)&1)==0 ? "polite" : "rude";
        }

        private static bool KnownGender(string value)
        {
            return string.Equals(value,"Male",StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value,"Female",StringComparison.OrdinalIgnoreCase);
        }
    }
}
