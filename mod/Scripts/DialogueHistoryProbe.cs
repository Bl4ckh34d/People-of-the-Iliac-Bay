using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.UserInterface;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class DialogueHistoryProbe
    {
        public static IEnumerator Run(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var pair = typeof(PeopleOfTheIliacBayMod).GetMethod("AppendDialoguePair", flags);
            var read = typeof(PeopleOfTheIliacBayMod).GetMethod("ReadConversationHistory", BindingFlags.Static | BindingFlags.NonPublic);
            for (int i = 0; i < 150; i++)
                pair.Invoke(host, new object[] { talk, "Earlier we discussed the weather and ordinary daily errands, meeting " + i + ".",
                    "Indeed, there is much to do before evening and the weather changes often." });
            pair.Invoke(host, new object[] { talk, "My horse is named Copper.", "Copper is a fine name for your horse." });
            pair.Invoke(host, new object[] { talk, "I enjoy the rain.", "It can be refreshing." });
            pair.Invoke(host, new object[] { talk, "I brought an apple today.", "That sounds pleasant." });
            pair.Invoke(host, new object[] { talk, "I prefer quiet evenings.", "A little peace does us all good." });
            string before = (string)read.Invoke(null, new object[] { talk });
            if (!before.Contains("Player: My horse is named Copper.") || !before.Contains("NPC: A little peace"))
                throw new InvalidOperationException("Full native transcript lost turn text or speaker attribution.");
            typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText", flags).Invoke(host,
                new object[] { talk, "What name did I give my horse earlier?" });
            var busy = typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy", flags);
            float deadline = Time.realtimeSinceStartup + 45f;
            while ((bool)busy.GetValue(host) && Time.realtimeSinceStartup < deadline) yield return null;
            if ((bool)busy.GetValue(host)) throw new InvalidOperationException("History recall request timed out.");
            string after = (string)read.Invoke(null, new object[] { talk });
            string added = after.Substring(before.Length);
            if (!added.Contains("NPC: ") || added.IndexOf("Copper", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Live model failed earlier-turn recall: " + added);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_HISTORY_PASS: oversized native transcript retained; speaker roles; horse name recalled beyond last two exchanges after sliding history.");
        }
    }
}
