using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // The room may create a new guard between coroutine samples. Suppress damage
    // before native EnemyAttack.Update, including after combat startup enables it.
    [DefaultExecutionOrder(-10000)]
    internal sealed class GuardDoorProbeSafety : MonoBehaviour
    {
        internal GuardPopulation Guards;
        internal string First,Second;
        private void Update(){if(Guards)Guards.ProbeDoorMute(First,Second);}
    }

    // Explicit disposable-fixture acceptance, never called by ordinary play.
    // Placement and the two sourced 1 HP hits are staged. Crossing, perception,
    // pursuit, streaming and target loss must run through production adapters.
    internal static class GuardDoorNativeProbe
    {
        internal static void Require(bool condition,string message)
        {if(!condition)throw new InvalidOperationException("Guard door probe: "+message);}
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            Require(Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE")=="guard-door" &&
                !string.IsNullOrEmpty(character) && character.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal),
                "requires guard-door and an owned fixture character.");
            string mode=Environment.GetEnvironmentVariable("POTIB_GUARD_DOOR_MODE")??"witnessed";
            Require(mode=="witnessed" || mode=="unwitnessed","mode must be witnessed or unwitnessed.");
            var game=GameManager.Instance;var guards=UnityEngine.Object.FindObjectOfType<GuardPopulation>();
            Require(host && streets && guards && game!=null && game.PlayerEntity.Name==character &&
                !game.PlayerEnterExit.IsPlayerInside,"requires loaded exterior host, residents and guards.");
            float deadline=Time.realtimeSinceStartup+120;
            while(Time.realtimeSinceStartup<deadline && (!streets.Ready || guards.Roster==null || guards.ActiveCount<2))yield return null;
            Require(streets.Ready && guards.Roster!=null && guards.ActiveCount>=2,"exterior did not become ready.");
            yield return streets.ProbeGuardDoor(host,guards,mode=="witnessed");
        }
        internal static GuardActor Actor(string id)
        {
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                if(actor.GuardId==id && actor.gameObject.activeInHierarchy)return actor;
            return null;
        }
        internal static void Quiet(GuardActor actor,bool freezeMotor)
        {
            if(!actor)return;
            // Disable only the actor's optional archery tick and damage delivery;
            // GuardPopulation continues its actual physical route/search updates.
            actor.enabled=false;actor.GetComponent<EnemyAttack>().enabled=false;
            if(freezeMotor)actor.GetComponent<EnemyMotor>().enabled=false;
        }
        internal static WorldStateStore SaveCopy(PeopleOfTheIliacBayMod host)
        {
            var captured=(WorldState)host.GetSaveData();var serializer=new FullSerializer.fsSerializer();
            FullSerializer.fsData data;Require(serializer.TrySerialize(captured,out data).Succeeded,"live save serialization failed.");
            WorldState restored=null;
            Require(serializer.TryDeserialize(FullSerializer.fsJsonParser.Parse(FullSerializer.fsJsonPrinter.CompressedJson(data)),
                ref restored).Succeeded,"live save deserialization failed.");
            var detached=new WorldStateStore(0);detached.Restore(restored);return detached;
        }
    }

    public sealed partial class GuardPopulation
    {
        internal void ProbeDoorMute(string first,string second)
        {
            GuardActor actor;
            if(first!=null && actors.TryGetValue(first,out actor))GuardDoorNativeProbe.Quiet(actor,true);
            if(second!=null && actors.TryGetValue(second,out actor))GuardDoorNativeProbe.Quiet(actor,true);
        }
        internal List<GuardActor> ProbeDoorAvailableGuards()
        {
            var result=new List<GuardActor>();
            foreach(var record in Roster.Guards)
            {
                GuardActor actor;
                if(record.IsAlive && record.Duty==GuardDuty.Posted && record.Interior==null && record.Refuge==null &&
                    record.PostRecall==null && actors.TryGetValue(record.Id,out actor) && actor && actor.gameObject.activeInHierarchy &&
                    !actor.IsInterior && !actor.CombatReady && !actor.DefenseTarget)result.Add(actor);
                if(result.Count==2)break;
            }
            return result;
        }
        internal void ProbeDoorAssign(string report,List<GuardActor> selected,ResidentActor resident,Vector3[] grounds)
        {
            double now=Now();var routes=new Dictionary<string,List<SavedRoutePoint>>();
            foreach(var actor in selected)
            {var point=Roster.Position(actor.GuardId,now);routes.Add(actor.GuardId,new List<SavedRoutePoint>{point,point});}
            GuardDoorNativeProbe.Require(Roster.DispatchDefense(report,resident.Resident.Id,now,routes,2,2.8,
                DaggerfallUnity.Instance.WorldTime.TimeScale).Count==2,"could not assign exactly two existing guards.");
            for(int i=0;i<selected.Count;i++)
            {
                var actor=selected[i];var body=actor.GetComponent<DaggerfallEntityBehaviour>();
                Roster.Engage(actor.GuardId,now);
                float offset=actor.transform.position.y-actor.GroundPosition.y;
                actor.transform.position=grounds[i]+Vector3.up*offset;
                Roster.ObserveEngagement(actor.GuardId,Local(actor.transform.position),
                    (double)body.Entity.CurrentHealth/body.Entity.MaxHealth,now,true,Local(resident.transform.position));
                actor.BeginDefenseCombat(resident.GetComponent<DaggerfallEntityBehaviour>(),now);
            }
        }
        internal void ProbeDoorRestore(GuardRoster saved,string first,string second,string report)
        {
            // Clear native views before restoring records; Capture must not write
            // staged room positions back over the original guard assignments.
            ClearViews();
            foreach(string id in new[]{first,second})
            {
                var live=Roster.Guards.Find(item=>item.Id==id);var original=saved.Guards.Find(item=>item.Id==id);
                if(live==null || original==null || !live.IsAlive)continue; // Never resurrect.
                foreach(var field in typeof(GuardRecord).GetFields(BindingFlags.Instance|BindingFlags.Public))
                    field.SetValue(live,field.GetValue(original));
            }
            Roster.Reports.RemoveAll(item=>item.Id==report);
        }
    }

    public sealed partial class StreetPopulation
    {
        private sealed class GuardDoorFixture
        {
            public ResidentActor Resident;
            public DaggerfallStaticDoors Owner;
            public StaticDoor Door;
            public Vector3 DoorGround,ResidentGround,PlayerGround;
            public Vector3[] GuardGrounds;
        }
        private Vector3 ProbeDoorGround(Vector3 point)
        {
            var local=directory.transform.InverseTransformPoint(point);
            return geometry.ScenePoint(new RoutePoint(local.x,local.z));
        }
        private static bool ProbeDoorClear(Vector3 ground)
        {
            foreach(var collider in Physics.OverlapCapsule(ground+Vector3.up*.3f,ground+Vector3.up*1.65f,.22f,~0,QueryTriggerInteraction.Ignore))
                if(CivilianResponses.Blocker(collider))return false;
            foreach(var hit in Physics.RaycastAll(ground+Vector3.up*.4f,Vector3.down,.8f,~0,QueryTriggerInteraction.Ignore))
                if(CivilianResponses.Blocker(hit.collider) && hit.normal.y>.85f && Mathf.Abs(hit.point.y-ground.y)<.3f)return true;
            return false;
        }
        private GuardDoorFixture ProbeDoorChoose(List<GuardActor> selected)
        {
            ResidentActor resident=null;double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            foreach(var actor in actors.Values)
            {
                var body=actor?actor.GetComponent<DaggerfallEntityBehaviour>():null;
                if(actor && actor.gameObject.activeInHierarchy && !actor.IsSleeping && !actor.IsSeated && !actor.IsConversing &&
                    actor.Resident!=null && actor.Resident.IsAlive && !actor.Resident.IsQuestProtected &&
                    plans.Exists(plan=>plan.Resident.Id==actor.Resident.Id) && body && body.Entity is CivilianEntity &&
                    body.Entity.CurrentHealth>1 && !body.Entity.IsInvisible && !body.GetComponent<GuardAttackEvidence>() &&
                    worldStore.ActiveAlarm(actor.Resident.Id,now)==null && worldStore.ActiveNpcAction(actor.Resident.Id)==null &&
                    worldStore.ActiveNpcTask(actor.Resident.Id)==null && !lawfulGuardFlights.ContainsKey(actor.Resident.Id) &&
                    worldStore.ResidentDefenseShelter(actor.Resident.Id,population.SettlementId)==null)
                {resident=actor;break;}
            }
            GuardDoorNativeProbe.Require(resident,"no unoccupied living ordinary resident with a nonlethal health margin.");
            var nearby=new List<TownGeometry.Entrance>(geometry.Entrances.Values);
            nearby.Sort((a,b)=>(geometry.SceneDoor(a)-resident.transform.position).sqrMagnitude.CompareTo(
                (geometry.SceneDoor(b)-resident.transform.position).sqrMagnitude));
            int checkedDoors=0;
            foreach(var entrance in nearby)
            {
                if(checkedDoors++>=32)break;
                BuildingSummary summary;StaticDoor door;
                if(!directory.GetBuildingSummary(entrance.BuildingKey,out summary) ||
                    !(RMBLayout.IsShop(summary.BuildingType) || summary.BuildingType==DFLocation.BuildingTypes.Tavern ||
                      summary.BuildingType==DFLocation.BuildingTypes.GuildHall) ||
                    !ShelterAccessible(entrance.BuildingKey,resident.Resident.HomeKey,null,now) ||
                    !ReadBuildingActionDoor(entrance.BuildingKey,null,out door))continue;
                var foot=FollowExteriorDoorGround(door);var normal=DaggerfallStaticDoors.GetDoorNormal(door);normal.y=0;
                if(normal.sqrMagnitude<.01f)continue;normal.Normalize();var side=Vector3.Cross(Vector3.up,normal);
                var residentGround=ProbeDoorGround(foot+normal*2.4f);
                var guardGrounds=new[]{ProbeDoorGround(foot+normal*7+side*1.5f),ProbeDoorGround(foot+normal*7-side*1.5f)};
                var observer=ProbeDoorGround(foot+normal*9+side*6);
                if(Mathf.Abs(ProbeDoorGround(foot).y-foot.y)>.3f || !ProbeDoorClear(foot) || !ProbeDoorClear(residentGround) ||
                    !ProbeDoorClear(guardGrounds[0]) || !ProbeDoorClear(guardGrounds[1]) || !ProbeDoorClear(observer) ||
                    !CivilianResponses.Sees(residentGround+Vector3.up,foot+Vector3.up,20) ||
                    !CivilianResponses.Sees(guardGrounds[0]+Vector3.up,foot+Vector3.up,20) ||
                    !CivilianResponses.Sees(guardGrounds[1]+Vector3.up,foot+Vector3.up,20) ||
                    !CivilianResponses.Sees(residentGround+Vector3.up,observer+Vector3.up,20))continue;
                var destination=directory.transform.InverseTransformPoint(foot);
                var local=directory.transform.InverseTransformPoint(residentGround);
                var end=new SavedRoutePoint{X=destination.x,Z=destination.z};
                if(geometry.RouteBetweenPoints(new SavedRoutePoint{X=local.x,Z=local.z},end).Count<2)continue;
                bool reachable=true;
                for(int i=0;i<2;i++)
                {
                    local=directory.transform.InverseTransformPoint(guardGrounds[i]);
                    if(geometry.RouteBetweenPoints(new SavedRoutePoint{X=local.x,Z=local.z},end).Count<2 ||
                        geometry.RouteBetweenPoints(end,GetComponent<GuardPopulation>().Roster.Guards.Find(item=>item.Id==selected[i].GuardId).Post).Count<2)
                    {reachable=false;break;}
                }
                if(!reachable)continue;
                var source=buildingActionEntrances[entrance.BuildingKey];
                return new GuardDoorFixture{Resident=resident,Owner=source.Owner,Door=door,DoorGround=foot,
                    ResidentGround=residentGround,GuardGrounds=guardGrounds,PlayerGround=observer};
            }
            throw new InvalidOperationException("Guard door probe: no accessible real primary doorway with clear supported staging and return routes.");
        }

        internal IEnumerator ProbeGuardDoor(PeopleOfTheIliacBayMod host,GuardPopulation guards,bool witnessed)
        {
            var game=GameManager.Instance;var selected=guards.ProbeDoorAvailableGuards();
            GuardDoorNativeProbe.Require(selected.Count==2,"requires two existing posted guards.");
            var fixture=ProbeDoorChoose(selected);var originalResident=fixture.Resident;
            var residentState=originalResident.Resident;
            string id=originalResident.Resident.Id,first=selected[0].GuardId,second=selected[1].GuardId;
            int settlement=population.SettlementId;string doorId=BuildingActionDoorId(fixture.Door);
            var savedRoster=GuardRoster.CopyValidated(guards.Roster);
            string report="guard-door-probe:"+Guid.NewGuid().ToString("N");
            var body=originalResident.GetComponent<DaggerfallEntityBehaviour>();
            int oldHealth=body.Entity.CurrentHealth,oldPlayerHealth=game.PlayerEntity.CurrentHealth;
            GuardDoorNativeProbe.Require(oldPlayerHealth>1,"player has no nonlethal health margin.");
            var oldPosition=originalResident.transform.position;var oldFacing=originalResident.transform.rotation;
            var oldParent=originalResident.transform.parent;var oldPlayer=game.PlayerMotor.transform.position;
            var oldPlayerRotation=game.PlayerMotor.transform.rotation;
            bool oldDefense=guards.EnemyDefense,oldGuardEnabled=guards.enabled,oldReactions=ReactionsEnabled,oldEnabled=enabled;
            bool oldGod=game.PlayerEntity.GodMode,oldMotor=game.PlayerMotor.enabled;
            var controller=game.PlayerMotor.GetComponent<CharacterController>();bool oldController=controller && controller.enabled;
            var oldCrime=game.PlayerEntity.CrimeCommitted;ResidentActor removed=null,controlledResident=null;bool transitioned=false;
            bool controlledResidentEnabled=true;
            var hidden=new List<GuardActor>();
            var safety=gameObject.AddComponent<GuardDoorProbeSafety>();safety.Guards=guards;safety.First=first;safety.Second=second;
            try
            {
                enabled=false;guards.EnemyDefense=true;ReactionsEnabled=true;
                game.PlayerEntity.GodMode=false;game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;
                game.PlayerMotor.enabled=false;if(controller)controller.enabled=false;
                game.PlayerMotor.transform.position=fixture.PlayerGround+Vector3.up;
                var navigation=originalResident.GetComponent<NativeResidentNavigation>();if(navigation)navigation.ResetPlacement();
                originalResident.GetComponent<CrowdMotion>().RestorePlacement(fixture.ResidentGround);
                guards.ProbeDoorAssign(report,selected,originalResident,fixture.GuardGrounds);
                // Staged native source event establishes real resident-assault eligibility.
                // Other posted witnesses are hidden for this synchronous call only.
                foreach(var other in UnityEngine.Object.FindObjectsOfType<GuardActor>())
                    if(!selected.Contains(other) && other.gameObject.activeSelf){hidden.Add(other);other.gameObject.SetActive(false);}
                try {game.PlayerEntityBehaviour.DamageHealthFromSource(body,1,true,body.transform.position);}
                finally {foreach(var other in hidden)if(other)other.gameObject.SetActive(true);hidden.Clear();}
                GuardDoorNativeProbe.Require(game.PlayerEntity.CurrentHealth==oldPlayerHealth-1 && body.GetComponent<GuardAttackEvidence>(),
                    "native sourced resident hit did not establish assault evidence.");
                float deadline=Time.realtimeSinceStartup+5;
                while(Time.realtimeSinceStartup<deadline && (!selected[0].CombatReady || !selected[1].CombatReady))
                {foreach(var actor in selected)GuardDoorNativeProbe.Quiet(actor,true);yield return null;}
                GuardDoorNativeProbe.Require(selected[0].CombatReady && selected[1].CombatReady,"native guard targeting did not start.");
                foreach(var actor in selected)GuardDoorNativeProbe.Quiet(actor,true);
                selected[0].transform.forward=(witnessed?1:-1)*(originalResident.transform.position-selected[0].transform.position).normalized;
                selected[1].transform.forward=(selected[1].transform.position-originalResident.transform.position).normalized;
                GuardDoorNativeProbe.Require(GuardPopulation.SeesDoorTarget(selected[0],body)==witnessed &&
                    !GuardPopulation.SeesDoorTarget(selected[1],body),"native FOV/LOS does not match controlled witness condition.");
                enabled=true;
                body.DamageHealthFromSource(selected[0].GetComponent<DaggerfallEntityBehaviour>(),1,true,selected[0].transform.position);
                GuardDoorNativeProbe.Require(body.Entity.CurrentHealth==oldHealth-1 && lawfulGuardFlights.ContainsKey(id),
                    "native guard-source hit did not start ordinary lawful flight.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_DOOR_STAGE: mode="+(witnessed?"witnessed":"unwitnessed")+
                    "; resident="+id+"; witness="+first+"; backward="+second+"; door="+doorId+
                    "; nativeDoorHeight="+fixture.Door.size.y+"; stagedBodies=true; stagedSourcedHits=true; playerCollider=false");
                Vector3 last=originalResident.transform.position;float travel=0,maxStep=0,heartbeat=0;int movingSamples=0;
                DefenseShelter shelter=null;deadline=Time.realtimeSinceStartup+60;
                while(Time.realtimeSinceStartup<deadline)
                {
                    foreach(var actor in selected)GuardDoorNativeProbe.Quiet(actor,true);
                    if(originalResident && originalResident.gameObject.activeInHierarchy)
                    {float step=Vector3.Distance(last,originalResident.transform.position);if(step>.01f)movingSamples++;
                        travel+=step;maxStep=Mathf.Max(maxStep,step);last=originalResident.transform.position;}
                    shelter=worldStore.ResidentDefenseShelter(id,settlement);if(shelter!=null)break;
                    if(Time.realtimeSinceStartup>=heartbeat)
                    {heartbeat=Time.realtimeSinceStartup+3;Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_DOOR_WALK: travel="+travel+"; doorDistance="+Vector3.Distance(last-Vector3.up,fixture.DoorGround));}
                    yield return null;
                }
                GuardDoorNativeProbe.Require(shelter!=null && shelter.DoorId==doorId && !shelter.LandingObserved,
                    "ordinary physical flight did not reach the selected real doorway before timeout.");
                var crossed=directory.transform.TransformPoint(new Vector3((float)shelter.Reaction.X,(float)shelter.Reaction.Y,(float)shelter.Reaction.Z));
                GuardDoorNativeProbe.Require(travel>1 && movingSamples>=2 && Vector3.Distance(crossed,fixture.DoorGround)<=.65f,
                    "entry lacks measured exterior movement and actual doorway proximity.");
                deadline=Time.realtimeSinceStartup+3;
                while(Time.realtimeSinceStartup<deadline && originalResident && originalResident.gameObject.activeInHierarchy)yield return null;
                GuardDoorNativeProbe.Require(!originalResident || !originalResident.gameObject.activeInHierarchy,
                    "crossed resident still has an active exterior body.");
                var firstRecord=guards.Roster.Guards.Find(item=>item.Id==first);var secondRecord=guards.Roster.Guards.Find(item=>item.Id==second);
                GuardDoorNativeProbe.Require((firstRecord.DoorPursuit!=null)==witnessed && secondRecord.DoorPursuit==null &&
                    guards.Roster.Reports.Find(item=>item.Id==report).BuildingKey==null,"per-guard observation leaked or shared report learned the room.");
                int followers=guards.Roster.Guards.FindAll(item=>item.DoorPursuit!=null && item.DoorPursuit.ThreatId==id).Count;
                GuardDoorNativeProbe.Require(followers==(witnessed?1:0) && followers<=GuardRoster.MaximumCorpseSearchGuards,
                    "unexpected guard received doorway evidence.");
                var copied=GuardDoorNativeProbe.SaveCopy(host);
                GuardDoorNativeProbe.Require(copied.ResidentDefenseShelter(id,settlement).DoorId==doorId &&
                    (copied.GetGuards(settlement).Guards.Find(item=>item.Id==first).DoorPursuit!=null)==witnessed,
                    "live exterior save-copy lost private shelter or individual knowledge.");
                if(witnessed)
                {
                    var pursuer=selected[0];var start=fixture.GuardGrounds[0];float moved=Vector3.Distance(start,pursuer.GroundPosition);
                    deadline=Time.realtimeSinceStartup+30;
                    while(Time.realtimeSinceStartup<deadline && firstRecord.Duty!=GuardDuty.Searching)
                    {if(pursuer)moved=Mathf.Max(moved,Vector3.Distance(start,pursuer.GroundPosition));yield return null;}
                    GuardDoorNativeProbe.Require(pursuer && firstRecord.Duty==GuardDuty.Searching && moved>1 &&
                        Vector3.Distance(pursuer.GroundPosition,fixture.DoorGround)<.65f && !pursuer.gameObject.activeSelf,
                        "witness did not physically approach and cross its exact native door.");
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_DOOR_EXTERIOR_PASS: witnessed="+witnessed+"; travel="+travel+
                    "; maximumFrameStep="+maxStep+"; individualEvidence=true; exteriorSaveCopy=true; physicalDoor=true");
                guards.enabled=false; // Stream the resident first, preserving the pursuit deadline.
                TownSmokeHarness.EnterInterior(fixture.Owner.transform,fixture.Door);transitioned=true;
                deadline=Time.realtimeSinceStartup+45;ResidentActor landed=null;GuardActor indoorGuard=null;
                while(Time.realtimeSinceStartup<deadline)
                {
                    landed=FindActor(id);
                    if(Ready && interior && interior.EntryDoor.buildingKey==fixture.Door.buildingKey && landed &&
                        landed.gameObject.activeInHierarchy && landed.transform.parent==interior.transform)break;
                    yield return null;
                }
                GuardDoorNativeProbe.Require(landed && interior && landed.transform.parent==interior.transform &&
                    indoorGeometry!=null && indoorGeometry.EntryGround.HasValue,"matching room did not produce the actual sheltered resident landing.");
                shelter=worldStore.ResidentDefenseShelter(id,settlement);
                GuardDoorNativeProbe.Require(shelter.LandingObserved && Vector3.Distance(landed.transform.position-Vector3.up,
                    indoorGeometry.EntryGround.Value)<.65f,"resident landing was not the real matching entrance.");
                enabled=false;guards.enabled=true;deadline=Time.realtimeSinceStartup+8;
                do
                {
                    yield return null;indoorGuard=GuardDoorNativeProbe.Actor(first);
                    GuardDoorNativeProbe.Quiet(indoorGuard,true);
                }
                while(Time.realtimeSinceStartup<deadline && witnessed && (!indoorGuard || !indoorGuard.IsInterior));
                GuardDoorNativeProbe.Require(GuardDoorNativeProbe.Actor(second)==null && (indoorGuard!=null)==witnessed,
                    "unobserving guard appeared in loaded refuge.");
                if(indoorGuard)
                {
                    GuardDoorNativeProbe.Quiet(indoorGuard,true);
                    GuardDoorNativeProbe.Require(Vector3.Distance(indoorGuard.GroundPosition,indoorGeometry.EntryGround.Value)<.65f,
                        "guard did not land at the real matching entrance.");
                    // Separate overlapping entry bodies on a reachable real room anchor;
                    // this is explicit setup for the native reacquisition control.
                    Vector3 anchor=Vector3.zero;bool found=false;
                    foreach(var local in indoorGeometry.LocalAnchors)
                    {var point=interior.transform.TransformPoint(local);float distance=Vector3.Distance(point,indoorGeometry.EntryGround.Value);
                        if(distance>3 && distance<12 && ProbeDoorClear(point) && CivilianResponses.Sees(point+Vector3.up,landed.transform.position,20) &&
                            indoorGeometry.RouteBetween(interior.transform.InverseTransformPoint(indoorGeometry.EntryGround.Value),local).Count>=2)
                        {anchor=point;found=true;break;}}
                    GuardDoorNativeProbe.Require(found,"no collision-clear visible interior observation anchor.");
                    float offset=indoorGuard.transform.position.y-indoorGuard.GroundPosition.y;
                    indoorGuard.transform.position=anchor+Vector3.up*offset;
                    indoorGuard.transform.forward=(landed.transform.position-indoorGuard.transform.position).normalized;
                    indoorGuard.Capture(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                    deadline=Time.realtimeSinceStartup+8;
                    while(Time.realtimeSinceStartup<deadline && !indoorGuard.CombatReady)
                    {GuardDoorNativeProbe.Quiet(indoorGuard,true);yield return null;}
                    GuardDoorNativeProbe.Quiet(indoorGuard,true);
                    GuardDoorNativeProbe.Require(indoorGuard.CombatReady && indoorGuard.DefenseTarget==landed.GetComponent<DaggerfallEntityBehaviour>() &&
                        indoorGuard.GetComponent<EnemySenses>().Target==indoorGuard.DefenseTarget,"loaded native target was not reacquired by actual sight.");
                    // A resident looking away must not claim to see the guard.
                    landed.transform.forward=(landed.transform.position-indoorGuard.transform.position).normalized;
                }
                controlledResident=landed;controlledResidentEnabled=landed.enabled;landed.enabled=false;
                enabled=true;
                string context;Vector3 ignored;
                GuardDoorNativeProbe.Require(!guards.TryVisibleResidentPursuer(landed,out ignored) &&
                    TryGuardRefugeContext(id,key=>game.TalkManager.GetBuildingNameForBuildingKey(key),out context) &&
                    context==shelter.ActivityContext(fixture.Door.buildingKey,game.TalkManager.GetBuildingNameForBuildingKey(fixture.Door.buildingKey),false),
                    "actual resident context incorrectly claims a visible pursuer.");
                var held=landed.transform.position;deadline=Time.realtimeSinceStartup+2;
                while(Time.realtimeSinceStartup<deadline)
                {GuardDoorNativeProbe.Quiet(indoorGuard,true);yield return null;}
                GuardDoorNativeProbe.Require(Vector3.Distance(held,landed.transform.position)<.05f,"unthreatened sheltered resident fled the innocent player.");
                copied=GuardDoorNativeProbe.SaveCopy(host);
                GuardDoorNativeProbe.Require(copied.ResidentDefenseShelter(id,settlement).LandingObserved &&
                    (copied.GetGuards(settlement).Guards.Find(item=>item.Id==first).Interior!=null)==witnessed,
                    "live loaded-room save-copy lost actual landing.");
                if(witnessed)
                {
                    double until=firstRecord.SearchUntil;var seen=firstRecord.Interior.LastSeenSuspect;
                    GuardDoorNativeProbe.Require(seen!=null,"real reacquisition did not capture last seen target.");
                    enabled=false;removed=landed;removed.gameObject.SetActive(false);deadline=Time.realtimeSinceStartup+5;
                    while(Time.realtimeSinceStartup<deadline && (indoorGuard.CombatReady || indoorGuard.DefenseTarget))yield return null;
                    GuardDoorNativeProbe.Require(!indoorGuard.CombatReady && !indoorGuard.DefenseTarget && firstRecord.SearchUntil==until &&
                        firstRecord.Interior.LastSeenSuspect.X==seen.X && firstRecord.Interior.LastSeenSuspect.Z==seen.Z,
                        "removed actual target did not release native combat into bounded last-seen search.");
                    GuardDoorNativeProbe.SaveCopy(host);
                }
                GuardDoorNativeProbe.Require(game.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None,
                    "fixture charged the innocent player.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_DOOR_NATIVE_PASS: mode="+(witnessed?"witnessed":"unwitnessed")+
                    "; door="+doorId+"; stagedFixture=true; nativeCrossing=true; nativeLanding=true; nativeSenses=true; liveSaveCopy=true; fullGameReload=false; truthfulContext=true; targetLoss="+witnessed);
            }
            finally
            {
                if(safety){safety.enabled=false;Destroy(safety);}
                try
                {
                    foreach(var other in hidden)if(other)other.gameObject.SetActive(true);
                    if(removed && removed.Resident.IsAlive)removed.gameObject.SetActive(true);
                    if(controlledResident)controlledResident.enabled=controlledResidentEnabled;
                    var current=FindActor(id);
                    if(current)
                    {
                        var currentBody=current.GetComponent<DaggerfallEntityBehaviour>();
                        if(currentBody && currentBody.Entity.CurrentHealth>0)
                        {currentBody.Entity.SetHealth(oldHealth,true);var vitals=current.GetComponent<NativeNpcVitalsBinding>();if(vitals)vitals.Capture();}
                        var evidence=current.GetComponent<GuardAttackEvidence>();if(evidence)Destroy(evidence);
                    }
                    else if(residentState.IsAlive)
                    {
                        var archetype=worldStore.ResolveNpcArchetype(id);var vitals=worldStore.ReadNpcVitals(id,archetype);
                        if(vitals.IsAlive)worldStore.TryCaptureNpcVitals(id,archetype,Math.Min(oldHealth,vitals.MaximumHealth),
                            vitals.CurrentMagicka,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),vitals.Revision);
                    }
                    lawfulGuardFlights.Remove(id);var record=worldStore.GetDefenseCombatant(id);if(record!=null && record.Death==null)record.Shelter=null;
                    if(transitioned && game.PlayerEnterExit.IsPlayerInsideBuilding)game.PlayerEnterExit.TransitionExterior(false);
                    guards.ProbeDoorRestore(savedRoster,first,second,report);
                    if(originalResident && originalResident.Resident.IsAlive && originalResident.transform.parent==oldParent)
                    {originalResident.GetComponent<CrowdMotion>().RestorePlacement(oldPosition-Vector3.up);originalResident.transform.rotation=oldFacing;}
                }
                finally
                {
                    guards.EnemyDefense=oldDefense;guards.enabled=oldGuardEnabled;ReactionsEnabled=oldReactions;enabled=oldEnabled;
                    game.PlayerMotor.transform.position=oldPlayer;game.PlayerMotor.transform.rotation=oldPlayerRotation;
                    game.PlayerMotor.enabled=oldMotor;if(controller)controller.enabled=oldController;
                    game.PlayerEntity.GodMode=oldGod;game.PlayerEntity.CrimeCommitted=oldCrime;
                    if(game.PlayerEntity.CurrentHealth>0)game.PlayerEntity.SetHealth(oldPlayerHealth,true);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_DOOR_CLEANUP: survivingSettingsRestored=true; resurrection=false");
                }
            }
        }
    }
}
