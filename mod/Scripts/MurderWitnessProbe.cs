using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class MurderWitnessProbe
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        public static IEnumerator Run(LivingCitiesMod adapter, StreetPopulation streets)
        {
            yield return null;
            if (!adapter || !streets) throw new InvalidOperationException("Murder witness probe needs live adapters.");

            var game = GameManager.Instance;
            if (game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None)
                throw new InvalidOperationException("Murder witness probe needs no current player crime.");

            var population = (SettlementPopulation)typeof(LivingCitiesMod).GetField("current", PrivateInstance).GetValue(adapter);
            var liveStore = (WorldStateStore)typeof(LivingCitiesMod).GetField("store", PrivateInstance).GetValue(adapter);
            if (population == null || liveStore == null) throw new InvalidOperationException("Murder witness probe needs loaded town state.");

            var planResidents = LivePlanResidents(streets);
            Resident victim = null, witness = null, warned = null;
            foreach (var witnessHome in population.Households)
            {
                var eligibleWitnesses = witnessHome.Residents.FindAll(resident => resident.IsAlive && !resident.IsQuestProtected &&
                    resident.NativeIdentity == null);
                foreach (var candidate in eligibleWitnesses)
                {
                    if (!planResidents.Contains(candidate.Id)) continue;
                    foreach (var victimHome in population.Households)
                    {
                        if (victimHome.BuildingKey == witnessHome.BuildingKey) continue;
                        victim = victimHome.Residents.Find(resident => resident.IsAlive && !resident.IsQuestProtected &&
                            resident.NativeIdentity == null && !IsFamilyWitness(population, candidate, resident.Id));
                        if (victim != null) break;
                    }
                    if (victim == null) continue;
                    witness = candidate;
                    foreach (var otherHome in population.Households)
                    {
                        warned = otherHome.Residents.Find(resident => resident.IsAlive && !resident.IsQuestProtected &&
                            resident.NativeIdentity == null && resident.Id != witness.Id && resident.Id != victim.Id &&
                            !IsFamilyWitness(population, candidate, resident.Id));
                        if (warned != null) break;
                    }
                    break;
                }
                if (witness != null) break;
            }
            if (victim == null || witness == null)
                throw new InvalidOperationException("Murder witness probe needs a planned witness and living victim from different households.");

            int settlementId = population.SettlementId;
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            if (now < 1200) throw new InvalidOperationException("Murder witness probe needs at least 1,200 seconds of world time.");
            double incidentStart = now - 1200;

            // Every phase runs on a deep validated copy. Live resident state stays untouched.
            var isolated = new WorldStateStore(0,LivingCitiesMod.ConversationArchiveRoot());
            isolated.Restore(liveStore.Snapshot());
            var isolatedTown = isolated.GetOrCreate(settlementId, new int[0]);
            Resident isolatedWitness = null;
            foreach (var home in isolatedTown.Households)
            {
                isolatedWitness = home.Residents.Find(resident => resident.Id == witness.Id);
                if (isolatedWitness != null) break;
            }
            if (isolatedWitness == null) throw new InvalidOperationException("Isolated town lost selected witness identity.");
            var incident = isolated.RecordViolence(settlementId, victim.Id, incidentStart);
            isolated.LocateViolence(incident.Id, -1, 0, 0, 0);
            var direct = isolated.Witness(incident.Id, witness.Id, incidentStart);
            if (direct == null || !direct.DirectWitness)
                throw new InvalidOperationException("Murder witness probe could not record direct witness.");
            if (warned != null && !isolated.Warn(incident.Id, witness.Id, warned.Id, incidentStart + 1))
                throw new InvalidOperationException("Murder witness probe could not record warned witness.");
            if (!Population.MarkDead(isolatedTown, victim.Id))
                throw new InvalidOperationException("Murder witness probe could not mark isolated victim dead.");
            isolated.ConfirmCorpse(incident.Id, incidentStart + 1);
            isolated.MarkPlayerMurder(incident.Id);

            if (incident.ExpiresAt >= now || game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None ||
                !incident.PlayerAttributed || !direct.DirectWitness || direct.Reaction != CivilianReaction.Flee ||
                !isolated.HasPersistentMurderFear(witness.Id) ||
                warned != null && !isolated.HasPersistentMurderFear(warned.Id) ||
                isolated.HoldsPartnerGrudge(isolatedWitness, now))
                throw new InvalidOperationException("Expired direct witness lost player-attributed fear before report.");

            var worldStoreField = typeof(StreetPopulation).GetField("worldStore", PrivateInstance);
            var responsesField = typeof(StreetPopulation).GetField("responses", PrivateInstance);
            bool previousEnabled = streets.enabled;
            bool previousReactions = streets.ReactionsEnabled;
            object previousWorldStore = worldStoreField.GetValue(streets);
            object previousResponses = responsesField.GetValue(streets);
            var previousCrime = game.PlayerEntity.CrimeCommitted;
            try
            {
                // IsAlarmed reads these two bindings. Disable Update while the isolated store is bound.
                streets.enabled = false;
                streets.ReactionsEnabled = true;
                worldStoreField.SetValue(streets, isolated);
                responsesField.SetValue(streets, new CivilianResponses(isolated, settlementId));
                if (!streets.IsAlarmed(witness.Id))
                    throw new InvalidOperationException("StreetPopulation.IsAlarmed lost expired direct witness before report.");

                isolated.ResolveAccusedPlayerMurders();
                if (incident.AccusationIssued || incident.CrimeResolved || !isolated.HasPersistentMurderFear(witness.Id) ||
                    !streets.IsAlarmed(witness.Id) || game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None)
                    throw new InvalidOperationException("Crime None before accusation incorrectly cleared witness fear.");

                isolated.MarkMurderAccusationIssued(incident.Id);
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Murder;
                if (!incident.AccusationIssued || incident.CrimeResolved || !isolated.HasPersistentMurderFear(witness.Id) ||
                    !streets.IsAlarmed(witness.Id))
                    throw new InvalidOperationException("Murder charge cleared witness response before crime resolution.");

                string json = SaveLoadManager.Serialize(typeof(WorldState), isolated.Snapshot());
                var saved = (WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json);
                var restored = new WorldStateStore(0,LivingCitiesMod.ConversationArchiveRoot());
                restored.Restore(saved);
                var restoredIncident = restored.Snapshot().AlarmIncidents.Find(item => item.Id == incident.Id);
                if (restoredIncident == null || !restoredIncident.PlayerAttributed || !restoredIncident.AccusationIssued ||
                    restoredIncident.CrimeResolved || !restored.HasPersistentMurderFear(witness.Id) ||
                    warned != null && !restored.HasPersistentMurderFear(warned.Id))
                    throw new InvalidOperationException("Native FullSerializer roundtrip lost unresolved murder witness state.");

                worldStoreField.SetValue(streets, restored);
                responsesField.SetValue(streets, new CivilianResponses(restored, settlementId));
                if (!streets.IsAlarmed(witness.Id))
                    throw new InvalidOperationException("Native save restore lost street witness response.");

                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
                restored.ResolveAccusedPlayerMurders();
                restoredIncident = restored.Snapshot().AlarmIncidents.Find(item => item.Id == incident.Id);
                if (restoredIncident == null || !restoredIncident.CrimeResolved || restored.HasPersistentMurderFear(witness.Id) ||
                    warned != null && restored.HasPersistentMurderFear(warned.Id) || streets.IsAlarmed(witness.Id) ||
                    restored.HoldsPartnerGrudge(isolatedWitness, now))
                    throw new InvalidOperationException("Resolved accused murder kept ordinary witness fear.");

                Debug.Log("LIVING_CITIES_MURDER_WITNESS_PASS: expired player-attributed witness remains alarmed before report, accusation and native restore; fear clears only after accused crime resolves; witness=" + witness.Id);
            }
            finally
            {
                worldStoreField.SetValue(streets, previousWorldStore);
                responsesField.SetValue(streets, previousResponses);
                streets.ReactionsEnabled = previousReactions;
                streets.enabled = previousEnabled;
                game.PlayerEntity.CrimeCommitted = previousCrime;
            }
        }

        private static HashSet<string> LivePlanResidents(StreetPopulation streets)
        {
            var result = new HashSet<string>();
            var field = typeof(StreetPopulation).GetField("plans", PrivateInstance);
            foreach (var plan in (IEnumerable)field.GetValue(streets))
            {
                var resident = (Resident)plan.GetType().GetField("Resident").GetValue(plan);
                if (resident != null) result.Add(resident.Id);
            }
            return result;
        }

        private static bool IsFamilyWitness(SettlementPopulation population, Resident witness, string victimId)
        {
            if (witness.PartnerId == victimId) return true;
            foreach (var home in population.Households)
                foreach (var resident in home.Residents)
                    if (resident.Id == victimId && (resident.MotherId == witness.Id || resident.FatherId == witness.Id))
                        return true;
            return false;
        }
    }
}
