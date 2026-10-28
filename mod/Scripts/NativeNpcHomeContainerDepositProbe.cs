using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Banking;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in fixture probe. It uses existing eligible NPC/home items and native loot.
    // Fixture observer placement uses a surveyed reachable room anchor; it never moves NPCs or invents items.
    public static class NativeNpcHomeContainerDepositProbe
    {
        private const BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
        private const int MaximumHomesToInspect=48;
        private const float ResidentMaterializationWaitSeconds=2f;
        private sealed class MovementTrace
        {
            public Vector3 Last;
            public float PathLength,MaxStep;
            public MovementTrace(Vector3 start){Last=start;}
            public void Sample(Vector3 position)
            {
                float step=Vector3.Distance(Last,position);if(step>MaxStep)MaxStep=step;
                PathLength+=step;Last=position;
            }
        }
        private sealed class PlayerStage
        {
            public bool Succeeded;
            public string Failure;
            public float SurveyedRouteLength,StartGap,FinalGap,SelectedAnchorGap;
        }

        private static float StageDeadline(float overall,float seconds)
        {return Math.Min(overall,Time.realtimeSinceStartup+seconds);}
        private static void RequireTime(float overall,string stage)
        {if(Time.realtimeSinceStartup>=overall)throw new InvalidOperationException("Home-container fixture exceeded its shared 540-second deadline during "+stage+".");}
        private static Vector3 IndoorTarget(DaggerfallInterior interior,NpcActionTarget target)
        {return interior.transform.TransformPoint(new Vector3((float)target.X,(float)target.Y,(float)target.Z));}
        private static void RequireMeasuredTravel(MovementTrace trace,ResidentActor actor,Vector3 target,string stage)
        {
            Vector3 final=actor.transform.position-Vector3.up;
            if(Vector3.Distance(trace.Last,final)>0.001f)trace.Sample(final);
            if(trace.PathLength<0.75f || trace.MaxStep>0.5f || Vector3.Distance(final,target)>0.5f)
                throw new InvalidOperationException(stage+" did not show bounded real walking to the native container (path="+
                    trace.PathLength.ToString("0.00")+"m, max-step="+trace.MaxStep.ToString("0.00")+"m, final-error="+
                    Vector3.Distance(final,target).ToString("0.00")+"m).");
        }

        private static bool TryResolveActionTarget(StreetPopulation streets,NpcActionJob job,out Vector3 target,out float arrival,out string reason)
        {
            target=Vector3.zero;arrival=0f;reason="The runtime action target resolver is unavailable.";
            MethodInfo resolver=typeof(StreetPopulation).GetMethod("ResolveActionTarget",PrivateInstance);
            if(resolver==null)return false;
            object[] arguments={job,Vector3.zero,0f,null};
            try
            {
                if(!(bool)resolver.Invoke(streets,arguments))
                {reason=arguments[3] as string ?? "The runtime rejected the action target.";return false;}
                target=(Vector3)arguments[1];arrival=(float)arguments[2];reason=null;return true;
            }
            catch(Exception exception)
            {reason="Could not read the runtime-resolved target: "+exception.GetBaseException().Message;return false;}
        }

        private static string HomeContainerState(DaggerfallInterior room)
        {
            var state=new StringBuilder();
            foreach(var loot in room.GetComponentsInChildren<DaggerfallLoot>())
                if(loot && loot.ContainerType==LootContainerTypes.HouseContainers)
                    state.Append(loot.LoadID).Append(':').Append(loot.stockedDate).Append(':').Append(loot.Items.SerializeItems()).Append('\n');
            return state.ToString();
        }

        private static bool HasNativeStockDueHomeContainer(DaggerfallInterior room,int today)
        {
            foreach(var loot in room.GetComponentsInChildren<DaggerfallLoot>())
                if(loot && loot.ContainerType==LootContainerTypes.HouseContainers && loot.stockedDate<today && loot.Items.Count==0)
                    return true;
            return false;
        }

        // Runs from the opt-in town harness. It loads only an existing save owned
        // by the disposable fixture character, advances that fixture to a real
        // awake-at-home window, and enters an existing native home door. No actor,
        // item, ledger, or container is created by the fixture setup.
        public static IEnumerator RunFixture(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            float fixtureDeadline=Time.realtimeSinceStartup+540f;
            Require(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-container-deposit-probe")>=0 &&
                !string.IsNullOrWhiteSpace(character) && NativeProbeText.IsTestCharacter(character),
                "Home-container fixture requires its explicit flag and an owned test character.");
            Require(host && streets,"Home-container fixture has no live town adapter.");
            var game=GameManager.Instance;game.SaveLoadManager.EnumerateSaves();
            string[] fixtureNames={"People of the Iliac Bay - Daggerfall city preview","People - Daggerfall day",
                "People of the Iliac Bay - furnished shop entrance","People - Shop entrance"};
            string loadedFixture=null;int existing=0;
            foreach(string fixtureName in fixtureNames)
            {
                int slot=game.SaveLoadManager.FindSaveFolderByNames(character,fixtureName);if(slot<0)continue;existing++;
                bool loaded=false;SaveLoadManager.OnLoadEventHandler onLoad=data=>loaded=true;SaveLoadManager.OnLoad+=onLoad;
                try
                {
                    game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();
                    float until=StageDeadline(fixtureDeadline,180f);
                    while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<until)yield return null;
                    if(!loaded || !streets.Ready || game.PlayerEntity.Name!=character)continue;
                }
                finally {SaveLoadManager.OnLoad-=onLoad;}
                loadedFixture=fixtureName;break;
            }
            RequireTime(fixtureDeadline,"save loading");
            Require(loadedFixture!=null,existing==0?
                "No existing owned city/shop fixture save was found; setup did not manufacture one.":
                "Existing owned city fixture saves could not be loaded with a ready town population.");
            var store=LiveStore(streets);var state=host.GetSaveData() as WorldState;
            Require(store!=null && state!=null,"Loaded fixture has no current persisted People of the Iliac Bay state.");

            if(game.PlayerEnterExit.IsPlayerInside)
            {
                game.PlayerEnterExit.TransitionExterior();float exitBy=StageDeadline(fixtureDeadline,90f);
                while((game.PlayerEnterExit.IsPlayerInside || !streets.Ready) && Time.realtimeSinceStartup<exitBy)yield return null;
            }
            Require(!game.PlayerEnterExit.IsPlayerInside && streets.Ready,"Could not reach the fixture town exterior.");
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            Require(directory!=null,"Fixture town has no loaded native building directory.");
            int wake=ReadHour(streets,"wakeHour",6),sleep=ReadHour(streets,"sleepHour",22);
            double now=Now();int day=(int)Math.Floor(now/86400d);double local=(wake*60+10)*60d;
            if(now>=day*86400d+local)day++;
            double homeAt=day*86400d+local;
            var town=state.Routines.Find(r=>r.SettlementId==directory.MapID);
            var population=state.Settlements.Find(p=>p.SettlementId==directory.MapID);
            Require(town!=null && population!=null,"Fixture town has no existing routines or household ownership data.");
            var candidates=new List<Resident>();var candidateHomes=new HashSet<int>();
            foreach(var household in population.Households)
            {
                if(household==null || household.BuildingKey<0 || household.Residents==null)continue;
                BuildingSummary summary;DaggerfallStaticDoors doorOwner;StaticDoor door;int doorIndex;
                if(!directory.GetBuildingSummary(household.BuildingKey,out summary) || !IsPrivateHome(summary) ||
                    DaggerfallBankManager.IsHouseOwned(household.BuildingKey) ||
                    !FindDoor(directory,household.BuildingKey,out doorOwner,out door,out doorIndex))continue;
                foreach(var resident in household.Residents)
                {
                    if(!EligibleResident(resident,household.BuildingKey))continue;
                    var routine=town.Residents.Find(r=>r.ResidentId==resident.Id);
                    if(routine==null || routine.Itinerary!=null && routine.ItineraryDepartureSeconds<=local+300)continue;
                    try
                    {
                        var schedule=routine.Itinerary==null?DailySchedule.HomeDay(resident.HomeKey,wake*60,sleep*60):
                            DailySchedule.ItineraryDay(resident.HomeKey,routine.Itinerary,routine.ItineraryDepartureSeconds,wake*60,sleep*60);
                        if(schedule.EvaluateSeconds(homeAt-day*86400d).Activity!=Activity.Home)continue;
                    }
                    catch(ArgumentException){continue;}
                    if(HasFixedBooking(store.AppointmentsFor(resident.Id),homeAt))continue;
                    var ledger=store.ReadNpcInventory(resident.Id);
                    if(ledger!=null)
                    {
                        if(ledger.Items==null || ledger.Items.Count==0)continue;
                    }
                    else
                    {
                        var progress=store.ReadNpcProgress(resident.Id);
                        var goods=NpcStartingBelongings.Generate(state.WorldSeed,resident.Id,
                            progress==null?NpcArchetype.Citizen:progress.Archetype);
                        if(goods==null || goods.Count==0)continue;
                    }
                    if(candidateHomes.Add(resident.HomeKey))candidates.Add(resident);
                }
            }
            Require(candidates.Count>0,"Fixture preflight found no existing adult permanent-household resident scheduled awake at home with an eligible existing item ledger or non-empty deterministic starting-goods plan.");

            // Test schedules all use this real home interval. Time moves only in
            // the disposable owned test save; the player enters each actual home
            // via DFU's native transition until a genuine action offer is found.
            var clock=DaggerfallUnity.Instance.WorldTime.Now;clock.RaiseTime((float)(homeAt-Now()));
            yield return null;yield return null;
            string noCandidate="No loaded permanent home exposed real authorized home actions.";
            int attemptedHomes=0,depositOptionFailures=0,retrievalOptionFailures=0,giftOptionFailures=0;bool hitHomeCap=false;
            string lastOfferState="none";
            foreach(var resident in candidates)
            {
                RequireTime(fixtureDeadline,"home selection");
                if(attemptedHomes>=MaximumHomesToInspect){hitHomeCap=true;break;}
                attemptedHomes++;
                if(game.PlayerEnterExit.IsPlayerInside)
                {
                    game.PlayerEnterExit.TransitionExterior();float exitBy=StageDeadline(fixtureDeadline,90f);
                    while((game.PlayerEnterExit.IsPlayerInside || !streets.Ready) && Time.realtimeSinceStartup<exitBy)yield return null;
                }
                Require(!game.PlayerEnterExit.IsPlayerInside && streets.Ready,"Fixture could not exit the previous native home.");
                DaggerfallStaticDoors owner;StaticDoor entry;int entryIndex;
                if(!FindDoor(directory,resident.HomeKey,out owner,out entry,out entryIndex))continue;
                Type rosyHooks=Type.GetType("PeopleOfTheIliacBay.QuestHooks.RosyInteriorCompatibilityHooks, PeopleOfTheIliacBay.QuestHooks",false);
                bool rosyInstalled=rosyHooks!=null && (bool)rosyHooks.GetProperty("Installed").GetValue(null,null);
                int rosyCorrectionsBefore=rosyInstalled?(int)rosyHooks.GetProperty("Completed").GetValue(null,null):0;
                TownSmokeHarness.EnterInterior(owner.transform,entry);DaggerfallUI.Instance.PopToHUD();
                float enterBy=StageDeadline(fixtureDeadline,120f);
                while(Time.realtimeSinceStartup<enterBy && (!streets.Ready || !game.PlayerEnterExit.Interior ||
                    game.PlayerEnterExit.Interior.EntryDoor.buildingKey!=resident.HomeKey))yield return null;
                var room=game.PlayerEnterExit.Interior;
                if(!room || room.EntryDoor.buildingKey!=resident.HomeKey){noCandidate="Native home entry did not settle at building "+resident.HomeKey+".";continue;}
                float residentReadyBy=StageDeadline(fixtureDeadline,ResidentMaterializationWaitSeconds);
                ResidentActor awakeResident=null;
                while(Time.realtimeSinceStartup<residentReadyBy)
                {
                    if(game.PlayerEnterExit.Interior==room && streets.Ready)
                    {
                        var candidate=streets.FindActor(resident.Id);
                        if(candidate && candidate.gameObject.activeInHierarchy && candidate.transform.IsChildOf(room.transform) &&
                            !candidate.IsSleeping && !candidate.IsConversing)
                        {awakeResident=candidate;break;}
                    }
                    yield return null;
                }
                if(!awakeResident)
                {noCandidate="Home "+resident.HomeKey+" resident "+resident.Id+" did not materialize awake under the entered interior within "+
                    ResidentMaterializationWaitSeconds.ToString("0")+" seconds.";continue;}
                if(rosyInstalled)
                {
                    float correctionDeadline=StageDeadline(fixtureDeadline,15f);
                    while((int)rosyHooks.GetProperty("Completed").GetValue(null,null)<=rosyCorrectionsBefore &&
                        Time.realtimeSinceStartup<correctionDeadline)
                    {
                        DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                        if(game.PlayerEnterExit.Interior!=room || !streets.Ready)
                            throw new InvalidOperationException("Native home scene changed before Rosy's doorway correction settled.");
                        var correctedActor=streets.FindActor(resident.Id);
                        if(!correctedActor || !correctedActor.gameObject.activeInHierarchy || correctedActor.IsSleeping)
                            throw new InvalidOperationException("The awake household resident disappeared while waiting for Rosy's doorway correction.");
                        yield return null;
                    }
                    int correctionsAfter=(int)rosyHooks.GetProperty("Completed").GetValue(null,null);
                    if(correctionsAfter<=rosyCorrectionsBefore)
                    {noCandidate="Rosy's native home-entry correction did not settle within 15 seconds for home "+resident.HomeKey+".";continue;}
                    awakeResident=streets.FindActor(resident.Id);
                    if(!awakeResident || !awakeResident.gameObject.activeInHierarchy || awakeResident.IsSleeping ||
                        !awakeResident.transform.IsChildOf(room.transform))
                        throw new InvalidOperationException("Rosy's completed home-entry correction left no awake resident in the native home.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOME_CONTAINER_ENTRY_SETTLED: home="+resident.HomeKey+"; resident="+resident.Id+
                        "; corrections="+rosyCorrectionsBefore+"->"+correctionsAfter+"; player="+game.PlayerMotor.transform.position);
                }
                int today=DaggerfallLoot.CreateStockedDate(DaggerfallUnity.Instance.WorldTime.Now);
                lastOfferState=DescribeHomeOfferState(room,awakeResident,game.PlayerMotor.transform.position);
                if(!HasNativeStockDueHomeContainer(room,today))
                {noCandidate="Home "+resident.HomeKey+" has no empty native house chest due for first-open stocking, so ordinary-chat no-stock behavior cannot be exercised. "+lastOfferState;continue;}
                var playerStage=new PlayerStage();
                yield return StagePlayerAtReachableHomeAnchor(game,streets,room,awakeResident,playerStage);
                if(!playerStage.Succeeded)
                {noCandidate="Home "+resident.HomeKey+" could not stage the player at a safe surveyed anchor near the awake resident: "+playerStage.Failure;continue;}
                lastOfferState=DescribeHomeOfferState(room,awakeResident,game.PlayerMotor.transform.position);
                string beforeOrdinaryChat=HomeContainerState(room);
                streets.ConversationActionOptions(resident.Id,"Hello, how are you?");
                if(HomeContainerState(room)!=beforeOrdinaryChat)
                    throw new InvalidOperationException("Ordinary conversation materialized or changed native home-container contents before any transfer intent.");
                bool depositAvailable=HasRealDepositOption(streets,resident.Id,game.PlayerMotor.transform.position,store);
                bool retrievalAvailable=HasRealExistingHomeRetrievalOption(streets,resident.Id,store);
                bool giftAvailable=HasRealHomeGiftOption(streets,resident.Id,game.PlayerEntity.Items);
                if(!depositAvailable)depositOptionFailures++;
                if(!retrievalAvailable)retrievalOptionFailures++;
                if(!giftAvailable)giftOptionFailures++;
                lastOfferState=DescribeHomeOfferState(room,awakeResident,game.PlayerMotor.transform.position);
                if(!depositAvailable || !retrievalAvailable || !giftAvailable)
                {
                    noCandidate="Home "+resident.HomeKey+" resident "+resident.Id+" action offers: deposit="+depositAvailable+
                        ", retrieval="+retrievalAvailable+", player-gift="+giftAvailable+". "+lastOfferState;
                    continue;
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOME_CONTAINER_FIXTURE_SELECTED: save="+loadedFixture+"; resident="+resident.Id+
                    "; home="+resident.HomeKey+"; real-native-door=true; awake-home-window="+local.ToString("0")+"; item-source=existing-ledger-or-deterministic-plan; retrieval-source=existing-household-container-item; player-observer-stage="+
                    "surveyed-anchor; route-length="+playerStage.SurveyedRouteLength.ToString("0.00")+"m; resident-gap="+
                    playerStage.StartGap.ToString("0.00")+"->"+playerStage.FinalGap.ToString("0.00")+"m");
                yield return Run(streets,character,fixtureDeadline);
                yield break;
            }
            RequireTime(fixtureDeadline,"final home search");
            if(hitHomeCap)
                noCandidate="Stopped after the bounded "+MaximumHomesToInspect+" distinct home candidates; last result: "+noCandidate;
            throw new InvalidOperationException("Home-container fixture preflight exhausted existing native homes. "+noCandidate+
                " Attempted "+attemptedHomes+" candidates; unavailable offers: deposit="+depositOptionFailures+
                ", retrieval="+retrievalOptionFailures+", player-gift="+giftOptionFailures+
                ". Last native offer state: "+lastOfferState+" No fixture-only actors, items, routes, ledgers, or containers were fabricated; an explicit retrieval intent may invoke DFU's native first-open stocking.");
        }

        private static IEnumerator StagePlayerAtReachableHomeAnchor(GameManager game,StreetPopulation streets,DaggerfallInterior room,
            ResidentActor actor,PlayerStage result)
        {
            result.Failure="No reachable surveyed home anchor is within 2.5-4.0m of the awake resident.";
            var geometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",PrivateInstance).GetValue(streets);
            var motor=game.PlayerMotor;var controller=motor?motor.GetComponent<CharacterController>():null;
            if(geometry==null || !room || !actor || !motor || !controller || !controller.enabled)
            {result.Failure="Current home has no surveyed indoor route or active player controller.";yield break;}

            Vector3 start=motor.transform.position;
            Vector3 selectedAnchor=Vector3.zero;float selectedLength=float.PositiveInfinity;float selectedGap=0f;bool foundAnchor=false;
            foreach(Vector3 candidateAnchor in geometry.LocalAnchors)
            {
                Vector3 target=room.transform.TransformPoint(candidateAnchor)+Vector3.up;
                float gap=Vector3.Distance(target,actor.transform.position);
                if(gap<2.5f || gap>4f)continue;
                var route=geometry.RouteToExit(room.transform.TransformPoint(candidateAnchor));
                if(route==null || route.Count<2)continue;
                float length=0f;Vector3 previous=room.transform.TransformPoint(new Vector3((float)route[0].X,
                    (float)route[0].Y,(float)route[0].Z))+Vector3.up;
                for(int i=1;i<route.Count;i++)
                {
                    var point=route[i];Vector3 current=room.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z))+Vector3.up;
                    length+=Vector3.Distance(previous,current);previous=current;
                }
                if(length>=selectedLength)continue;
                selectedAnchor=candidateAnchor;selectedLength=length;selectedGap=gap;foundAnchor=true;
            }
            if(!foundAnchor)yield break;
            result.StartGap=Vector3.Distance(start,actor.transform.position);
            result.SurveyedRouteLength=selectedLength;result.SelectedAnchorGap=selectedGap;
            Vector3 anchor=room.transform.TransformPoint(selectedAnchor)+Vector3.up;
            // A deterministic fixture-only observer position is acceptable here;
            // the NPC's subsequent inventory actions still use native routes and movement.
            bool controllerEnabled=controller.enabled;controller.enabled=false;
            try {motor.transform.position=anchor;Physics.SyncTransforms();}
            finally {controller.enabled=controllerEnabled;}
            result.FinalGap=Vector3.Distance(motor.transform.position,actor.transform.position);
            if(result.FinalGap<2.1f || result.FinalGap>7.5f)
            {result.Failure="Surveyed observer staging point left an unusable resident gap (gap="+result.FinalGap.ToString("0.00")+
                "m; selected-anchor-gap="+
                selectedGap.ToString("0.00")+"m).";yield break;}
            result.Succeeded=true;result.Failure=null;
        }

        public static IEnumerator Run(StreetPopulation streets,string expectedCharacter,float fixtureDeadline)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-container-deposit-probe")<0 ||
                string.IsNullOrWhiteSpace(expectedCharacter) || !NativeProbeText.IsTestCharacter(expectedCharacter))
                throw new InvalidOperationException("Home container probe requires an explicitly owned fixture character and flag.");
            var game=GameManager.Instance;var room=game==null?null:game.PlayerEnterExit.Interior;
            if(!streets || !streets.Ready || game.PlayerEntity.Name!=expectedCharacter || !room)
                throw new InvalidOperationException("Home container probe requires the owned fixture character inside a loaded home.");
            string failure="No nearby permanent resident could complete home wait, gift, and deposit actions.";
            ResidentActor selected=null;NpcActionJob accepted=null;
            DaggerfallLoot existingHouseholdLoot=null;DaggerfallUnityItem existingHouseholdItem=null;
            var store=LiveStore(streets);
            var actors=(Dictionary<string,ResidentActor>)typeof(StreetPopulation).GetField("actors",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(streets);
            foreach(var actor in new List<ResidentActor>(actors.Values))
            {
                if(!actor || !actor.gameObject.activeInHierarchy || actor.IsSleeping || actor.IsConversing ||
                    actor.Resident.HomeKey!=room.EntryDoor.buildingKey || actor.Resident.AgeGroup!=ResidentAgeGroup.Adult ||
                    Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)>8) continue;
                var owner=actor.GetComponent<NativeNpcInventoryOwner>();if(!owner || owner.Binding==null)continue;
                NpcActionOption initialRetrieval;
                if(!FindRealExistingHomeRetrievalOption(streets,actor,out existingHouseholdLoot,out existingHouseholdItem,out initialRetrieval)) continue;
                DaggerfallUnityItem item=null;
                for(int i=0;i<owner.Binding.Items.Count;i++)
                {
                    var candidate=owner.Binding.Items.GetItem(i);
                    if(candidate!=null && !candidate.IsQuestItem && !candidate.IsEquipped && !candidate.IsSummoned &&
                        !(candidate is HouseholdPropertyKeyItem) && candidate.stackCount>0 &&
                        (candidate.stackCount==1 || candidate.IsAStack() && string.IsNullOrEmpty(candidate.GetSaveData().className)))
                    {item=candidate;break;}
                }
                if(item==null)continue;
                string request="store "+item.stackCount+" "+item.LongName+" at home";
                var options=streets.ConversationActionOptions(actor.Resident.Id,request);
                foreach(var option in options)
                {
                    if(option.Kind!=NpcActionKind.Transfer || option.Target==null ||
                        option.Target.TransferDirection!=NpcActionTransferDirection.NpcToContainer || option.Target.ItemId!=item.UID.ToString()) continue;
                    selected=actor;break;
                }
                if(selected)break;
            }
            if(!selected)throw new InvalidOperationException("Native home action fixture was not found: "+failure);

            RequireTime(fixtureDeadline,"home Wait acceptance");
            var waitOptions=streets.ConversationActionOptions(selected.Resident.Id,"Wait here.");
            var waitOption=waitOptions.Find(option=>option.Kind==NpcActionKind.Wait);
            if(waitOption==null || !NpcProbeCommandSource.TryAcceptAction(streets,selected.Resident.Id,
                "native-home-wait:"+Guid.NewGuid().ToString("N"),new NpcActionDecision {Option=waitOption,DurationSeconds=10},out failure))
                throw new InvalidOperationException("Normal home Wait action was not accepted: "+failure);
            var waitJob=store.ActiveNpcAction(selected.Resident.Id);
            if(waitJob==null || waitJob.Kind!=NpcActionKind.Wait)throw new InvalidOperationException("Home Wait action was not persisted.");
            float waitDeadline=StageDeadline(fixtureDeadline,120f);NpcActionJob waitResult=waitJob;
            while(Time.realtimeSinceStartup<waitDeadline)
            {
                waitResult=store.ReadNpcAction(waitJob.Id);
                if(waitResult==null || NpcActionExecutor.IsTerminal(waitResult) && !waitResult.ReturningToRoutine)break;
                yield return null;
            }
            RequireTime(fixtureDeadline,"home Wait completion");
            if(waitResult==null || waitResult.Phase!=NpcActionPhase.Completed || waitResult.ReturningToRoutine)
                throw new InvalidOperationException("Normal home Wait action did not complete before its stage deadline: "+(waitResult==null?"missing":waitResult.Phase+" "+waitResult.FailureReason));

            RequireTime(fixtureDeadline,"home gift acceptance");
            var playerItems=game.PlayerEntity.Items;DaggerfallUnityItem giftItem=null;
            for(int i=0;i<playerItems.Count;i++)
            {var item=playerItems.GetItem(i);if(Transferable(item) && item.stackCount==1){giftItem=item;break;}}
            if(giftItem==null)throw new InvalidOperationException("Home gift fixture lost its existing transferable single item.");
            ulong giftUid=giftItem.UID;ItemGroups giftGroup=giftItem.ItemGroup;int giftTemplate=giftItem.TemplateIndex;
            int playerGiftBefore=CountMatching(playerItems,giftGroup,giftTemplate);
            int npcGiftBefore=CountMatching(ownerAfterGiftItems(selected),giftGroup,giftTemplate);
            string giftRequest="take one "+giftItem.LongName+" from me";
            var giftOptions=streets.ConversationActionOptions(selected.Resident.Id,giftRequest);
            NpcActionOption giftOption=null;
            foreach(var option in giftOptions)
                if(option.Kind==NpcActionKind.Transfer && option.Target!=null &&
                    option.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc &&
                    option.Target.ItemId==giftUid.ToString(System.Globalization.CultureInfo.InvariantCulture))
                {giftOption=option;break;}
            if(giftOption==null || !NpcProbeCommandSource.TryAcceptAction(streets,selected.Resident.Id,
                "native-home-gift:"+Guid.NewGuid().ToString("N"),new NpcActionDecision {Option=giftOption,DurationSeconds=60},out failure))
                throw new InvalidOperationException("Normal home gift action was not accepted: "+failure);
            var giftJob=store.ActiveNpcAction(selected.Resident.Id);
            if(giftJob==null || giftJob.Kind!=NpcActionKind.Transfer ||
                giftJob.Target.TransferDirection!=NpcActionTransferDirection.PlayerToNpc)
                throw new InvalidOperationException("Home gift action was not persisted.");
            float giftDeadline=StageDeadline(fixtureDeadline,120f);NpcActionJob giftResult=giftJob;
            while(Time.realtimeSinceStartup<giftDeadline)
            {
                giftResult=store.ReadNpcAction(giftJob.Id);
                if(giftResult==null || NpcActionExecutor.IsTerminal(giftResult) && !giftResult.ReturningToRoutine)break;
                yield return null;
            }
            RequireTime(fixtureDeadline,"home gift completion");
            if(giftResult==null || giftResult.Phase!=NpcActionPhase.Completed || giftResult.ReturningToRoutine ||
                giftResult.Target.ItemId!=giftUid.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                giftResult.Target.Quantity!=1 || giftResult.Target.TransferDirection!=NpcActionTransferDirection.PlayerToNpc ||
                playerItems.GetItem(giftUid)!=null ||
                CountMatching(playerItems,giftGroup,giftTemplate)!=playerGiftBefore-1 ||
                CountMatching(ownerAfterGiftItems(selected),giftGroup,giftTemplate)!=npcGiftBefore+1)
                throw new InvalidOperationException("Home gift did not transfer exact existing player UID once: "+(giftResult==null?"missing":giftResult.Phase+" "+giftResult.FailureReason));

            NpcActionOption depositOption=null;
            var ownerAfterGift=selected.GetComponent<NativeNpcInventoryOwner>();
            Vector3 depositStart=selected.transform.position-Vector3.up;
            for(int i=0;i<ownerAfterGift.Binding.Items.Count && depositOption==null;i++)
            {
                var item=ownerAfterGift.Binding.Items.GetItem(i);
                if(item==null || item.UID!=giftUid || !Transferable(item))continue;
                var options=streets.ConversationActionOptions(selected.Resident.Id,"store "+item.stackCount+" "+item.LongName+" at home");
                foreach(var option in options)
                    if(option.Kind==NpcActionKind.Transfer && option.Target!=null &&
                        option.Target.TransferDirection==NpcActionTransferDirection.NpcToContainer &&
                        option.Target.ItemId==item.UID.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                        Vector3.Distance(depositStart,IndoorTarget(room,option.Target))>1f)
                    {depositOption=option;break;}
            }
            RequireTime(fixtureDeadline,"home deposit acceptance");
            if(depositOption==null || !NpcProbeCommandSource.TryAcceptAction(streets,selected.Resident.Id,
                "native-container-probe:"+Guid.NewGuid().ToString("N"),
                new NpcActionDecision {Option=depositOption,DurationSeconds=300},out failure))
                throw new InvalidOperationException("Normal home deposit action was not accepted: "+failure);
            accepted=selected?store.ActiveNpcAction(selected.Resident.Id):null;
            if(!selected || accepted==null) throw new InvalidOperationException("Native home container action was not accepted: "+failure);
            Vector3 depositTarget=IndoorTarget(room,accepted.Target);var depositMotion=new MovementTrace(depositStart);
            if(Vector3.Distance(depositStart,depositTarget)<=1f)
                throw new InvalidOperationException("Accepted deposit target was not more than 1m from the actor's measured start.");
            float deadline=StageDeadline(fixtureDeadline,180);
            NpcActionJob result=accepted;
            while(Time.realtimeSinceStartup<deadline)
            {
                depositMotion.Sample(selected.transform.position-Vector3.up);
                result=store.ReadNpcAction(accepted.Id);
                if(result==null || NpcActionExecutor.IsTerminal(result))break;
                yield return null;
            }
            RequireTime(fixtureDeadline,"home deposit completion");
            if(result==null || result.Phase!=NpcActionPhase.Completed)
                throw new InvalidOperationException("Physical home deposit did not complete: "+(result==null?"missing":result.Phase+" "+result.FailureReason));
            RequireMeasuredTravel(depositMotion,selected,depositTarget,"Home deposit");
            DaggerfallLoot savedLoot=null;
            foreach(var loot in room.GetComponentsInChildren<DaggerfallLoot>())
                if(loot && loot.LoadID.ToString(System.Globalization.CultureInfo.InvariantCulture)==accepted.Target.ContainerId) savedLoot=loot;
            ulong uid=ulong.Parse(accepted.Target.ItemId,System.Globalization.CultureInfo.InvariantCulture);
            if(!savedLoot || savedLoot.GetComponent<SerializableLootContainer>()==null || savedLoot.Items.GetItem(uid)==null ||
                selected.GetComponent<NativeNpcInventoryOwner>().Binding.Items.GetItem(uid)!=null)
                throw new InvalidOperationException("Observed completion did not leave the exact UID in real native home loot and remove it from the NPC ledger.");
            var lootData=savedLoot.GetComponent<SerializableLootContainer>().GetSaveData();
            var lootCopy=(LootContainerData_v1)SaveLoadManager.Deserialize(lootData.GetType(),SaveLoadManager.Serialize(lootData.GetType(),lootData));
            var restoredItems=new ItemCollection();restoredItems.DeserializeItems(lootCopy.items);
            if(restoredItems.GetItem(uid)==null || restoredItems.GetItem(uid).stackCount!=accepted.Target.Quantity)
                throw new InvalidOperationException("Native home loot serializer lost the deposited exact item UID/count.");
            var tomorrow=new DaggerfallDateTime(DaggerfallUnity.Instance.WorldTime.Now);
            tomorrow.RaiseTime(DaggerfallDateTime.SecondsPerDay);
            int nextDayStockDate=DaggerfallLoot.CreateStockedDate(tomorrow);
            if(!NpcHomeContainerLifecycle.IsPersistentStorage(lootCopy.stockedDate) || lootCopy.stockedDate<nextDayStockDate ||
                NpcHomeContainerLifecycle.ShouldNativeStock(true,true,false,false,false,false,restoredItems.Count,lootCopy.stockedDate,nextDayStockDate))
                throw new InvalidOperationException("Serialized native home loot would meet PlayerActivate's next-day clearing-stock condition.");
            savedLoot.GetComponent<SerializableLootContainer>().RestoreSaveData(lootCopy);
            if(savedLoot.stockedDate!=int.MaxValue || savedLoot.Items.GetItem(uid)==null ||
                savedLoot.Items.GetItem(uid).stackCount!=accepted.Target.Quantity)
                throw new InvalidOperationException("Native LootContainerData_v1 restore lost the deposit marker or exact item UID/count.");

            NpcActionOption retrievalOption;
            RequireTime(fixtureDeadline,"household retrieval acceptance");
            if(!FindRealExistingHomeRetrievalOption(streets,selected,out existingHouseholdLoot,out existingHouseholdItem,out retrievalOption,uid) ||
                !NpcProbeCommandSource.TryAcceptAction(streets,selected.Resident.Id,
                "native-home-retrieval:"+Guid.NewGuid().ToString("N"),
                new NpcActionDecision {Option=retrievalOption,DurationSeconds=300},out failure))
                throw new InvalidOperationException("Normal home household-item retrieval was not accepted: "+failure);
            var retrieval=store.ActiveNpcAction(selected.Resident.Id);
            if(retrieval==null || retrieval.Kind!=NpcActionKind.Transfer ||
                retrieval.Target.TransferDirection!=NpcActionTransferDirection.HomeContainerToNpc ||
                retrieval.Target.ItemId!=existingHouseholdItem.UID.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Home retrieval action did not persist its exact household-container target.");
            Vector3 retrievalStart=selected.transform.position-Vector3.up;
            Vector3 retrievalTarget=IndoorTarget(room,retrieval.Target);var retrievalMotion=new MovementTrace(retrievalStart);
            if(Vector3.Distance(retrievalStart,retrievalTarget)<=1f)
                throw new InvalidOperationException("Accepted retrieval target was not more than 1m from the actor's measured start.");
            float retrievalDeadline=StageDeadline(fixtureDeadline,180f);NpcActionJob retrievalResult=retrieval;
            while(Time.realtimeSinceStartup<retrievalDeadline)
            {
                retrievalMotion.Sample(selected.transform.position-Vector3.up);
                retrievalResult=store.ReadNpcAction(retrieval.Id);
                if(retrievalResult==null || NpcActionExecutor.IsTerminal(retrievalResult))break;
                yield return null;
            }
            RequireTime(fixtureDeadline,"household retrieval completion");
            var currentNpcItems=selected.GetComponent<NativeNpcInventoryOwner>().Binding.Items;
            if(retrievalResult==null || retrievalResult.Phase!=NpcActionPhase.Completed ||
                existingHouseholdLoot.Items.GetItem(existingHouseholdItem.UID)!=null ||
                currentNpcItems.GetItem(existingHouseholdItem.UID)==null ||
                currentNpcItems.GetItem(existingHouseholdItem.UID).stackCount!=existingHouseholdItem.stackCount)
                throw new InvalidOperationException("Physical household retrieval did not move the existing exact UID once: "+
                    (retrievalResult==null?"missing":retrievalResult.Phase+" "+retrievalResult.FailureReason));
            RequireMeasuredTravel(retrievalMotion,selected,retrievalTarget,"Household retrieval");
            var retrievedLootData=existingHouseholdLoot.GetComponent<SerializableLootContainer>().GetSaveData();
            var retrievedLootCopy=(LootContainerData_v1)SaveLoadManager.Deserialize(retrievedLootData.GetType(),
                SaveLoadManager.Serialize(retrievedLootData.GetType(),retrievedLootData));
            var restoredRetrievalLoot=new ItemCollection();restoredRetrievalLoot.DeserializeItems(retrievedLootCopy.items);
            if(restoredRetrievalLoot.GetItem(existingHouseholdItem.UID)!=null)
                throw new InvalidOperationException("Native household-container serializer resurrected the retrieved UID.");

            if(Vector3.Distance(selected.transform.position,game.PlayerMotor.transform.position)>8f)
                throw new InvalidOperationException("Pre-existing household retrieval ended outside normal nearby task-acceptance range.");
            string storeExistingRequest="store "+existingHouseholdItem.stackCount+" "+existingHouseholdItem.LongName+" at home";
            NpcActionOption restoreExistingOption=null;
            foreach(var option in streets.ConversationActionOptions(selected.Resident.Id,storeExistingRequest))
                if(option.Kind==NpcActionKind.Transfer && option.Target!=null &&
                    option.Target.TransferDirection==NpcActionTransferDirection.NpcToContainer &&
                    option.Target.ContainerId==existingHouseholdLoot.LoadID.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                    option.Target.ItemId==existingHouseholdItem.UID.ToString(System.Globalization.CultureInfo.InvariantCulture))
                {restoreExistingOption=option;break;}
            if(restoreExistingOption==null || !NpcProbeCommandSource.TryAcceptAction(streets,selected.Resident.Id,
                "native-home-retrieve-restore:"+Guid.NewGuid().ToString("N"),
                new NpcActionDecision {Option=restoreExistingOption,DurationSeconds=300},out failure))
                throw new InvalidOperationException("Could not safely return pre-existing household UID to its original home container before composed-task check: "+failure);
            var restoreExistingJob=store.ActiveNpcAction(selected.Resident.Id);
            if(restoreExistingJob==null || restoreExistingJob.Target.ContainerId!=existingHouseholdLoot.LoadID.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                restoreExistingJob.Target.ItemId!=existingHouseholdItem.UID.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Home-container restore action changed the pre-existing exact UID or source container.");
            float restoreExistingDeadline=StageDeadline(fixtureDeadline,120f);NpcActionJob restoreExistingResult=restoreExistingJob;
            while(Time.realtimeSinceStartup<restoreExistingDeadline)
            {
                restoreExistingResult=store.ReadNpcAction(restoreExistingJob.Id);
                if(restoreExistingResult==null || NpcActionExecutor.IsTerminal(restoreExistingResult))break;
                yield return null;
            }
            RequireTime(fixtureDeadline,"pre-existing household item restore");
            if(restoreExistingResult==null || restoreExistingResult.Phase!=NpcActionPhase.Completed ||
                existingHouseholdLoot.Items.GetItem(existingHouseholdItem.UID)==null ||
                selected.GetComponent<NativeNpcInventoryOwner>().Binding.Items.GetItem(existingHouseholdItem.UID)!=null ||
                savedLoot.Items.GetItem(uid)==null || savedLoot.Items.GetItem(uid).stackCount!=accepted.Target.Quantity)
                throw new InvalidOperationException("Restoring the existing household UID changed or lost either stored item.");

            string takeExistingRequest="take "+existingHouseholdItem.stackCount+" "+existingHouseholdItem.LongName+" from my home storage";
            NpcActionOption taskRetrievalOption=null;
            foreach(var option in streets.ConversationActionOptions(selected.Resident.Id,takeExistingRequest))
                if(option.Kind==NpcActionKind.Transfer && option.Target!=null &&
                    option.Target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc &&
                    option.Target.ContainerId==existingHouseholdLoot.LoadID.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                    option.Target.ItemId==existingHouseholdItem.UID.ToString(System.Globalization.CultureInfo.InvariantCulture))
                {taskRetrievalOption=option;break;}
            var handoverOption=NpcActionSequenceChat.AcquiredItemHandoverSemantic(0,
                taskRetrievalOption==null?null:new NpcActionDecision {Option=taskRetrievalOption,DurationSeconds=300});
            var composedClauses=new[]{"take "+existingHouseholdItem.stackCount+" "+existingHouseholdItem.LongName+
                " from my home storage", "give it to me"};
            var composedDecisions=taskRetrievalOption==null || handoverOption==null?null:NpcActionSequenceChat.ParseSemantic(composedClauses,
                new List<IList<NpcActionOption>> {new[]{taskRetrievalOption},new[]{handoverOption}},
                new[]{"DIRECT|Transfer|"+taskRetrievalOption.Id+"|"+taskRetrievalOption.Target.Quantity+"|300","DIRECT|Transfer|"+handoverOption.Id+"|"+handoverOption.Target.Quantity+"|300"});
            RequireTime(fixtureDeadline,"composed home retrieval and handover acceptance");
            string composedTaskId="native-home-retrieval-handover:"+Guid.NewGuid().ToString("N");
            if(composedDecisions==null || composedDecisions.Count!=2 || composedDecisions[1].Option.ItemSourceStep!=1 ||
                !NpcProbeCommandSource.TryAcceptTask(streets,selected.Resident.Id,composedTaskId,composedDecisions,out failure))
                throw new InvalidOperationException("Normal parsed home retrieval-to-exact-handover task was not accepted: "+failure);
            var composedTask=store.ActiveNpcTask(selected.Resident.Id);
            if(composedTask==null || composedTask.ProposalId!=composedTaskId)
                throw new InvalidOperationException("Canonical composed task lost its fixture proposal correlation.");
            if(composedTask==null || composedTask.Steps.Count!=2 || composedTask.Steps[0].Target.ItemId!=existingHouseholdItem.UID.ToString() ||
                composedTask.Steps[1].ItemSourceStep!=1)
                throw new InvalidOperationException("Accepted composed home task lost its exact pre-existing retrieval dependency.");
            Vector3 composedStart=selected.transform.position-Vector3.up;
            var composedMotion=new MovementTrace(composedStart);bool betweenStepsSnapshot=false;
            bool handoverTargetResolved=false,handoverPositionCaptured=false;
            Vector3 handoverTarget=Vector3.zero,handoverCompletionPosition=Vector3.zero;
            float handoverArrivalRange=0f;string handoverTargetReason=null;
            float composedDeadline=StageDeadline(fixtureDeadline,240f);NpcTaskPlan composedResult=composedTask;
            NpcActionJob sourceResult=null,handoverResult=null;
            while(Time.realtimeSinceStartup<composedDeadline)
            {
                composedResult=store.ReadNpcTask(composedTask.Id);
                sourceResult=store.ReadNpcAction(composedTask.Id+":step:0");
                handoverResult=store.ReadNpcAction(composedTask.Id+":step:1");
                if(!handoverTargetResolved && handoverResult!=null)
                    handoverTargetResolved=TryResolveActionTarget(streets,handoverResult,out handoverTarget,out handoverArrivalRange,out handoverTargetReason);
                if(!handoverPositionCaptured && handoverResult!=null && handoverResult.Phase==NpcActionPhase.Completed)
                {
                    // The action persists its exact position in the same update that commits
                    // the transfer. Prefer that completion snapshot over a later routine tick.
                    var completedRoom=game.PlayerEnterExit.Interior;
                    if(handoverResult.LastPosition!=null && handoverResult.LastPosition.Kind==NpcActionTargetKind.IndoorPoint &&
                        completedRoom && completedRoom.EntryDoor.buildingKey==handoverResult.LastPosition.BuildingKey)
                        handoverCompletionPosition=IndoorTarget(completedRoom,handoverResult.LastPosition);
                    else handoverCompletionPosition=selected.transform.position-Vector3.up;
                    composedMotion.Sample(handoverCompletionPosition);handoverPositionCaptured=true;
                }
                else if(!handoverPositionCaptured)composedMotion.Sample(selected.transform.position-Vector3.up);
                if(!betweenStepsSnapshot && composedResult!=null && composedResult.CurrentStepIndex==1 &&
                    sourceResult!=null && sourceResult.Phase==NpcActionPhase.Completed && handoverResult!=null &&
                    handoverResult.Phase==NpcActionPhase.Accepted)
                {
                    var snapshotData=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
                    var snapshotStore=new WorldStateStore(0);
                    snapshotStore.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),snapshotData));
                    var snapshotTask=snapshotStore.ReadNpcTask(composedTask.Id);
                    var resolvedTarget=snapshotTask==null?null:snapshotStore.ResolveNpcTaskTarget(snapshotTask,1);
                    NativeNpcInventoryBinding snapshotInventory;string snapshotReason="snapshot restore validation failed.";
                    if(snapshotTask==null || snapshotTask.CurrentStepIndex!=1 || resolvedTarget==null ||
                        resolvedTarget.ItemId!=existingHouseholdItem.UID.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                        snapshotStore.ReadNpcAction(composedTask.Id+":step:0").AcquiredItemId!=resolvedTarget.ItemId ||
                        !NativeNpcInventoryBridge.TryOpen(snapshotStore,selected.Resident.Id,null,0,out snapshotInventory,out snapshotReason) ||
                        snapshotInventory.Items.GetItem(existingHouseholdItem.UID)==null)
                        throw new InvalidOperationException("World-state snapshot between task effects lost exact acquired household UID: "+snapshotReason);
                    betweenStepsSnapshot=true;
                }
                if(composedResult!=null && (composedResult.Phase==NpcTaskPhase.Failed || composedResult.Phase==NpcTaskPhase.Cancelled))break;
                if(sourceResult!=null && NpcActionExecutor.IsTerminal(sourceResult) && sourceResult.Phase!=NpcActionPhase.Completed)break;
                if(handoverResult!=null && NpcActionExecutor.IsTerminal(handoverResult) && handoverResult.Phase!=NpcActionPhase.Completed)break;
                if(composedResult!=null && composedResult.Phase==NpcTaskPhase.Completed && handoverPositionCaptured)break;
                yield return null;
            }
            RequireTime(fixtureDeadline,"composed home handover completion");
            composedResult=store.ReadNpcTask(composedTask.Id);sourceResult=store.ReadNpcAction(composedTask.Id+":step:0");
            handoverResult=store.ReadNpcAction(composedTask.Id+":step:1");
            var composedNpcItems=selected.GetComponent<NativeNpcInventoryOwner>().Binding.Items;
            if(composedResult==null || composedResult.Phase!=NpcTaskPhase.Completed || composedResult.CurrentStepIndex!=2 ||
                sourceResult==null || sourceResult.Phase!=NpcActionPhase.Completed ||
                sourceResult.AcquiredItemId!=existingHouseholdItem.UID.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                handoverResult==null || handoverResult.Phase!=NpcActionPhase.Completed || handoverResult.Target.ItemId!=sourceResult.AcquiredItemId ||
                existingHouseholdLoot.Items.GetItem(existingHouseholdItem.UID)!=null ||
                composedNpcItems.GetItem(existingHouseholdItem.UID)!=null || game.PlayerEntity.Items.GetItem(existingHouseholdItem.UID)==null ||
                game.PlayerEntity.Items.GetItem(existingHouseholdItem.UID).stackCount!=existingHouseholdItem.stackCount ||
                savedLoot.Items.GetItem(uid)==null || savedLoot.Items.GetItem(uid).stackCount!=accepted.Target.Quantity)
                throw new InvalidOperationException("Composed home retrieval handover did not complete or conserve original UID/deposited player gift (task="+
                    (composedResult==null?"missing":composedResult.Phase.ToString())+", source="+
                    (sourceResult==null?"missing":sourceResult.Phase+"/"+sourceResult.FailureReason)+", handover="+
                    (handoverResult==null?"missing":handoverResult.Phase+"/"+handoverResult.FailureReason)+").");
            if(!handoverTargetResolved || !handoverPositionCaptured || handoverArrivalRange<=0f)
                throw new InvalidOperationException("Could not verify the composed handover's runtime-resolved arrival target: "+handoverTargetReason);
            float handoverDistance=Vector3.Distance(handoverCompletionPosition,handoverTarget);
            if(composedMotion.PathLength<0.75f || composedMotion.MaxStep>0.5f || handoverDistance>handoverArrivalRange+0.001f)
                throw new InvalidOperationException("Composed handover did not show bounded real walking into the runtime-resolved transfer range (path="+
                    composedMotion.PathLength.ToString("0.00")+"m, max-step="+composedMotion.MaxStep.ToString("0.000")+"m, completion-distance="+
                    handoverDistance.ToString("0.00")+"m, arrival-range="+handoverArrivalRange.ToString("0.00")+"m).");

            var encoded=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
            var restoredStore=new WorldStateStore(0);restoredStore.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),encoded));
            NativeNpcInventoryBinding reopened;string reason;
            if(!NativeNpcInventoryBridge.TryOpen(restoredStore,selected.Resident.Id,null,0,out reopened,out reason) || reopened.Items.GetItem(uid)!=null ||
                reopened.Items.GetItem(existingHouseholdItem.UID)!=null || game.PlayerEntity.Items.GetItem(existingHouseholdItem.UID)==null ||
                restoredStore.ReadNpcAction(accepted.Id).Phase!=NpcActionPhase.Completed ||
                restoredStore.ReadNpcAction(retrieval.Id).Phase!=NpcActionPhase.Completed ||
                restoredStore.ReadNpcAction(giftJob.Id).Phase!=NpcActionPhase.Completed || restoredStore.ReadNpcAction(waitJob.Id).Phase!=NpcActionPhase.Completed ||
                restoredStore.ReadNpcTask(composedTask.Id).Phase!=NpcTaskPhase.Completed ||
                restoredStore.ReadNpcAction(composedTask.Id+":step:0").AcquiredItemId!=existingHouseholdItem.UID.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                restoredStore.ReadNpcAction(composedTask.Id+":step:1").Target.ItemId!=existingHouseholdItem.UID.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                restoredStore.ReadNpcInventory(selected.Resident.Id).LastTransactionId!=composedTask.Id+":step:1:transfer")
                throw new InvalidOperationException("World-state snapshot restore lost home Wait/gift/deposit/retrieval completion or item ownership: "+reason);
            var replay=restoredStore.ReadNpcAction(retrieval.Id);
            if(NativeNpcInventoryBridge.TryCompleteActionTransfer(restoredStore,replay,Now(),null,existingHouseholdLoot.Items,
                reopened,null,()=>true,out reason) || reopened.Items.GetItem(existingHouseholdItem.UID)!=null ||
                game.PlayerEntity.Items.GetItem(existingHouseholdItem.UID)==null ||
                game.PlayerEntity.Items.GetItem(existingHouseholdItem.UID).stackCount!=existingHouseholdItem.stackCount ||
                existingHouseholdLoot.Items.GetItem(existingHouseholdItem.UID)!=null ||
                savedLoot.Items.GetItem(uid)==null || savedLoot.Items.GetItem(uid).stackCount!=accepted.Target.Quantity)
                throw new InvalidOperationException("Restored completed retrieval replay changed household or NPC inventory.");
            var depositReplay=restoredStore.ReadNpcAction(accepted.Id);
            if(NativeNpcInventoryBridge.TryCompleteActionTransfer(restoredStore,depositReplay,Now(),reopened,null,
                null,savedLoot.Items,()=>true,out reason) || reopened.Items.GetItem(uid)!=null || savedLoot.Items.GetItem(uid)==null)
                throw new InvalidOperationException("Restored completed deposit replay changed household or NPC inventory.");
            var handoverReplay=restoredStore.ReadNpcAction(composedTask.Id+":step:1");
            if(NativeNpcInventoryBridge.TryCompleteActionHandover(restoredStore,handoverReplay,Now(),reopened,game.PlayerEntity.Items,
                ()=>true,out reason) || reopened.Items.GetItem(existingHouseholdItem.UID)!=null ||
                game.PlayerEntity.Items.GetItem(existingHouseholdItem.UID)==null || savedLoot.Items.GetItem(uid)==null)
                throw new InvalidOperationException("Restored completed home handover replay changed NPC, player, or household ownership.");
            string keeperId=selected.Resident.Id;
            ulong retrievedUid=existingHouseholdItem.UID;int retrievedCount=existingHouseholdItem.stackCount;
            var ownerBeforeDisk=selected.GetComponent<NativeNpcInventoryOwner>();
            var ledgerBeforeDisk=store.ReadNpcInventory(keeperId);
            if(ownerBeforeDisk==null || ownerBeforeDisk.Binding==null || ledgerBeforeDisk==null ||
                ledgerBeforeDisk.LastTransactionId!=composedTask.Id+":step:1:transfer")
                throw new InvalidOperationException("Completed home task has no live keeper inventory receipt for the native disk round trip.");
            string keeperItemsBeforeDisk=NativeItems(ownerBeforeDisk.Binding.Items);
            long keeperRevisionBeforeDisk=ownerBeforeDisk.Binding.Revision;
            int keeperGoldBeforeDisk=ledgerBeforeDisk.WalletGold;
            int containerStockDateBeforeDisk=savedLoot.stockedDate;
            yield return SaveReloadAndVerifyHomeState(game,streets,expectedCharacter,keeperId,room.EntryDoor.buildingKey,
                accepted.Target.ContainerId,uid,accepted.Target.Quantity,containerStockDateBeforeDisk,
                retrievedUid,retrievedCount,keeperItemsBeforeDisk,
                keeperRevisionBeforeDisk,keeperGoldBeforeDisk,composedTask.Id+":step:1:transfer",
                accepted.Id,retrieval.Id,composedTask.Id+":step:1",fixtureDeadline);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOME_CONTAINER_DEPOSIT_PASS: actor="+keeperId+"; loadId="+accepted.Target.ContainerId+
                "; depositUid="+uid+"; quantity="+accepted.Target.Quantity+"; measured-walk="+depositMotion.PathLength.ToString("0.00")+"m; max-step="+depositMotion.MaxStep.ToString("0.000")+"m; loot-serializer=pass; world-state-restore=pass; replay=blocked");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOME_CONTAINER_RETRIEVAL_PASS: actor="+keeperId+"; loadId="+retrieval.Target.ContainerId+
                "; retrievalUid="+retrievedUid+"; source=preexisting-household-content; wait=completed; gift=completed; retrieval=completed; "+
                "exact-uid=pass; measured-walk="+retrievalMotion.PathLength.ToString("0.00")+"m; max-step="+retrievalMotion.MaxStep.ToString("0.000")+
                "m; capacity=pass; loot-serializer=pass; restore-to-chest=pass; composed-handover=pass; handoverUid="+
                retrievedUid+"; handover-completion-distance="+handoverDistance.ToString("0.00")+"m; arrival-range="+
                handoverArrivalRange.ToString("0.00")+"m; composed-walk="+composedMotion.PathLength.ToString("0.00")+"m; snapshot-between-effects="+
                betweenStepsSnapshot+"; world-state-restore=pass; native-disk-save-load=pass; replay=blocked");
        }

        private static IEnumerator SaveReloadAndVerifyHomeState(GameManager game,StreetPopulation streets,string character,string residentId,
            int buildingKey,string containerId,ulong giftUid,int giftCount,int expectedStockDate,ulong receivedUid,int receivedCount,
            string expectedKeeperItems,long expectedKeeperRevision,int expectedKeeperGold,string expectedTransactionId,
            string depositActionId,string retrievalActionId,string handoverActionId,float fixtureDeadline)
        {
            var beforeRoom=game.PlayerEnterExit.Interior;
            if(!beforeRoom || beforeRoom.EntryDoor.buildingKey!=buildingKey || !game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Native home disk checkpoint must begin in the actual keeper home interior.");
            string saveName="People of the Iliac Bay - home container disk "+Guid.NewGuid().ToString("N");
            bool saved=false,loaded=false;
            SaveLoadManager.OnSaveEventHandler onSave=data=>saved=true;
            SaveLoadManager.OnLoadEventHandler onLoad=data=>loaded=true;
            SaveLoadManager.OnSave+=onSave;SaveLoadManager.OnLoad+=onLoad;
            int slot=-1;
            try
            {
                game.SaveLoadManager.Save(character,saveName);
                float saveUntil=StageDeadline(fixtureDeadline,60f);
                while(!saved && Time.realtimeSinceStartup<saveUntil)yield return null;
                if(!saved)throw new InvalidOperationException("Completed home task native save timed out.");
                game.SaveLoadManager.EnumerateSaves();
                slot=game.SaveLoadManager.FindSaveFolderByNames(character,saveName);
                if(slot<0)throw new InvalidOperationException("Completed home task native disk save slot was not created.");
                game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();
                float loadUntil=StageDeadline(fixtureDeadline,90f);
                while((!loaded || game.SaveLoadManager.LoadInProgress || !streets.Ready) && Time.realtimeSinceStartup<loadUntil)
                    yield return null;
                if(!loaded || game.SaveLoadManager.LoadInProgress || !streets.Ready)
                    throw new InvalidOperationException("Completed home task native disk reload timed out.");
            }
            finally {SaveLoadManager.OnSave-=onSave;SaveLoadManager.OnLoad-=onLoad;}

            if(game.PlayerEntity.Name!=character)
                throw new InvalidOperationException("Native home disk reload switched away from the owned fixture character.");
            DaggerfallInterior room=null;ResidentActor actor=null;
            float actorUntil=StageDeadline(fixtureDeadline,ResidentMaterializationWaitSeconds);
            while(Time.realtimeSinceStartup<actorUntil)
            {
                var currentRoom=game.PlayerEnterExit.Interior;
                if(currentRoom && currentRoom.EntryDoor.buildingKey==buildingKey && game.PlayerEnterExit.IsPlayerInside)
                {
                    var candidate=streets.FindActor(residentId);
                    if(candidate && candidate.gameObject.activeInHierarchy && candidate.transform.IsChildOf(currentRoom.transform))
                    {room=currentRoom;actor=candidate;break;}
                }
                yield return null;
            }
            if(!room || !actor)
                throw new InvalidOperationException("Native disk reload did not rematerialize the actual home room and household resident "+residentId+".");

            DaggerfallLoot loot=null;int matches=0;
            foreach(var candidate in room.GetComponentsInChildren<DaggerfallLoot>())
                if(candidate && candidate.LoadID.ToString(System.Globalization.CultureInfo.InvariantCulture)==containerId)
                {loot=candidate;matches++;}
            if(matches!=1 || !loot || loot.ContainerType!=LootContainerTypes.HouseContainers ||
                loot.GetComponent<SerializableLootContainer>()==null)
                throw new InvalidOperationException("Native disk reload did not reacquire exactly one serialized household chest by its stable LoadID.");
            var gift=loot.Items.GetItem(giftUid);
            var received=game.PlayerEntity.Items.GetItem(receivedUid);
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            if(owner==null || owner.Binding==null || owner.Binding.PersonId!=residentId ||
                NativeItems(owner.Binding.Items)!=expectedKeeperItems || owner.Binding.Revision!=expectedKeeperRevision ||
                gift==null || gift.stackCount!=giftCount || loot.stockedDate!=expectedStockDate ||
                !NpcHomeContainerLifecycle.IsPersistentStorage(loot.stockedDate) ||
                received==null || received.stackCount!=receivedCount)
                throw new InvalidOperationException("Native home disk reload changed keeper binding/items or lost the exact received/stored UID payload.");

            WorldStateStore diskStore=LiveStore(streets);
            var ledger=diskStore==null?null:diskStore.ReadNpcInventory(residentId);
            if(ledger==null || ledger.PersonId!=residentId || ledger.Revision!=expectedKeeperRevision ||
                ledger.WalletGold!=expectedKeeperGold || ledger.LastTransactionId!=expectedTransactionId ||
                ledger.Items==null || ledger.Items.Count!=owner.Binding.Items.Count)
                throw new InvalidOperationException("Native disk reload did not restore the same household inventory ledger binding and receipt.");
            foreach(var savedItem in ledger.Items)
                if(owner.Binding.Items.GetItem(savedItem.Uid)==null)
                    throw new InvalidOperationException("Native disk reload ledger contains an item missing from the keeper's bound inventory: "+savedItem.Uid);
            if(owner.Binding.Items.GetItem(receivedUid)!=null || owner.Binding.Items.GetItem(giftUid)!=null)
                throw new InvalidOperationException("Native disk reload duplicated an item into the keeper after handover/deposit.");

            var deposit=diskStore.ReadNpcAction(depositActionId);
            var retrieval=diskStore.ReadNpcAction(retrievalActionId);
            var handover=diskStore.ReadNpcAction(handoverActionId);
            string reason=null;
            if(deposit==null || retrieval==null || handover==null || deposit.Phase!=NpcActionPhase.Completed ||
                retrieval.Phase!=NpcActionPhase.Completed || handover.Phase!=NpcActionPhase.Completed)
                throw new InvalidOperationException("Native disk reload lost a completed deposit, retrieval, or handover action.");
            if(NativeNpcInventoryBridge.TryCompleteActionTransfer(diskStore,deposit,Now(),owner.Binding,null,null,loot.Items,()=>true,out reason))
                throw new InvalidOperationException("Native-disk completed deposit replay was accepted: "+reason);
            if(NativeNpcInventoryBridge.TryCompleteActionTransfer(diskStore,retrieval,Now(),null,loot.Items,owner.Binding,null,()=>true,out reason))
                throw new InvalidOperationException("Native-disk completed retrieval replay was accepted: "+reason);
            if(NativeNpcInventoryBridge.TryCompleteActionHandover(diskStore,handover,Now(),owner.Binding,game.PlayerEntity.Items,()=>true,out reason))
                throw new InvalidOperationException("Native-disk completed handover replay was accepted: "+reason);
            if(loot.Items.GetItem(giftUid)==null || loot.Items.GetItem(giftUid).stackCount!=giftCount || loot.stockedDate!=expectedStockDate ||
                game.PlayerEntity.Items.GetItem(receivedUid)==null || game.PlayerEntity.Items.GetItem(receivedUid).stackCount!=receivedCount ||
                owner.Binding.Items.GetItem(receivedUid)!=null || owner.Binding.Items.GetItem(giftUid)!=null ||
                NativeItems(owner.Binding.Items)!=expectedKeeperItems || diskStore.ReadNpcInventory(residentId).LastTransactionId!=expectedTransactionId)
                throw new InvalidOperationException("Replaying disk-restored actions changed home chest, player, or keeper inventory contents.");

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOME_CONTAINER_DISK_ROUNDTRIP_PASS: save="+saveName+"; resident="+residentId+"; home="+buildingKey+
                "; loadId="+containerId+"; keeper-binding=pass; keeper-revision="+expectedKeeperRevision+"; transaction="+expectedTransactionId+
                "; chest-date="+expectedStockDate+"; storedUid="+giftUid+"; storedCount="+giftCount+"; playerUid="+receivedUid+
                "; playerCount="+receivedCount+"; native-save-load=pass; completed-replay=blocked");
        }

        private static WorldStateStore LiveStore(StreetPopulation streets)
        {return typeof(StreetPopulation).GetField("worldStore",PrivateInstance).GetValue(streets) as WorldStateStore;}
        private static string NativeItems(ItemCollection items)
        {var data=items.SerializeItems();return SaveLoadManager.Serialize(data.GetType(),data,false);}
        private static int ReadHour(StreetPopulation streets,string name,int fallback)
        {var field=typeof(StreetPopulation).GetField(name,PrivateInstance);return field==null?fallback:(int)field.GetValue(streets);}
        private static double Now()
        {var clock=DaggerfallUnity.Instance.WorldTime.Now;return clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));}
        private static bool EligibleResident(Resident resident,int home)
        {return resident!=null && resident.IsAlive && !resident.IsQuestProtected && resident.NativeIdentity==null &&
            resident.AgeGroup==ResidentAgeGroup.Adult && resident.HomeKey==home;}
        private static bool IsPrivateHome(BuildingSummary summary)
        {return summary.FactionId==0 && summary.BuildingType>=DFLocation.BuildingTypes.House1 &&
            summary.BuildingType<=DFLocation.BuildingTypes.House6 && !RMBLayout.IsShop(summary.BuildingType) &&
            summary.BuildingType!=DFLocation.BuildingTypes.Tavern;}
        private static bool FindDoor(BuildingDirectory directory,int building,out DaggerfallStaticDoors owner,out StaticDoor door,out int index)
        {
            owner=null;door=default(StaticDoor);index=-1;
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                if(!doors || doors.Doors==null)continue;
                for(int i=0;i<doors.Doors.Length;i++)if(doors.Doors[i].doorType==DoorTypes.Building && doors.Doors[i].buildingKey==building)
                {owner=doors;door=doors.Doors[i];index=i;return true;}
            }
            return false;
        }
        private static bool HasFixedBooking(IList<AppointmentRecord> appointments,double at)
        {
            if(appointments==null)return false;
            foreach(var booking in appointments)if(booking!=null && (booking.Status=="accepted" || booking.Status=="waiting") &&
                at>=booking.StartsAt && at<booking.EndsAt)return true;
            return false;
        }
        private static bool HasRealDepositOption(StreetPopulation streets,string residentId,Vector3 player,WorldStateStore store)
        {
            var actor=streets.FindActor(residentId);if(!actor || !actor.gameObject.activeInHierarchy || actor.IsSleeping ||
                actor.IsConversing || Vector3.Distance(actor.transform.position,player)>8 || store.ActiveNpcTask(residentId)!=null)return false;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();if(!owner || owner.Binding==null)return false;
            for(int i=0;i<owner.Binding.Items.Count;i++)
            {
                var item=owner.Binding.Items.GetItem(i);
                if(item==null || item.IsQuestItem || item.IsEquipped || item.IsSummoned || item.stackCount<1 ||
                    item is HouseholdPropertyKeyItem || item.stackCount>1 && !item.IsAStack())continue;
                var options=streets.ConversationActionOptions(residentId,"store "+item.stackCount+" "+item.LongName+" at home");
                foreach(var option in options)if(option.Kind==NpcActionKind.Transfer && option.Target!=null &&
                    option.Target.TransferDirection==NpcActionTransferDirection.NpcToContainer &&
                    option.Target.ItemId==item.UID.ToString(System.Globalization.CultureInfo.InvariantCulture))return true;
            }
            return false;
        }
        private static bool HasRealHomeGiftOption(StreetPopulation streets,string residentId,ItemCollection playerItems)
        {
            if(playerItems==null)return false;
            for(int i=0;i<playerItems.Count;i++)
            {
                var item=playerItems.GetItem(i);
                if(!Transferable(item) || item.stackCount!=1)continue;
                string request="take one "+item.LongName+" from me";
                foreach(var option in streets.ConversationActionOptions(residentId,request))
                    if(option.Kind==NpcActionKind.Transfer && option.Target!=null &&
                        option.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc &&
                        option.Target.ItemId==item.UID.ToString(System.Globalization.CultureInfo.InvariantCulture))return true;
            }
            return false;
        }
        private static string DescribeHomeOfferState(DaggerfallInterior room,ResidentActor actor,Vector3 playerPosition)
        {
            var builder=new StringBuilder();
            var owner=actor?actor.GetComponent<NativeNpcInventoryOwner>():null;
            float playerDistance=actor?Vector3.Distance(actor.transform.position,playerPosition):float.PositiveInfinity;
            int npcItems=owner==null || owner.Binding==null?0:owner.Binding.Items.Count;
            int transferableNpcItems=0;
            if(owner!=null && owner.Binding!=null)
                for(int i=0;i<owner.Binding.Items.Count;i++)if(Transferable(owner.Binding.Items.GetItem(i)))transferableNpcItems++;
            builder.Append("actorActive=").Append(actor && actor.gameObject.activeInHierarchy)
                .Append(", awake=").Append(actor && !actor.IsSleeping)
                .Append(", conversing=").Append(actor && actor.IsConversing)
                .Append(", ownHome=").Append(room && actor && actor.Resident.HomeKey==room.EntryDoor.buildingKey)
                .Append(", playerDistance=").Append(playerDistance.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)).Append("m")
                .Append(", inventory=").Append(npcItems).Append("/").Append(transferableNpcItems).Append(" transferable");
            if(!room)return builder.Append("; no interior").ToString();
            builder.Append("; containers=[");bool first=true;
            foreach(var loot in room.GetComponentsInChildren<DaggerfallLoot>())
            {
                if(!loot || loot.ContainerType!=LootContainerTypes.HouseContainers)continue;
                if(!first)builder.Append(" | ");first=false;
                int transferable=0;
                for(int i=0;i<loot.Items.Count;i++)if(Transferable(loot.Items.GetItem(i)))transferable++;
                builder.Append(loot.LoadID).Append(":active=").Append(loot.gameObject.activeInHierarchy)
                    .Append(",houseOwned=").Append(loot.houseOwned).Append(",playerOwned=").Append(loot.playerOwned)
                    .Append(",custom=").Append(loot.customDrop).Append(",quest=").Append(loot.corpseQuestUID!=0)
                    .Append(",serializer=").Append(loot.GetComponent<SerializableLootContainer>()!=null)
                    .Append(",stockDate=").Append(loot.stockedDate).Append(",items=").Append(loot.Items.Count)
                    .Append("/").Append(transferable).Append(" transferable");
            }
            return builder.Append(']').ToString();
        }
        private static bool HasRealExistingHomeRetrievalOption(StreetPopulation streets,string residentId,WorldStateStore store)
        {
            var actor=streets.FindActor(residentId);DaggerfallLoot loot;DaggerfallUnityItem item;NpcActionOption option;
            return actor && !actor.IsSleeping && !actor.IsConversing && store.ActiveNpcTask(residentId)==null &&
                FindRealExistingHomeRetrievalOption(streets,actor,out loot,out item,out option);
        }
        private static bool FindRealExistingHomeRetrievalOption(StreetPopulation streets,ResidentActor actor,out DaggerfallLoot selectedLoot,
            out DaggerfallUnityItem selectedItem,out NpcActionOption selectedOption,ulong excludeUid=0)
        {
            selectedLoot=null;selectedItem=null;selectedOption=null;var room=GameManager.Instance.PlayerEnterExit.Interior;
            if(!room || actor.Resident.HomeKey!=room.EntryDoor.buildingKey)return false;
            Vector3 actorPosition=actor.transform.position-Vector3.up;
            foreach(var loot in room.GetComponentsInChildren<DaggerfallLoot>())
            {
                if(!loot || !loot.gameObject.activeInHierarchy || loot.LoadID==0 || loot.ContainerType!=LootContainerTypes.HouseContainers ||
                    loot.playerOwned || loot.customDrop || loot.corpseQuestUID!=0 ||
                    !loot.GetComponent<SerializableLootContainer>() || loot.GetComponentInParent<QuestResourceBehaviour>() ||
                    loot.GetComponentInParent<DaggerfallAction>())continue;
                for(int i=0;i<loot.Items.Count;i++)
                {
                    var item=loot.Items.GetItem(i);if(!Transferable(item) || item.UID==excludeUid)continue;
                    string request="take "+item.stackCount+" "+item.LongName+" from my home storage";
                    foreach(var option in streets.ConversationActionOptions(actor.Resident.Id,request))
                        if(option.Kind==NpcActionKind.Transfer && option.Target!=null &&
                            option.Target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc &&
                            option.Target.ContainerId==loot.LoadID.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                            option.Target.ItemId==item.UID.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                            Vector3.Distance(actorPosition,IndoorTarget(room,option.Target))>1f &&
                            Vector3.Distance(IndoorTarget(room,option.Target),GameManager.Instance.PlayerMotor.transform.position-Vector3.up)>1f &&
                            Vector3.Distance(IndoorTarget(room,option.Target),GameManager.Instance.PlayerMotor.transform.position-Vector3.up)<=8f)
                        {selectedLoot=loot;selectedItem=item;selectedOption=option;return true;}
                }
            }
            return false;
        }
        private static bool Transferable(DaggerfallUnityItem item)
        {
            return item!=null && !item.IsQuestItem && !item.IsEquipped && !item.IsSummoned &&
                !(item is HouseholdPropertyKeyItem) && item.stackCount>0 &&
                (item.stackCount==1 || item.IsAStack() && string.IsNullOrEmpty(item.GetSaveData().className));
        }
        private static ItemCollection ownerAfterGiftItems(ResidentActor actor)
        {
            var owner=actor?actor.GetComponent<NativeNpcInventoryOwner>():null;
            return owner==null || owner.Binding==null?null:owner.Binding.Items;
        }
        private static int CountMatching(ItemCollection items,ItemGroups group,int template)
        {
            int count=0;if(items==null)return count;
            for(int i=0;i<items.Count;i++)
            {var item=items.GetItem(i);if(item!=null && item.ItemGroup==group && item.TemplateIndex==template)count+=item.stackCount;}
            return count;
        }
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    }
}
