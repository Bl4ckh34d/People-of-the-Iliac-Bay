using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Owned editor fixture: use a real eligible resident and surveyed static
        // person. Only the starting proximity is arranged; ordinary selection,
        // standing, context, memory and route resumption remain under test.
        internal IEnumerator VerifyStreetEncounter()
        {
            var game=GameManager.Instance;var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((43200-clock.ToSeconds()%86400+86400)%86400));
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            ResidentActor visitor=null;StationaryParticipant partner=null;Vector3 start=Vector3.zero;
            float deadline=Time.realtimeSinceStartup+90;
            while(!visitor && Time.realtimeSinceStartup<deadline)
            {
                SurveyStationaryObservers();double now=clock.ToSeconds();
                if(NativeNavigationWorld.Current && NativeNavigationWorld.Current.Ready)
                foreach(var a in actors.Values)
                {
                    if(!ChatEligible(a,now) || !ChatReady(a.Resident.Id,now))continue;
                    foreach(var entry in stationaryParticipants.Values)
                    {
                        if(!StaticChatEligible(entry,now) || !ChatReady(entry.Person.Id,now) ||
                            !StreetChatTiming.ShouldMeet(a.Resident.AppearanceSeed,unchecked((uint)entry.Person.NativeHash),(uint)(now/600)) ||
                            !ChatHasTime(a,now,now+180))continue;
                        if(!TryEncounterProbeStart(entry,out start))continue;
                        visitor=a;partner=entry;break;
                    }
                    if(visitor)break;
                }
                if(!visitor)yield return null;
            }
            if(!visitor)throw new InvalidOperationException("No eligible native street/static pair; bodies="+actors.Count+
                "; stationary="+stationaryParticipants.Count+"; inside="+(bool)interior);
            bool enabledBefore=enabled;var playerBefore=game.PlayerMotor.transform.position;
            var visitorBefore=visitor.transform.position;var postBefore=partner.Npc.transform.position;
            var cooldowns=new Dictionary<string,double>(nextStreetChat);enabled=false;
            try
            {
                double now=clock.ToSeconds();
                var previousActivity=GetState(visitor.Resident.Id,now);
                if(!previousActivity.TravelDestination.HasValue)
                    throw new InvalidOperationException("Street encounter fixture requires an existing travel destination.");
                var expectedDestination=geometry.ScenePoint(previousActivity.TravelDestination.Value);
                var previousEvents=new HashSet<string>();
                foreach(var prior in worldStore.Snapshot().SceneMemories)previousEvents.Add(prior.EventId);
                foreach(var a in actors.Values)if(a!=visitor)nextStreetChat[a.Resident.Id]=now+3600;
                foreach(var entry in stationaryParticipants.Values)if(entry!=partner)nextStreetChat[entry.Person.Id]=now+3600;
                var native=visitor.GetComponent<NativeResidentNavigation>();if(native)native.ResetPlacement();
                visitor.GetComponent<CrowdMotion>().RestorePlacement(start);
                game.PlayerMotor.transform.position=start+Vector3.up+Vector3.right*2.5f;
                for(int i=0;i<actors.Count+1 && !streetChats.ContainsKey(visitor.Resident.Id);i++)
                {nextChatScan=0;PrepareStreetChats(now);}
                StreetChat chat;
                if(!streetChats.TryGetValue(visitor.Resident.Id,out chat) || chat.Stationary!=partner)
                    throw new InvalidOperationException("Production street selector did not choose the eligible static partner.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STREET_ENCOUNTER_START: resident="+visitor.Resident.Id+
                    "; static="+partner.Person.Id+"; duration="+(chat.EndsAt-now)+"; timeScale="+DaggerfallUnity.Instance.WorldTime.TimeScale);
                // Keep ordinary updates running. Advancing the clock past the end
                // would test time-skip reconstruction, not a physical departure.
                enabled=enabledBefore;
                float minFacing=1;deadline=Time.realtimeSinceStartup+3;
                while(Time.realtimeSinceStartup<deadline)
                {
                    yield return null;
                    if(!streetChats.ContainsKey(visitor.Resident.Id))throw new InvalidOperationException("Encounter lost movement ownership early.");
                    var direction=partner.Npc.transform.position-visitor.transform.position;direction.y=0;
                    var heading=visitor.transform.forward;heading.y=0;
                    minFacing=Mathf.Min(minFacing,Vector3.Dot(heading.normalized,direction.normalized));
                    if(!visitor.IsStandingStill || Vector3.Distance(postBefore,partner.Npc.transform.position)>0.01f)
                        throw new InvalidOperationException("Encounter moves the stationary person or keeps the visitor walking.");
                }
                string firstContext,otherContext;
                if(minFacing<0.8f || !TryStreetChatContext(visitor.Resident.Id,now,out firstContext) ||
                    !firstContext.Contains(partner.Person.Name) || !TryStreetChatContext(partner.Person.Id,now,out otherContext) ||
                    !otherContext.Contains(visitor.Person.NameNPC))throw new InvalidOperationException("Encounter facing/context mismatch.");
                var snapshot=worldStore.Snapshot();
                var memory=snapshot.SceneMemories.Find(m=>m.ObserverId==visitor.Resident.Id &&
                    !previousEvents.Contains(m.EventId) && m.Kind==NpcSceneEventKind.Conversation && m.Observation.Contains(partner.Person.Name));
                if(memory==null || !snapshot.SceneMemories.Exists(m=>m.ObserverId==partner.Person.Id && m.EventId==memory.EventId))
                    throw new InvalidOperationException("Encounter lacks both participants' memories.");
                var restored=new WorldStateStore(1);restored.Restore(snapshot);
                if(!restored.Snapshot().SceneMemories.Exists(m=>m.ObserverId==partner.Person.Id && m.EventId==memory.EventId) ||
                    !restored.RecallSceneEvents(partner.Person.Id,clock.ToSeconds()).Contains(visitor.Person.NameNPC) ||
                    !restored.RecallSceneEvents(visitor.Resident.Id,clock.ToSeconds()).Contains(partner.Person.Name))
                    throw new InvalidOperationException("Static encounter memory failed snapshot restore.");
                deadline=Time.realtimeSinceStartup+90;
                var previous=visitor.transform.position;float maximumStep=0;var resumedFrom=previous;
                while(streetChats.ContainsKey(visitor.Resident.Id) && Time.realtimeSinceStartup<deadline)
                {
                    resumedFrom=previous;yield return null;
                    if(!visitor)throw new InvalidOperationException("Visitor disappeared before ending the encounter.");
                    float step=Vector3.Distance(previous,visitor.transform.position);maximumStep=Mathf.Max(maximumStep,step);
                    if(step>0.7f)throw new InvalidOperationException("Encounter transition teleports instead of walking: "+step);
                    previous=visitor.transform.position;
                }
                now=clock.ToSeconds();
                if(streetChats.ContainsKey(visitor.Resident.Id) || streetChats.ContainsKey(partner.Person.Id) ||
                    TryStreetChatContext(visitor.Resident.Id,now,out firstContext))throw new InvalidOperationException("Expired encounter retains ownership/context.");
                deadline=Time.realtimeSinceStartup+20;
                while(visitor && Vector3.Distance(resumedFrom,visitor.transform.position)<1 && Time.realtimeSinceStartup<deadline)
                {
                    yield return null;if(!visitor)break;
                    float step=Vector3.Distance(previous,visitor.transform.position);maximumStep=Mathf.Max(maximumStep,step);
                    if(step>0.7f)throw new InvalidOperationException("Encounter departure teleports instead of walking: "+step);
                    previous=visitor.transform.position;
                }
                if(!visitor || Vector3.Distance(resumedFrom,visitor.transform.position)<1)
                    throw new InvalidOperationException("Visitor did not resume actual route movement after the encounter.");
                native=visitor.GetComponent<NativeResidentNavigation>();
                if(!native || !native.HasDestination || Vector3.Distance(native.Destination,expectedDestination)>0.4f)
                    throw new InvalidOperationException("Encounter did not resume the previous native travel destination.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STREET_ENCOUNTER_PASS: resident="+visitor.Resident.Id+
                    "; static="+partner.Person.Id+"; minimumFacing="+minFacing+"; bothContexts=true; bothMemories=true; restore=true; resumedMetres="+
                    Vector3.Distance(resumedFrom,visitor.transform.position)+"; maximumStep="+maximumStep+
                    "; previousDestination=true; directionalStaticArt="+(bool)partner.Npc.GetComponent<DirectionalKeeperPose>());
            }
            finally
            {
                ClearStreetChats();nextStreetChat.Clear();foreach(var pair in cooldowns)nextStreetChat[pair.Key]=pair.Value;
                if(visitor)visitor.GetComponent<CrowdMotion>().RestorePlacement(visitorBefore-Vector3.up);
                game.PlayerMotor.transform.position=playerBefore;enabled=enabledBefore;
            }
        }

        private bool TryEncounterProbeStart(StationaryParticipant entry,out Vector3 position)
        {
            position=Vector3.zero;
            for(int i=0;i<16;i++)
            {
                float angle=i*Mathf.PI/8;NavMeshHit hit;
                var point=entry.Npc.transform.position+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*1.15f;
                if(!NavMesh.SamplePosition(point,out hit,2,NavMesh.AllAreas))continue;
                var eye=hit.position+Vector3.up;
                if(!SocialInteractionSpacing.Contains(Vector3.Distance(eye,entry.Npc.transform.position)) ||
                    !CivilianResponses.Sees(eye,entry.Npc.transform.position,3))continue;
                position=hit.position;return true;
            }
            return false;
        }
    }
}
