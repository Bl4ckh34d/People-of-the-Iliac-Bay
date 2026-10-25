using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class ConversationWeather
    {
        public static string Context(string settlement, string region, string weather)
        {
            string place = string.IsNullOrWhiteSpace(settlement) ? "the wilderness" : settlement.Trim();
            if (!string.IsNullOrWhiteSpace(region)) place += " in " + region.Trim();
            string condition = string.IsNullOrWhiteSpace(weather) ? "unknown" : weather.Trim().ToLowerInvariant();
            if (condition == "thunder") condition = "thunderstorm";
            return "Current location: " + place + ". Current weather outside: " + condition + ".";
        }
    }
}
