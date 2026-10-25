using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private void UpdateDefenseCorpseScenes(double now,bool outside)
        {
            if(!outside)return;
            var guards=GetComponent<GuardPopulation>();if(!guards)return;
            var corpses=worldStore.DefenseCorpsesFor(population.SettlementId);
            guards.RefreshNativeCorpseViews(corpses);
            foreach(var record in corpses)
            {
                Vector3 point;
                if(!guards.NativeCorpsePoint(record,out point))continue;
                if(record.Death.Cleanup!=null && record.Death.Cleanup.Cleared)
                {guards.RemoveClearedNativeCorpse(record);continue;}
                foreach(var actor in actors.Values)
                {
                    if(!actor||!actor.Resident.IsAlive||!actor.gameObject.activeInHierarchy||actor.IsSleeping||
                        !CivilianResponses.Sees(actor.transform.position,point+Vector3.up*0.3f,20))continue;
                    worldStore.NoticeDefenseCorpse(record.Id,actor.Resident.Id,false,now);
                }
                guards.ObserveNativeCorpse(record,point,now);
            }
        }
    }
    public sealed partial class GuardPopulation
    {
        private readonly Dictionary<string,DaggerfallLoot> nativeCorpseViews=new Dictionary<string,DaggerfallLoot>();
        private WorldStateStore nativeCorpseViewStore;
        public void RefreshNativeCorpseViews(List<DefenseCombatant> corpses)
        {
            nativeCorpseViews.Clear();nativeCorpseViewStore=worldStore;
            if(!OwnsExterior||corpses.Count==0)return;
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var record in corpses)ids.Add(record.Death.CorpseId);
            foreach(var loot in UnityEngine.Object.FindObjectsOfType<DaggerfallLoot>())
            {
                if(!loot||!loot.gameObject.activeInHierarchy||loot.LoadID==0||loot.WorldContext!=WorldContext.Exterior||
                    loot.ContainerType!=LootContainerTypes.CorpseMarker)continue;
                string id="loot:"+loot.LoadID.ToString(CultureInfo.InvariantCulture);
                if(ids.Contains(id))nativeCorpseViews[id]=loot;
            }
        }
        private DaggerfallLoot NativeCorpseView(DefenseCombatant record)
        {
            DaggerfallLoot loot;
            if(!OwnsExterior||nativeCorpseViewStore!=worldStore||record==null||record.SettlementId!=directory.MapID||
                record.Death==null||record.Death.CorpseId==null||!nativeCorpseViews.TryGetValue(record.Death.CorpseId,out loot)||!loot)return null;
            var expected=directory.transform.TransformPoint(new Vector3((float)record.Death.X,(float)record.Death.Y,(float)record.Death.Z));
            // A UID alone cannot move a corpse into another settlement/scene.
            return (loot.transform.position-expected).sqrMagnitude<=36?loot:null;
        }
        public bool NativeCorpsePoint(DefenseCombatant record,out Vector3 point)
        {
            var loot=NativeCorpseView(record);point=loot?loot.transform.position:Vector3.zero;return loot;
        }
        private static bool MayRemoveNativeCorpse(DaggerfallLoot loot)
        {
            if(!loot)return false;
            var inventory=DaggerfallUI.UIManager.TopWindow as DaggerfallInventoryWindow;
            if(inventory!=null&&inventory.LootTarget==loot)return false;
            if(loot.corpseQuestUID!=0)
            {
                var quest=QuestMachine.Instance.GetQuest(loot.corpseQuestUID);
                if(quest!=null&&!quest.QuestTombstoned)return false;
            }
            return true;
        }
        private bool RemoveNativeCorpse(DefenseCombatant record)
        {
            var loot=NativeCorpseView(record);if(!MayRemoveNativeCorpse(loot))return false;
            loot.gameObject.SetActive(false);
            // Native SerializableLootContainer.OnDestroy deregisters this exact container.
            UnityEngine.Object.Destroy(loot.gameObject);
            nativeCorpseViews.Remove(record.Death.CorpseId);return true;
        }
        public void RemoveClearedNativeCorpse(DefenseCombatant record)
        {
            if(record.Death.Cleanup!=null&&record.Death.Cleanup.Cleared)RemoveNativeCorpse(record);
        }
        public void ObserveNativeCorpse(DefenseCombatant record,Vector3 point,double now)
        {
            if(!OwnsExterior)return;
            foreach(var pair in actors)
            {
                var actor=pair.Value;
                if(!actor||!actor.gameObject.activeInHierarchy||!Roster.Guards.Exists(g=>g.Id==pair.Key&&g.IsAlive&&g.Interior==null)||
                    !CivilianResponses.Sees(actor.transform.position,point+Vector3.up*0.3f,20))continue;
                worldStore.NoticeDefenseCorpse(record.Id,pair.Key,true,now);
                ReportNativeCorpse(record,actor.transform.position,pair.Key,now);
            }
        }
        private bool ReportNativeCorpse(DefenseCombatant record,Vector3 reporter,string guardId,double now)
        {
            if(!OwnsExterior||record.SettlementId!=directory.MapID||record.Death==null||record.Death.Cleanup==null)return false;
            var death=record.Death;
            if(death.Cleanup.Cleared)return true;
            if(Roster.Reports.Exists(r=>r.CleanupCorpseId==death.CorpseId && Roster.Guards.Exists(g=>g.IsAlive&&g.IncidentId==r.Id&&
                g.Duty!=GuardDuty.Posted&&g.Duty!=GuardDuty.Returning)))return true;
            var target=new SavedRoutePoint{X=death.X,Z=death.Z};
            var routes=NearbyResponseRoutes(reporter,guardId,target);if(routes.Count==0)return false;
            string id="cleanup:"+record.Id+":"+now.ToString("R",CultureInfo.InvariantCulture);
            var sent=Roster.DispatchCorpseCleanup(id,death.CorpseId,now,routes,1,2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
            if(sent.Count==0)return false;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_AGGRESSOR_CORPSE_DISPATCH: "+record.Id+"; guard="+sent[0].Id+"; investigation=false");
            return true;
        }
        public bool DeliverNativeBodyReport(DefenseCombatant record,string guardId,Vector3 from,double now)
        {
            Vector3 point;
            if(!BodyReportGuardPosition(guardId,out point)||(point-from).sqrMagnitude>16||!CivilianResponses.Sees(from,point,5))return false;
            return ReportNativeCorpse(record,from,guardId,now);
        }
        private void ReturnAfterNativeCleanup(GuardRecord guard,GuardActor actor,double now)
        {
            if(guard.Duty==GuardDuty.Returning||guard.Duty==GuardDuty.Posted)return;
            var current=Local(actor.transform.position);
            var route=geometry.RouteBetweenPoints(current,guard.Post);if(route.Count<2)return;
            actor.EndCombat();
            if(guard.Duty!=GuardDuty.Engaged)Roster.Engage(guard.Id,now);
            Roster.ObserveEngagement(guard.Id,current,guard.HealthFraction,now,false);
            Roster.ReturnFromEngagement(guard.Id,now,route,2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
        }
        private bool TryMurdererCorpseCleanup(GuardRecord guard,GuardActor actor,GuardReport source,double now)
        {
            if(source.CorpseSuspect.Kind!=CorpseSuspectKind.HostileEntity)return false;
            var record=worldStore.GetDefenseCombatant(source.CorpseSuspect.Identity);Vector3 point;
            if(record==null || record.Death==null || !record.Death.Lawful || record.PlayerProvoked)return false;
            if(!NativeCorpsePoint(record,out point))
            {
                RefreshNativeCorpseViews(worldStore.DefenseCorpsesFor(directory.MapID));
                if(!NativeCorpsePoint(record,out point))return false;
            }
            if(record.Death.Cleanup!=null && record.Death.Cleanup.Cleared)return false;
            if(Roster.Reports.Exists(r=>r.CleanupCorpseId==record.Death.CorpseId &&
                Roster.Guards.Exists(g=>g.IsAlive && g.IncidentId==r.Id && g.Duty!=GuardDuty.Posted && g.Duty!=GuardDuty.Returning)))return false;
            var current=Local(actor.transform.position);var destination=Local(point+Vector3.up);
            var path=geometry.RouteBetweenPoints(current,destination);
            if(path.Count<2 && Vector3.Distance(actor.transform.position,point)<3)
                path=new List<SavedRoutePoint>{current,current};
            if(path.Count<2)return false;
            var home=geometry.RouteBetweenPoints(path[path.Count-1],guard.Post);if(home.Count<2)return false;
            worldStore.NoticeDefenseCorpse(record.Id,guard.Id,true,now);
            if(guard.Duty!=GuardDuty.Engaged)Roster.Engage(guard.Id,now);
            Roster.ObserveEngagement(guard.Id,current,guard.HealthFraction,now,false);
            Roster.RedirectMurdererCorpseCleanup(guard.Id,record.Id,"cleanup:murder:"+source.Id+":"+guard.Id,
                record.Death.CorpseId,now,path,home,2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MURDERER_CLEANUP_HANDOFF: "+record.Id+"; guard="+guard.Id);
            return true;
        }
        private bool UpdateNativeCorpseCleanup(GuardRecord guard,GuardActor actor,double now)
        {
            var report=Roster.Reports.Find(r=>r.Id==guard.IncidentId);
            if(report==null||report.CleanupCorpseId==null||guard.Duty==GuardDuty.Returning||guard.Duty==GuardDuty.Engaged)return false;
            // Cleanup never runs generic suspect acquisition or starts combat against a bystander.
            actor.EndCombat();actor.Place(now);
            var record=worldStore.DefenseCorpsesFor(directory.MapID).Find(c=>c.Death.CorpseId==report.CleanupCorpseId);
            if(record==null||record.Death.Cleanup!=null&&record.Death.Cleanup.Cleared)
            {ReturnAfterNativeCleanup(guard,actor,now);return true;}
            var local=directory.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            if(worldStore.TryExecuteDefenseCorpseCleanup(record.Id,guard.Id,local.x,local.y,local.z,now,
                corpseId=>corpseId==record.Death.CorpseId && RemoveNativeCorpse(record)))
            {
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_AGGRESSOR_CORPSE_REMOVED: "+record.Id+"; guard="+guard.Id+"; investigation=false");
                ReturnAfterNativeCleanup(guard,actor,now);
            }
            return true;
        }
    }
}
