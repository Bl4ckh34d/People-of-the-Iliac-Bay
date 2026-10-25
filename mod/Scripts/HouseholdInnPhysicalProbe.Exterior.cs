using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class HouseholdInnPhysicalProbe
    {
        private static IEnumerator VerifyOutdoorFamilyRuntime(WorldState initial,WorldStateStore catalogOwner,HouseholdDayPlan selected,
            StreetPopulation streets,BuildingDirectory directory,TownGeometry geometry)
        {
            var game=GameManager.Instance;var fixture=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());fixture.Restore(initial);
            var catalog=catalogOwner.ReadNpcInnServiceCatalog(selected.SettlementId,selected.JointInn.InnKey);
            var keeper=catalogOwner.NativePeopleFor(selected.SettlementId).Find(p=>p.Id==catalog.KeeperId);
            fixture.RegisterNativePerson(keeper.SettlementId,keeper.WorkKey,keeper.NativeHash,keeper.Name,keeper.Sex,keeper.Race,keeper.FactionId,keeper.TextureArchive,keeper.TextureRecord);
            fixture.RegisterNpcInnServiceCatalog(catalog);
            HouseholdDayPlan unused;var request=HouseholdDayPersistenceProbe.CreateRequest(initial,directory,geometry,6*60,22*60,
                selected.PlannedAt-86400,out unused);
            request.AbsoluteDay=selected.AbsoluteDay;request.PlannedAt=selected.PlannedAt;
            var storeField=typeof(StreetPopulation).GetField("worldStore",Private);var originalStore=storeField.GetValue(streets);
            var roomField=typeof(StreetPopulation).GetField("interior",Private);var originalRoom=roomField.GetValue(streets);
            var geometryField=typeof(StreetPopulation).GetField("indoorGeometry",Private);var originalGeometry=geometryField.GetValue(streets);
            var activitiesField=typeof(StreetPopulation).GetField("shopGeometry",Private);var originalActivities=activitiesField.GetValue(streets);
            var bodies=(Dictionary<string,ResidentActor>)typeof(StreetPopulation).GetField("actors",Private).GetValue(streets);
            var physical=(Dictionary<string,PhysicalShopVisit>)typeof(StreetPopulation).GetField("physicalShopVisits",Private).GetValue(streets);
            var originalPhysical=new Dictionary<string,PhysicalShopVisit>(physical);
            var slots=(Dictionary<string,int>)typeof(StreetPopulation).GetField("indoorSlots",Private).GetValue(streets);var originalSlots=new Dictionary<string,int>(slots);
            var savedBodies=new Dictionary<string,ResidentActor>();var bodyActive=new Dictionary<string,bool>();
            var ids=new[]{selected.First.ResidentId,selected.Second.ResidentId};
            foreach(string id in ids){ResidentActor body;if(bodies.TryGetValue(id,out body) && body){savedBodies[id]=body;bodyActive[id]=body.gameObject.activeSelf;body.gameObject.SetActive(false);bodies.Remove(id);}}
            try
            {
                storeField.SetValue(streets,fixture);
                roomField.SetValue(streets,null);geometryField.SetValue(streets,null);activitiesField.SetValue(streets,null);physical.Clear();
                var plans=(IList)typeof(StreetPopulation).GetField("plans",Private).GetValue(streets);object first=null,second=null;
                foreach(var plan in plans)
                {var resident=(Resident)plan.GetType().GetField("Resident").GetValue(plan);if(resident.Id==ids[0])first=plan;if(resident.Id==ids[1])second=plan;}
                if(first==null || second==null)throw new InvalidOperationException("Live family producer lacks both native parent plans.");
                var prepare=typeof(StreetPopulation).GetMethod("TryFamilyJointCandidate",Private);var home=request.Household;
                var arguments=new object[]{home,first,second,selected.AbsoluteDay,selected.PlannedAt,false,null};
                float deadline=Time.realtimeSinceStartup+120;
                while(!(bool)prepare.Invoke(streets,arguments) && Time.realtimeSinceStartup<deadline)yield return null;
                request.JointInn=arguments[6] as HouseholdJointInn;
                if(request.JointInn==null)throw new InvalidOperationException("Production family selector did not build a supported evening Inn outing.");
                HouseholdDayPlan source;string reason;
                if(!HouseholdDayPlanner.TryCreate(request,out source,out reason) || !fixture.TryPublishJointHouseholdDay(source,source.PlannedAt,out reason))
                    throw new InvalidOperationException("Native paired day publication failed: "+reason);
                Fund(fixture,ids[0]);Fund(fixture,ids[1]);
                double start=source.AbsoluteDay*86400d+Math.Max(source.JointInn.First.DepartureSeconds,source.JointInn.Second.DepartureSeconds);
                double walk=Math.Max(source.JointInn.First.Itinerary.Stops[0].WalkSeconds,source.JointInn.Second.Itinerary.Stops[0].WalkSeconds);
                var sim=fixture.AdvanceHouseholdJointSimulation(source.Id,start+Math.Min(10,walk/4));double at=sim.AdvancedThrough;
                game.PlayerMotor.transform.position=geometry.ScenePoint(new RoutePoint(sim.First.Position.X,sim.First.Position.Z))+Vector3.up+Vector3.right*4;
                Physics.SyncTransforms();
                var restore=typeof(StreetPopulation).GetMethod("RestoreHouseholdExteriorBodies",Private);
                if(!(bool)restore.Invoke(streets,new object[]{fixture.ReadHouseholdJointProgress(source.SettlementId,source.HomeKey),sim,at}))
                    throw new InvalidOperationException("Production pair exterior reconstruction failed.");
                var a=bodies[ids[0]];var b=bodies[ids[1]];var movement=fixture.GetHouseholdJointMovement(source.SettlementId,source.HomeKey,now:at);
                var executor=new HouseholdJointBodyMotion(movement,a,b,geometry,directory.transform,p=>fixture.SaveHouseholdJointProgress(p));
                var before=movement.Snapshot();var ap=a.transform.position;var bp=b.transform.position;
                a.SetConversationFacing(true);executor.Tick(at,true,true);
                if(a.transform.position!=ap || b.transform.position!=bp)throw new InvalidOperationException("Native conversation did not hold both exterior parents.");
                a.SetConversationFacing(false);
                for(int frame=0;frame<40;frame++){at+=Math.Max(.001,Time.unscaledDeltaTime*DaggerfallUnity.Instance.WorldTime.TimeScale);executor.Tick(at,true,true);yield return null;}
                if(movement.Snapshot().First.Distance<=before.First.Distance && movement.Snapshot().Second.Distance<=before.Second.Distance)
                    throw new InvalidOperationException("Actual paired native exterior bodies did not resume walking.");
                if(geometry.UsesNativeNavigation)
                {
                    var nativeA=a.GetComponent<NativeResidentNavigation>();var nativeB=b.GetComponent<NativeResidentNavigation>();
                    if(!nativeA || !nativeB || !nativeA.HasDestination || !nativeB.HasDestination)
                        throw new InvalidOperationException("Family reconstruction regression needs both existing native route owners.");
                    fixture.BeginHouseholdJointSimulation(source.Id,movement.Snapshot().Sequence,at);
                    sim=fixture.AdvanceHouseholdJointSimulation(source.Id,at+0.1);at=sim.AdvancedThrough;
                    if(!(bool)restore.Invoke(streets,new object[]{fixture.ReadHouseholdJointProgress(source.SettlementId,source.HomeKey),sim,at}) ||
                        bodies[ids[0]]!=a || bodies[ids[1]]!=b || nativeA.Active || nativeB.Active ||
                        nativeA.HasDestination || nativeB.HasDestination ||
                        !a.GetComponent<CrowdMotion>().HasPlacement || !b.GetComponent<CrowdMotion>().HasPlacement)
                        throw new InvalidOperationException("Family reconstruction retained a prior native destination or replaced its observed bodies.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_NATIVE_REBIND_PASS: sameBodies=true; priorNativeDestinationsCleared=true; observedPlacementRetained=true");
                    movement=fixture.GetHouseholdJointMovement(source.SettlementId,source.HomeKey,now:at);
                }
                fixture.BeginHouseholdJointSimulation(source.Id,movement.Snapshot().Sequence,at);
                foreach(string id in ids){bodies[id].gameObject.SetActive(false);UnityEngine.Object.Destroy(bodies[id].gameObject);bodies.Remove(id);}
                at=Math.Max(at,start+walk+60);
                fixture.AdvanceHouseholdJointLifecycle(source.Id,at,NativeInnCalendar.ForServiceAt);
                var service=fixture.ReadHouseholdInnServiceAdmission(source.Id);
                if(service==null)throw new InvalidOperationException("Native produced family did not obtain service admission.");
                PeopleOfTheIliacBayMod.PrepareHouseholdInnBuyers(fixture,source.Id);
                // Actual frame steps or service preparation may already be
                // later than the planned seated instant. Never rewind this test.
                double visiting=Math.Max(at,service.SeatedAt+1);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_RUNTIME_CLOCK: afterWalking="+at+"; seated="+service.SeatedAt+"; visiting="+visiting);
                fixture.AdvanceHouseholdJointLifecycle(source.Id,visiting,NativeInnCalendar.ForServiceAt);
                if(visiting>DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(visiting-DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()));
                if(streets.HouseholdRoutineLocked(source.HomeKey)!=true)throw new InvalidOperationException("Actual child-only home did not lock during the produced shared outing.");
                ActivateFamilyHome(directory,source.HomeKey,false);
                DaggerfallUI.Instance.PopToHUD(); // Dismiss the expected locked-door notice.
                double end=service.ReturnAt+Math.Max(source.JointInn.First.Itinerary.ExitWalkSeconds,source.JointInn.Second.Itinerary.ExitWalkSeconds)+600;
                sim=fixture.AdvanceHouseholdJointLifecycle(source.Id,end,NativeInnCalendar.ForServiceAt);
                if(sim.Phase!=HouseholdJointPhase.Completed)
                {
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_PENDING_STATE: "+SaveLoadManager.Serialize(typeof(WorldState),fixture.Snapshot(),false));
                    throw new InvalidOperationException("Produced shared outing did not finish its retained home route: phase="+
                        sim.Phase+"; pause="+sim.PauseReason+"; service="+fixture.ReadHouseholdInnServiceAdmission(source.Id)?.Outcome);
                }
                if(end>DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(end-DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()));
                ActivateFamilyHome(directory,source.HomeKey,true);
                yield return null;
                // DFU defers residential entry until the greeting closes.
                // PopToHUD removes it without raising that native OnClose event.
                var greeting=DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;
                if(greeting!=null)greeting.CloseWindow();
                float homeReady=Time.realtimeSinceStartup+10;
                while(!game.PlayerEnterExit.IsPlayerInsideBuilding && Time.realtimeSinceStartup<homeReady)yield return null;
                DaggerfallUI.Instance.PopToHUD();
                var room=game.PlayerEnterExit.Interior;
                if(!room || room.EntryDoor.buildingKey!=source.HomeKey)throw new InvalidOperationException("Native household door did not enter the correct returned home.");
                roomField.SetValue(streets,room);geometryField.SetValue(streets,new InteriorGeometry(room,CrowdMotion.BodyRadius));activitiesField.SetValue(streets,null);slots.Clear();
                var restoreHome=typeof(StreetPopulation).GetMethod("RestoreHouseholdHomeBodies",Private);
                if(!(bool)restoreHome.Invoke(streets,new object[]{fixture.ReadHouseholdJointProgress(source.SettlementId,source.HomeKey),sim,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()}))
                    throw new InvalidOperationException("Native paired home-room handback failed.");
                fixture=NativeReload(fixture);storeField.SetValue(streets,fixture);
                if(fixture.ReadHouseholdJointSimulation(source.SettlementId,source.HomeKey).HomeArrival==null ||
                    fixture.HasActiveHouseholdJoint(source.SettlementId,ids[0]))throw new InvalidOperationException("Native home proof did not survive serialization or release joint ownership.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_RUNTIME_PASS: productionSelector=true; pairedNativeRoutes=true; exteriorBodies=true; conversationHoldsBoth=true; nativeActivateChildLock=true; nativeActivateHomeEntry=true; pairedHomeBodies=true; nativeSerializer=true; controlledClock=true");
            }
            finally
            {
                foreach(string id in ids){ResidentActor body;if(bodies.TryGetValue(id,out body) && body){body.gameObject.SetActive(false);UnityEngine.Object.Destroy(body.gameObject);}bodies.Remove(id);}
                if(game.PlayerEnterExit.IsPlayerInsideBuilding)game.PlayerEnterExit.TransitionExterior();
                storeField.SetValue(streets,originalStore);roomField.SetValue(streets,originalRoom);geometryField.SetValue(streets,originalGeometry);activitiesField.SetValue(streets,originalActivities);
                slots.Clear();foreach(var pair in originalSlots)slots[pair.Key]=pair.Value;
                physical.Clear();foreach(var pair in originalPhysical)physical[pair.Key]=pair.Value;
                foreach(var pair in savedBodies)if(pair.Value){bodies[pair.Key]=pair.Value;pair.Value.gameObject.SetActive(bodyActive[pair.Key]);}
            }
        }
        private static void ActivateFamilyHome(BuildingDirectory directory,int home,bool shouldEnter)
        {
            var game=GameManager.Instance;var activate=game.PlayerActivate;BuildingSummary summary;
            if(!directory.GetBuildingSummary(home,out summary) || activate.BuildingIsUnlocked(summary)!=shouldEnter)
                throw new InvalidOperationException("Native household lock state differs from living parent presence.");
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())foreach(var door in doors.Doors)
            {
                if(door.doorType!=DoorTypes.Building || door.buildingKey!=home)continue;
                var center=doors.transform.TransformPoint(door.buildingMatrix.MultiplyPoint3x4(door.centre));
                var normal=doors.transform.TransformDirection(door.buildingMatrix.MultiplyVector(door.normal)).normalized;
                var origin=center+normal*1.2f;game.PlayerMotor.transform.position=origin-Vector3.up*.6f;Physics.SyncTransforms();
                RaycastHit hit;int player=LayerMask.NameToLayer("Player");int mask=player>=0?~(1<<player):~0;
                if(!Physics.Raycast(origin,-normal,out hit,3,mask))throw new InvalidOperationException("Native home activation ray missed the door.");
                Transform owner;var actualDoors=activate.GetDoors(hit.transform,out owner);StaticDoor matched;
                if(!actualDoors || !actualDoors.HasHit(hit.point,out matched) || matched.buildingKey!=home)
                    throw new InvalidOperationException("Native home ray did not identify the expected static door.");
                Transform buildingOwner;var buildings=activate.GetBuildings(hit.transform,out buildingOwner);StaticBuilding building;
                if(!buildings || !buildings.HasHit(hit.point,out building) || building.buildingKey!=home)
                    throw new InvalidOperationException("Native home ray did not identify its actual building.");
                var mode=activate.CurrentMode;var crime=game.PlayerEntity.CrimeCommitted;activate.ChangeInteractionMode(PlayerActivateModes.Grab,false);
                try{typeof(PlayerActivate).GetMethod("ActivateStaticDoor",Private).Invoke(activate,new object[]{actualDoors,hit,true,building,
                    summary.BuildingType,activate.BuildingIsUnlocked(summary),activate.GetBuildingLockValue(summary),owner,false});}
                finally{activate.ChangeInteractionMode(mode,false);}
                if(!shouldEnter && game.PlayerEnterExit.IsPlayerInsideBuilding || game.PlayerEntity.CrimeCommitted!=crime)
                    throw new InvalidOperationException("Native lock activation entered a child-only home or changed the player's crime state.");
                return;
            }
            throw new InvalidOperationException("Native family home has no exterior static door.");
        }
    }
}
