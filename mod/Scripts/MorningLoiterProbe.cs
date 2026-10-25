using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace PeopleOfTheIliacBay.DFU
{
    // Reproduces the reported native 07:59 -> 08:59 loiter boundary in a
    // disposable exterior fixture and proves ordinary frames resume afterwards.
    internal static class MorningLoiterProbe
    {
        internal const string RunFlag = "-peopleoftheiliacbay-morning-loiter-probe";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static IEnumerator Run(StreetPopulation streets)
        {
            if (!streets || !streets.Ready || GameManager.Instance == null)
                throw new InvalidOperationException("Morning loiter probe requires a ready exterior population.");

            var game = GameManager.Instance;
            if (game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Morning loiter probe requires an exterior fixture.");

            game.PlayerEntity.GodMode = true;
            game.PlayerEntity.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
            DaggerfallUI.Instance.PopToHUD();
            game.PauseGame(false);

            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            double now = clock.ToSeconds();
            double day = Math.Floor(now / 86400d) * 86400d;
            double beforeEight = day + 7d * 3600d + 59d * 60d;
            if (beforeEight <= now) beforeEight += 86400d;
            clock.RaiseTime((float)(beforeEight - now));
            for (int frame = 0; frame < 12; frame++) yield return null;

            // Use DFU's native window and native loiter callback directly. The
            // fixture can contain nearby spawned actors that make the ordinary
            // rest hotkey show a refusal box before this regression reaches the
            // reported time boundary.
            DaggerfallUI.UIManager.PushWindow(new DaggerfallRestWindow(DaggerfallUI.UIManager, false));
            float openDeadline = Time.realtimeSinceStartup + 10f;
            while (!(DaggerfallUI.UIManager.TopWindow is DaggerfallRestWindow) &&
                Time.realtimeSinceStartup < openDeadline) yield return null;
            var rest = DaggerfallUI.UIManager.TopWindow as DaggerfallRestWindow;
            if (rest == null) throw new InvalidOperationException("Native morning loiter window failed to open.");

            double from = clock.ToSeconds();
            typeof(DaggerfallRestWindow).GetMethod("LoiterPrompt_OnGotUserInput", Private)
                .Invoke(rest, new object[] { null, "1" });
            float loiterDeadline = Time.realtimeSinceStartup + 45f;
            while (clock.ToSeconds() - from < 3600d && Time.realtimeSinceStartup < loiterDeadline)
                yield return null;
            if (clock.ToSeconds() - from < 3600d)
                throw new InvalidOperationException("Native 07:59 loiter did not advance through 08:00.");

            DaggerfallUI.Instance.PopToHUD();
            game.PauseGame(false);

            var timer = Stopwatch.StartNew();
            long previous = timer.ElapsedTicks;
            double maximumFrameMilliseconds = 0;
            int completedFrames = 0;
            float resumeDeadline = Time.realtimeSinceStartup + 30f;
            while (completedFrames < 120 && Time.realtimeSinceStartup < resumeDeadline)
            {
                yield return null;
                long current = timer.ElapsedTicks;
                double frameMilliseconds = (current - previous) * 1000d / Stopwatch.Frequency;
                previous = current;
                maximumFrameMilliseconds = Math.Max(maximumFrameMilliseconds, frameMilliseconds);
                completedFrames++;
            }

            if (completedFrames < 120 || !game.IsPlayingGame() ||
                DaggerfallUI.UIManager.TopWindow != DaggerfallUI.Instance.DaggerfallHUD)
                throw new InvalidOperationException("Gameplay did not resume after crossing 08:00 by native loiter.");
            if (maximumFrameMilliseconds >= 5000d)
                throw new InvalidOperationException("Morning population activation blocked a frame for " +
                    maximumFrameMilliseconds.ToString("F1") + " ms.");

            int activeResidents = UnityEngine.Object.FindObjectsOfType<ResidentActor>().Length;
            if (activeResidents > 80)
                throw new InvalidOperationException("Morning population exceeded the exterior actor cap.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MORNING_LOITER_PASS: from=" + from.ToString("F0") +
                "; to=" + clock.ToSeconds().ToString("F0") + "; crossed-08=true; resumed-frames=" +
                completedFrames + "; max-frame-ms=" + maximumFrameMilliseconds.ToString("F1") +
                "; active-residents=" + activeResidents + ".");
        }
    }
}
