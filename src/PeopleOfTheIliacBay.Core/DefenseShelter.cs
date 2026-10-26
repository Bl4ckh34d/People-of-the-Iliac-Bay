using System;

namespace PeopleOfTheIliacBay.Core
{
    // The resident's physical state is private to the resident adapter. Guard
    // knowledge remains separately gated by each guard's witnessed doorway.
    [Serializable]
    public sealed class DefenseShelter
    {
        public string DoorId;
        public double EnteredAt,Until;
        public bool LandingObserved;
        public AlarmPerson Reaction;

        public string ActivityContext(int observedBuilding,string buildingName,bool visiblePursuer)
        {
            if(!LandingObserved || Reaction==null || Reaction.BuildingKey!=observedBuilding || string.IsNullOrWhiteSpace(buildingName))return null;
            return "I took shelter in "+buildingName+" after fleeing a guard. My ordinary routine is interrupted. "+
                (visiblePursuer ? "A pursuing guard is visible here." : "I do not currently see a pursuing guard.");
        }
    }
    public sealed partial class WorldStateStore
    {
        public void RecordDefenseShelter(string residentId,int settlementId,string doorId,AlarmPerson reaction,double now)
        {
            var resident=ResolveResident(settlementId,residentId);
            Require(resident!=null && resident.IsAlive && !resident.IsQuestProtected && reaction!=null &&
                reaction.ResidentId==residentId && reaction.ReachedRefuge && reaction.BuildingKey>=0,
                "Defense shelter requires the living resident's actual arrival.");
            var record=GetDefenseCombatant(residentId);
            if(record==null)record=RegisterDefenseCombatant(residentId,settlementId,CombatOrigin.Civilian,now);
            Require(record.SettlementId==settlementId && record.Death==null && now>=record.ObservedAt,
                "Defense shelter changed resident or settlement.");
            var candidate=new DefenseShelter {DoorId=doorId,EnteredAt=now,Until=now+1800,Reaction=reaction};
            record.Shelter=CopyDefenseShelter(candidate,record);
        }

        public DefenseShelter ResidentDefenseShelter(string residentId,int settlementId)
        {
            var record=GetDefenseCombatant(residentId);var resident=ResolveResident(settlementId,residentId);
            return record!=null && record.SettlementId==settlementId && record.Death==null && resident!=null &&
                resident.IsAlive ? record.Shelter : null;
        }

        public bool ObserveDefenseShelterLanding(string residentId,int settlementId,int buildingKey,string doorId,
            double x,double y,double z,double now)
        {
            var shelter=ResidentDefenseShelter(residentId,settlementId);
            if(shelter==null || shelter.LandingObserved || shelter.DoorId!=doorId || shelter.Reaction.BuildingKey!=buildingKey ||
                !DefenseTime(now) || now<shelter.EnteredAt || !DefenseCoordinate(x) || !DefenseCoordinate(y) || !DefenseCoordinate(z))return false;
            shelter.Reaction.X=x;shelter.Reaction.Y=y;shelter.Reaction.Z=z;
            shelter.Reaction.ReachedRefuge=false;shelter.LandingObserved=true;return true;
        }

        public void ReleaseDefenseShelter(string residentId,double now)
        {
            var record=GetDefenseCombatant(residentId);
            if(record!=null && record.Shelter!=null && DefenseTime(now) && now>=record.Shelter.Until)record.Shelter=null;
        }

        private static DefenseShelter CopyDefenseShelter(DefenseShelter source,DefenseCombatant record)
        {
            if(source==null)return null;
            var person=source.Reaction;
            Require(record.Death==null && !string.IsNullOrWhiteSpace(source.DoorId) && source.DoorId.Length<=256 &&
                DefenseTime(source.EnteredAt) && source.EnteredAt>=record.ObservedAt && DefenseTime(source.Until) &&
                source.Until==source.EnteredAt+1800 && person!=null && person.ResidentId==record.Id &&
                person.Reaction==CivilianReaction.Flee && person.HasPosition && person.BuildingKey>=0 &&
                person.ReachedRefuge==!source.LandingObserved && DefenseCoordinate(person.X) &&
                DefenseCoordinate(person.Y) && DefenseCoordinate(person.Z),"Invalid resident defense shelter.");
            return new DefenseShelter {DoorId=source.DoorId,EnteredAt=source.EnteredAt,Until=source.Until,
                LandingObserved=source.LandingObserved,Reaction=new AlarmPerson {ResidentId=person.ResidentId,
                    Reaction=CivilianReaction.Flee,DirectWitness=true,HasPosition=true,ReachedRefuge=person.ReachedRefuge,
                    BuildingKey=person.BuildingKey,X=person.X,Y=person.Y,Z=person.Z}};
        }
    }
}
