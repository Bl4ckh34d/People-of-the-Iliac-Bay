using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        public Action<Exception> BeginEnemyResidentAttack(ResidentActor victim,DaggerfallEntityBehaviour source)
        {
            if(!Ready||!ReactionsEnabled||!victim||!source||source.Entity==null||interior||worldStore==null||FindActor(victim.Resident.Id)!=victim)return null;
            var guards=GetComponent<GuardPopulation>();if(!guards)return null;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var store=worldStore;var local=directory.transform.InverseTransformPoint(victim.transform.position-Vector3.up);
            var alarm=store.RecordViolence(population.SettlementId,victim.Resident.Id,now);
            if(!alarm.HasLocation)store.LocateViolence(alarm.Id,-1,local.x,local.y,local.z);
            // Capture knowledge before native damage can turn or remove a body.
            // CivilianEntity.Name can be empty; the mobile person owns its name.
            var attackingResident=source.GetComponent<ResidentActor>();
            string attackerName=attackingResident && attackingResident.Person?attackingResident.Person.NameNPC:source.Entity.Name;
            bool sourceVisible=source.gameObject.activeInHierarchy && !source.Entity.IsInvisible;
            bool identifiesAttacker=!string.IsNullOrWhiteSpace(attackerName) && sourceVisible &&
                SeesSceneEvent(victim,source.transform.position,20);
            if(string.IsNullOrWhiteSpace(attackerName))attackerName="an unidentified attacker";
            var witnesses=new System.Collections.Generic.List<ResidentActor>();
            foreach(var actor in actors.Values)
                if(sourceVisible&&actor&&actor!=victim&&actor!=attackingResident&&actor.Resident.IsAlive&&actor.gameObject.activeInHierarchy&&
                    SeesSceneEvent(actor,victim.transform.position,20)&&SeesSceneEvent(actor,source.transform.position,20))witnesses.Add(actor);
            ObserveExteriorStaticWitnesses(alarm,victim.transform.position,now,source);
            var body=victim.GetComponent<DaggerfallEntityBehaviour>();int healthBefore=body && body.Entity!=null?body.Entity.CurrentHealth:0;
            return error=>{
                if(error!=null||store!=worldStore||!victim)return;
                if(body && body.Entity!=null && body.Entity.CurrentHealth<healthBefore)
                {
                    string occurrence="attack:"+Guid.NewGuid().ToString("N");
                    NativeSceneRecollection.Remember(store,victim.Resident.Id,occurrence,NpcSceneEventKind.Assault,
                        identifiesAttacker?"I was attacked by "+attackerName+". I do not know why.":
                        "I was attacked, but I could not identify my attacker. I do not know why.",-3,victim.Resident.Id);
                    foreach(var witness in witnesses)if(witness)
                        NativeSceneRecollection.Remember(store,witness.Resident.Id,occurrence,NpcSceneEventKind.Assault,
                            "I saw "+attackerName+" attack "+victim.Person.NameNPC+". I do not know why.",subject:victim.Resident.Id);
                }
                if(victim.Resident.IsAlive)return;
                if(!source)return;
                var evidence=source.GetComponent<GuardAttackEvidence>();if(!evidence)evidence=source.gameObject.AddComponent<GuardAttackEvidence>();
                evidence.Until=now+3600;
                var sourceResident=source.GetComponent<ResidentActor>();
                if(sourceResident!=null)
                {
                    if(sourceResident.Resident==null || !sourceResident.Resident.IsAlive || sourceResident.Resident.IsQuestProtected ||
                        !(source.Entity is CivilianEntity))return;
                    var scene=DefenseScene.For(source);
                    if(scene==null)return;
                    evidence.ResidentAssault=true;evidence.SceneRoot=scene.Root;evidence.Victim=victim.GetComponent<DaggerfallEntityBehaviour>();
                }
                string key=guards.RegisterHostileThreat(source);if(key==null)return;
                store.ConfirmCorpse(alarm.Id,now,-1,local.x,local.y,local.z);
                alarm.PlayerAttributed=false;alarm.AccusationIssued=false;alarm.KillerThreatId=key;
                foreach(var actor in witnesses)
                {
                    if(!actor||!actor.Resident.IsAlive)continue;
                    var person=store.Witness(alarm.Id,actor.Resident.Id,now);if(person==null)continue;
                    var position=directory.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
                    person.OutdoorReport=new OutdoorBodyReport {SeenAt=now};
                    person.Reaction=CivilianReaction.Flee;person.HasPosition=true;person.BuildingKey=-1;person.X=position.x;person.Y=position.y;person.Z=position.z;
                }
                foreach(var person in alarm.People)if(person.DirectWitness)person.Reaction=CivilianReaction.Flee;
            };
        }
    }
    public sealed partial class GuardPopulation
    {
        private bool ReportOutdoorMurderWitness(CivilianIncident alarm,Vector3 from)
        {
            if(!OwnsExterior||alarm.Corpse==null||alarm.CrimeResolved)return false;
            var streets=GetComponent<StreetPopulation>();if(!streets)return false;
            bool reporter=false;
            foreach(var witness in alarm.People)
            {
                if(!witness.DirectWitness||witness.StaticWitness||witness.BuildingKey!=-1)continue;
                var actor=streets.FindActor(witness.ResidentId);
                if(!actor||!actor.Resident.IsAlive||(actor.transform.position-from).sqrMagnitude>4)continue;
                reporter=true;if(witness.StatementDelivered)continue;
                var witnessPosition=actor.transform.position;
                string guard;Vector3 point;
                if(!FindBodyReportGuard(witnessPosition,true,out guard,out point,alarm.Id)||(point-witnessPosition).sqrMagnitude>16)continue;
                worldStore.NoticeOutdoorCorpse(alarm.Id,witness.ResidentId,false,Now());
                if(DeliverBodyReport(alarm,guard,witnessPosition,witness.ResidentId))ReceiveMurderIdentity(alarm,witness.ResidentId);
            }
            return reporter;
        }
        public void ReceiveMurderIdentity(CivilianIncident alarm,string witnessId)
        {
            if(!OwnsExterior||alarm.Corpse==null||!alarm.Corpse.Cleared||!alarm.People.Exists(p=>p.ResidentId==witnessId&&p.DirectWitness&&p.StatementDelivered))return;
            var report=Roster.Reports.Find(r=>r.Id=="outdoor:"+alarm.Id);
            if(report!=null)foreach(var id in report.GuardIds)LearnCorpseSuspect(alarm,id,Now());
        }
        private void LearnCorpseSuspect(CivilianIncident alarm,string guardId,double now)
        {
            if(alarm.Corpse==null || !alarm.Corpse.Cleared)return;
            foreach(var receipt in alarm.Corpse.Inquiries)
                if(receipt.GuardId==guardId && receipt.ReceivedWitnessStatement &&
                    worldStore.TryLearnCorpseSuspect(alarm.Id,guardId,receipt.PersonId,now))return;
            foreach(var witness in alarm.People)
                if(witness.StatementDelivered && witness.OutdoorReport!=null && witness.OutdoorReport.DeliveredAt>=0 &&
                    worldStore.TryLearnCorpseSuspect(alarm.Id,guardId,witness.ResidentId,now))return;
        }
        private void ReconcileEnemyMurderResolution()
        {
            if(worldStore==null||!directory)return;
            foreach(var alarm in worldStore.OutdoorCorpses(directory.MapID))
            {
                if(alarm.KillerThreatId==null||alarm.CrimeResolved)continue;
                var record=worldStore.GetDefenseCombatant(alarm.KillerThreatId);
                if(record==null || record.Death==null || record.SettlementId!=alarm.SettlementId)continue;
                var known=Roster.Reports.Find(r=>r.Id=="outdoor:"+alarm.Id && r.CorpseSuspect!=null &&
                    r.CorpseSuspect.Kind==CorpseSuspectKind.HostileEntity && r.CorpseSuspect.Identity==record.Id);
                if(known==null)continue;
                // The dead actor leaves the live-threat registry. Its saved death
                // remains authoritative, but does not reveal a remote corpse.
                if(record.Death.Cleanup!=null && record.Death.Cleanup.Cleared) {alarm.CrimeResolved=true;continue;}
                Vector3 point;
                if(!NativeCorpsePoint(record,out point))
                {
                    RefreshNativeCorpseViews(worldStore.DefenseCorpsesFor(directory.MapID));
                    if(!NativeCorpsePoint(record,out point))continue;
                }
                foreach(var id in known.GuardIds)
                {
                    GuardActor actor;
                    if(actors.TryGetValue(id,out actor) && actor && actor.gameObject.activeInHierarchy &&
                        Roster.Guards.Exists(g=>g.Id==id && g.IsAlive && g.IncidentId==known.Id) &&
                        CivilianResponses.Sees(actor.transform.position,point+Vector3.up*0.3f,20))
                    {alarm.CrimeResolved=true;break;}
                }
            }
        }
    }
}
