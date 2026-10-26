using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;

internal static class Program
{
    private static void Require(bool value, string reason)
    { if (!value) throw new InvalidOperationException(reason); }

    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("catalog includes every eligible native skill with exact trainer and quote", () =>
            {
                var scene = new StreetPopulation();
                var options = scene.Offers();
                Require(options.Count == 2, "Only the first native skill was offered.");
                Require(options[0].Target.ItemId == "3" && options[1].Target.ItemId == "23", "Native skill identities changed.");
                foreach (var option in options)
                    Require(option.Kind == NpcActionKind.Purchase && option.Target.PersonId == "trainer" &&
                        option.Target.QuotedGold == 400 && option.Target.Quantity == 1, "Native service contract changed.");
            }),
            ("non-first skill survives proposal preparation and execution", () =>
            {
                var scene = new StreetPopulation();
                var selected = scene.Offers().Find(option => option.Target.ItemId == "23");
                Require(selected != null, "Requested native skill cannot be selected.");
                var job = scene.Job(selected);
                string reason;
                Require(scene.Prepare(job, out reason), reason);
                Require(job.Target.ItemId == "23" && job.ServicePrice == 400 && job.ExpectedInventoryRevision == 7,
                    "Preparation changed the selected native lesson.");
                job.Phase = NpcActionPhase.Executing;
                Require(scene.Execute(job, out reason), reason);
                Require(NativeNpcTempleTraining.ExecutedSkill == 23, "Executor substituted a different lesson.");
            }),
            ("removed selected skill cannot fall back to another native skill", () =>
            {
                var scene = new StreetPopulation();
                var job = scene.Job(new NpcActionOption { Kind = NpcActionKind.Purchase,
                    Target = scene.Target(3) });
                NativeNpcTempleTraining.Skills.Remove(3);
                string reason;
                Require(!scene.Prepare(job, out reason), "Missing skill was replaced with another lesson.");
            }),
            ("catalog respects existing semantic and legacy limits", () =>
            {
                var scene = new StreetPopulation();
                Require(scene.Offers(255).Count == 256, "Semantic candidate cap changed.");
                Require(scene.Offers(23, false).Count == 24, "Legacy candidate cap changed.");
                Require(scene.Offers(256).Count == 256, "Full catalog was extended.");
            }),
            ("closed inaccessible or unaffordable native service publishes no lesson", () =>
            {
                var scene = new StreetPopulation();
                DaggerfallWorkshop.Game.PlayerActivate.Open = false;
                Require(scene.Offers().Count == 0, "Closed temple offered training.");
                DaggerfallWorkshop.Game.PlayerActivate.Open = true;
                scene.Reachable = false;
                Require(scene.Offers().Count == 0, "Unreachable trainer offered training.");
                scene.Reachable = true;
                NativeNpcTempleTraining.Funded = false;
                Require(scene.Offers().Count == 0, "Unaffordable native lesson was offered.");
            }),
            ("changed trainer and price require a new agreement", () =>
            {
                var scene = new StreetPopulation();
                var job = scene.Job(scene.Offers()[0]);
                string reason;
                job.Target.QuotedGold++;
                Require(!scene.Prepare(job, out reason), "Changed quote was accepted.");
                job.Target.QuotedGold--;
                job.Target.PersonId = "other-trainer";
                Require(!scene.Prepare(job, out reason), "Different trainer was accepted.");
            }),
        };
        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        Console.WriteLine($"Native temple training catalog: {cases.Count - failed}/{cases.Count} passed; {failed} failed.");
        return failed == 0 ? 0 : 1;
    }
}
