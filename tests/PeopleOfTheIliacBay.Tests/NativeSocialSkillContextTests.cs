using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NativeSocialSkillContextTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Native social skill context reports verified skills without claiming a roll", ReportsSkills));
            cases.Add(("Native social skill context keeps unknown trust unknown", UnknownTrust));
            cases.Add(("Native social skill context retains enchanted and debuffed live values", ModifiedLiveValues));
            cases.Add(("Native social skill context rejects invalid native values", InvalidValues));
        }

        private static void ReportsSkills()
        {
            string text = NativeSocialSkillContext.Format(3, 72, 18, 51, 64, 82);
            Require(text.Contains("Nobility") && text.Contains("Etiquette 72") &&
                text.Contains("Streetwise 18") && text.Contains("Mercantile 51") &&
                text.Contains("Personality 64") && text.Contains("reliability trust 82"),
                "Grounded values absent.");
            Require(text.Contains("native result") && text.Contains("quoted price") &&
                text.Contains("quest") && text.Contains("consent"), "Authority boundary absent.");
        }

        private static void UnknownTrust()
        {
            string text = NativeSocialSkillContext.Format(9, 0, 100, 25, 50, null);
            Require(text.Contains("merchant tone modifier") && text.Contains("trust is unestablished") &&
                !text.Contains("reliability trust 0"), "Missing trust or group was invented.");
        }

        private static void ModifiedLiveValues()
        {
            string text = NativeSocialSkillContext.Format(3, 120, -8, 135, -3, null);
            Require(text.Contains("Etiquette 120") && text.Contains("Streetwise -8") &&
                text.Contains("Mercantile 135") && text.Contains("Personality -3"),
                "Live modified skill values were discarded or clamped.");
        }

        private static void InvalidValues()
        {
            foreach (Action invalid in new Action[] {
                () => NativeSocialSkillContext.Format(-1, 50, 50, 50, 50, null),
                () => NativeSocialSkillContext.Format(0, 50, 50, 50, 50, -1) })
            {
                try { invalid(); throw new Exception("Invalid value accepted."); }
                catch (ArgumentOutOfRangeException) { }
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
