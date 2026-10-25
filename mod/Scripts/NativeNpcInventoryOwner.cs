using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Attach only to ordinary mod-owned actors after entity initialization.
    // Quest/authored protected entities must keep their native inventories.
    public sealed class NativeNpcInventoryOwner : MonoBehaviour
    {
        private WorldStateStore store;
        private DaggerfallEntity entity;
        private NativeNpcInventoryBinding binding;
        private bool releasedAtDeath;
        public NativeNpcInventoryBinding Binding {get{return binding;}}
        public void Bind(DaggerfallEntity owner,WorldStateStore world,Resident resident)
        {
            if(resident==null)throw new ArgumentNullException(nameof(resident));
            bool eligible=resident.IsAlive && resident.AgeGroup==ResidentAgeGroup.Adult && !resident.IsQuestProtected;
            Bind(owner,world,resident.Id,eligible,resident);
        }
        public void Bind(DaggerfallEntity owner,WorldStateStore world,string personId)
        {Bind(owner,world,personId,false,null);}
        private void Bind(DaggerfallEntity owner,WorldStateStore world,string personId,bool allowStartingBelongings,Resident resident)
        {
            if(entity!=null)throw new InvalidOperationException("NPC inventory component already bound.");
            var nativeItems=owner.Items;
            bool firstLedger=!world.HasNpcInventory(personId);
            if(allowStartingBelongings && nativeItems.Count==0 && firstLedger)
            {
                var visiblePerson=owner.EntityBehaviour==null?null:owner.EntityBehaviour.GetComponent<MobilePersonNPC>();
                var initial=NativeNpcStartingBelongings.CreateInitialItems(world,personId,visiblePerson,resident);
                while(initial.Count>0)
                {
                    var item=initial.GetItem(0);initial.RemoveItem(item);nativeItems.AddItem(item,noStack:true);
                }
            }
            string reason;NativeNpcInventoryBinding loaded;
            if(!NativeNpcInventoryBridge.TryOpen(world,personId,nativeItems,NativeNpcInventoryBridge.InitialWalletGold(world,personId),out loaded,out reason))
                throw new InvalidOperationException(reason);
            if(allowStartingBelongings && firstLedger && !NativeNpcStartingBelongings.TryEnsureVerifiedHomeKey(world,resident,loaded.Items,out reason))
                throw new InvalidOperationException(reason);
            // DFU's Items setter calls ReplaceAll, cloning every item with a new
            // UID. TransferAll also merges compatible saved stacks. Preserve the
            // native collection and move each exact restored object without merging.
            nativeItems.Clear();
            while(loaded.Items.Count>0)
            {
                var item=loaded.Items.GetItem(0);
                loaded.Items.RemoveItem(item);nativeItems.AddItem(item,noStack:true);
            }
            loaded.Items=nativeItems;
            entity=owner;store=world;binding=loaded;entity.OnDeath+=OnDeath;
            if(allowStartingBelongings && world.Traveler(personId)!=null)
            {
                var archetype=world.ResolveNpcArchetype(personId);
                if(archetype==NpcArchetype.Warrior || archetype==NpcArchetype.Thief)
                {
                    EquipOwnedAdventurerMelee();
                }
            }
            if(allowStartingBelongings)
            {
                EquipOwnedWearables();
                NativeNpcCivilianArmor.Initialize(entity);
                Capture();
            }
        }
        private void EquipOwnedAdventurerMelee()
        {
            if(entity.ItemEquipTable.GetItem(EquipSlots.RightHand)!=null)return;
            DaggerfallUnityItem selected=null;
            for(int i=0;i<entity.Items.Count;i++)
            {
                var item=entity.Items.GetItem(i);
                if(item.ItemGroup!=ItemGroups.Weapons || item.currentCondition<=0 || item.RepairData.IsBeingRepaired() || item.GetWeaponSkillUsed()<0 ||
                    item.TemplateIndex==(int)Weapons.Arrow || item.TemplateIndex==(int)Weapons.Short_Bow ||
                    item.TemplateIndex==(int)Weapons.Long_Bow)continue;
                if(selected==null || item.EquipSlot==EquipSlots.RightHand)selected=item;
                if(item.EquipSlot==EquipSlots.RightHand)break;
            }
            if(selected!=null)entity.ItemEquipTable.EquipItem(selected,true,false);
        }
        private void EquipOwnedWearables()
        {
            for(int i=0;i<entity.Items.Count;i++)
            {
                var item=entity.Items.GetItem(i);
                if(item==null || item.EquipSlot==EquipSlots.None || entity.ItemEquipTable.GetItem(item.EquipSlot)!=null)continue;
                if(item.ItemGroup==ItemGroups.MensClothing || item.ItemGroup==ItemGroups.WomensClothing ||
                    item.ItemGroup==ItemGroups.Jewellery || item.ItemGroup==ItemGroups.Armor)
                    entity.ItemEquipTable.EquipItem(item,true,false);
            }
        }
        public void Capture()
        {
            if(binding==null || releasedAtDeath)return;
            string reason;if(!NativeNpcInventoryBridge.TryCapture(store,binding,out reason))throw new InvalidOperationException(reason);
        }
        private void OnDeath(DaggerfallEntity ignored){Capture();releasedAtDeath=true;}
        private void OnDestroy()
        {
            if(entity!=null)entity.OnDeath-=OnDeath;
            // The owning population explicitly captures before streaming/restore;
            // a late Unity OnDestroy must not write into a newly restored branch.
        }
    }
}
