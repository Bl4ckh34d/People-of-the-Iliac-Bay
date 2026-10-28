using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private void UpdateDungeonCustody(DaggerfallDungeon dungeon,double now)
        {
            var cell=NativeNpcCustody.Cell;
            if(worldStore==null || !dungeon || !dungeon.gameObject.activeInHierarchy || cell==null ||
                !cell.Current || cell.Root!=dungeon.transform)return;
            foreach(var plan in plans)
            {
                var row=worldStore.ReadNpcCustody(plan.Resident.Id);
                if(row==null || row.Phase>=NpcCustodyPhase.Released || !cell.Matches(row) ||
                    row.LastPosition.WorldSpaceId!=cell.Space || worldStore.ReadNpcInventory(plan.Resident.Id)==null)continue;
                ResidentActor actor;actors.TryGetValue(plan.Resident.Id,out actor);
                if(actor)
                {
                    var owner=actor.GetComponent<NativeNpcInventoryOwner>();
                    // An exterior body needs a genuine portal handoff. A loaded
                    // dungeon alone never reparents or duplicates that person.
                    if(!actor.transform.IsChildOf(cell.Root) || !owner || !owner.IsBoundTo(worldStore))continue;
                }
                ApplyNpcCustodyOverlay(plan,ref actor,now);
            }
        }

        private bool ApplyNpcCustodyOverlay(Plan plan,ref ResidentActor actor,double now)
        {
            var row=worldStore.ReadNpcCustody(plan.Resident.Id);
            if(row==null || row.Phase>=NpcCustodyPhase.Released)return false;
            var job=worldStore.ActiveNpcAction(plan.Resident.Id);
            if(job!=null)EndAction(job,now,"Native guard custody interrupted this action.",false);
            var cell=NativeNpcCustody.Cell;
            var entering=GameManager.Instance.PlayerEnterExit;
            var root=entering.IsPlayerInsideDungeon ? (entering.Dungeon?entering.Dungeon.transform:null) :
                interior?interior.transform:directory.transform;
            if(cell==null || !cell.Matches(row) || cell.Root!=root)return true;
            if(!actor)
            {
                var position=cell.World(row.LastPosition);
                if(Vector3.Distance(position,GameManager.Instance.PlayerMotor.transform.position)>140 || actors.Count>=80)return true;
                var go=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,cell.Root);
                go.name="People of the Iliac Bay "+plan.Resident.Id;actor=go.AddComponent<ResidentActor>();
                actor.Initialize(plan.Resident,plan.SurnameSeed,navigation,onDeath:TravelerActionDeathHandler(plan),inventoryStore:worldStore);
                actor.SetRunningPilotAtlas(runningPilotAtlas);
                var crowd=actor.GetComponent<CrowdMotion>();if(crowd)crowd.RestorePlacement(position);else actor.transform.position=position+Vector3.up;
                actors[plan.Resident.Id]=actor;go.SetActive(true);
            }
            var session=actor.GetComponent<NativeNpcCustodySession>();
            if(!session)
            {
                // A saved escort needs its actual guard body to resume. Time
                // passing or reloading cannot complete its journey to the cell.
                GuardActor escort=null;
                if(row.Phase==NpcCustodyPhase.Escorting)
                {
                    foreach(var candidate in Object.FindObjectsOfType<GuardActor>())
                        if(candidate.GuardId==row.GuardId && candidate.transform.IsChildOf(cell.Root)){escort=candidate;break;}
                    if(!escort)return true;
                }
                session=actor.gameObject.AddComponent<NativeNpcCustodySession>();session.Bind(worldStore,actor,cell,escort);
            }
            return session.Tick(now);
        }
    }
}
