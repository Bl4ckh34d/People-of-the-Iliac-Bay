using System;
using System.Globalization;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        public string ActionAwarenessContext(string residentId,double now,Func<int,string> buildingName)
        {
            if(string.IsNullOrWhiteSpace(residentId) || worldStore==null) return string.Empty;
            var actor=FindActor(residentId);
            var job=worldStore.ActiveNpcAction(residentId);
            var task=worldStore.ActiveNpcTask(residentId);
            if(job==null && task==null)return string.Empty;
            string physical=PhysicalActionLocation(actor,buildingName);
            string target=DescribeActionTarget(job,actor,buildingName);
            Vector3 body,targetPoint;
            if(actor && actor.gameObject.activeInHierarchy && TryActionTargetPosition(job,actor,out targetPoint))
            {
                body=actor.transform.position-Vector3.up;
                target+=" ("+Vector3.Distance(body,targetPoint).ToString("F1",CultureInfo.InvariantCulture)+" metres from target)";
            }
            string next=NextActionCommitment(residentId,now,buildingName);
            return ConversationActionAwareness.Format(job,task,physical,target,next);
        }

        private string PhysicalActionLocation(ResidentActor actor,Func<int,string> buildingName)
        {
            if(!actor || !actor.gameObject.activeInHierarchy)
                return "no active physical body is observed; the schedule and saved marker do not prove location";
            if(interior && actor.transform.IsChildOf(interior.transform))
            {
                int building=interior.EntryDoor.buildingKey;
                string name=buildingName==null?null:buildingName(building);
                return "inside "+(string.IsNullOrWhiteSpace(name)?"building "+building.ToString(CultureInfo.InvariantCulture):name);
            }
            if(directory && actor.transform.IsChildOf(directory.transform))
            {
                string town=GameManager.Instance.PlayerGPS.HasCurrentLocation?GameManager.Instance.PlayerGPS.CurrentLocation.Name:null;
                return "outdoors"+(string.IsNullOrWhiteSpace(town)?"":" in "+town);
            }
            return "active body observed in another loaded scene; room is unresolved";
        }

        private string DescribeActionTarget(NpcActionJob job,ResidentActor actor,Func<int,string> buildingName)
        {
            if(job==null || job.Target==null) return null;
            if(job.ReturningToRoutine)
            {
                ActionMotion motion;
                return actionMotions.TryGetValue(job.ResidentId,out motion) && motion.RecoverySelected && motion.RecoveryOrigin>=0 ?
                    "routine rejoin doorway at "+ActionAwarenessBuildingName(motion.RecoveryOrigin,buildingName):
                    "verified routine doorway; rejoin route is being selected";
            }
            var target=job.Target;
            string person=target.Kind==NpcActionTargetKind.Person?ActionAwarenessPersonName(target.PersonId):null;
            string item=ActionItemName(job,actor) ?? "unresolved item";
            switch(job.Kind)
            {
                case NpcActionKind.Wait:return "accepted waiting point";
                case NpcActionKind.Follow:return "player's current position";
                case NpcActionKind.MoveTo:
                case NpcActionKind.Lead:
                    if(target.Kind==NpcActionTargetKind.Building)
                        return ActionAwarenessBuildingName(target.BuildingKey,buildingName)+" exterior entrance"+
                            (job.Kind==NpcActionKind.Lead?" while keeping the player nearby":"");
                    if(target.Kind==NpcActionTargetKind.Person)return person+" current position";
                    return target.Kind==NpcActionTargetKind.IndoorPoint?"interior point in "+ActionAwarenessBuildingName(target.BuildingKey,buildingName):"saved local point";
                case NpcActionKind.Interact:return "approach and face "+(person??"the other person")+"; no dialogue or transfer is part of this action";
                case NpcActionKind.CastSpell:
                    return job.MagicHealing==null?"an unsupported spell action":
                        "self-treatment with the selected Heal-Health spell; expected cost "+
                        job.MagicHealing.MagickaCost.ToString(CultureInfo.InvariantCulture)+
                        " magicka; actual healing requires an observed native treatment receipt";
                case NpcActionKind.Purchase:
                    return "buy "+target.Quantity.ToString(CultureInfo.InvariantCulture)+" x "+item+" from "+
                        (person??"the local shop")+" for "+job.ServicePrice.ToString(CultureInfo.InvariantCulture)+" gold";
                case NpcActionKind.Transfer:
                    return target.TransferDirection==NpcActionTransferDirection.PlayerToNpc?
                        "receive "+target.Quantity.ToString(CultureInfo.InvariantCulture)+" x "+item+" from the player into NPC belongings":
                        target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc?
                        "retrieve "+target.Quantity.ToString(CultureInfo.InvariantCulture)+" x "+item+" from household storage at home into NPC belongings":
                        "give "+target.Quantity.ToString(CultureInfo.InvariantCulture)+" x "+item+" to "+(person??"the counterparty");
                default:
                    if(target.Kind==NpcActionTargetKind.Building)return ActionAwarenessBuildingName(target.BuildingKey,buildingName)+" entrance";
                    if(target.Kind==NpcActionTargetKind.Person)return person+" current position";
                    return target.Kind==NpcActionTargetKind.IndoorPoint?"interior point in "+ActionAwarenessBuildingName(target.BuildingKey,buildingName):"saved local point";
            }
        }

        private string ActionAwarenessPersonName(string id)
        {
            if(id=="player")return "the player";
            var actor=FindActor(id);
            if(actor && actor.Person && !string.IsNullOrWhiteSpace(actor.Person.NameNPC))return actor.Person.NameNPC;
            if(population!=null && worldStore!=null)
            {
                var person=worldStore.NativePeopleFor(population.SettlementId).Find(p=>p.Id==id);
                if(person!=null && !string.IsNullOrWhiteSpace(person.Name))return person.Name;
            }
            return "the named counterparty";
        }

        private static string ActionAwarenessBuildingName(int key,Func<int,string> buildingName)
        {
            string name=key<0 || buildingName==null?null:buildingName(key);
            return string.IsNullOrWhiteSpace(name)?"building "+key.ToString(CultureInfo.InvariantCulture):name;
        }

        private bool TryActionTargetPosition(NpcActionJob job,ResidentActor actor,out Vector3 target)
        {
            target=Vector3.zero;
            if(job==null || job.Target==null || !actor)return false;
            if(job.ReturningToRoutine)
            {
                ActionMotion motion;
                if(actionMotions.TryGetValue(job.ResidentId,out motion) && motion.RecoverySelected)
                {target=motion.Goal;return true;}
                return false;
            }
            if(job.Kind==NpcActionKind.Follow || job.Target.Kind==NpcActionTargetKind.Person && job.Target.PersonId=="player")
            {target=GameManager.Instance.PlayerMotor.transform.position-Vector3.up;return true;}
            if(job.Target.Kind==NpcActionTargetKind.Person)
            {
                var other=FindActor(job.Target.PersonId);
                if(!other || !other.gameObject.activeInHierarchy)return false;
                target=other.transform.position-Vector3.up;return true;
            }
            if(job.Target.Kind==NpcActionTargetKind.Building && !interior && geometry!=null)
            {
                TownGeometry.Entrance entrance;
                if(!geometry.Entrances.TryGetValue(job.Target.BuildingKey,out entrance))return false;
                target=geometry.SceneDoor(entrance);return true;
            }
            if(!ActionSpaceMatches(job.Target))return false;
            target=ActionWorldPoint(job.Target);return true;
        }

        private string NextActionCommitment(string residentId,double now,Func<int,string> buildingName)
        {
            var plan=plans.Find(p=>p.Resident.Id==residentId);
            if(plan==null || !FiniteRecoveryTime(now))return null;
            var visits=ActiveSchedule(plan).ScheduledVisits(now);
            double local=now%86400d;ResidentVisitSnapshot selected=default(ResidentVisitSnapshot);bool found=false;
            foreach(var visit in visits)
            {
                if(visit.LeaveAt<=local)continue;
                if(!found || visit.EnterAt<selected.EnterAt){selected=visit;found=true;}
            }
            if(!found)
            {
                double homeAt=ActiveSchedule(plan).ReturnHomeAt(now);
                return homeAt>now && homeAt<(Math.Floor(now/86400d)+1d)*86400d?
                    "return home around "+ActionAwarenessClock(homeAt%86400d):"no later routine commitment is scheduled today";
            }
            string name=ActionAwarenessBuildingName(selected.BuildingKey,buildingName);
            string when=selected.EnterAt<=local?"current until "+ActionAwarenessClock(selected.LeaveAt):
                "at "+ActionAwarenessClock(selected.EnterAt);
            return when+": "+selected.Purpose.ToString().ToLowerInvariant()+" at "+name;
        }

        private static string ActionAwarenessClock(double seconds)
        {
            int minute=Math.Max(0,Math.Min(1439,(int)Math.Floor(seconds/60d)));
            return (minute/60).ToString("00",CultureInfo.InvariantCulture)+":"+(minute%60).ToString("00",CultureInfo.InvariantCulture);
        }
    }
}
