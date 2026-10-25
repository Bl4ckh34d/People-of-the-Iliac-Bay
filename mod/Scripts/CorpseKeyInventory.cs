using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private IDisposable corpseTransferHook;
        private DaggerfallLoot corpseInventory;
        private string corpseInventoryIncident;
        private NativeNpcInventoryBinding corpseInventoryBinding;
        private readonly HashSet<DaggerfallUnityItem> issuedCorpseKeys = new HashSet<DaggerfallUnityItem>();

        private void InitializeCorpseKeys(CivilianIncident alarm)
        {
            if (alarm.Corpse.PropertyKeyClaimsInitialized) return;
            int? workplace = null;
            var person = worldStore.NativePeopleFor(population.SettlementId).Find(p => p.Id == alarm.VictimId);
            BuildingSummary building;
            if (person != null && directory.GetBuildingSummary(person.WorkKey,out building) &&
                RMBLayout.IsShop(building.BuildingType) && NativeBusinessServices.OffersKeeperService(person.FactionId,building.BuildingType))
                workplace = person.WorkKey;
            worldStore.InitializeCorpseHomeKey(alarm.Id,workplace,AllowsPropertyKey);
        }

        private bool AllowsPropertyKey(int settlement,int buildingKey)
        {
            BuildingSummary summary;
            // Foreign generated households are residential; never replace their
            // home with the current inn's key. Local authored sites are classified.
            return !directory || population==null || settlement!=population.SettlementId ||
                !directory.GetBuildingSummary(buildingKey,out summary) || summary.BuildingType!=DaggerfallConnect.DFLocation.BuildingTypes.Tavern;
        }
        private bool AllowsCorpsePropertyKey(string victimId,int settlement,int buildingKey)
        {
            if(!AllowsPropertyKey(settlement,buildingKey)) return false;
            var person=worldStore.NativePeopleFor(settlement).Find(p=>p.Id==victimId);
            if(person==null || person.WorkKey!=buildingKey) return true;
            if(person.HomeKey==buildingKey && person.BedSlot>=0 && person.BedSlot<person.BedCapacity) return true;
            BuildingSummary summary;
            return directory && population!=null && population.SettlementId==settlement &&
                directory.GetBuildingSummary(buildingKey,out summary) && RMBLayout.IsShop(summary.BuildingType) &&
                NativeBusinessServices.OffersKeeperService(person.FactionId,summary.BuildingType);
        }

        private void InstallCorpseTransferHook()
        {
            var type = Type.GetType("PeopleOfTheIliacBay.QuestHooks.NativeItemTransferHooks, PeopleOfTheIliacBay.QuestHooks", true);
            corpseTransferHook = (IDisposable)Activator.CreateInstance(type, new object[] {
                new Func<DaggerfallUnityItem,ItemCollection,ItemCollection,bool>((item,source,target) =>
                    corpseInventory && (ReferenceEquals(source,corpseInventory.Items) || ReferenceEquals(target,corpseInventory.Items))),
                new Func<DaggerfallUnityItem,ItemCollection,ItemCollection,bool>((item,source,target) => {
                    bool outgoing=ReferenceEquals(source,corpseInventory.Items);
                    if(outgoing && item is HouseholdPropertyKeyItem && !issuedCorpseKeys.Contains(item))return false;
                    // Persist item ownership before consuming one-time key claim.
                    // Rejected snapshots cause the transfer hook to restore native collections.
                    if(!TryCaptureOpenCorpseInventory())return false;
                    if(outgoing && issuedCorpseKeys.Contains(item) &&
                        !worldStore.MarkCorpsePropertyKeyTaken(corpseInventoryIncident,item.enchantmentPoints,item.message))return false;
                    return true;
                }),
                new Action<DaggerfallUnityItem,ItemCollection,ItemCollection>((item,source,target) => {
                    bool outgoing=ReferenceEquals(source,corpseInventory.Items);
                    if(outgoing && issuedCorpseKeys.Contains(item))issuedCorpseKeys.Remove(item);
                    if(!outgoing)return;
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
                }),
                new Action<DaggerfallUnityItem,ItemCollection,ItemCollection>((item,source,target) => {
                    // A successful first capture may precede a rejected key claim.
                    // Resnapshot restored source so save ledger matches native state.
                    CaptureOpenCorpseInventory();
                })
            });
            DaggerfallUI.UIManager.OnWindowChange += OnCorpseInventoryWindowChanged;
        }

        private void OnCorpseInventoryWindowChanged(object sender, EventArgs args) { UpdateCorpseInventory(); }

        private void DisposeCorpseInventory()
        {
            DaggerfallUI.UIManager.OnWindowChange -= OnCorpseInventoryWindowChanged;
            if (corpseTransferHook != null) corpseTransferHook.Dispose();
            CloseCorpseInventory();
        }

        public void OpenCorpseInventory(string incidentId)
        {
            GameObject body;
            if (!Ready || worldStore == null || !corpses.TryGetValue(incidentId,out body) || !body ||
                Vector3.Distance(body.transform.position,GameManager.Instance.PlayerMotor.transform.position)>3) return;
            if (corpseInventory) return;
            worldStore.InitializeCorpseHomeKey(incidentId,null,AllowsPropertyKey);
            var claims=worldStore.GetAvailableCorpsePropertyKeyClaims(incidentId,AllowsCorpsePropertyKey);
            string owner=worldStore.CorpseInventoryOwner(incidentId),reason;
            NativeNpcInventoryBinding binding;
            if(owner==null || !NativeNpcInventoryBridge.TryOpen(worldStore,owner,null,
                NativeNpcInventoryBridge.InitialWalletGold(worldStore,owner),out binding,out reason))
            {DaggerfallUI.AddHUDText("The body's saved inventory is unavailable.");return;}
            if(!NativeNpcInventoryBridge.TryMaterializeCorpseWallet(worldStore,binding,out reason))
                throw new InvalidOperationException(reason);
            issuedCorpseKeys.Clear();
            foreach(var claim in claims)
            {
                DaggerfallUnityItem key=null;
                for(int i=0;i<binding.Items.Count;i++)
                    if(HouseholdPropertyKeyItem.IsMatching(binding.Items.GetItem(i),claim.SettlementId,claim.BuildingKey))
                    {key=binding.Items.GetItem(i);break;}
                if(key==null){key=new HouseholdPropertyKeyItem(claim.SettlementId,claim.BuildingKey);binding.Items.AddItem(key,noStack:true);}
                issuedCorpseKeys.Add(key);
            }
            if(!NativeNpcInventoryBridge.TryCapture(worldStore,binding,out reason))throw new InvalidOperationException(reason);
            var container=new GameObject("People of the Iliac Bay corpse inventory");
            container.transform.position=body.transform.position;
            corpseInventory=container.AddComponent<DaggerfallLoot>();
            corpseInventory.ContainerType=LootContainerTypes.CorpseMarker;
            corpseInventoryIncident=incidentId;
            for(int i=0;i<binding.Items.Count;i++)corpseInventory.Items.AddItem(binding.Items.GetItem(i),noStack:true);
            binding.Items=corpseInventory.Items;corpseInventoryBinding=binding;
            DaggerfallUI.Instance.InventoryWindow.LootTarget=corpseInventory;
            DaggerfallUI.UIManager.PushWindow(DaggerfallUI.Instance.InventoryWindow);
        }

        private void UpdateCorpseInventory()
        {
            if (corpseInventoryBinding==null || corpseInventory && DaggerfallUI.Instance.InventoryWindow.LootTarget==corpseInventory) return;
            CloseCorpseInventory();
        }
        public void CaptureNpcInventories()
        {
            foreach(var actor in actors.Values)if(actor)actor.CaptureState();
            CaptureOpenCorpseInventory();
        }
        private void CaptureOpenCorpseInventory()
        {
            if(corpseInventoryBinding==null)return;
            string reason;if(!NativeNpcInventoryBridge.TryCapture(worldStore,corpseInventoryBinding,out reason))throw new InvalidOperationException(reason);
        }
        private bool TryCaptureOpenCorpseInventory()
        {
            if(corpseInventoryBinding==null)return false;
            string reason;return NativeNpcInventoryBridge.TryCapture(worldStore,corpseInventoryBinding,out reason);
        }
        public void CloseCorpseInventory()
        {
            if(corpseInventoryBinding==null && !corpseInventory)return;
            CaptureOpenCorpseInventory();
            if(corpseInventory && DaggerfallUI.Instance.InventoryWindow.LootTarget==corpseInventory)DaggerfallUI.Instance.InventoryWindow.LootTarget=null;
            if(corpseInventory)Destroy(corpseInventory.gameObject);
            corpseInventory=null; corpseInventoryIncident=null;corpseInventoryBinding=null;issuedCorpseKeys.Clear();
        }
    }
}
