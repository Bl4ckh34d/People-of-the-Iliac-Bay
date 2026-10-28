using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class NativeDungeonFollowProbe
    {
        // Select one actual guest, enter only that guest's actual Inn, and
        // approach the existing body. Never retry random Inns for minutes.
        private static IEnumerator FindOrdinaryFixtureCompanion(PeopleOfTheIliacBayMod host,StreetPopulation streets,
            WorldStateStore store,Fixture fixture)
        {
            var game=GameManager.Instance;float deadline=Time.realtimeSinceStartup+30;TravelerVisit selected=null;
            if(!string.IsNullOrWhiteSpace(fixture.residentId))
            {
                var roads=host.GetComponent<RoadTravelActors>();var body=streets.FindActor(fixture.residentId);
                if(!body && roads)body=roads.FindPaidCompanion(fixture.residentId);NpcServiceContract contract;
                if(body && body.gameObject.activeInHierarchy && NpcPaidCompanionTravel.TryReadActiveTravel(store,store.ActiveNpcAction(fixture.residentId),Now(),out contract))yield break;
            }
            while(selected==null && Time.realtimeSinceStartup<deadline)
            {
                var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                if(directory)foreach(var visit in store.TravelerVisitsFor(directory.MapID))
                {
                    var person=store.Traveler(visit.PersonId);var journey=store.TravelerJourneyFor(visit.Id);
                    if(person==null || !person.IsAlive || !TravelerAdventurerPolicy.IsAdventurer(store.ResolveNpcArchetype(person.Id)) ||
                        !string.IsNullOrWhiteSpace(fixture.residentId) && person.Id!=fixture.residentId ||
                        visit.Admission==TravelerAdmission.Refused || Now()<journey.CheckInAt || Now()>=journey.CheckOutAt ||
                        store.ActiveNpcAction(person.Id)!=null || store.ActiveNpcTask(person.Id)!=null)continue;
                    selected=visit;break;
                }
                if(selected==null){DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);yield return null;}
            }
            Need(selected!=null,"No concrete actual adventurer guest became available within 30 seconds. Supply the known existing residentId; no identity or booking is injected.");
            fixture.residentId=selected.PersonId;
            if(!game.PlayerEnterExit.IsPlayerInsideBuilding || game.PlayerEnterExit.Interior.EntryDoor.buildingKey!=selected.TavernKey)
            {
                Need(!game.PlayerEnterExit.IsPlayerInside,"Concrete guest belongs to a different actual Inn; load its exterior fixture.");
                DaggerfallStaticDoors owner=null;StaticDoor entry=default(StaticDoor);
                foreach(var candidate in game.StreamingWorld.GetCurrentBuildingDirectory().GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach(var door in candidate.Doors??new StaticDoor[0])
                        if(door.doorType==DoorTypes.Building && door.buildingKey==selected.TavernKey){owner=candidate;entry=door;break;}
                Need(owner,"Concrete actual guest Inn has no native entrance.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_DISCOVERY_INN: person="+selected.PersonId+"; inn="+selected.TavernKey+
                    "; ordinaryVisit=true; admission="+selected.Admission+"; oneConcreteGuest=true; readinessLimit=30; noNpcInjection=true");
                TownSmokeHarness.EnterInterior(owner.transform,entry);
            }
            while(Time.realtimeSinceStartup<deadline)
            {
                var actor=streets.FindActor(selected.PersonId);
                if(streets.Ready && actor && actor.gameObject.activeInHierarchy && selected.Admission==TravelerAdmission.Admitted && store.TravelerBed(selected.Id,Now())!=null)
                {
                    NativeTravelerFixtureWarmup.PlacePlayerNearActualActor(game,streets,actor);
                    int dailyGold;
                    if(!streets.TryReadPaidCompanionWage(selected.PersonId,out dailyGold)){yield return null;continue;}
                    int before=game.PlayerEntity.GetGoldAmount(),added=Math.Max(0,dailyGold-before);
                    if(added>0)game.PlayerEntity.GoldPieces=checked(game.PlayerEntity.GoldPieces+added);
                    var decision=new NpcActionDecision {DurationSeconds=NpcPaidCompanionAction.DurationSeconds,
                        Option=new NpcActionOption {Kind=NpcActionKind.Follow,PaidCompanion=true,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"}}};
                    int quote;string reason;bool eligible=streets.TryQuotePaidCompanion(selected.PersonId,decision,out quote,out reason);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_DISCOVERY_CONTACT: person="+selected.PersonId+"; actualBed=true; distance="+
                        Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)+"; addedFixturePlayerGold="+added+"; eligible="+eligible+"; reason="+reason);
                    Need(eligible,"Concrete actual guest cannot accept paid service: "+reason);yield break;
                }
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);yield return null;
            }
            throw new InvalidOperationException("Concrete guest's actual admission/body did not become ready within the 30-second fixture limit: "+selected.PersonId+"; inn="+selected.TavernKey+"; admission="+selected.Admission);
        }
    }
}
