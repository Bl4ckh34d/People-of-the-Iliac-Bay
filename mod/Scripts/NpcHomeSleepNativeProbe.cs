using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NpcHomeSleepNativeProbe
    {
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            var field=typeof(StreetPopulation).GetField("sleepingArt",BindingFlags.NonPublic|BindingFlags.Instance);
            object original=field.GetValue(streets);
            bool missingArt=Environment.GetEnvironmentVariable("POTIB_HOME_SLEEP_NO_ART")=="1";
            try
            {
                if(missingArt)field.SetValue(streets,null);
                yield return RunCase(host,streets,character,missingArt);
            }
            finally {field.SetValue(streets,original);}
        }
        private static IEnumerator RunCase(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character,bool missingArt)
        {
            string save=null;var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-peopleoftheiliacbay-fixture-save")save=args[i+1];
            var game=GameManager.Instance;
            if(game.PlayerEnterExit.IsPlayerInside)game.PlayerEnterExit.TransitionExterior(true);
            float until=Time.realtimeSinceStartup+90;
            while((game.PlayerEnterExit.IsPlayerInside || !streets.Ready) && Time.realtimeSinceStartup<until)yield return null;
            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(streets);
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var population=(SettlementPopulation)typeof(StreetPopulation).GetField("population",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(streets);
            var art=(SleepingArt)typeof(StreetPopulation).GetField("sleepingArt",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(streets);
            Resident person=null;DaggerfallStaticDoors doorOwner=null;StaticDoor door=default(StaticDoor);
            foreach(var home in population.Households)
            {
                if(home.Residents.Count!=1 || home.BedCapacity<1)continue;
                var candidate=home.Residents[0];
                if(!candidate.IsAlive || candidate.IsQuestProtected || candidate.AgeGroup!=ResidentAgeGroup.Adult ||
                    !missingArt && art.ForResident(candidate)==null || store.ActiveNpcAction(candidate.Id)!=null ||
                    store.ActiveNpcTask(candidate.Id)!=null || store.ReadNpcHealthCare(candidate.Id)!=null)continue;
                foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var entry in owner.Doors)
                    if(entry.doorType==DoorTypes.Building && entry.buildingKey==home.BuildingKey)
                    {person=candidate;doorOwner=owner;door=entry;break;}
                if(person!=null)break;
            }
            Require(person!=null,"No verified single-adult home with sleep art for ordinary rest fixture.");
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            int sleepHour=(int)typeof(StreetPopulation).GetField("sleepHour",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(streets);
            int wakeHour=(int)typeof(StreetPopulation).GetField("wakeHour",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(streets);
            double evening=Math.Floor(now/86400)*86400+(sleepHour-1)*3600;
            if(evening<now)evening+=86400;
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(evening-now));
            TownSmokeHarness.EnterInterior(doorOwner.transform,door);
            yield return new WaitForSecondsRealtime(2);DaggerfallUI.Instance.PopToHUD();
            var actor=streets.FindActor(person.Id);Require(actor,"Home resident did not materialize.");
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            int maximum=body.Entity.MaxHealth;
            int rate=NativeNpcRestTreatment.RecoveryRatePerGameHour(body.Entity);
            int injured=Math.Max((maximum*3+3)/4,maximum-2*rate);
            Require(injured<maximum,"Sleep fixture needs a missing-health range above two-thirds.");
            body.Entity.SetHealth(injured);actor.GetComponent<NativeNpcVitalsBinding>().Capture();
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime(3600);
            yield return new WaitForSecondsRealtime(1);
            actor=streets.FindActor(person.Id);
            float bedDeadline=Time.realtimeSinceStartup+30;
            while(!Resting(streets,actor,missingArt) && Time.realtimeSinceStartup<bedDeadline)
            {yield return null;actor=streets.FindActor(person.Id);}
            Require(Resting(streets,actor,missingArt),"Ordinary sleep did not use its real bed; missingArt="+missingArt);
            body=actor.GetComponent<DaggerfallEntityBehaviour>();
            Require(body.Entity.CurrentHealth==injured,"Rest healed before a full native hour.");
            rate=NativeNpcRestTreatment.RecoveryRatePerGameHour(body.Entity);
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime(3600);
            yield return new WaitForSecondsRealtime(1);
            actor=streets.FindActor(person.Id);body=actor.GetComponent<DaggerfallEntityBehaviour>();
            Require(body.Entity.CurrentHealth==Math.Min(maximum,injured+rate),"Ordinary bed rest did not apply the exact native hourly rate.");

            var ownerItems=actor.GetComponent<NativeNpcInventoryOwner>();
            for(int i=ownerItems.Binding.Items.Count-1;i>=0;i--)
                if(ownerItems.Binding.Items.GetItem(i).IsPotion)ownerItems.Binding.Items.RemoveItem(ownerItems.Binding.Items.GetItem(i));
            ownerItems.Capture();
            var wallet=store.ReadNpcInventory(person.Id);
            Require(store.CommitNpcInventoryTransaction("sleep-fixture:"+wallet.Revision,new[] {new NpcInventoryUpdate {
                PersonId=person.Id,ExpectedRevision=wallet.Revision,WalletGold=0,
                Items=NativeNpcInventoryBridge.CaptureItems(ownerItems.Binding.Items)}}),"Fixture wallet could not be set.");
            ownerItems.Capture();
            for(int i=body.Entity.SpellbookCount()-1;i>=0;i--)body.Entity.DeleteSpell(i);
            body.Entity.SetHealth(injured);body.Entity.SetMagicka(0);var effects=actor.GetComponent<EntityEffectManager>();
            // Consumption blocks rest without Plague's native midnight HP damage.
            effects.AssignBundle(effects.CreateDisease(Diseases.Consumption),AssignBundleFlags.BypassSavingThrows);
            actor.GetComponent<NativeNpcVitalsBinding>().Capture();
            int before=body.Entity.CurrentHealth;
            Require(before<maximum,"Disease blocking fixture must still be injured.");
            yield return new WaitForSecondsRealtime(0.6f);
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime(3600);
            yield return new WaitForSecondsRealtime(1);
            actor=streets.FindActor(person.Id);body=actor.GetComponent<DaggerfallEntityBehaviour>();effects=actor.GetComponent<EntityEffectManager>();
            Require(effects.DiseaseCount==1 && body.Entity.CurrentHealth==before,"Untreated disease did not block ordinary healing: diseases="+
                effects.DiseaseCount+"; health="+before+"->"+body.Entity.CurrentHealth);
            effects.CureAllDiseases();actor.GetComponent<NativeNpcVitalsBinding>().Capture();
            yield return new WaitForSecondsRealtime(0.6f);
            int nightHours=(24-sleepHour+wakeHour)%24;
            int remainingHours=nightHours-3;
            Require(remainingHours>=1,"Configured sleep window is too short for the rest fixture.");
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime(remainingHours*3600);
            yield return new WaitForSecondsRealtime(1);
            actor=streets.FindActor(person.Id);body=actor.GetComponent<DaggerfallEntityBehaviour>();
            Require(body.Entity.CurrentHealth==maximum,"Remaining injury did not finish healing during the ordinary night.");
            Require(store.ReadNpcInventory(person.Id).WalletGold==0,"Free home rest changed the wallet.");

            body.Entity.SetHealth(injured);actor.GetComponent<NativeNpcVitalsBinding>().Capture();
            game.PlayerEnterExit.TransitionExterior(true);
            yield return new WaitForSecondsRealtime(1);
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime(24*3600);
            yield return new WaitForSecondsRealtime(1);
            TownSmokeHarness.EnterInterior(doorOwner.transform,door);
            yield return new WaitForSecondsRealtime(2);DaggerfallUI.Instance.PopToHUD();
            actor=streets.FindActor(person.Id);Require(Resting(streets,actor,missingArt),"Resident missing from its bed after an unloaded ordinary night.");
            body=actor.GetComponent<DaggerfallEntityBehaviour>();
            Require(body.Entity.CurrentHealth==maximum,"Known home sleep did not catch up on materialization.");
            Require(store.ReadNpcInventory(person.Id).WalletGold==0,"Unloaded home rest charged money.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOME_SLEEP_NATIVE_PASS: nativeHourRate=true; realHomeBed=true; diseaseBlocks=true; fullNightRecovery=true; unloadedNight=true; noPotionOrGold=true");
            if(missingArt)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOME_SLEEP_NO_ART_PASS: standingBesideAssignedBed=true; healingWithoutSprite=true");
            yield return ConversationMoodProbe.LoadSave(host,streets,character,save,false);
        }
        private static bool Resting(StreetPopulation streets,ResidentActor actor,bool missingArt)
        {
            if(!actor)return false;
            if(!missingArt)return actor.IsSleeping;
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            var claims=(System.Collections.Generic.Dictionary<string,BedAssignment>)typeof(StreetPopulation).GetField("bedClaims",flags).GetValue(streets);
            var beds=(System.Collections.Generic.Dictionary<int,BedAnchor>)typeof(StreetPopulation).GetField("indoorBeds",flags).GetValue(streets);
            var geometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",flags).GetValue(streets);
            BedAssignment claim;Vector3 beside;
            return !actor.IsSleeping && claims.TryGetValue(actor.Resident.Id,out claim) &&
                geometry.StandBesideBed(beds[claim.BedRecordIndex],out beside) &&
                Vector3.Distance(actor.transform.position-Vector3.up,beside)<=0.65f;
        }
        static void Require(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    }
}
