using System;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Grounded native social-skill facts for free-text dialogue; never resolves a native roll.</summary>
    public static class NativeSocialSkillContext
    {
        public static string Format(int socialGroup, int etiquette, int streetwise,
            int mercantile, int personality, int? reliabilityTrust)
        {
            if (socialGroup < 0 ||
                (reliabilityTrust.HasValue && (reliabilityTrust.Value < 0 || reliabilityTrust.Value > 100)))
                throw new ArgumentOutOfRangeException(nameof(socialGroup), "Invalid native social context value.");

            string group = socialGroup == 0 ? "Commoners" : socialGroup == 1 ? "Merchants" :
                socialGroup == 2 ? "Scholars" : socialGroup == 3 ? "Nobility" :
                socialGroup == 4 ? "Underworld" : "other native group (merchant tone modifier)";
            string trust = reliabilityTrust.HasValue
                ? " NPC's evidence-based reliability trust " + reliabilityTrust.Value.ToString(CultureInfo.InvariantCulture) +
                    "/100 concerns dependability, not affection."
                : " NPC's reliability trust is unestablished.";
            return "Native social context: NPC social group " + group + "; player Etiquette " +
                etiquette.ToString(CultureInfo.InvariantCulture) + " live, Streetwise " +
                streetwise.ToString(CultureInfo.InvariantCulture) + " live, Mercantile " +
                mercantile.ToString(CultureInfo.InvariantCulture) + " live, Personality " +
                personality.ToString(CultureInfo.InvariantCulture) + " live." + trust +
                " Native polite topic checks use Etiquette; native blunt topic checks use Streetwise. " +
                "Both tone checks include native social-group modifiers and live Personality. " +
                "The native result controls any topic answer. Mercantile and Personality already affect the native quoted price. " +
                "Do not invent a skill success, change a quoted price, grant unknown facts, override duties or quest control, or infer consent.";
        }
    }
}
