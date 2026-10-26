using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit disposable fixture preparation. Ordinary production creates,
    // admits and positions the traveler; this helper never edits an NPC.
    internal static class NativeTravelerFixtureWarmup
    {
        private const string Character="PeopleOfTheIliacBayTest-9a5f197b";
        private const string SaveName="People - Daggerfall day 2";
        private const string TravelerId="traveler:1291010263:197386:145804:0";
        private const int Town=1291010263,Inn=197386;
        private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;

        internal static IEnumerator EnterAfterOrdinaryWarmup(int buildingKey,Func<int,IEnumerator> enterInterior)
        {
            string selected=Environment.GetEnvironmentVariable("POTIB_TRAVELER_FIXTURE_INN");
            Require(enterInterior!=null,"Native traveler fixture lacks its existing interior entry adapter.");
            if(string.IsNullOrEmpty(selected)) {yield return enterInterior(buildingKey);yield break;}
            string mode=Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE");
            int requested;
            var game=GameManager.Instance;
            Require(int.TryParse(selected,NumberStyles.None,CultureInfo.InvariantCulture,out requested) && requested==Inn &&
                buildingKey==Inn && (mode=="starting-spells" || mode=="paid-companion" ||
                    mode=="dungeon-follow" && Environment.GetEnvironmentVariable("POTIB_DUNGEON_FOLLOW_PREPARE")=="1") &&
                Environment.GetEnvironmentVariable("POTIB_NAVMESH_TRIAL")=="1" && game && game.PlayerEntity!=null &&
                game.PlayerEntity.Name==Character && Argument("-peopleoftheiliacbay-fixture-character")==Character &&
                Argument("-peopleoftheiliacbay-fixture-save")==SaveName && !game.PlayerEnterExit.IsPlayerInside &&
                game.PlayerGPS.HasCurrentLocation && game.PlayerGPS.CurrentLocation.MapTableData.MapId==Town,
                "Traveler warmup requires the exact owned SAVE18 character/save, Dirty Goat 197386 and an explicit paid/spell roadmap probe.");
            var streets=UnityEngine.Object.FindObjectOfType<StreetPopulation>();
            Require(streets,"Native traveler fixture has no managed street population.");
            var world=typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets) as WorldStateStore;
            Require(world!=null && world.WorldSeed==652049168u,
                "Native traveler fixture world differs from the verified ordinary seed.");
            double start=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            Require(Math.Floor(start/86400d)==145807 && start<12597759840d,
                "Native traveler fixture is outside its natural pre-checkout day. No clock change is allowed.");
            // Dungeon preparation also waits for the normal exterior survey queue.
            // This is a bounded fixture allowance, not a change to production demand.
            int waitSeconds=mode=="dungeon-follow" ? 180 : 90;
            float began=Time.realtimeSinceStartup,deadline=began+waitSeconds,nextReport=0;
            TravelerVisit admitted=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                Require(!game.PlayerEnterExit.IsPlayerInside && game.PlayerEntity.Name==Character,
                    "Traveler warmup must remain outside in its owned disposable character.");
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                Require(Math.Floor(now/86400d)==145807 && now<12597759840d,
                    "Ordinary traveler warmup crossed its natural pre-checkout fixture window. No clock change is allowed.");
                var person=world.Traveler(TravelerId);
                if(streets.Ready && streets.EntranceSurveyReady && person!=null && person.IsAlive)
                {
                    Require(world.ResolveNpcArchetype(TravelerId)==NpcArchetype.Mage &&
                        TravelerAdventurerPolicy.Archetype(person.AppearanceSeed)==NpcArchetype.Mage,
                        "Ordinary traveler identity does not match the expected seeded Mage.");
                    var visit=world.TravelerVisitsFor(Town).Find(v=>v.PersonId==TravelerId && v.TavernKey==Inn);
                    Require(visit==null || visit.Admission!=TravelerAdmission.Refused,
                        "Ordinary room admission refused the actual Mage. The fixture will not manufacture a booking.");
                    if(visit!=null && visit.Admission==TravelerAdmission.Admitted && world.TravelerBed(visit.Id,now)!=null)
                    {admitted=visit;break;}
                }
                if(Time.realtimeSinceStartup>=nextReport)
                {
                    nextReport=Time.realtimeSinceStartup+10;
                    ReportDemand(streets,world,began,waitSeconds,false);
                }
                yield return null;
            }
            if(admitted==null)ReportDemand(streets,world,began,waitSeconds,true);
            Require(admitted!=null,"Ordinary traveler demand/admission did not produce the real Mage within "+waitSeconds+
                " seconds outside. Inspect TRAVELER_FIXTURE_WAIT; missing traveler and pending admission are separate prerequisites.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_FIXTURE_WARMUP_PASS: actor="+TravelerId+
                "; inn="+Inn+"; ordinaryDemand=true; admitted=true; noNpcInjection=true; noTimeSkip=true; start="+start+
                "; now="+DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            yield return enterInterior(buildingKey);
            deadline=Time.realtimeSinceStartup+30;
            ResidentActor actor=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                actor=streets.FindActor(TravelerId);
                if(actor && actor.gameObject.activeInHierarchy && streets.HasBoundTravelerActionPlan(TravelerId))break;
                yield return null;
            }
            Require(actor && actor.gameObject.activeInHierarchy && actor.Resident!=null && actor.Resident.IsAlive &&
                !actor.Resident.IsQuestProtected && !actor.IsSleeping && streets.HasBoundTravelerActionPlan(TravelerId),
                "The admitted generated Mage did not materialize as its actual awake Inn actor.");
            if(mode=="paid-companion" || mode=="dungeon-follow")
            {
                int dailyGold;
                Require(streets.TryReadPaidCompanionWage(TravelerId,out dailyGold),"Actual traveler level is unavailable for fixture funding.");
                int before=game.PlayerEntity.GetGoldAmount(),added=Math.Max(0,dailyGold-before);
                if(added>0)game.PlayerEntity.GoldPieces=checked(game.PlayerEntity.GoldPieces+added);
                Require(game.PlayerEntity.GetGoldAmount()>=dailyGold,
                    "Controlled disposable player purse could not fund the quoted native wage.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_FIXTURE_PLAYER_FUNDS: before="+before+"; added="+added+
                    "; after="+game.PlayerEntity.GetGoldAmount()+"; setupOnly=true; nativeGoldPieces=true");
                PlacePlayerNearActualActor(game,streets,actor);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_FIXTURE_ACTOR_READY: actor="+TravelerId+
                "; name="+actor.Person.NameNPC+"; class=Mage; inn="+Inn+"; ordinaryActor=true; npcMoved=false");
        }

        // Read only: never spends the shared work gate, surveys a route, generates
        // a traveler or advances admission merely to make this fixture ready.
        private static void ReportDemand(StreetPopulation streets,WorldStateStore world,float began,int limit,bool terminal)
        {
            var inns=Field<List<int>>(streets,"travelerDemandInns");
            var completed=Field<HashSet<int>>(streets,"travelerDemandCompleted");
            var routes=Field<Dictionary<int,List<TownGeometry.TravelerAccess>>>(streets,"travelerDemandRoutes");
            var geometry=Field<TownGeometry>(streets,"geometry");
            var directory=Field<BuildingDirectory>(streets,"directory");
            int index=Field<int>(streets,"travelerDemandNextInn");
            List<TownGeometry.TravelerAccess> targetRoutes;
            int cachedRoutes=routes.TryGetValue(Inn,out targetRoutes) ? targetRoutes.Count : -1;
            var visits=world.TravelerVisitsFor(Town);
            var visit=visits.Find(v=>v.PersonId==TravelerId && v.TavernKey==Inn);
            var now=DaggerfallUnity.Instance.WorldTime.Now;
            int openGates=0,gateCount=0;
            if(directory)
                foreach(var gate in directory.GetComponentsInChildren<DaggerfallCityGate>())
                {gateCount++;if(Field<bool>(gate,"isOpen"))openGates++;}
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_FIXTURE_WAIT: ready="+streets.Ready+
                "; entrances="+streets.EntranceSurveyReady+"; ordinaryTraveler="+(world.Traveler(TravelerId)!=null)+"; inn="+Inn+
                "; elapsed="+(Time.realtimeSinceStartup-began).ToString("F1",CultureInfo.InvariantCulture)+"; limit="+limit+
                "; terminal="+terminal+"; playing="+GameManager.Instance.IsPlayingGame()+"; paused="+GameManager.IsGamePaused+
                "; worldSeconds="+now.ToSeconds()+"; night="+now.IsNight+"; openGates="+openGates+"/"+gateCount+
                "; sameStore="+ReferenceEquals(world,Field<WorldStateStore>(streets,"worldStore"))+
                "; demandStoreCurrent="+ReferenceEquals(world,Field<WorldStateStore>(streets,"travelerDemandStore"))+
                "; demandEpochCurrent="+world.IsNpcVitalsWriteEpochCurrent(Field<object>(streets,"travelerDemandEpoch"))+
                "; demandDay="+Field<int>(streets,"travelerDemandDay")+"; demandIndex="+index+
                "; inns="+inns.Count+"; targetIndex="+inns.IndexOf(Inn)+"; nextInn="+(inns.Count>0 ? inns[index%inns.Count] : -1)+
                "; completedInns="+completed.Count+"; targetCompleted="+completed.Contains(Inn)+
                "; cachedRouteInns="+routes.Count+"; targetCachedRoutes="+cachedRoutes+
                "; targetEntrance="+(geometry!=null && geometry.Entrances.ContainsKey(Inn))+
                "; nextDemandIn="+(Field<float>(streets,"nextTravelerDemandAt")-Time.unscaledTime).ToString("F2",CultureInfo.InvariantCulture)+
                "; townVisits="+visits.Count+"; targetVisit="+(visit==null ? "absent" : visit.Admission.ToString())+
                "; targetBed="+(visit!=null && world.TravelerBed(visit.Id,now.ToSeconds())!=null)+
                streets.TravelerDemandProbeStatus(Inn));
        }

        private static T Field<T>(object instance,string name)
        {return (T)instance.GetType().GetField(name,Private).GetValue(instance);}

        private static void PlacePlayerNearActualActor(GameManager game,StreetPopulation streets,ResidentActor actor)
        {
            Vector3 actual=actor.transform.position-Vector3.up;
            if(Vector3.Distance(actual,game.PlayerMotor.transform.position-Vector3.up)<=6)return;
            var room=game.PlayerEnterExit.Interior;
            var geometry=typeof(StreetPopulation).GetField("indoorGeometry",Private).GetValue(streets) as InteriorGeometry;
            Require(room && geometry!=null,"Paid fixture lacks its actual Inn floor geometry.");
            foreach(var local in geometry.StandingNear(room.transform.InverseTransformPoint(actual),6))
            {
                Vector3 ground=room.transform.TransformPoint(local);
                if(Vector3.Distance(ground,actual)<1 || Physics.CheckCapsule(ground+Vector3.up*0.78f,
                    ground+Vector3.up*1.6f,CrowdMotion.BodyRadius,~0,QueryTriggerInteraction.Ignore))continue;
                game.PlayerMotor.transform.position=ground+Vector3.up;Physics.SyncTransforms();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_FIXTURE_PLAYER_APPROACH: actualActor="+TravelerId+
                    "; distance="+Vector3.Distance(ground,actual)+"; playerOnly=true; npcMoved=false");
                return;
            }
            throw new InvalidOperationException("No verified clear player standing point is within six metres of the actual Mage.");
        }

        private static string Argument(string flag)
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,flag);
            return index>=0 && index+1<args.Length?args[index+1]:null;
        }
        private static void Require(bool condition,string message)
        {if(!condition)throw new InvalidOperationException(message);}
    }
}
