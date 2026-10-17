using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        private IDisposable corpseTransferHook;
        private DaggerfallLoot corpseInventory;
        private string corpseInventoryIncident;
        private readonly HashSet<DaggerfallUnityItem> issuedCorpseKeys = new HashSet<DaggerfallUnityItem>();

        private void InitializeCorpseKeys(CivilianIncident alarm)
        {
            if (alarm.Corpse.PropertyKeyClaimsInitialized) return;
            int? workplace = null;
            var person = worldStore.NativePeopleFor(population.SettlementId).Find(p => p.Id == alarm.VictimId);
            BuildingSummary building;
            DaggerfallConnect.Arena2.FactionFile.FactionData faction;
            if (person != null && directory.GetBuildingSummary(person.WorkKey,out building) &&
                (RMBLayout.IsShop(building.BuildingType) || building.BuildingType == DaggerfallConnect.DFLocation.BuildingTypes.Tavern) &&
                GameManager.Instance.PlayerEntity.FactionData.GetFactionData(person.FactionId,out faction) &&
                faction.sgroup == (int)DaggerfallConnect.Arena2.FactionFile.SocialGroups.Merchants)
                workplace = person.WorkKey;
            worldStore.InitializeCorpseHomeKey(alarm.Id,workplace);
        }

        private void InstallCorpseTransferHook()
        {
            var type = Type.GetType("LivingCities.QuestHooks.NativeItemTransferHooks, LivingCities.QuestHooks", true);
            corpseTransferHook = (IDisposable)Activator.CreateInstance(type, new object[] {
                new Func<DaggerfallUnityItem,ItemCollection,ItemCollection,bool>((item,source,target) =>
                    corpseInventory && ReferenceEquals(source,corpseInventory.Items) && issuedCorpseKeys.Contains(item)),
                new Action<DaggerfallUnityItem,ItemCollection,ItemCollection>((item,source,target) => {
                    if (!worldStore.MarkCorpsePropertyKeyTaken(corpseInventoryIncident,item.enchantmentPoints,item.message))
                        throw new InvalidOperationException("Transferred corpse key had no available ownership claim.");
                    issuedCorpseKeys.Remove(item);
                    if (GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding)
                    {
                        if (ReactionsEnabled && HasTheftWitness() == true)
                        {
                            if (GameManager.Instance.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None ||
                                GameManager.Instance.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.Trespassing)
                                GameManager.Instance.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Theft;
                            var guards = GetComponent<GuardPopulation>();
                            if (guards) guards.ReportWitnessedInteriorLoot();
                        }
                    }
                    else ReportCorpseLoot(corpseInventoryIncident);
                })
            });
            DaggerfallUI.UIManager.OnWindowChange += OnCorpseInventoryWindowChanged;
        }

        private void OnCorpseInventoryWindowChanged(object sender, EventArgs args) { UpdateCorpseInventory(); }

        private void DisposeCorpseInventory()
        {
            DaggerfallUI.UIManager.OnWindowChange -= OnCorpseInventoryWindowChanged;
            if (corpseTransferHook != null) corpseTransferHook.Dispose();
            if (corpseInventory) Destroy(corpseInventory.gameObject);
        }

        public void OpenCorpseInventory(string incidentId)
        {
            GameObject body;
            if (!Ready || worldStore == null || !corpses.TryGetValue(incidentId,out body) || !body ||
                Vector3.Distance(body.transform.position,GameManager.Instance.PlayerMotor.transform.position)>3) return;
            if (corpseInventory) return;
            worldStore.InitializeCorpseHomeKey(incidentId);
            var claims=worldStore.GetAvailableCorpsePropertyKeyClaims(incidentId);
            if (claims.Count==0) { DaggerfallUI.AddHUDText("The body carries nothing you can take."); return; }
            var container=new GameObject("People of the Iliac Bay corpse inventory");
            container.transform.position=body.transform.position;
            corpseInventory=container.AddComponent<DaggerfallLoot>();
            corpseInventory.ContainerType=LootContainerTypes.CorpseMarker;
            corpseInventoryIncident=incidentId;
            issuedCorpseKeys.Clear();
            foreach(var claim in claims)
            {
                var key=new HouseholdPropertyKeyItem(claim.SettlementId,claim.BuildingKey);
                issuedCorpseKeys.Add(key); corpseInventory.Items.AddItem(key);
            }
            DaggerfallUI.Instance.InventoryWindow.LootTarget=corpseInventory;
            DaggerfallUI.UIManager.PushWindow(DaggerfallUI.Instance.InventoryWindow);
        }

        private void UpdateCorpseInventory()
        {
            if (!corpseInventory || DaggerfallUI.Instance.InventoryWindow.LootTarget==corpseInventory) return;
            // Native inventory permits deposits. Preserve them in its serializable
            // ground container; unclaimed generated keys remain in our corpse ledger.
            DaggerfallLoot dropped=null;
            foreach(var item in new List<DaggerfallUnityItem>(corpseInventory.Items.Export()))
            {
                if (issuedCorpseKeys.Contains(item)) continue;
                if (!dropped) dropped=GameObjectHelper.CreateDroppedLootContainer(GameManager.Instance.PlayerObject,DaggerfallUnity.NextUID);
                dropped.Items.Transfer(item,corpseInventory.Items);
            }
            Destroy(corpseInventory.gameObject);
            corpseInventory=null; corpseInventoryIncident=null; issuedCorpseKeys.Clear();
        }
    }
}
