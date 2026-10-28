using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class NativeDungeonFollowProbe
    {
        private static bool CurrentImmediateThreat(Evidence evidence)
        {return RoadTravelActors.CurrentDungeonThreat(evidence.Actor)!=null;}
        private static List<DaggerfallEntityBehaviour> ObservedDungeonThreats(ResidentActor actor,float range)
        {
            var threats=new List<DaggerfallEntityBehaviour>();
            foreach(var candidate in GameManager.Instance.PlayerEnterExit.Dungeon.GetComponentsInChildren<DaggerfallEntityBehaviour>())
                if(NativeNpcCombatAssist.Identity(candidate)!=null &&
                    NativeNpcCombatAssist.ThreatensPlayerOrActor(candidate,actor,false) &&
                    NativeNpcCombatAssist.SameVisibleScene(actor,candidate,range))threats.Add(candidate);
            return threats;
        }

        private static IEnumerator VerifyMultipleThreats(Evidence evidence)
        {
            int previousRate=Application.targetFrameRate,previousVsync=QualitySettings.vSyncCount;
            QualitySettings.vSyncCount=0;Application.targetFrameRate=20;
            var observer=GameManager.Instance.PlayerMotor.transform.position;
            float metres=0;int groupFrames=0;
            evidence.Stage="multiple-threats";evidence.ObserverHeldForThreat=true;evidence.ResetMotion();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MULTITHREAT_SETUP: actualEnemies=true; controlledFrameRate=20; observerHeldAtActualPosition=true; npcPlaced=false; enemyChanged=false");
            try
            {
                float deadline=Time.realtimeSinceStartup+12;
                while(Time.realtimeSinceStartup<deadline)
                {
                    var threats=ObservedDungeonThreats(evidence.Actor,12);
                    var positions=new List<Vector3>();var identities=new List<string>();
                    foreach(var threat in threats){positions.Add(threat.transform.position);identities.Add(NativeNpcCombatAssist.Identity(threat));}
                    var before=evidence.Actor.transform.position-Vector3.up;
                    yield return null;
                    evidence.Sample();
                    var after=evidence.Actor.transform.position-Vector3.up;
                    Need(Vector3.Distance(observer,GameManager.Instance.PlayerMotor.transform.position)<0.01f,
                        "Multi-threat observer moved after the actual encounter began.");
                    if(positions.Count<2)continue;
                    groupFrames++;metres+=Vector3.Distance(before,after);
                    for(int index=0;index<positions.Count;index++)
                    {
                        if(!threats[index])continue;
                        var endPosition=threats[index].transform.position;
                        // Enemy movement is not companion movement. Fail only
                        // when the companion step is unsafe against both native
                        // enemy positions sampled around this frame.
                        Need(FlightSafety.SafeStep(before.x,before.z,after.x,after.z,positions[index].x,positions[index].z) ||
                            FlightSafety.SafeStep(before.x,before.z,after.x,after.z,endPosition.x,endPosition.z),
                            "Native companion fled toward another observed attacker; target="+identities[index]+"; count="+positions.Count+
                            "; from="+before.ToString("F5")+"; to="+after.ToString("F5")+"; threatAtStepStart="+positions[index].ToString("F5")+
                            "; threatAtStepEnd="+endPosition.ToString("F5"));
                    }
                }
                evidence.CheckIdentity();
                Need(groupFrames>=10 && metres>=0.5f,"Multi-threat acceptance needs actual movement with at least two visible attackers; frames="+groupFrames+"; metres="+metres);
                evidence.MultipleThreatsVerified=true;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MULTITHREAT_PASS: groupFrames="+groupFrames+"; actualMetres="+metres+
                    "; everyObservedThreatChecked=true; sameBodyInventoryAndContract=true; npcPlaced=false; enemyChanged=false; controlledFrameRate=20");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_PROBE_PASS: multipleThreats=true; containerApproach=false");
            }
            finally {Application.targetFrameRate=previousRate;QualitySettings.vSyncCount=previousVsync;}
        }
    }
}
