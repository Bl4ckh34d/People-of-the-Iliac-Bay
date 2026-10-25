using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    // Describes observed outcomes, never promises or invented NPC speech.
    public static class NpcActionFeedback
    {
        public static string Describe(NpcActionJob job,string actorName,string itemName,string recipientName)
        {
            if(job==null || string.IsNullOrWhiteSpace(actorName)) return null;
            if(job.Phase==NpcActionPhase.Failed) return actorName+" could not finish the agreed action.";
            if(job.Phase==NpcActionPhase.Cancelled) return actorName+" stops the agreed action.";
            if(job.Phase!=NpcActionPhase.Completed || string.IsNullOrWhiteSpace(job.CompletedEffectId)) return null;
            switch(job.Kind)
            {
                case NpcActionKind.UseHealingItem:
                    return job.TreatmentReceipt!=null && job.TreatmentReceipt.Kind==NpcTreatmentKind.HealingItem &&
                        job.TreatmentReceipt.HealthAfter>job.TreatmentReceipt.HealthBefore?
                        actorName+" uses a healing potion and recovers some health.":null;
                case NpcActionKind.RestToHeal:
                    return job.TreatmentReceipt!=null && job.TreatmentReceipt.Kind==NpcTreatmentKind.Rest &&
                        job.TreatmentReceipt.HealthAfter>job.TreatmentReceipt.HealthBefore?
                        actorName+" finishes resting and recovers some health.":null;
                case NpcActionKind.CastSpell:
                    return job.MagicHealing!=null && job.TreatmentReceipt!=null &&
                        job.TreatmentReceipt.Kind==NpcTreatmentKind.MagicSelfCast &&
                        job.TreatmentReceipt.HealthAfter>job.TreatmentReceipt.HealthBefore?
                        actorName+" casts a healing spell and recovers some health.":null;
                case NpcActionKind.Transfer:
                    if(job.Target==null || job.Target.Quantity<1 || string.IsNullOrWhiteSpace(itemName)) return actorName+" completes the item handover.";
                    if(job.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc)
                        return actorName+" accepts "+job.Target.Quantity.ToString(CultureInfo.InvariantCulture)+" x "+itemName+" from you.";
                    if(job.Target.TransferDirection==NpcActionTransferDirection.NpcToContainer)
                        return actorName+" stores "+job.Target.Quantity.ToString(CultureInfo.InvariantCulture)+" x "+itemName+
                            " in home container "+job.Target.ContainerId+".";
                    if(job.Target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc)
                        return actorName+" retrieves "+job.Target.Quantity.ToString(CultureInfo.InvariantCulture)+" x "+itemName+
                            " from household storage at home.";
                    string recipient=job.Target.PersonId=="player"?"you":recipientName;
                    if(string.IsNullOrWhiteSpace(recipient)) return actorName+" completes the item handover.";
                    return actorName+" hands "+recipient+" "+job.Target.Quantity.ToString(CultureInfo.InvariantCulture)+" x "+itemName+".";
                case NpcActionKind.MoveTo:return actorName+" reaches the agreed destination.";
                case NpcActionKind.Interact:return actorName+" reaches the person you indicated.";
                case NpcActionKind.ReportMurder:return actorName+" passes your anonymous murder report to a guard.";
                case NpcActionKind.Wait:return actorName+" finishes waiting.";
                case NpcActionKind.Follow:return actorName+" finishes accompanying you.";
                case NpcActionKind.TakeSeat:return actorName+" finishes the agreed rest.";
                case NpcActionKind.ChangeSchedule:return actorName+" leaves to head home.";
                case NpcActionKind.Purchase:return actorName+" completes the agreed service.";
                case NpcActionKind.Lead:return actorName+" reaches the agreed destination with the player nearby.";
                case NpcActionKind.Enter:
                    return job.BuildingTransitionReceipt==null?null:actorName+" enters the agreed building.";
                case NpcActionKind.Exit:
                    return job.BuildingTransitionReceipt==null?null:actorName+" exits the agreed building.";
                default:return null;
            }
        }
    }
}
