using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // Materialize pure deterministic plans only for a first, empty inventory binding.
    public static class NativeNpcStartingBelongings
    {
        public static ItemCollection CreateInitialItems(WorldStateStore world,string personId,MobilePersonNPC visiblePerson=null,Resident resident=null)
        {
            if(world==null)throw new ArgumentNullException(nameof(world));
            if(string.IsNullOrWhiteSpace(personId))throw new ArgumentException("Invalid NPC identity.",nameof(personId));
            resident=resident??world.ResolveResident(personId);
            bool hasIdentity=visiblePerson!=null || resident!=null;
            Genders gender=visiblePerson!=null?visiblePerson.Gender:
                resident!=null && resident.Sex==ResidentSex.Female?Genders.Female:Genders.Male;
            // Match ResidentActor's existing native race fallback, including for
            // a first offscreen inn visit before the body is materialized.
            Races race=visiblePerson!=null?visiblePerson.Race:
                resident!=null && resident.Race>=0 && Enum.IsDefined(typeof(Races),resident.Race)?(Races)resident.Race:Races.Breton;
            var archetype=world.ResolveNpcArchetype(personId);
            var plan=NpcStartingBelongings.Generate(world.WorldSeed,personId,archetype);
            var items=new ItemCollection();
            foreach(var entry in plan)
            {
                ItemGroups group;
                switch(entry.Group)
                {
                    case NpcStartingItemGroup.UselessItems1: group=ItemGroups.UselessItems1;break;
                    case NpcStartingItemGroup.UselessItems2: group=ItemGroups.UselessItems2;break;
                    case NpcStartingItemGroup.MiscItems: group=ItemGroups.MiscItems;break;
                    case NpcStartingItemGroup.Jewellery: group=ItemGroups.Jewellery;break;
                    default: throw new InvalidOperationException("Unsupported starting item group.");
                }
                var item=ItemBuilder.CreateItem(group,entry.TemplateIndex);
                item.stackCount=entry.Count;
                items.AddItem(item,noStack:true);
            }
            if(world.Traveler(personId)!=null && (archetype==NpcArchetype.Warrior || archetype==NpcArchetype.Thief))
            {
                items.AddItem(ItemBuilder.CreateWeapon(archetype==NpcArchetype.Warrior?Weapons.Longsword:Weapons.Dagger,
                    WeaponMaterialTypes.Iron),noStack:true);
                if(hasIdentity)
                {
                    items.AddItem(ItemBuilder.CreateArmor(gender,race,Armor.Cuirass,
                        ArmorMaterialTypes.Leather,0),noStack:true);
                    items.AddItem(ItemBuilder.CreateArmor(gender,race,Armor.Boots,
                        ArmorMaterialTypes.Leather,0),noStack:true);
                }
            }
            if(NpcStartingBelongings.ShouldStartWithHealingPotion(world.WorldSeed,personId,archetype))
            {
                int recipeKey;
                if(NativeNpcHealingItem.TryFindHealingPotionRecipeKey(out recipeKey))
                {
                    DaggerfallUnityItem potion=ItemBuilder.CreatePotion(recipeKey);
                    if(NativeNpcHealingItem.IsSupportedPotion(potion))
                    {
                        potion.stackCount=1;
                        items.AddItem(potion,noStack:true);
                    }
                }
            }
            if(hasIdentity && world.Traveler(personId)!=null &&
                NpcStartingBelongings.ShouldStartWithMagicItem(world.WorldSeed,personId,archetype))
            {
                DaggerfallUnityItem magic=CreateDeterministicMageMagicItem(world,personId,gender,race);
                if(magic!=null && !magic.IsArtifact && !magic.IsQuestItem)items.AddItem(magic,noStack:true);
            }
            AddVerifiedHomeKey(world,resident,items);
            if(hasIdentity)items.AddItem(CreateNativeClothing(archetype,gender,race),noStack:true);
            return items;
        }
        private static void AddVerifiedHomeKey(WorldStateStore world,Resident resident,ItemCollection items)
        {
            int settlement;
            if(NpcStartingBelongings.TryResolveVerifiedResidentHome(world.Snapshot(),resident,out settlement))
                items.AddItem(new HouseholdPropertyKeyItem(settlement,resident.HomeKey),noStack:true);
        }
        // First-ledger initialization only. Saved losses are authoritative;
        // the binding owner must never use this to refill a loaded inventory.
        public static bool TryEnsureVerifiedHomeKey(WorldStateStore world,Resident resident,ItemCollection items,out string reason)
        {
            reason=null;
            if(world==null || resident==null || items==null){reason="Resident home-key binding is unavailable.";return false;}
            int settlement;
            if(!NpcStartingBelongings.TryResolveVerifiedResidentHome(world.Snapshot(),resident,out settlement))return true;
            for(int i=0;i<items.Count;i++)if(HouseholdPropertyKeyItem.IsMatching(items.GetItem(i),settlement,resident.HomeKey))return true;
            var key=new HouseholdPropertyKeyItem(settlement,resident.HomeKey);
            if(!NativeNpcInventoryBridge.CanAppendExactItem(items,key,out reason))return false;
            items.AddItem(key,noStack:true);
            return true;
        }
        private static DaggerfallUnityItem CreateNativeClothing(NpcArchetype archetype,Genders gender,Races race)
        {
            if(gender==Genders.Male)
            {
                MensClothing garment=(archetype==NpcArchetype.Mage || archetype==NpcArchetype.Healer)?MensClothing.Plain_robes:
                    (archetype==NpcArchetype.Merchant || archetype==NpcArchetype.Innkeeper)?MensClothing.Formal_tunic:MensClothing.Short_tunic;
                return ItemBuilder.CreateMensClothing(garment,race,0,DyeColors.Blue);
            }
            WomensClothing dress=(archetype==NpcArchetype.Mage || archetype==NpcArchetype.Healer)?WomensClothing.Plain_robes:
                (archetype==NpcArchetype.Merchant || archetype==NpcArchetype.Innkeeper)?WomensClothing.Day_gown:WomensClothing.Casual_dress;
            return ItemBuilder.CreateWomensClothing(dress,race,0,DyeColors.Blue);
        }
        public static DaggerfallUnityItem CreateDeterministicMageMagicItem(WorldStateStore world,string personId,Genders gender,Races race)
        {
            UnityEngine.Random.State prior=UnityEngine.Random.state;
            uint priorDfRandom=DFRandom.Seed;
            try
            {
                uint seed=StableSeed.Combine(world.WorldSeed,Hash(personId),(uint)NpcArchetype.Mage,0x4d414731u);
                UnityEngine.Random.InitState(unchecked((int)seed));
                DFRandom.Seed=seed;
                return ItemBuilder.CreateRegularMagicItem(-1,1,gender,race);
            }
            catch{return null;}
            finally{UnityEngine.Random.state=prior;DFRandom.Seed=priorDfRandom;}
        }
        private static uint Hash(string value)
        {
            uint hash=2166136261u;
            unchecked{foreach(char c in value)hash=(hash^c)*16777619u;}
            return hash;
        }
    }
}
