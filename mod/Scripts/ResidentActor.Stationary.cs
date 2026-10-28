using System;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class ResidentActor
    {
        public bool IsStationary { get; private set; }
        private bool ownsStationaryEntity,ownsStationaryEffects;
        public string DisplayName { get { return Person ? Person.NameNPC : Resident == null ? null : Resident.FullName; } }
        public Vector3 BodyOffset
        {
            get
            {
                var billboard=IsStationary?GetComponent<DaggerfallBillboard>():null;
                return billboard?Vector3.up*billboard.Summary.Size.y*0.5f:Vector3.up;
            }
        }
        public Vector3 GroundPosition { get { return transform.position-BodyOffset; } }

        // One authored static body, one household member, and the same native
        // entity, inventory, profile, vital and effect bindings as mobile people.
        // Rendering and ordinary post movement remain owned by the native flat.
        public void InitializeStationary(Resident resident, WorldStateStore world, Action onDeath = null)
        {
            if (Resident != null)
            {
                if(IsStationary && ReferenceEquals(Resident,resident) && stateStore==world &&
                    world.IsNpcVitalsWriteEpochCurrent(stateWriteEpoch))return;
                if(!IsStationary)throw new InvalidOperationException("Mobile actor cannot be rebound as a stationary flat.");
                ReleaseStationary();
            }
            var npc = GetComponent<StaticNPC>();
            if (!npc || world == null || resident == null || resident.NativeIdentity == null ||
                resident.IsQuestProtected || !resident.IsAlive || resident.AgeGroup != ResidentAgeGroup.Adult ||
                !NativeStaticNpcInventoryOwner.Matches(npc, resident.NativeIdentity))
                throw new InvalidOperationException("Stationary actor lacks an exact ordinary native household identity.");
            var body = GetComponent<DaggerfallEntityBehaviour>();
            if (body && body.Entity != null && !(body.Entity is CivilianEntity))
                throw new InvalidOperationException("Stationary actor already has a protected native entity.");
            if (!body) {body = gameObject.AddComponent<DaggerfallEntityBehaviour>();ownsStationaryEntity=true;}
            if (body.Entity == null)
            {
                var nativeSet = typeof(DaggerfallEntityBehaviour).GetMethod("SetEntityType", BindingFlags.Instance | BindingFlags.NonPublic);
                if (nativeSet == null) throw new MissingMethodException("Native civilian entity initialization API changed.");
                body.EntityType = EntityTypes.CivilianNPC;
                nativeSet.Invoke(body, new object[] { EntityTypes.CivilianNPC });
            }
            Resident = resident; IsStationary = true; death = onDeath;
            stateStore = world; stateWriteEpoch = world.CaptureNpcVitalsWriteEpoch(); entity = body.Entity;
            if (!GetComponent<EntityEffectManager>()) {gameObject.AddComponent<EntityEffectManager>();ownsStationaryEffects=true;}
            var owner = GetComponent<NativeNpcInventoryOwner>();
            if (!owner) owner = gameObject.AddComponent<NativeNpcInventoryOwner>();
            if(owner.Binding==null)owner.Bind(entity, world, resident);
            else if(owner.Binding.PersonId!=resident.Id || !ReferenceEquals(owner.Binding.Items,entity.Items))
                throw new InvalidOperationException("Stationary actor inventory belongs to another native person.");
            NativeNpcVitalsBinding.BindTo(gameObject, entity, world, resident);
            NativeNpcPoisonOnsetBinding.BindTo(gameObject, world, resident);
            entity.OnDeath += Died;
            completedPose=CompletedNpcPose.Bind(this);
        }

        public void ReleaseStationary()
        {
            if(!IsStationary)return;
            if(completedPose){completedPose.Release();UnityEngine.Object.DestroyImmediate(completedPose);completedPose=null;}
            if(stateStore!=null && stateStore.IsNpcVitalsWriteEpochCurrent(stateWriteEpoch))CaptureState();
            if(entity!=null)entity.OnDeath-=Died;
            RemoveStationaryBinding<NativeNpcInventoryOwner>();
            RemoveStationaryBinding<NativeNpcVitalsBinding>();
            RemoveStationaryBinding<NativeNpcPoisonOnsetBinding>();
            RemoveStationaryBinding<NativeNpcConditionBinding>();
            RemoveStationaryBinding<NativeNpcKnownSpellBinding>();
            RemoveStationaryBinding<NativeNpcSceneObserver>();
            if(ownsStationaryEffects)RemoveStationaryBinding<EntityEffectManager>();
            if(ownsStationaryEntity)RemoveStationaryBinding<DaggerfallEntityBehaviour>();
            Resident=null;entity=null;death=null;stateStore=null;stateWriteEpoch=null;
            IsStationary=false;ownsStationaryEntity=ownsStationaryEffects=false;
        }
        private void RemoveStationaryBinding<T>() where T : Component
        {
            var component=GetComponent<T>();
            // Clear must release subscriptions and exact item ownership before
            // another save branch binds this same native sprite in this frame.
            if(component)UnityEngine.Object.DestroyImmediate(component);
        }
    }
}
