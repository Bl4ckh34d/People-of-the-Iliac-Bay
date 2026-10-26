using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class RoadTravelActors
    {
        // Loaded-scene ownership only. Persistent truth stays on the paid Follow action.
        private readonly Dictionary<string,ResidentActor> paidCompanions=new Dictionary<string,ResidentActor>();
        private readonly Dictionary<string,string> paidActions=new Dictionary<string,string>();
        private readonly HashSet<string> retiredPaidCompanions=new HashSet<string>();

        private bool HasPaidCompanions { get { return paidCompanions.Count>0 || paidActions.Count>0; } }

        public bool OwnsPaidCompanion(string residentId)
        {
            return !string.IsNullOrEmpty(residentId) &&
                (paidActions.ContainsKey(residentId) || paidCompanions.ContainsKey(residentId));
        }

        public ResidentActor FindPaidCompanion(string residentId)
        {
            ResidentActor actor;
            paidCompanions.TryGetValue(residentId,out actor);
            return actor;
        }

        // A native exterior door adapter may inspect this live body before it
        // changes owner. It does not alter its pose, contract, or action state.
        public bool TryGetPaidCompanionForDoor(string residentId,WorldStateStore store,double now,out ResidentActor actor)
        {
            actor=null;
            NpcServiceContract contract;
            var action=store==null?null:store.ActiveNpcAction(residentId);
            return ReferenceEquals(current,store) && paidCompanions.TryGetValue(residentId,out actor) && actor &&
                actor.gameObject.activeInHierarchy && NpcPaidCompanionTravel.TryReadActiveTravel(store,action,now,out contract);
        }

        // Caller has already captured the actual exterior body point for the
        // existing native door-follow adapter. Release never destroys or moves it.
        public bool TryReleasePaidCompanionToStreet(string residentId,WorldStateStore store,double now,out ResidentActor actor)
        {
            if(!TryGetPaidCompanionForDoor(residentId,store,now,out actor))return false;
            paidCompanions.Remove(residentId);paidActions.Remove(residentId);retiredPaidCompanions.Remove(residentId);
            return true;
        }

        // Exterior teardown only. Preserve this visible native body and world pose
        // through a seamless gate by changing owner, not by destroying/recreating it.
        public bool TryDeferPaidCompanion(ResidentActor actor,WorldStateStore store,double now)
        {
            if(!actor || actor.Resident==null || !actor.Resident.IsAlive || source==null ||
                !ReferenceEquals(source(),store)) return false;
            var action=store.ActiveNpcAction(actor.Resident.Id);
            NpcServiceContract contract;
            if(!NpcPaidCompanionTravel.TryReadActiveTravel(store,action,now,out contract)) return false;
            if(action.PaidTravelHandoff==null)
            {
                if(!store.TryDeferPaidTravel(action.Id,action.Revision,now)) return false;
                action=store.ReadNpcAction(action.Id);
            }
            if(action==null || action.PaidTravelHandoff==null ||
                action.PaidTravelHandoff.ContractId!=contract.ContractId)return false;
            current=store;
            actor.TransferOwner(transform,navigation);
            paidActions[actor.Resident.Id]=action.Id;
            paidCompanions[actor.Resident.Id]=actor;
            return true;
        }

        private void UpdatePaidCompanions(WorldStateStore store,double now)
        {
            foreach(var action in store.PendingNpcActions())
            {
                if(OwnsDungeonFollow(action.ResidentId) || NpcFollowTransition.IsDungeonSpace(action.LastPosition) ||
                    action.FollowTransition!=null && NpcFollowTransition.IsDungeonSpace(action.FollowTransition.Destination))continue;
                var handoff=action.PaidTravelHandoff;
                NpcServiceContract contract;
                if(handoff!=null && handoff.ContractId==NpcPaidCompanionAction.ContractId(action) &&
                    NpcPaidCompanionTravel.TryReadActiveTravel(store,action,now,out contract))
                    paidActions[action.ResidentId]=action.Id;
            }
            foreach(var pair in new List<KeyValuePair<string,string>>(paidActions))
            {
                string residentId=pair.Key;
                if(OwnsDungeonFollow(residentId))continue;
                var action=store.ReadNpcAction(pair.Value);
                NpcServiceContract contract;
                if(!NpcPaidCompanionTravel.TryReadActiveTravel(store,action,now,out contract))
                { if(!HoldRetiredPaidCompanion(residentId,store,action,now)) RemovePaidCompanion(residentId); continue; }
                ResidentActor actor;
                if(!paidCompanions.TryGetValue(residentId,out actor) || !actor)
                {
                    // A local owner wins if an ordinary doorway-follow already rebuilt it.
                    if(streets!=null && streets.FindActor(residentId)) continue;
                    if(!TrySpawnPaidCompanion(store,action,now,out actor)) continue;
                    paidCompanions[residentId]=actor;
                }
                retiredPaidCompanions.Remove(residentId);
                FollowPlayer(actor);
            }
        }

        private bool TrySpawnPaidCompanion(WorldStateStore store,NpcActionJob action,double now,out ResidentActor actor)
        {
            actor=null;
            if(action==null || action.PaidTravelHandoff==null || GameManager.Instance.IsPlayerInside ||
                GameManager.Instance.StreamingWorld.IsInit || streets!=null && streets.FindActor(action.ResidentId)) return false;
            var resident=store.ResolveResident(action.ResidentId);
            if(resident==null || !resident.IsAlive) return false;
            var body=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,transform);
            body.name="People of the Iliac Bay paid companion "+resident.Id;
            actor=body.AddComponent<ResidentActor>();
            actor.Initialize(resident,0,navigation,inventoryStore:store);
            // Do not consume the deferred handoff unless the replacement body is
            // actually live. A failed prefab/scene attach must leave it retryable.
            if(!actor.gameObject.activeInHierarchy)
            { actor.gameObject.SetActive(false); Destroy(body); actor=null; return false; }
            Vector3 ground=GameManager.Instance.PlayerMotor.transform.position-Vector3.up;
            actor.Place(ground,-GameManager.Instance.PlayerMotor.transform.forward,preferNative:false);
            // No yield occurs between this live body and the claim. If service
            // changed, discard it before it can become a second owner.
            if(!store.TryResumePaidTravel(action.Id,action.Revision,now))
            { actor.CaptureState(); actor.gameObject.SetActive(false); Destroy(body); actor=null; return false; }
            return true;
        }

        private static void FollowPlayer(ResidentActor actor)
        {
            Vector3 player=GameManager.Instance.PlayerMotor.transform.position-Vector3.up;
            Vector3 delta=player-(actor.transform.position-Vector3.up); delta.y=0;
            if(delta.sqrMagnitude>1.8f*1.8f) actor.Place(player,delta.normalized,preferNative:false);
            else actor.Place(actor.transform.position-Vector3.up,
                delta.sqrMagnitude>0.0001f?delta.normalized:actor.transform.forward,
                standing:true,freeStanding:true,preferNative:false);
        }

        // Called only while DFU is initializing a new exterior. This is the one
        // unobserved path allowed to replace a body; no old-world position is stored.
        private void CapturePaidForUnobservedReload(WorldStateStore store,double now)
        {
            foreach(var pair in new List<KeyValuePair<string,ResidentActor>>(paidCompanions))
            {
                var actor=pair.Value;
                string actionId;
                if(!actor || !paidActions.TryGetValue(pair.Key,out actionId)) continue;
                var action=store.ReadNpcAction(actionId);
                NpcServiceContract contract;
                if(!NpcPaidCompanionTravel.TryReadActiveTravel(store,action,now,out contract))
                { RemovePaidCompanion(pair.Key); continue; }
                // A previous seamless handoff is already durable. A body resumed
                // after an earlier reload must be deferred again before this body
                // can disappear, otherwise it could never be reconstructed.
                if(action.PaidTravelHandoff==null &&
                    !store.TryDeferPaidTravel(action.Id,action.Revision,now)) continue;
                actor.CaptureState(); actor.gameObject.SetActive(false); Destroy(actor.gameObject);
                paidCompanions.Remove(pair.Key);
            }
        }

        private void CapturePaidCompanionStates()
        {
            foreach(var actor in paidCompanions.Values) if(actor) actor.CaptureState();
        }

        private void ClearPaidCompanions()
        {
            foreach(var actor in paidCompanions.Values)
                if(actor) {actor.CaptureState(); actor.gameObject.SetActive(false); Destroy(actor.gameObject);}
            paidCompanions.Clear(); paidActions.Clear(); retiredPaidCompanions.Clear();
        }

        // There is no measured traveler return route in a remote exterior. Do
        // not invent one or make a visible paid traveler vanish at expiry: keep
        // the real body at its last observed point, then unload only after the
        // player has actually left sight (or DFU begins a real load).
        private bool HoldRetiredPaidCompanion(string residentId,WorldStateStore store,NpcActionJob action,double now)
        {
            ResidentActor actor;
            if(!paidCompanions.TryGetValue(residentId,out actor) || !actor)return false;
            if(action!=null && !NpcActionExecutor.IsTerminal(action) &&
                store.TryReleaseExpiredPaidTravel(action.Id,action.Revision,now))
            {
                action=store.ReadNpcAction(action.Id);
                if(action!=null && !NpcActionExecutor.IsTerminal(action))
                    store.ObserveNpcAction(action.Id,action.Revision,now,
                        NpcActionObservation.Interrupted("The paid Follow service reached its agreed end."));
                action=store.ReadNpcAction(action.Id);
            }
            if(action==null || !NpcPaidCompanionAction.IsPaidFollowJob(action))return false;
            retiredPaidCompanions.Add(residentId);
            string ignored;
            if(NpcActionExecutor.IsTerminal(action) && streets!=null)
                streets.TrySettlePaidCompanionAction(action,NpcServiceEndReason.NaturalExpiry,out ignored);
            Vector3 delta=actor.transform.position-GameManager.Instance.PlayerMotor.transform.position;
            if(delta.sqrMagnitude>165f*165f) {RemovePaidCompanion(residentId);return false;}
            actor.Place(actor.transform.position-Vector3.up,actor.transform.forward,standing:true,freeStanding:true,preferNative:false);
            return true;
        }

        private void RemovePaidCompanion(string residentId)
        {
            ResidentActor actor;
            if(paidCompanions.TryGetValue(residentId,out actor) && actor)
            {actor.CaptureState(); actor.gameObject.SetActive(false); Destroy(actor.gameObject);}
            paidCompanions.Remove(residentId); paidActions.Remove(residentId);
            retiredPaidCompanions.Remove(residentId);
        }
    }
}
