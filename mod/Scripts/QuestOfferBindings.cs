using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Caller supplies an explicit eligibility rule; never classify arbitrary quest homes here.
    public sealed class QuestOfferBindings : IDisposable
    {
        private readonly WorldStateStore store;
        private ConditionalWeakTable<Quest, QuestBindingPlan> pending = new ConditionalWeakTable<Quest, QuestBindingPlan>();
        private readonly Func<Quest, string[], QuestResidentBinding> select;
        private readonly Func<bool> allowNewBindings;
        private readonly IDisposable hooks;
        private List<QuestResidentBinding> committed;
        private readonly Dictionary<string, List<QuestResidentBinding>> residentContracts = new Dictionary<string, List<QuestResidentBinding>>();
        private bool disposed;
        public QuestOfferBindings(WorldStateStore store, Func<Quest, string[], QuestResidentBinding> select = null, Func<bool> allowNewBindings = null)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            this.store = store;
            this.select = select;
            this.allowNewBindings = allowNewBindings;
            committed = store.Snapshot().QuestBindings;
            IndexResidents();
            var type = Type.GetType("LivingCities.QuestHooks.NativeQuestOfferHooks, LivingCities.QuestHooks", true);
            hooks = (IDisposable)Activator.CreateInstance(type, new object[] {
                new Func<Quest, bool>(CanStart), new Action<Quest>(Started), new Action<Quest>(Discard), new Func<StaticNPC, string>(ResidentName),
                new Action<Quest, string[], bool>(Parsed) });
        }
        private void Parsed(Quest quest, string[] source, bool partial)
        {
            if (disposed || partial || quest == null || select == null) return;
            var binding = select(quest, source);
            if (binding == null) return;
            try { Prepare(quest, new[] { binding }); }
            catch (ArgumentException ex) { Debug.LogWarning("LIVING_CITIES_BINDING_SKIPPED: " + ex.Message); }
        }
        public void Prepare(Quest quest, IEnumerable<QuestResidentBinding> candidates)
        {
            if (disposed) throw new ObjectDisposedException(nameof(QuestOfferBindings));
            if (quest == null) throw new ArgumentNullException(nameof(quest));
            QuestBindingPlan existing;
            if (QuestMachine.Instance.GetQuest(quest.UID) != null || pending.TryGetValue(quest, out existing))
                throw new ArgumentException("Cannot rewrite a started or already offered quest.");
            var plan = store.PrepareQuestBindings(candidates);
            foreach (var binding in plan.Bindings) ValidateResource(quest, binding, false);
            var rollback = new List<Action>();
            try
            {
                foreach (var binding in plan.Bindings)
                {
                    var person = quest.GetPerson(new Symbol(binding.PersonSymbol));
                    var home = person.GetHomePlace();
                    var oldPerson = (Person.SaveData_v1)person.GetSaveData(); var oldSite = home.SiteDetails;
                    rollback.Add(() => { person.RestoreSaveData(oldPerson); home.SiteDetails = oldSite; });
                    var changedPerson = oldPerson; changedPerson.displayName = binding.PersonName;
                    var changedSite = oldSite; changedSite.buildingName = binding.HomeName;
                    person.RestoreSaveData(changedPerson); home.SiteDetails = changedSite;
                }
                pending.Add(quest, plan);
            }
            catch { for (int i = rollback.Count - 1; i >= 0; i--) rollback[i](); throw; }
        }
        private static void ValidateResource(Quest quest, QuestResidentBinding binding, bool published)
        {
            var person = quest.GetPerson(new Symbol(binding.PersonSymbol));
            if (binding.QuestUid != quest.UID || binding.QuestName != quest.QuestName || person == null ||
                person.IsIndividualNPC || person.IsQuestor || person.GetHomePlace() == null || binding.PersonSymbol != person.Symbol.Original)
                throw new ArgumentException("Binding does not identify an eligible procedural quest person.");
            var site = person.GetHomePlace().SiteDetails;
            if (site.mapId != binding.SettlementId || site.buildingKey != binding.HomeKey ||
                (published && (person.DisplayName != binding.PersonName || site.buildingName != binding.HomeName)))
                throw new ArgumentException("Quest resource disagrees with the published household contract.");
        }
        private bool CanStart(Quest quest)
        {
            QuestBindingPlan plan;
            if (quest == null || !pending.TryGetValue(quest, out plan)) return true;
            if (allowNewBindings != null && !allowNewBindings()) return false;
            try
            {
                store.ValidateQuestBindings(plan);
                foreach (var binding in plan.Bindings) ValidateResource(quest, binding, true);
                return true;
            }
            catch (ArgumentException ex)
            {
                Debug.LogWarning("LIVING_CITIES_OFFER_UNAVAILABLE: " + ex.Message);
                return false;
            }
        }
        private void Started(Quest quest)
        {
            QuestBindingPlan plan;
            if (!pending.TryGetValue(quest, out plan)) return;
            store.CommitQuestBindings(plan); committed = store.Snapshot().QuestBindings; IndexResidents(); pending.Remove(quest);
        }
        private void IndexResidents()
        {
            residentContracts.Clear();
            foreach (var binding in committed)
            {
                List<QuestResidentBinding> contracts;
                if (!residentContracts.TryGetValue(binding.ResidentId, out contracts))
                    residentContracts.Add(binding.ResidentId, contracts = new List<QuestResidentBinding>());
                contracts.Add(binding);
            }
        }
        // The native quest owns placement, hiding and activation until it ends. Suppress the
        // scheduled representation even when the native person is in another building or hidden.
        public bool OwnsResident(string residentId)
        {
            List<QuestResidentBinding> contracts;
            if (disposed || !residentContracts.TryGetValue(residentId, out contracts)) return false;
            foreach (var binding in contracts)
            {
                var quest = QuestMachine.Instance.GetQuest(binding.QuestUid);
                if (quest != null && !quest.QuestComplete && quest.QuestName == binding.QuestName &&
                    quest.GetPerson(new Symbol(binding.PersonSymbol)) != null) return true;
            }
            return false;
        }
        private string ResidentName(StaticNPC npc)
        {
            if (!npc) return null;
            var resource = npc.GetComponent<QuestResourceBehaviour>();
            var person = resource ? resource.TargetResource as Person : null;
            if (person == null || person.IsIndividualNPC || person.IsQuestor) return null;
            foreach (var binding in committed)
                if (binding.QuestUid == resource.QuestUID && binding.QuestName == person.ParentQuest.QuestName &&
                    new Symbol(binding.PersonSymbol).Name == resource.TargetSymbol.Name)
                    return binding.PersonName;
            return null;
        }
        private void Discard(Quest quest) { if (quest != null) pending.Remove(quest); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; pending = new ConditionalWeakTable<Quest, QuestBindingPlan>(); hooks.Dispose();
        }
    }
}
