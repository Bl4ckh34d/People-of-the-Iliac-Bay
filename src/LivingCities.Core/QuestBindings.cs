using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    [Serializable]
    public sealed class QuestResidentBinding
    {
        public ulong QuestUid;
        public string QuestName;
        public string PersonSymbol;
        public int SettlementId;
        public int HomeKey;
        public string ResidentId;
        public string PersonName;
        public string HomeName;
        public string RuleId;

        internal QuestResidentBinding Copy() { return (QuestResidentBinding)MemberwiseClone(); }
    }

    public sealed class QuestBindingPlan
    {
        internal readonly object Epoch;
        private readonly QuestResidentBinding[] bindings;
        internal QuestBindingPlan(object epoch, List<QuestResidentBinding> bindings)
        { Epoch = epoch; this.bindings = bindings.ToArray(); }

        // Callers may edit their view, but cannot change the offer after publication.
        public QuestResidentBinding[] Bindings
        {
            get
            {
                var copy = new QuestResidentBinding[bindings.Length];
                for (int i = 0; i < copy.Length; i++) copy[i] = bindings[i].Copy();
                return copy;
            }
        }
    }

    public sealed partial class WorldStateStore
    {
        private object bindingEpoch = new object();

        // The native adapter must establish permanent residency and eligibility first.
        // This ledger never guesses residency from a quest-generated home location.
        public QuestBindingPlan PrepareQuestBindings(IEnumerable<QuestResidentBinding> candidates)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            var proposed = new List<QuestResidentBinding>();
            foreach (var candidate in candidates)
            {
                Require(candidate != null, "Null quest binding.");
                proposed.Add(candidate.Copy());
            }
            MergeQuestBindings(Snapshot(), proposed);
            return new QuestBindingPlan(bindingEpoch, proposed);
        }

        public void CommitQuestBindings(QuestBindingPlan plan)
        {
            // One assignment after all validation. Keep live actor/population references.
            state.QuestBindings = ValidatedQuestBindings(plan).QuestBindings;
        }

        public void ValidateQuestBindings(QuestBindingPlan plan) { ValidatedQuestBindings(plan); }

        private WorldState ValidatedQuestBindings(QuestBindingPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            Require(ReferenceEquals(bindingEpoch, plan.Epoch), "Quest offer belongs to another world or a previous load.");
            var incoming = Snapshot();
            MergeQuestBindings(incoming, plan.Bindings);
            return incoming;
        }

        private static string BindingKey(QuestResidentBinding binding)
        { return binding.QuestUid.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + binding.PersonSymbol; }

        private static void MergeQuestBindings(WorldState world, IEnumerable<QuestResidentBinding> proposed)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in proposed)
            {
                Require(keys.Add(BindingKey(binding)), "Duplicate quest offer symbol.");
                var existing = world.QuestBindings.Find(b => b.QuestUid == binding.QuestUid && b.PersonSymbol == binding.PersonSymbol);
                if (existing != null)
                {
                    Require(SameBinding(existing, binding), "Published quest binding cannot be changed.");
                    continue;
                }
                Resident resident = BindingResident(world, binding);
                Require(resident.IsAlive, "A new quest cannot bind a deceased resident.");
                world.QuestBindings.Add(binding.Copy());
            }
            world.QuestBindings = CopyQuestBindings(world);
        }

        private static bool SameBinding(QuestResidentBinding a, QuestResidentBinding b)
        {
            return a.QuestUid == b.QuestUid && a.QuestName == b.QuestName && a.PersonSymbol == b.PersonSymbol &&
                a.SettlementId == b.SettlementId && a.HomeKey == b.HomeKey && a.ResidentId == b.ResidentId &&
                a.PersonName == b.PersonName && a.HomeName == b.HomeName && a.RuleId == b.RuleId;
        }

        private static Resident BindingResident(WorldState world, QuestResidentBinding binding)
        {
            var town = world.Settlements.Find(t => t.SettlementId == binding.SettlementId);
            var home = town == null ? null : town.Households.Find(h => h.BuildingKey == binding.HomeKey);
            var resident = home == null ? null : home.Residents.Find(r => r.Id == binding.ResidentId);
            Require(resident != null, "Quest binding does not belong to a saved household resident.");
            return resident;
        }

        private static List<QuestResidentBinding> CopyQuestBindings(WorldState world)
        {
            Require(world.QuestBindings != null, "Missing quest binding ledger.");
            var copy = new List<QuestResidentBinding>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var questNames = new Dictionary<ulong, string>();
            var personNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var homeNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var binding in world.QuestBindings)
            {
                Require(binding != null, "Null quest binding.");
                // DFU's FullSerializer primitive converter stores unsigned integers as Int64.
                Require(binding.QuestUid <= long.MaxValue, "Quest UID exceeds the native serializer range.");
                Require(!string.IsNullOrWhiteSpace(binding.QuestName) && !string.IsNullOrWhiteSpace(binding.PersonSymbol) &&
                    !string.IsNullOrWhiteSpace(binding.RuleId) && !string.IsNullOrWhiteSpace(binding.PersonName) &&
                    !string.IsNullOrWhiteSpace(binding.HomeName), "Missing quest binding contract or provenance.");
                Require(keys.Add(BindingKey(binding)), "Duplicate saved quest symbol.");
                BindingResident(world, binding); // Dead residents retain their historical quest contracts.
                string value;
                Require(!questNames.TryGetValue(binding.QuestUid, out value) || value == binding.QuestName,
                    "Quest UID conflicts with another source quest.");
                questNames[binding.QuestUid] = binding.QuestName;
                Require(!personNames.TryGetValue(binding.ResidentId, out value) || value == binding.PersonName,
                    "Resident has conflicting published quest names.");
                personNames[binding.ResidentId] = binding.PersonName;
                string homeKey = binding.SettlementId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                    binding.HomeKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Require(!homeNames.TryGetValue(homeKey, out value) || value == binding.HomeName,
                    "Household has conflicting published residence labels.");
                homeNames[homeKey] = binding.HomeName;
                copy.Add(binding.Copy());
            }
            return copy;
        }
    }
}
