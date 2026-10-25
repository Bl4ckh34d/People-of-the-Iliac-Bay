using System;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class NpcVerbalCharge
    {
        public string PersonId, ReceiptId;
        public int SettlementId;
        public double IssuedAt;
    }

    public static class NpcVerbalJusticePolicy
    {
        public const int FineGold = 40;
        public static int DismissalChance(int skill,int personality,int legalReputation)
        {
            // The ordinary native court formula, with a bonus for an uncorroborated verbal accusation.
            return Math.Max(35,Math.Min(95,legalReputation+(skill+personality)/2+45));
        }
    }

    public sealed partial class WorldStateStore
    {
        public bool TryBeginNpcVerbalCharge(string personId,string receiptId,int settlementId,double now)
        {
            if(settlementId<0 || double.IsNaN(now) || double.IsInfinity(now) || now<0)return false;
            var boundary=ReadNpcConversationBoundary(personId,now);
            if(boundary==null || boundary.ReceiptId!=receiptId ||
                boundary.SpeechKind!=NpcExperienceSpeechActKind.DirectThreat ||
                boundary.Action!=ConversationSpeechBoundaryAction.EndAndReport ||
                boundary.ReportStatus!=NpcConversationBoundaryReportStatus.Delivered)return false;
            state.NpcVerbalCharge=new NpcVerbalCharge {PersonId=personId,ReceiptId=receiptId,
                SettlementId=settlementId,IssuedAt=now};
            return true;
        }

        public NpcVerbalCharge ReadNpcVerbalCharge() {return CopyVerbalCharge(state.NpcVerbalCharge);}
        public void ClearNpcVerbalCharge() {state.NpcVerbalCharge=null;}

        private static NpcVerbalCharge CopyVerbalCharge(NpcVerbalCharge value)
        {
            return value==null?null:new NpcVerbalCharge {PersonId=value.PersonId,ReceiptId=value.ReceiptId,
                SettlementId=value.SettlementId,IssuedAt=value.IssuedAt};
        }

        private static NpcVerbalCharge CopyNpcVerbalCharge(WorldState source,WorldState copy)
        {
            if(source.NpcVerbalCharge==null)return null;
            var value=source.NpcVerbalCharge;
            var attitude=copy.ConversationAttitudes.Find(row=>row.PersonId==value.PersonId);
            var boundary=attitude==null?null:attitude.Boundary;
            Require(value.SettlementId>=0 && !double.IsNaN(value.IssuedAt) && !double.IsInfinity(value.IssuedAt) &&
                boundary!=null && value.IssuedAt>=boundary.ObservedAt && boundary.ReceiptId==value.ReceiptId &&
                boundary.SpeechKind==NpcExperienceSpeechActKind.DirectThreat &&
                boundary.Action==ConversationSpeechBoundaryAction.EndAndReport &&
                boundary.ReportStatus==NpcConversationBoundaryReportStatus.Delivered,
                "Verbal charge lacks its delivered direct-threat receipt.");
            return CopyVerbalCharge(value);
        }
    }
}
