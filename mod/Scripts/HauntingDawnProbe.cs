using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class HauntingDawnProbe
    {
        private static void TimeOfDay(double seconds)
        {
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)((seconds-now%86400+86400)%86400));
        }
        public static IEnumerator Run()
        {
            var game=GameManager.Instance;
            game.PlayerEntity.GodMode=true;
            game.PlayerEntity.GlobalVars.SetGlobalVar(0,false);
            var ids=QuestMachine.Instance.FindQuests("S0000977",true);
            if(ids.Length==0)
            {
                // Older disposable fixtures did not initialize the main-quest loop.
                // Start the real script in this test session only; never rewrite the save.
                QuestMachine.Instance.StartQuest("S0000977");
                ids=QuestMachine.Instance.FindQuests("S0000977",true);
            }
            if(ids.Length==0) throw new InvalidOperationException("Native haunting quest failed to start.");
            var quest=QuestMachine.Instance.GetQuest(ids[0]);
            var resource=quest.GetFoe(new Symbol("_F.00_"));
            if(resource==null) throw new InvalidOperationException("Native haunting ghost resource missing.");
            GameObject ghost=null,control=null;
            try
            {
                TimeOfDay(19*3600);
                yield return NativeHauntingTargetsProbe.Run(quest);
                TimeOfDay(4*3600+59*60);
                var position=game.PlayerMotor.transform.position+game.PlayerMotor.transform.forward*5;
                ghost=GameObjectHelper.CreateFoeGameObjects(position,MobileTypes.Ghost,1,MobileReactions.Hostile,resource)[0];
                control=GameObjectHelper.CreateFoeGameObjects(position+Vector3.right*3,MobileTypes.Ghost,1,MobileReactions.Hostile)[0];
                ghost.transform.SetParent(game.PlayerEnterExit.ExteriorParent.transform,true);
                control.transform.SetParent(game.PlayerEnterExit.ExteriorParent.transform,true);
                ghost.SetActive(true); control.SetActive(true);
                yield return new WaitForSecondsRealtime(1);
                var owner=ghost.GetComponent<QuestResourceBehaviour>();
                if(!owner || owner.QuestUID!=quest.UID) throw new InvalidOperationException("Ghost lacks genuine quest ownership.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DAWN_NIGHT: native quest ghost active="+ghost.activeInHierarchy);
                TimeOfDay(5*3600+60);
                yield return new WaitForSecondsRealtime(3);
                if(!control || !control.activeInHierarchy) throw new InvalidOperationException("Unrelated ghost incorrectly removed at dawn.");
                if(ghost && ghost.activeInHierarchy) throw new InvalidOperationException("Native quest-owned haunting ghost survives dawn.");
                TimeOfDay(4*3600+59*60);
                ghost=GameObjectHelper.CreateFoeGameObjects(position,MobileTypes.Wraith,1,MobileReactions.Hostile,
                    quest.GetFoe(new Symbol("_wraith_")))[0];
                ghost.transform.SetParent(game.PlayerEnterExit.ExteriorParent.transform,true);
                ghost.SetActive(true);
                yield return null;
                var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                DaggerfallStaticDoors inn=null; StaticDoor entry=default(StaticDoor);
                foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach(var door in candidate.Doors ?? new StaticDoor[0])
                    {
                        BuildingSummary summary;
                        if(door.doorType==DoorTypes.Building && directory.GetBuildingSummary(door.buildingKey,out summary) &&
                            summary.BuildingType==DaggerfallConnect.DFLocation.BuildingTypes.Tavern)
                        {inn=candidate;entry=door;break;}
                    }
                if(!inn) throw new InvalidOperationException("Dawn probe needs an inn.");
                TownSmokeHarness.EnterInterior(inn.transform,entry);
                yield return new WaitForSecondsRealtime(.5f);
                if(!game.IsPlayerInside || !ghost || ghost.activeInHierarchy)
                    throw new InvalidOperationException("Expected native exterior ghost suspended outside the inn.");
                TimeOfDay(5*3600+60);
                yield return new WaitForSecondsRealtime(.5f);
                if(ghost) throw new InvalidOperationException("Inactive exterior haunting actor survived dawn indoors.");
                game.PlayerEnterExit.TransitionExterior();
                yield return new WaitForSecondsRealtime(.5f);
                if(!control || !control.activeInHierarchy) throw new InvalidOperationException("Unrelated exterior ghost lost across inn visit.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HAUNTING_INN_DAWN_PASS: inactive exterior wraith expires; control returns.");
                TimeOfDay(4*3600+59*60);
                ghost=GameObjectHelper.CreateFoeGameObjects(position,MobileTypes.Ghost,1,MobileReactions.Hostile,resource)[0];
                TimeOfDay(5*3600+60);
                yield return null;
                yield return null;
                if(!ghost) throw new InvalidOperationException("Expiry destroyed a pending native placement before its caller finished.");
                ghost.SetActive(true);
                if(ghost.activeInHierarchy) throw new InvalidOperationException("Late native placement activated an expired haunting enemy.");
                yield return null;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HAUNTING_PENDING_PASS: delayed native placement stays valid until activation and cannot attack.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HAUNTING_DAWN_PASS: quest ghost expires; unrelated ghost remains.");
            }
            finally
            {
                if(ghost) UnityEngine.Object.Destroy(ghost);
                if(control) UnityEngine.Object.Destroy(control);
            }
        }
    }
}
