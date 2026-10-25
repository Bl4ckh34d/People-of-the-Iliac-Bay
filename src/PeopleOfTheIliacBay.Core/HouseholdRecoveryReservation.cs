using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum HouseholdVacancyOutcome { NewFamily=0, BankSale=1 }
    public enum HouseholdRecoveryStatus { Pending=0, Listed=1, Sold=2, Settled=3 }

    // Saved once per extinct generation. Neither loading nor later settings reroll it.
    [Serializable]
    public sealed class HouseholdRecoveryReservation
    {
        public uint WorldSeed;
        public int SettlementId,BuildingKey,Generation,DelayDays;
        public double ConfirmedLastDeathAt,RecoveryAt;
        public HouseholdRecoveryTier Tier;
        public HouseholdVacancyOutcome Outcome;
        public HouseholdRecoveryStatus Status;

        public static HouseholdRecoveryReservation Create(HouseholdRecoveryDecision decision,
            HouseholdVacancyOutcome outcome=HouseholdVacancyOutcome.NewFamily)
        {
            if(decision==null || !decision.IsEligible) throw new ArgumentException("Recovery requires eligible confirmed extinction.");
            return Copy(new HouseholdRecoveryReservation {WorldSeed=decision.WorldSeed,
                SettlementId=decision.SettlementId,BuildingKey=decision.BuildingKey,Generation=decision.Generation,
                DelayDays=decision.DelayDays,ConfirmedLastDeathAt=decision.ConfirmedLastDeathAt,
                RecoveryAt=decision.RecoveryAt,Tier=decision.Tier,Outcome=outcome});
        }

        public static HouseholdRecoveryReservation Copy(HouseholdRecoveryReservation value)
        {
            if(value==null || value.Outcome<HouseholdVacancyOutcome.NewFamily || value.Outcome>HouseholdVacancyOutcome.BankSale ||
                value.Status<HouseholdRecoveryStatus.Pending || value.Status>HouseholdRecoveryStatus.Settled ||
                value.Outcome==HouseholdVacancyOutcome.NewFamily && (value.Status==HouseholdRecoveryStatus.Listed || value.Status==HouseholdRecoveryStatus.Sold) ||
                value.Outcome==HouseholdVacancyOutcome.BankSale && value.Status==HouseholdRecoveryStatus.Settled)
                throw new ArgumentException("Invalid household recovery reservation.");
            var decision=HouseholdRecoveryPolicy.Evaluate(value.WorldSeed,value.SettlementId,value.BuildingKey,
                value.Generation,value.ConfirmedLastDeathAt,value.Tier,new HouseholdRecoveryEligibility {
                    AllPermanentResidentsConfirmedDead=true,PriorPermanentResidentCount=1,BedCount=1});
            if(value.DelayDays!=decision.DelayDays || value.RecoveryAt!=decision.RecoveryAt)
                throw new ArgumentException("Recovery reservation deadline does not match its recorded decision.");
            return new HouseholdRecoveryReservation {WorldSeed=value.WorldSeed,SettlementId=value.SettlementId,
                BuildingKey=value.BuildingKey,Generation=value.Generation,DelayDays=value.DelayDays,
                ConfirmedLastDeathAt=value.ConfirmedLastDeathAt,RecoveryAt=value.RecoveryAt,Tier=value.Tier,Outcome=value.Outcome,Status=value.Status};
        }

        public bool CanActivate(double now,HouseholdRecoveryEligibility facts,bool playerOutsideDwelling,bool corpsesCleared)
        {
            var saved=Copy(this);
            var decision=new HouseholdRecoveryDecision(true,HouseholdRecoveryBlockReason.None,saved.WorldSeed,
                saved.SettlementId,saved.BuildingKey,saved.Generation,saved.ConfirmedLastDeathAt,saved.Tier,saved.DelayDays,saved.RecoveryAt);
            return saved.Status==HouseholdRecoveryStatus.Pending && HouseholdRecoveryPolicy.CanActivate(decision,now,facts,playerOutsideDwelling,corpsesCleared);
        }
    }
}
