using System;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // One adapter on the actual static body. Native enemy bodies retain their
    // existing death/corpse ownership; civilian flats use the shared NPC ledger.
    internal sealed class NativeStationaryLife : MonoBehaviour
    {
        internal NativePerson Person;
        private PeopleOfTheIliacBayMod host;
        private WorldStateStore store;
        private object epoch;
        private DaggerfallEntity entity;
        private bool ownEntity,ownInventory,ownEffects,finished,playerHit,nativeDeath;
        private NativeNpcInventoryOwner inventory;
        internal bool Current(WorldStateStore world){return world!=null && store==world && world.IsNpcVitalsWriteEpochCurrent(epoch);}
        internal void Bind(PeopleOfTheIliacBayMod owner,WorldStateStore world,NativePerson person)
        {
            host=owner;store=world;epoch=world.CaptureNpcVitalsWriteEpoch();Person=person;
            var body=GetComponent<DaggerfallEntityBehaviour>();
            if(!body){body=gameObject.AddComponent<DaggerfallEntityBehaviour>();ownEntity=true;}
            if(body.Entity==null)
            {
                body.EntityType=EntityTypes.CivilianNPC;
                typeof(DaggerfallEntityBehaviour).GetMethod("SetEntityType",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(body,new object[]{EntityTypes.CivilianNPC});
            }
            entity=body.Entity;nativeDeath=entity is EnemyEntity && GetComponent<EnemyDeath>();
            if(!nativeDeath)
            {
                if(!GetComponent<EntityEffectManager>()){gameObject.AddComponent<EntityEffectManager>();ownEffects=true;}
                inventory=GetComponent<NativeNpcInventoryOwner>();
                if(!inventory)
                {
                    if(!world.HasNpcInventory(person.Id) && entity.Items.Count==0)
                    {
                        var initial=NativeNpcStartingBelongings.CreateInitialItems(world,person.Id,
                            explicitGender:GetComponent<StaticNPC>().Data.gender,explicitRace:GetComponent<StaticNPC>().Data.race);
                        while(initial.Count>0){var item=initial.GetItem(0);initial.RemoveItem(item);entity.Items.AddItem(item,noStack:true);}
                    }
                    inventory=gameObject.AddComponent<NativeNpcInventoryOwner>();ownInventory=true;
                    var identity=world.ResolveResident(person.Id)??new Resident {Id=person.Id,NativeIdentity=person,
                        FullName=person.Name,HomeKey=person.HomeKey,Sex=person.Sex,Race=person.Race,IsAlive=person.IsAlive,
                        AgeGroup=GetComponent<StaticNPC>().IsChildNPC?ResidentAgeGroup.Child:ResidentAgeGroup.Adult};
                    inventory.Bind(entity,world,identity);
                }
            }
            entity.OnDeath+=Died;
        }
        internal Action<Exception> BeginPlayerHit(bool lethalMelee)
        {
            if(finished || !Current(store) || !Person.IsAlive)return null;
            playerHit=true;host.ObserveStationaryPlayerHit(this);
            return error=>
            {
                if(error==null && lethalMelee && !nativeDeath && entity.CurrentHealth>0)entity.SetHealth(0);
                // A surviving spell hit does not attribute a later unrelated death.
                if(entity.CurrentHealth>0)playerHit=false;
            };
        }
        private void Died(DaggerfallEntity source)
        {
            if(finished || !Current(store))return;finished=true;
            inventory=GetComponent<NativeNpcInventoryOwner>();if(inventory)inventory.Capture();
            if(nativeDeath)
            {
                Person.IsAlive=false;var member=store.ResolveResident(Person.Id);if(member!=null)member.IsAlive=false;
                return;
            }
            host.RecordStationaryDeath(this,playerHit);
            var resource=GetComponent<QuestResourceBehaviour>();
            if(resource)
            {
                var person=resource.TargetResource as Person;if(person!=null)person.DestroyNPC();
                var foe=resource.TargetResource as Foe;
                if(foe!=null && !resource.IsFoeDead){foe.SetInjured();foe.IncrementKills();}
            }
            var npc=GetComponent<StaticNPC>();var quests=QuestMachine.Instance;
            if(npc && quests)
                foreach(ulong uid in quests.GetAllActiveQuests())
                {
                    var quest=quests.GetQuest(uid);if(quest==null)continue;
                    foreach(Person person in quest.GetAllResources(typeof(Person)))
                        if(quests.IsNPCDataEqual(person.QuestorData,npc.Data) ||
                            quests.IsIndividualNPC(npc.Data.factionID) && person.FactionIndex==npc.Data.factionID)person.DestroyNPC();
                }
            gameObject.SetActive(false);
            // Keep native quest destruction notification, after inventory/death
            // capture. The existing corpse ledger owns the one replacement body.
            Destroy(gameObject);
        }
        internal void Capture(){inventory=GetComponent<NativeNpcInventoryOwner>();if(Current(store) && inventory)inventory.Capture();}
        internal void Release()
        {
            if(entity!=null)entity.OnDeath-=Died;
            if(ownInventory && inventory)DestroyImmediate(inventory);
            if(ownEffects){var effects=GetComponent<EntityEffectManager>();if(effects)DestroyImmediate(effects);}
            if(ownEntity){var body=GetComponent<DaggerfallEntityBehaviour>();if(body)DestroyImmediate(body);}
            store=null;host=null;
        }
        private void OnDestroy()
        {
            if(!finished && Current(store) && !ReferenceEquals(inventory,null))inventory.Capture();
            if(entity!=null)entity.OnDeath-=Died;
        }
    }
}
