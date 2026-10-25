using System;

namespace PeopleOfTheIliacBay.Core
{
    // Transient request facts, not a second persisted group or movement owner.
    public sealed class HouseholdOutingConversation
    {
        public string SpeakerId {get;}
        public string PartnerId {get;}
        public string PartnerName {get;}
        public string SourceId {get;}
        public string SourceFingerprint {get;}
        public int SourceSequence {get;}
        public int DestinationBuildingKey {get;}
        public int DestinationSettlementId {get;}
        public HouseholdJointPhase Phase {get;}
        public double CapturedAt {get;}
        public bool Interrupted {get;}
        public string DestinationName {get;}
        public bool PartnerObservedNearby {get;}
        public bool ObservedInsideDestination {get;}

        internal HouseholdOutingConversation(string speaker,Resident partner,HouseholdJointProgress progress,
            int destination,double now,bool interrupted)
        {
            SpeakerId=speaker;PartnerId=partner.Id;PartnerName=Clip(partner.FullName,80);
            SourceId=progress.HouseholdDayId;SourceFingerprint=progress.SourceFingerprint;SourceSequence=progress.Sequence;
            DestinationBuildingKey=destination;DestinationSettlementId=progress.SettlementId;
            Phase=progress.Phase;CapturedAt=now;Interrupted=interrupted;
        }
        private HouseholdOutingConversation(HouseholdOutingConversation source,string destinationName,bool nearby,bool inside)
        {
            SpeakerId=source.SpeakerId;PartnerId=source.PartnerId;PartnerName=source.PartnerName;
            SourceId=source.SourceId;SourceFingerprint=source.SourceFingerprint;SourceSequence=source.SourceSequence;
            DestinationBuildingKey=source.DestinationBuildingKey;DestinationSettlementId=source.DestinationSettlementId;
            Phase=source.Phase;CapturedAt=source.CapturedAt;
            Interrupted=source.Interrupted;DestinationName=Clip(destinationName,100);PartnerObservedNearby=nearby;
            ObservedInsideDestination=inside && source.Phase==HouseholdJointPhase.Visiting;
        }
        // Native capture supplies only names and presence observed for this request.
        public HouseholdOutingConversation WithObservation(string destinationName,bool partnerNearby,bool bothInsideDestination=false)
        {return new HouseholdOutingConversation(this,destinationName,partnerNearby,bothInsideDestination);}
        public bool HasSameOwnership(HouseholdOutingConversation other)
        {
            return other!=null && SpeakerId==other.SpeakerId && PartnerId==other.PartnerId && PartnerName==other.PartnerName &&
                SourceId==other.SourceId && SourceFingerprint==other.SourceFingerprint && SourceSequence==other.SourceSequence &&
                DestinationBuildingKey==other.DestinationBuildingKey && DestinationSettlementId==other.DestinationSettlementId &&
                Phase==other.Phase && Interrupted==other.Interrupted;
        }
        public string ToContext()
        {
            string place=string.IsNullOrEmpty(DestinationName)?
                (Phase==HouseholdJointPhase.Returning?"your shared home":"the planned Inn"):DestinationName;
            string phase=Phase.ToString().ToLowerInvariant();
            if(Phase==HouseholdJointPhase.AtInn)phase+=" (outside the entrance; entry is not confirmed)";
            if(Phase==HouseholdJointPhase.Visiting)phase+=ObservedInsideDestination?
                " (both NPCs observed inside the destination)":" (shared visit active; physical entry is not confirmed)";
            return "Shared NPC outing: with "+(string.IsNullOrEmpty(PartnerName)?"your companion":PartnerName)+
                "; the player is not a group member. "+(Interrupted?"The shared outing is interrupted; last confirmed phase: ":"Verified phase: ")+
                phase+"; destination: "+place+". "+
                (PartnerObservedNearby?"Partner observed nearby. ":"Partner's present position is unknown. ")+
                "Account for your companion and joint plans when answering or accepting requests. Group membership is not evidence of purchases, seating or your companion's words.";
        }
        private static string Clip(string value,int length)
        {
            if(string.IsNullOrWhiteSpace(value))return null;
            var chars=value.Trim().ToCharArray();for(int i=0;i<chars.Length;i++)if(char.IsControl(chars[i]))chars[i]=' ';
            var text=new string(chars);return text.Length<=length?text:text.Substring(0,length-1)+"…";
        }
    }

    public sealed partial class WorldStateStore
    {
        public HouseholdOutingConversation ReadHouseholdOutingConversation(string speaker,double now)
        {
            if(string.IsNullOrWhiteSpace(speaker) || !HouseholdJointProgress.Time(now))return null;
            var progress=state.HouseholdJointProgress.Find(p=>p.Active && p.Phase!=HouseholdJointPhase.Waiting &&
                (p.FirstResidentId==speaker || p.SecondResidentId==speaker));
            if(progress==null || now<progress.ObservedAt || SimulationOwnsHousehold(progress.SettlementId,progress.HomeKey))return null;
            try
            {
                var plan=ResolveJointSource(state,progress);
                var home=state.Settlements.Find(t=>t.SettlementId==progress.SettlementId).Households.Find(h=>h.BuildingKey==progress.HomeKey);
                string other=progress.FirstResidentId==speaker?progress.SecondResidentId:progress.FirstResidentId;
                var partner=home.Residents.Find(p=>p.Id==other);
                var policy=Core.HouseholdJointProgress.Copy(progress);policy.ObservedAt=now;
                bool interrupted=JointDuties(state,plan,policy,null)!=JointDutyState.Compatible;
                int destination=progress.Phase==HouseholdJointPhase.Returning?progress.HomeKey:plan.JointInn.InnKey;
                return new HouseholdOutingConversation(speaker,partner,progress,destination,now,interrupted);
            }
            catch(ArgumentException){return null;}
        }
    }
}
