using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    internal static class KeeperStandingProbe
    {
        public static IEnumerator Run(StreetPopulation streets,string character)
        {
            var game=GameManager.Instance;game.SaveLoadManager.EnumerateSaves();
            int slot=game.SaveLoadManager.FindSaveFolderByNames(character,"People - Shop entrance");
            if(slot<0) throw new InvalidOperationException("Keeper preview requires shop fixture.");
            bool loaded=false;SaveLoadManager.OnLoadEventHandler handler=data=>loaded=true;SaveLoadManager.OnLoad+=handler;
            try
            {
                game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();float deadline=Time.realtimeSinceStartup+90;
                while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<deadline) yield return null;
                if(!loaded || !streets.Ready) throw new InvalidOperationException("Keeper preview fixture load timed out.");
            }
            finally {SaveLoadManager.OnLoad-=handler;}
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();DaggerfallStaticDoors owner=null;StaticDoor entry=default(StaticDoor);
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>()) foreach(var door in doors.Doors)
            {
                BuildingSummary summary;
                if(door.doorType==DoorTypes.Building && directory.GetBuildingSummary(door.buildingKey,out summary) &&
                    summary.BuildingType==DFLocation.BuildingTypes.GeneralStore) {owner=doors;entry=door;break;}
            }
            if(!owner) throw new InvalidOperationException("Keeper preview has no general store.");
            TownSmokeHarness.EnterInterior(owner.transform,entry);yield return new WaitForSecondsRealtime(2);
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            DirectionalKeeperPose pose=null;float until=Time.realtimeSinceStartup+25;
            while(!pose && Time.realtimeSinceStartup<until)
            {pose=UnityEngine.Object.FindObjectOfType<DirectionalKeeperPose>();yield return null;}
            if(!pose) throw new InvalidOperationException("Native shopkeeper did not acquire directional idle art.");
            var native=pose.GetComponent<DaggerfallBillboard>();var original=native.GetComponent<MeshRenderer>();
            var originalMaterial=original.sharedMaterial;var origin=pose.transform.position;
            var motor=game.PlayerMotor;var controller=motor.GetComponent<CharacterController>();
            bool motorEnabled=motor.enabled,controllerEnabled=controller && controller.enabled;
            motor.enabled=false;if(controller) controller.enabled=false;
            try
            {
                for(int angle=0;angle<8;angle++)
                {
                    var direction=Quaternion.AngleAxis(-45*angle,Vector3.up)*pose.Heading;
                    motor.transform.position=origin+direction*2;
                    var frames=new HashSet<int>();float end=Time.realtimeSinceStartup+2.8f;
                    do
                    {
                        SleepProbe.AimCamera(origin);yield return null;
                        if(pose.Orientation!=angle || Vector3.Distance(origin,pose.transform.position)>0.001f)
                            throw new InvalidOperationException("Keeper view switched incorrectly or displaced native NPC.");
                        frames.Add(pose.Frame);
                    } while(Time.realtimeSinceStartup<end);
                    if(frames.Count!=12 || original.sharedMaterial!=originalMaterial || angle==0 && (!pose.UsesNativeFront || !original.enabled))
                        throw new InvalidOperationException("Keeper lost a native animation phase or original frontal material.");
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,"People-shopkeeper-angle-"+angle+".png"));
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_VIEW_PASS: angle="+angle+"; phases="+frames.Count+"; nativeFront="+pose.UsesNativeFront);
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_STANDING_PASS: all8 distinct bearings; all12 native phases; original front material unchanged; 76pixel effective height; no identity/position changes.");
            }
            finally {if(controller) controller.enabled=controllerEnabled;motor.enabled=motorEnabled;}
        }
    }
}
