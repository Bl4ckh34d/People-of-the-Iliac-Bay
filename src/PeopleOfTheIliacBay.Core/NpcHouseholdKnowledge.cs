using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class NpcKnownRelative
    {
        public string PersonId { get; }
        public string Name { get; }
        public bool IsAlive { get; }
        public bool DeathKnown { get; }
        internal NpcKnownRelative(Resident resident,Func<string,bool> knowsDeath)
        {
            PersonId = resident.Id;
            Name = resident.FullName ?? (resident.NativeIdentity == null ? null : resident.NativeIdentity.Name);
            IsAlive = resident.IsAlive;
            DeathKnown = !resident.IsAlive && knowsDeath(resident.Id);
        }
    }

    /// <summary>Detached self-knowledge from saved family links; not permission to disclose it.</summary>
    public sealed class NpcHouseholdKnowledge
    {
        public string PersonId { get; }
        public int LivingHouseholdCount { get; }
        public int AssignedHouseholdCount { get; }
        public NpcKnownRelative Partner { get; }
        public IReadOnlyList<NpcKnownRelative> Children { get; }

        internal NpcHouseholdKnowledge(Resident self, Household home, IEnumerable<Resident> family,Func<string,bool> knowsDeath)
        {
            PersonId = self.Id;
            LivingHouseholdCount = home.Residents.Count(r => r.IsAlive && r.HomeKey == self.HomeKey);
            AssignedHouseholdCount = home.Residents.Count(r => r.HomeKey == self.HomeKey);
            var people = family.ToList();
            var partner = people.FirstOrDefault(r => r.Id == self.PartnerId);
            Partner = partner == null ? null : new NpcKnownRelative(partner,knowsDeath);
            Children = new ReadOnlyCollection<NpcKnownRelative>(people.Where(r => r.MotherId == self.Id || r.FatherId == self.Id)
                .OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => new NpcKnownRelative(r,knowsDeath)).ToList());
        }

        public string ToContext()
        {
            string partner = Partner == null ? "no recorded partner" :
                "partner " + RelativeName(Partner) + (Partner.DeathKnown ? " (known deceased)" : " (no known death)");
            int knownDeaths = Children.Count(r => r.DeathKnown);
            return "Saved self-knowledge: " + AssignedHouseholdCount.ToString(CultureInfo.InvariantCulture) +
                " residents assigned to your home, including you; membership does not prove current presence or life state. " + partner +
                "; recorded children: " + Children.Count.ToString(CultureInfo.InvariantCulture) + "; known child deaths: " +
                knownDeaths.ToString(CultureInfo.InvariantCulture) + ". " +
                (Children.Count == 0 ? "No recorded child links does not prove childlessness; legacy family data may be incomplete. " :
                    "Recorded child names: " + string.Join(", ", Children.Take(8).Select(r => RelativeName(r) + (r.DeathKnown ? " (known deceased)" : ""))) +
                    (Children.Count > 8 ? "; additional names omitted" : "") + ". ") +
                "Marriage ceremony/status is not separately recorded. Disclosure is optional; withholding does not change these facts. " +
                "Do not invent other relatives, names, ages, addresses or whereabouts.";
        }

        private static string RelativeName(NpcKnownRelative person)
        {
            if (string.IsNullOrWhiteSpace(person.Name)) return "(name unavailable)";
            string name = person.Name.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");
            return "\"" + (name.Length > 80 ? name.Substring(0, 80) : name) + "\"";
        }
    }

    public sealed partial class WorldStateStore
    {
        public NpcHouseholdKnowledge ReadNpcHouseholdKnowledge(string personId)
        {
            if (string.IsNullOrWhiteSpace(personId)) return null;
            Household ownHome = null; Resident self = null;
            foreach (var town in state.Settlements)
            {
                foreach (var home in town.Households)
                {
                    self = home.Residents.Find(r => r.Id == personId);
                    if (self != null) { ownHome = home; break; }
                }
                if (self != null) break;
            }
            // Unmapped and retired-only actors have no current authoritative household projection.
            if (self == null || ownHome.BuildingKey != self.HomeKey) return null;
            var people = state.Settlements.SelectMany(t => t.Households).SelectMany(h => h.Residents)
                .Concat(state.HouseholdHistory.Where(h => h.Household != null).SelectMany(h => h.Household.Residents))
                .Where(r => r.Id == self.PartnerId || r.MotherId == self.Id || r.FatherId == self.Id)
                .GroupBy(r => r.Id, StringComparer.Ordinal).Select(g => g.First());
            return new NpcHouseholdKnowledge(self, ownHome, people,subject=>KnowsResidentDeath(self.Id,subject));
        }
    }
}
