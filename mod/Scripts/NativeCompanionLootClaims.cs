using System;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Questing;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeCompanionLootClaims
    {
        internal static Action<Exception> BeginDamage(WorldStateStore store,DaggerfallEntityBehaviour target,DaggerfallEntityBehaviour source)
        {
            var actor=source?source.GetComponent<ResidentActor>():null;
            if(store==null || !actor || actor.Resident==null || !actor.Resident.IsAlive ||
                !target || target.Entity==null || target.Entity.CurrentHealth<=0 || !NativeNpcCombatAssist.OwnershipClear(target))return null;
            var enterprise=store.ReadCompanionEnterprise(actor.Resident.Id);string enemy=NativeNpcCombatAssist.Identity(target);
            if(enterprise==null || !enterprise.Active || enemy==null)return null;
            return error=>
            {
                if(error!=null || !target || target.Entity==null || target.Entity.CurrentHealth>0 ||
                    actor.CompanionStateStore!=store)return;
                double now=ResidentActor.CompanionNow();
                if(!store.TryRecordCompanionDefeat(actor.Resident.Id,enterprise.Id,enemy,0,now))return;
                var receipt=target.GetComponent<NativeCompanionDefeatBinding>();
                if(!receipt)receipt=target.gameObject.AddComponent<NativeCompanionDefeatBinding>();
                receipt.Store=store;receipt.Person=actor.Resident.Id;receipt.Enterprise=enterprise.Id;receipt.Enemy=enemy;
            };
        }
        internal static void BindCorpse(WorldStateStore store,DaggerfallEntityBehaviour target)
        {
            var receipt=target?target.GetComponent<NativeCompanionDefeatBinding>():null;var loot=target?target.CorpseLootContainer:null;
            if(!receipt || receipt.Store!=store || !loot || loot.LoadID==0 || loot.ContainerType!=LootContainerTypes.CorpseMarker || loot.corpseQuestUID!=0)return;
            store.TryRecordCompanionDefeat(receipt.Person,receipt.Enterprise,receipt.Enemy,loot.LoadID,ResidentActor.CompanionNow());
        }
        internal static bool TryLootOrApproach(WorldStateStore store,ResidentActor actor,out Vector3 target)
        {bool transferred;return TryLootOrApproach(store,actor,out target,out transferred);}
        internal static bool TryLootOrApproach(WorldStateStore store,ResidentActor actor,out Vector3 target,out bool transferred)
        {
            target=Vector3.zero;transferred=false;var enterprise=store.ReadCompanionEnterprise(actor.Resident.Id);
            if(enterprise==null || !enterprise.Active || !enterprise.OwnDefeatedEnemies && !enterprise.Containers || actor.IsConversing || GameManager.IsGamePaused)return false;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            if(!owner || owner.Binding==null || !body || body.Entity==null)return false;
            var seeker=actor.GetComponent<NativeCompanionLootSeeker>();if(!seeker)seeker=actor.gameObject.AddComponent<NativeCompanionLootSeeker>();
            if(Time.unscaledTime>=seeker.RefreshAt)
            {seeker.RefreshAt=Time.unscaledTime+1f;seeker.Loaded=UnityEngine.Object.FindObjectsOfType<DaggerfallLoot>();}
            foreach(var loot in seeker.Loaded)
            {
                if(!loot || !loot.gameObject.activeInHierarchy || loot.LoadID==0 || !NativeSourcePermitted(enterprise,loot) ||
                    Vector3.Distance(actor.transform.position,loot.transform.position)>6 ||
                    !CivilianResponses.Sees(actor.transform.position,loot.transform.position+Vector3.up*0.3f,6,loot.transform))continue;
                var dungeon=GameManager.Instance.PlayerEnterExit.Dungeon;
                if(!dungeon || !loot.transform.IsChildOf(dungeon.transform))continue;
                for(int i=0;i<loot.Items.Count;i++)
                {
                    var item=loot.Items.GetItem(i);string reason;
                    if(item==null || item.IsQuestItem || item.IsArtifact || item.RepairData.IsBeingRepaired() || item.stackCount<1 ||
                        body.Entity.Items.GetWeight()+item.EffectiveUnitWeightInKg()>body.Entity.MaxEncumbrance ||
                        !NativeNpcInventoryBridge.CanAppendExactItem(owner.Binding.Items,item,out reason))continue;
                    target=loot.transform.position;
                    seeker.LastSource=loot.LoadID;seeker.LastItem=item.UID;seeker.LastFailure=null;
                    if(Vector3.Distance(actor.transform.position-Vector3.up,target)>1.1f)
                    {
                        // Corpse/treasure billboards are raised above their real
                        // floor. Navigate to that floor without changing the
                        // exact container or the close-range transfer check.
                        if(!TryLootFloor(loot,dungeon.transform,out target))continue;
                        return true;
                    }
                    if(!NativeNpcInventoryBridge.TryCapture(store,owner.Binding,out reason)){seeker.LastFailure=reason;return false;}
                    ulong uid=item.UID;string enterpriseId=enterprise.Id;
                    bool moved=NativeNpcInventoryBridge.TryReceiveFromExternal(store,loot.Items,owner.Binding,uid,1,()=>
                    {
                        var current=store.ReadCompanionEnterprise(actor.Resident.Id);
                        return actor.Resident.IsAlive && loot && loot.gameObject.activeInHierarchy &&
                            current!=null && current.Active && current.Id==enterpriseId && NativeSourcePermitted(current,loot) && loot.Items.GetItem(uid)==item &&
                            Vector3.Distance(actor.transform.position-Vector3.up,loot.transform.position)<=1.1f &&
                            CivilianResponses.Sees(actor.transform.position,loot.transform.position+Vector3.up*0.3f,6,loot.transform) &&
                            body.Entity.Items.GetWeight()+item.EffectiveUnitWeightInKg()<=body.Entity.MaxEncumbrance;
                    },"companion-loot:"+enterpriseId+":"+uid.ToString(CultureInfo.InvariantCulture)+":"+Guid.NewGuid().ToString("N"),out reason);
                    seeker.LastFailure=reason;transferred=moved;if(moved)actor.CaptureInventory();return moved;
                }
            }
            return false;
        }
        private static bool TryLootFloor(DaggerfallLoot loot,Transform dungeon,out Vector3 floor)
        {
            floor=Vector3.zero;float nearest=float.MaxValue;
            foreach(var hit in Physics.RaycastAll(loot.transform.position+Vector3.up*0.1f,Vector3.down,1.2f,~0,QueryTriggerInteraction.Ignore))
            {
                if(!hit.collider || !hit.transform.IsChildOf(dungeon) || hit.transform.IsChildOf(loot.transform) ||
                    NativeNavigationGeometry.IsDynamicNavigationBody(hit.collider) || hit.normal.y<0.7f || hit.distance>=nearest)continue;
                nearest=hit.distance;floor=hit.point;
            }
            return nearest<float.MaxValue;
        }
        private static bool NativeSourcePermitted(NpcCompanionEnterprise enterprise,DaggerfallLoot loot)
        {
            if(!loot || loot.corpseQuestUID!=0 || loot.GetComponentInParent<QuestResourceBehaviour>() || loot.playerOwned || loot.houseOwned)return false;
            bool ownCorpse=loot.ContainerType==LootContainerTypes.CorpseMarker && enterprise.Defeats.Exists(defeat=>defeat.CorpseLoadId==loot.LoadID);
            bool unownedTreasure=loot.ContainerType==LootContainerTypes.RandomTreasure;
            return enterprise.AllowsLoadedLoot(enterprise.Id,ownCorpse,unownedTreasure);
        }
    }
    internal sealed class NativeCompanionDefeatBinding : MonoBehaviour
    {internal WorldStateStore Store;internal string Person,Enterprise,Enemy;}
    internal sealed class NativeCompanionLootSeeker : MonoBehaviour
    {internal float RefreshAt;internal DaggerfallLoot[] Loaded=new DaggerfallLoot[0];internal string LastFailure;internal ulong LastSource,LastItem;}
}
