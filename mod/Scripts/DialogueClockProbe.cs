using System;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.UserInterface;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class DialogueClockProbe
    {
        public static void Run(LivingCitiesMod host, DaggerfallTalkWindow talk, ListBox transcript)
        {
            var submit = typeof(LivingCitiesMod).GetMethod("SubmitFreeText", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (string question in new[] { "What time is it?", "What is the current time?", "What date is it?" })
            {
                var now = DaggerfallUnity.Instance.WorldTime.Now;
                string expected = ConversationClock.Answer(now.MinTimeString(), now.DateString(), now.Year);
                int before = transcript.Count;
                submit.Invoke(host, new object[] { talk, question });
                if (transcript.Count != before + 2 || transcript.GetItem(before + 1).textLabel.Text != expected)
                    throw new InvalidOperationException("Clock reply did not match native WorldTime: " + question);
            }
            Debug.Log("LIVING_CITIES_DIALOGUE_CLOCK_PASS: both time phrasings and date match native clock immediately.");
        }
    }
}
