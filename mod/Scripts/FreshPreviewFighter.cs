using System;
using DaggerfallConnect;
using DaggerfallConnect.Save;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;

namespace PeopleOfTheIliacBay.DFU
{
    // Permanent loadout for the user-facing integration-test character.
    // This is invoked only by the explicit fresh-preview launcher.
    public static class FreshPreviewFighter
    {
        public const int Level = 20;
        public const int Gold = 100000;

        public static void Configure(PlayerEntity player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));

            var career = DaggerfallEntity.GetClassCareerTemplate(ClassCareers.Warrior);
            if (career == null) throw new InvalidOperationException("Warrior career template missing.");
            career.ForbiddenMaterials = 0;
            career.ForbiddenArmors = 0;
            career.ForbiddenShields = 0;
            career.ForbiddenProficiencies = 0;
            career.ExpertProficiencies |= DFCareer.ProficiencyFlags.Axes;
            career.Axes = DFCareer.Proficiency.Expert;
            career.SpellPointMultiplier = DFCareer.SpellPointMultipliers.Times_3_00;
            career.SpellPointMultiplierValue = 3f;
            player.Career = career;
            player.Level = Level;

            SetStat(player, DFCareer.Stats.Strength, 95);
            SetStat(player, DFCareer.Stats.Endurance, 95);
            SetStat(player, DFCareer.Stats.Agility, 85);
            SetStat(player, DFCareer.Stats.Speed, 85);
            SetStat(player, DFCareer.Stats.Intelligence, 80);
            SetStat(player, DFCareer.Stats.Willpower, 80);
            SetStat(player, DFCareer.Stats.Luck, 70);
            SetStat(player, DFCareer.Stats.Personality, 55);

            SetSkill(player, DFCareer.Skills.Axe, 95);
            SetSkill(player, DFCareer.Skills.CriticalStrike, 85);
            SetSkill(player, DFCareer.Skills.Dodging, 80);
            SetSkill(player, DFCareer.Skills.Running, 75);
            SetSkill(player, DFCareer.Skills.Climbing, 70);
            SetSkill(player, DFCareer.Skills.Medical, 65);
            SetSkill(player, DFCareer.Skills.Restoration, 70);
            SetSkill(player, DFCareer.Skills.Destruction, 65);
            SetSkill(player, DFCareer.Skills.Alteration, 60);
            SetSkill(player, DFCareer.Skills.Thaumaturgy, 60);
            SetSkill(player, DFCareer.Skills.Mysticism, 60);

            player.MaxHealth = 400;
            player.CurrentHealth = player.MaxHealth;
            player.CurrentFatigue = player.MaxFatigue;
            player.CurrentMagicka = player.MaxMagicka;
            player.GoldPieces = Gold;
            MakePursePortable(player);
            player.SetCurrentLevelUpSkillSum();
            player.StartingLevelUpSkillSum = player.CurrentLevelUpSkillSum;
            player.ReadyToLevelUp = false;

            AddEquipment(player);
            AddSupplies(player);
            AddSpells(player, new[] { 64, 17, 14, 94 }); // Heal, Shield, Fireball, Recall.
        }

        public static void RequireConfigured(PlayerEntity player, TransportManager transport)
        {
            if (player == null || transport == null || player.Level != Level || player.GetGoldAmount() != Gold ||
                player.CarriedWeight>=player.MaxEncumbrance ||
                player.Stats.PermanentStrength != 95 || player.Stats.PermanentEndurance != 95 ||
                player.Skills.GetPermanentSkillValue(DFCareer.Skills.Axe) != 95 ||
                player.Skills.GetPermanentSkillValue(DFCareer.Skills.CriticalStrike) != 85 ||
                player.Skills.GetPermanentSkillValue(DFCareer.Skills.Dodging) != 80 ||
                player.MaxHealth < 400 || player.MaxFatigue < 380 || player.MaxMagicka < 240 ||
                player.SpellbookCount() < 4 || !transport.HasHorse())
                throw new InvalidOperationException("Fresh preview fighter profile is incomplete.");

            var axe = player.ItemEquipTable.GetItem(EquipSlots.RightHand);
            if (axe == null || axe.ItemGroup != ItemGroups.Weapons || axe.TemplateIndex != (int)Weapons.Battle_Axe ||
                axe.NativeMaterialValue != (int)WeaponMaterialTypes.Daedric)
                throw new InvalidOperationException("Fresh preview fighter has no equipped Daedric battle axe.");
            foreach (var slot in new[] { EquipSlots.Head, EquipSlots.ChestArmor, EquipSlots.Gloves, EquipSlots.LegsArmor,
                EquipSlots.LeftArm, EquipSlots.RightArm, EquipSlots.Feet })
            {
                var armor = player.ItemEquipTable.GetItem(slot);
                if (armor == null || armor.ItemGroup != ItemGroups.Armor ||
                    armor.NativeMaterialValue != (int)ArmorMaterialTypes.Daedric)
                    throw new InvalidOperationException("Fresh preview fighter armor is incomplete at " + slot + ".");
            }
            UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEST_FIGHTER_READY: level=20; axe=95; critical=85; dodging=80; strength=95; endurance=95; health=" +
                player.MaxHealth + "; fatigue=" + player.MaxFatigue + "; magicka=" + player.MaxMagicka +
                "; gold=" + player.GoldPieces + "; spells=" + player.SpellbookCount() + "; horse=true; armor=Daedric; weapon=Daedric Battle Axe");
        }

        internal static void MakePursePortable(PlayerEntity player)
        {
            if(player.GoldPieces<=1000)return;
            var credit=ItemBuilder.CreateItem(ItemGroups.MiscItems,(int)MiscItems.Letter_of_credit);
            credit.value=player.GoldPieces-1000;
            player.Items.AddItem(credit,noStack:true);
            player.GoldPieces=1000;
        }

        static void SetStat(PlayerEntity player, DFCareer.Stats stat, int value)
        {
            player.Stats.SetPermanentStatValue(stat, value);
        }

        static void SetSkill(PlayerEntity player, DFCareer.Skills skill, short value)
        {
            player.Skills.SetPermanentSkillValue(skill, value);
        }

        static void AddEquipment(PlayerEntity player)
        {
            var axe = ItemBuilder.CreateWeapon(Weapons.Battle_Axe, WeaponMaterialTypes.Daedric);
            player.Items.AddItem(axe);
            player.ItemEquipTable.EquipItem(axe, true, false);
            foreach (var part in new[] { Armor.Helm, Armor.Cuirass, Armor.Gauntlets, Armor.Greaves,
                Armor.Left_Pauldron, Armor.Right_Pauldron, Armor.Boots })
            {
                var armor = ItemBuilder.CreateArmor(player.Gender, player.Race, part, ArmorMaterialTypes.Daedric);
                player.Items.AddItem(armor);
                player.ItemEquipTable.EquipItem(armor, true, false);
            }
            player.Items.AddItem(ItemBuilder.CreateWeapon(Weapons.War_Axe, WeaponMaterialTypes.Daedric));
        }

        static void AddSupplies(PlayerEntity player)
        {
            if (!player.Items.Contains(ItemGroups.MiscItems, (int)MiscItems.Spellbook))
                player.Items.AddItem(ItemBuilder.CreateItem(ItemGroups.MiscItems, (int)MiscItems.Spellbook));
            if (!player.Items.Contains(ItemGroups.Transportation, (int)Transportation.Horse))
                player.Items.AddItem(ItemBuilder.CreateItem(ItemGroups.Transportation, (int)Transportation.Horse));
            player.Items.AddItem(ItemBuilder.CreatePotion(PotionRecipe.classicRecipeKeys[0], 10));
            player.Items.AddItem(ItemBuilder.CreatePotion(PotionRecipe.classicRecipeKeys[2], 10));
            player.Items.AddItem(ItemBuilder.CreatePotion(PotionRecipe.classicRecipeKeys[11], 5));
        }

        static void AddSpells(PlayerEntity player, int[] ids)
        {
            var broker = GameManager.Instance.EntityEffectBroker;
            if (!broker) throw new InvalidOperationException("Native effect broker missing.");
            foreach (int id in ids)
            {
                SpellRecord.SpellRecordData record;
                EffectBundleSettings spell;
                if (!broker.GetClassicSpellRecord(id, out record) ||
                    !broker.ClassicSpellRecordDataToEffectBundleSettings(record, BundleTypes.Spell, out spell))
                    throw new InvalidOperationException("Classic test spell missing: " + id + ".");
                bool known = false;
                foreach (var existing in player.GetSpells())
                    if (existing.Name == spell.Name) { known = true; break; }
                if (!known) player.AddSpell(spell);
            }
        }
    }
}
