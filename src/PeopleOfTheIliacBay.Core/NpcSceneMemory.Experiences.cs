using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        private System.Collections.Generic.List<NpcSceneMemory> PrepareAppraisedSceneMemory(NpcExperienceEvent evidence,int moodDelta)
        {
            if(evidence.Origin!=NpcExperienceEventOrigin.NativeOutcome)return null;
            int index=state.SceneMemories.FindIndex(x=>x.ObserverId==evidence.ObserverId && x.EventId==evidence.OutcomeId);
            if(index<0)return null;
            var copy=NpcSceneMemory.Copy(state.SceneMemories[index]);copy.Valence=Math.Max(-5,Math.Min(5,moodDelta));
            var result=new System.Collections.Generic.List<NpcSceneMemory>(state.SceneMemories);result[index]=copy;return result;
        }
        // Only called after the existing native-source admission has succeeded.
        // The appraisal inbox remains the sole owner of mood/relationship effects.
        private void RememberVerifiedExperience(NpcExperienceEvent evidence)
        {
            if(evidence==null || evidence.Origin!=NpcExperienceEventOrigin.NativeOutcome || !evidence.EventKind.HasValue)return;
            var detail=evidence.NativeImpactDetails;
            NpcSceneEventKind kind;string text;int feeling;
            switch(evidence.EventKind.Value)
            {
                case NpcExperienceEventKind.Injury:
                    kind=NpcSceneEventKind.Injury;text="I was injured";feeling=-3;
                    if(detail!=null && detail.MaximumHealth>0)feeling=-(int)Math.Min(5,Math.Max(1,Math.Ceiling(5d*detail.TotalDamage/detail.MaximumHealth)));
                    break;
                case NpcExperienceEventKind.Healing:kind=NpcSceneEventKind.Healing;text="I was healed";feeling=3;break;
                case NpcExperienceEventKind.StatusOnset:kind=NpcSceneEventKind.Illness;text="I became poisoned";feeling=-3;break;
                case NpcExperienceEventKind.MoneyReceived:
                    kind=NpcSceneEventKind.Gift;text="I received "+(detail==null?"money":detail.Quantity+" gold");feeling=2;break;
                case NpcExperienceEventKind.GiftReceived:kind=NpcSceneEventKind.Gift;text="I received a gift";feeling=2;break;
                case NpcExperienceEventKind.ItemReceived:
                    kind=NpcSceneEventKind.Gift;text="I received "+(detail==null?"an item":detail.Quantity+" item(s)");feeling=1;break;
                case NpcExperienceEventKind.CombatHelp:kind=NpcSceneEventKind.Help;text="I received help in combat";feeling=4;break;
                case NpcExperienceEventKind.AppointmentAttended:kind=NpcSceneEventKind.Appointment;text="Our agreed meeting was attended";feeling=1;break;
                case NpcExperienceEventKind.AppointmentNoShow:kind=NpcSceneEventKind.Appointment;text="The other person missed our agreed meeting";feeling=-2;break;
                case NpcExperienceEventKind.ObligationFulfilled:kind=NpcSceneEventKind.Promise;text="An obligation to me was fulfilled";feeling=2;break;
                case NpcExperienceEventKind.ObligationBreached:kind=NpcSceneEventKind.Promise;text="An obligation to me was broken";feeling=-3;break;
                default:return;
            }
            string responsible=evidence.ResponsibleActorAttribution==NpcResponsibleActorAttribution.Known?evidence.ResponsibleActorId:null;
            if(responsible==NpcDirectedRelationship.PlayerTargetId)text+="; the player was responsible";
            else if(responsible!=null)
            {
                var resident=ResolveResident(responsible);
                text+="; "+(resident==null?"a known person":resident.FullName)+" was responsible";
            }
            else text+="; I do not know who was responsible";
            text+=". I do not know the motive from this event alone.";
            // The admission source may lack a native location. Do not invent one.
            RememberSceneEvent(evidence.ObserverId,evidence.OutcomeId,kind,evidence.ObservedGameTime,0,-1,text,
                feeling,evidence.SubjectPersonId,responsible);
        }
    }
}
