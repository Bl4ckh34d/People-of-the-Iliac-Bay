using System;
using System.Collections;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Compatibility step in the mood-dialogue fixture. The old personal
    // catalogue, Mercantile admission, and one-item confirmation box no
    // longer exist. PersonalTradeNativeProbe is the sole explicit native UI
    // run because it owns the real actor/item/window fixture lifecycle.
    internal static class NpcPersonalCommerceDialogueProbe
    {
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, ResidentActor actor,
            WorldStateStore store, DaggerfallTalkWindow talk)
        {
            if (host == null || actor == null || actor.Resident == null || store == null || talk == null)
                throw new InvalidOperationException("Mood fixture lost its current native Talk context.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_COMMERCE_DIALOGUE_DELEGATED: " +
                "legacyCatalog=false; explicitNativeProbe=PersonalTradeNativeProbe; fixtureUnchanged=true");
            yield break;
        }
    }
}
