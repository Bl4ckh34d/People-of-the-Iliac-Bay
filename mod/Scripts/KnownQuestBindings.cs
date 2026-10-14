using System;
using System.Security.Cryptography;
using System.Text;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;

namespace LivingCities.DFU
{
    // Initial explicit role rule. Extend only after reviewing story semantics and native delivery paths.
    public static class KnownQuestBindings
    {
        private const string DeliverySourceHash = "3423FFF5B75ABA8C8C8240C850A5096FAED612ACC0F3349E9241F586B2D69CAC";
        public static QuestResidentBinding Select(Quest quest, string[] source, SettlementPopulation town)
        {
            if (quest == null || quest.QuestName != "A0C00Y14" || source == null || town == null || town.SettlementId != 206728581) return null;
            using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", source)))).Replace("-", "") != DeliverySourceHash)
                    return null; // A mod overriding this quest name may assign a different role or relationship.
            var person = quest.GetPerson(new Symbol("_pickuplocal_"));
            if (person == null || person.IsIndividualNPC || person.IsQuestor || person.GetHomePlace() == null) return null;
            var site = person.GetHomePlace().SiteDetails;
            if (site.mapId != town.SettlementId) return null;
            var home = town.Households.Find(h => h.BuildingKey == site.buildingKey);
            // Service households retain native quest placement until a service-aware
            // binding rule can preserve the original actor and trade role together.
            if (home == null || home.HeadId != null) return null;
            var resident = home.Residents.Find(r => r.IsAlive && r.AgeGroup == ResidentAgeGroup.Adult &&
                (r.Sex == ResidentSex.Male) == (person.Gender == Genders.Male));
            if (resident == null) return null;
            string first, surname; uint oldSeed = DFRandom.Seed;
            try
            {
                DFRandom.Seed = resident.AppearanceSeed;
                first = DaggerfallUnity.Instance.NameHelper.FirstName(NameHelper.BankTypes.Breton, person.Gender);
                DFRandom.Seed = home.SurnameSeed;
                surname = DaggerfallUnity.Instance.NameHelper.Surname(NameHelper.BankTypes.Breton);
            }
            finally { DFRandom.Seed = oldSeed; }
            return new QuestResidentBinding { QuestUid = quest.UID, QuestName = quest.QuestName, PersonSymbol = person.Symbol.Original,
                SettlementId = site.mapId, HomeKey = site.buildingKey, ResidentId = resident.Id, PersonName = first + " " + surname,
                HomeName = TextManager.Instance.GetLocalizedText("theNamedResidence").Replace("%s", surname),
                RuleId = "vanilla/A0C00Y14/local-recipient/v1" };
        }
    }
}
