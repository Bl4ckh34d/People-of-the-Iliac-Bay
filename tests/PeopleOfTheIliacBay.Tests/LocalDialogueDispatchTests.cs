using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class LocalDialogueDispatchTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Background dialogue classification yields to foreground work", ForegroundPreemptsBackground));
            cases.Add(("Background dialogue classification cannot start during foreground work", BackgroundWaitsForForeground));
            cases.Add(("Background cancellation is ticket-scoped", BackgroundCancellationIsTicketScoped));
            cases.Add(("Foreground dialogue requests do not preempt each other", ForegroundRequestsRemainExclusive));
        }

        public static int Run()
        {
            var cases = new List<(string Name, Action Body)>();
            Add(cases);
            foreach (var test in cases)
            {
                test.Body();
                Console.WriteLine("PASS: " + test.Name);
            }
            return cases.Count;
        }

        private static void ForegroundPreemptsBackground()
        {
            var dispatch = new LocalDialogueDispatch();
            int background;
            Require(dispatch.TryBeginBackground(out background) && background > 0, "Background request admitted while idle.");
            Require(dispatch.IsBusy && dispatch.IsBackgroundCurrent(background), "Background request owns the slot.");
            int aborts = 0;
            Action backgroundAbort = () => aborts++;
            Require(dispatch.TryRegisterAbort(background, backgroundAbort), "Background abort registered.");

            int foreground = dispatch.TryBegin();

            Require(foreground > 0 && foreground != background, "Foreground request preempts background and gets a new ticket.");
            Require(aborts == 1, "Preemption aborts background transport once.");
            Require(!dispatch.IsBackgroundCurrent(background), "Preempted background ticket is stale.");
            Require(!dispatch.CancelBackground(background), "Stale background ticket cannot cancel current foreground work.");
            Require(dispatch.IsCurrent(foreground) && dispatch.IsBusy, "Foreground request remains active after stale cancellation.");
            int foregroundAborts = 0;
            Action foregroundAbort = () => foregroundAborts++;
            Require(dispatch.TryRegisterAbort(foreground, foregroundAbort), "Foreground abort registered after preemption.");
            Require(!dispatch.ClearAbort(background, backgroundAbort), "Old background worker cannot clear foreground abort.");
            dispatch.Cancel();
            Require(foregroundAborts == 1 && !dispatch.IsBusy, "Global teardown cancels current foreground transport only.");
        }

        private static void BackgroundWaitsForForeground()
        {
            var dispatch = new LocalDialogueDispatch();
            int foreground = dispatch.TryBegin();
            int background;
            Require(foreground > 0, "Foreground request admitted.");
            Require(!dispatch.TryBeginBackground(out background) && background == 0, "Background request rejected while foreground is active.");
            Require(dispatch.IsCurrent(foreground), "Rejected background request leaves foreground unchanged.");
            Require(!dispatch.CancelBackground(foreground), "Background cancellation cannot cancel foreground ticket.");
            Require(dispatch.IsCurrent(foreground), "Foreground remains current after background cancellation attempt.");
        }

        private static void BackgroundCancellationIsTicketScoped()
        {
            var dispatch = new LocalDialogueDispatch();
            int first;
            Require(dispatch.TryBeginBackground(out first), "First background request admitted.");
            int aborts = 0;
            Require(dispatch.TryRegisterAbort(first, () => aborts++), "First background abort registered.");
            Require(dispatch.CancelBackground(first), "Current background ticket cancels.");
            Require(aborts == 1 && !dispatch.IsBusy, "Background cancellation aborts and releases slot.");

            int second;
            Require(dispatch.TryBeginBackground(out second) && second != first, "New background request gets a new ticket.");
            Require(!dispatch.CancelBackground(first), "Old ticket cannot cancel later background work.");
            Require(dispatch.IsBackgroundCurrent(second), "Later background request remains active.");
            Require(dispatch.CancelBackground(second), "Current background ticket can cancel itself.");
        }

        private static void ForegroundRequestsRemainExclusive()
        {
            var dispatch = new LocalDialogueDispatch();
            int first = dispatch.TryBegin();
            Require(first > 0, "First foreground request admitted.");
            Require(dispatch.TryBegin() == 0, "Second foreground request is rejected while first remains active.");
            Require(dispatch.IsCurrent(first) && dispatch.IsBusy, "Rejected foreground request leaves first active.");
            Require(dispatch.TryFinish(first), "Foreground request completes once.");
            Require(!dispatch.IsBusy, "Completed request releases slot.");
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
