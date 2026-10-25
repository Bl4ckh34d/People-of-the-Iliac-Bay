using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Disposable Daggerfall fixture: reproduce 09:00 -> 11:00 with the simulation
    // suspended exactly as the native loiter window suspends gameplay updates.
    public static class IndoorCatchupProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static IEnumerator Run(StreetPopulation streets)
        {
            var game=GameManager.Instance;
            game.PlayerEntity.GodMode=true;
            game.PlayerEntity.CrimeCommitted=DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            double day=Math.Floor(clock.ToSeconds()/86400d)*86400d+86400;
            clock.RaiseTime((float)(day+9*3600-clock.ToSeconds()));
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            bool entered=false;
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                foreach(var door in owner.Doors ?? new StaticDoor[0])
                    if(door.doorType==DoorTypes.Building && door.buildingKey==263177)
                    {TownSmokeHarness.EnterInterior(owner.transform,door);entered=true;break;}
                if(entered)break;
            }
            if(!entered)throw new InvalidOperationException("Catch-up fixture inn door missing.");
            yield return new WaitForSecondsRealtime(2);
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            yield return null;
            // Exercise native minute ticks, UI pause, and UpdateNpcPresence on
            // closing the rest window, rather than approximating loiter by one jump.
            DaggerfallUI.PostMessage(DaggerfallUIMessages.dfuiOpenRestWindow);
            float opened=Time.realtimeSinceStartup+10;
            while(!(DaggerfallUI.UIManager.TopWindow is DaggerfallRestWindow) && Time.realtimeSinceStartup<opened)yield return null;
            var rest=DaggerfallUI.UIManager.TopWindow as DaggerfallRestWindow;
            if(rest==null)throw new InvalidOperationException("Native loiter window failed to open.");
            typeof(DaggerfallRestWindow).GetMethod("LoiterPrompt_OnGotUserInput",Private).Invoke(rest,new object[]{null,"2"});
            double beforeLoiter=clock.ToSeconds();float finish=Time.realtimeSinceStartup+45;
            while(clock.ToSeconds()-beforeLoiter<7200 && Time.realtimeSinceStartup<finish)yield return null;
            if(clock.ToSeconds()-beforeLoiter<7200)throw new InvalidOperationException("Native two-hour loiter interrupted before completion.");
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            for(int i=0;i<12;i++)yield return null;
            Verify(streets,"native two-hour loiter");
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-indoor-crowd-probe")>=0)
                yield return IndoorCrowdProgressProbe.Run(streets);
            // Existing physical visits, including occupied seats, must catch up
            // too. Newcomer reconstruction alone cannot pass this second check.
            game.PauseGame(true);clock.RaiseTime(15*60);yield return null;game.PauseGame(false);
            for(int i=0;i<12;i++)yield return null;
            Verify(streets,"existing visitors");
            var before=new Dictionary<string,double>();
            var physical=(Dictionary<string,PhysicalShopVisit>)typeof(StreetPopulation).GetField("physicalShopVisits",Private).GetValue(streets);
            foreach(var pair in physical)before[pair.Key]=pair.Value.OriginalEndAt;
            game.PauseGame(true);clock.RaiseTime(8*3600);yield return null;game.PauseGame(false);
            for(int i=0;i<12;i++)yield return null;
            int expired=0;
            foreach(var pair in before)
            {
                if(pair.Value>=clock.ToSeconds())continue;
                expired++;
                PhysicalShopVisit remaining;
                if(physical.TryGetValue(pair.Key,out remaining)&&remaining.OriginalEndAt<=pair.Value&&remaining.Snapshot.Phase!=ShopVisitPhase.Complete)
                    throw new InvalidOperationException("Expired visit replayed after time skip: "+pair.Key);
            }
            if(expired<3)throw new InvalidOperationException("Catch-up test lacks expired visits.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_CATCHUP_PASS: repeated skips, separated patrons, sitting and "+expired+" expired visits.");
        }
        private static void Verify(StreetPopulation streets,string label)
        {
            var game=GameManager.Instance;
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            var room=game.PlayerEnterExit.Interior;
            var journeys=(Dictionary<string,ShopVisitJourney>)typeof(StreetPopulation).GetField("shopJourneys",Private).GetValue(streets);
            int expected=0,correct=0,seated=0;
            var positions=new List<Vector3>();
            double now=clock.ToSeconds();
            foreach(var pair in journeys)
            {
                if(pair.Value==null)continue;
                var sample=pair.Value.Evaluate(now);
                if(sample.Phase!=ShopVisitPhase.Browsing)continue;
                var actor=streets.FindActor(pair.Key);
                if(!actor)throw new InvalidOperationException("Scheduled active patron missing after loiter: "+pair.Key);
                expected++;
                var target=room.transform.TransformPoint(new Vector3((float)sample.Position.X,(float)sample.Position.Y,(float)sample.Position.Z));
                float distance=Vector3.Distance(actor.transform.position-Vector3.up,target);
                ShopVisitSnapshot actual;
                bool matches=streets.TryShopActivity(pair.Key,out actual)&&actual.Phase==ShopVisitPhase.Browsing&&distance<0.2f;
                if(matches)correct++;
                if(actor.IsSeated)seated++;
                var ground=actor.transform.position-Vector3.up;
                foreach(var other in positions)
                {
                    var delta=ground-other;delta.y=0;
                    if(Math.Abs(ground.y-other.y)<2.1f&&delta.magnitude<CrowdMotion.Separation-0.01f)
                        throw new InvalidOperationException("Catch-up overlaps patrons: "+pair.Key);
                }
                positions.Add(ground);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CATCHUP_PERSON: id="+pair.Key+"; expected="+sample.Phase+"; distance="+distance+"; seated="+actor.IsSeated+"; matched="+matches);
            }
            if(expected<3 || correct!=expected || seated<1)
                throw new InvalidOperationException("Loiter catch-up failed: expected="+expected+"; correct="+correct+"; seated="+seated);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_CATCHUP_STAGE: "+label+"; expected="+expected+"; correct="+correct+"; seated="+seated);
        }
    }
}
