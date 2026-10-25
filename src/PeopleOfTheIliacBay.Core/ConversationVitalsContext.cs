using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public static class ConversationVitalsContext
    {
        public static bool MatchesMobileSpeaker(bool isMobileConversation,object boundSpeaker,object currentSpeaker)
        {return isMobileConversation && boundSpeaker!=null && ReferenceEquals(boundSpeaker,currentSpeaker);}

        public static string Format(bool eligible,bool bound,int currentHealth,int maximumHealth,
            int currentMagicka,int maximumMagicka)
        {
            if(!eligible || !bound || currentHealth<1 || maximumHealth<1 || currentHealth>maximumHealth ||
                currentMagicka<0 || maximumMagicka<1 || currentMagicka>maximumMagicka) return string.Empty;
            return "Current body: "+currentHealth.ToString(CultureInfo.InvariantCulture)+"/"+
                maximumHealth.ToString(CultureInfo.InvariantCulture)+" health and "+
                currentMagicka.ToString(CultureInfo.InvariantCulture)+"/"+
                maximumMagicka.ToString(CultureInfo.InvariantCulture)+
                " magicka. Do not infer skills, spells, or combat readiness from these values.";
        }
    }
}
