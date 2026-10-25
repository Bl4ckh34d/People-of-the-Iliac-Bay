using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    // Structured semantic intent is advisory. Native acceptance must revalidate the copied target and current world state.
    public static class NpcActionIntent
    {
        public static bool TryBind(string output,IList<NpcActionOption> candidates,out NpcActionDecision decision)
        {
            decision=null;
            if(string.IsNullOrWhiteSpace(output) || output.Length>160 || candidates==null)return false;
            var fields=output.Trim().Split('|');
            int id,quantity,seconds;
            NpcActionKind kind;
            if(fields.Length!=5 || fields[0]!="DIRECT" || !Enum.TryParse(fields[1],false,out kind) ||
                !Enum.IsDefined(typeof(NpcActionKind),kind) || fields[1]!=kind.ToString() ||
                !int.TryParse(fields[2],NumberStyles.None,CultureInfo.InvariantCulture,out id) || id<1 ||
                !int.TryParse(fields[3],NumberStyles.None,CultureInfo.InvariantCulture,out quantity) || quantity<1 || quantity>1000000 ||
                !int.TryParse(fields[4],NumberStyles.None,CultureInfo.InvariantCulture,out seconds))return false;
            NpcActionOption selected=null;
            foreach(var candidate in candidates) if(candidate!=null && candidate.Id==id)
            {
                // IDs identify one native candidate; duplicate IDs cannot be disambiguated by a model-generated kind.
                if(selected!=null)return false;
                selected=candidate;
            }
            if(selected==null || selected.Kind!=kind || selected.Target==null || selected.MaximumQuantity<0 || selected.MaximumQuantity>1000000 ||
                !NpcPaidCompanionAction.ValidDecisionDuration(selected,seconds))return false;
            if(selected.MaximumQuantity==0)
            {
                if(quantity!=selected.Target.Quantity)return false;
            }
            else if((kind!=NpcActionKind.Transfer && kind!=NpcActionKind.Purchase) || quantity>selected.MaximumQuantity)return false;
            var target=NpcActionExecutor.CopyTarget(selected.Target);
            target.Quantity=quantity;
            try { NpcActionContract.ValidateTarget(target); }
            catch(ArgumentException) { return false; }
            decision=new NpcActionDecision {DurationSeconds=seconds,Option=new NpcActionOption {Id=selected.Id,Kind=selected.Kind,
                Label=selected.Label,Target=target,ItemSourceStep=selected.ItemSourceStep,
                PaidCompanion=selected.PaidCompanion,
                CompanionCast=selected.CompanionCast==null?null:NpcCompanionCastSpec.CopyValidated(selected.CompanionCast)}};
            return true;
        }
    }
}
