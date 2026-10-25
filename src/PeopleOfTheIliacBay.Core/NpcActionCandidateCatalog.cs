using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    // Bounded factual retrieval. The model selects the source/target category, never a keyword filter.
    public sealed class NpcActionCandidateCatalog
    {
        public const int MaximumCandidates=256, MaximumPerGroup=32;
        public const int MaximumCompactCandidates=16, MaximumCompactRenderedCharacters=2048;
        private readonly List<string> labels=new List<string>();
        private readonly List<List<NpcActionOption>> groups=new List<List<NpcActionOption>>();
        public int GroupCount { get { return groups.Count; } }

        public static bool TryCreateCompactForStep(IList<NpcActionOption> candidates,int stepIndex,
            NpcActionDecision previousStep,out IList<NpcActionOption> compact,out int renderedCharacters)
        {
            compact=null;renderedCharacters=0;
            if(stepIndex<0 || stepIndex>NpcTaskContract.MaximumSteps ||
                (stepIndex<=1 && previousStep!=null) ||
                (stepIndex>1 && (previousStep==null || previousStep.Option==null || previousStep.Option.Target==null)))
                return false;
            if(previousStep!=null)
            {
                if(!Enum.IsDefined(typeof(NpcActionKind),previousStep.Option.Kind))return false;
                try {NpcActionContract.ValidateTarget(previousStep.Option.Target);}
                catch(ArgumentException) {return false;}
            }
            return TryCreateCompact(candidates,out compact,out renderedCharacters);
        }

        public static bool TryCreateCompact(IList<NpcActionOption> candidates,out IList<NpcActionOption> compact,
            out int renderedCharacters)
        {
            compact=null;renderedCharacters=0;
            if(candidates==null)return false;
            var eligible=new List<NpcActionOption>();var ids=new HashSet<int>();
            foreach(var option in candidates)
            {
                // A malformed entry or a dependency is reason to retain the
                // category path, never silently omit part of the native set.
                if(option==null || option.Target==null)return false;
                if(option.Id<1 || !Enum.IsDefined(typeof(NpcActionKind),option.Kind) || option.MaximumQuantity<0 ||
                    option.MaximumQuantity>1000000 || option.ItemSourceStep!=0 || !ids.Add(option.Id))return false;
                try {NpcActionContract.ValidateTarget(option.Target);}
                catch(ArgumentException) {return false;}
                eligible.Add(option);
                if(eligible.Count>MaximumCompactCandidates)return false;
            }
            if(eligible.Count==0)return false;
            string rows=NpcActionChat.RenderSemanticCandidateRows(eligible);
            renderedCharacters=rows.Length;
            if(renderedCharacters>MaximumCompactRenderedCharacters)return false;
            var copy=new List<NpcActionOption>(eligible.Count);
            foreach(var option in eligible)copy.Add(Copy(option));
            compact=copy;
            return true;
        }

        public NpcActionCandidateCatalog(IList<NpcActionOption> candidates)
        {
            if(candidates==null)return;
            int count=0;
            // A dependency option is derived from an already selected earlier
            // step. Keep it inside the same global page budget even when native
            // scene candidates have filled the ordinary target page.
            for(int pass=0;pass<2 && count<MaximumCandidates;pass++)
                foreach(var option in candidates)
                {
                    if(option==null || option.Target==null || (option.ItemSourceStep>0)!=(pass==0))continue;
                    if(count++>=MaximumCandidates)break;
                    string label=Category(option);int index=labels.IndexOf(label);
                    if(index<0){index=labels.Count;labels.Add(label);groups.Add(new List<NpcActionOption>());}
                    if(groups[index].Count<MaximumPerGroup)groups[index].Add(Copy(option));
                }
        }
        private static NpcActionOption Copy(NpcActionOption option)
        {return new NpcActionOption {Id=option.Id,Kind=option.Kind,Label=option.Label,MaximumQuantity=option.MaximumQuantity,
            PaidCompanion=option.PaidCompanion,
            ItemSourceStep=option.ItemSourceStep,Target=NpcActionExecutor.CopyTarget(option.Target),
            CompanionCast=option.CompanionCast==null?null:NpcCompanionCastSpec.CopyValidated(option.CompanionCast)};}
        private static string Category(NpcActionOption option)
        {
            if(option.ItemSourceStep>0)return "Hand over an item produced by an earlier task step after that step succeeds";
            if(option.Kind==NpcActionKind.Transfer)
            {
                switch(option.Target.TransferDirection)
                {
                    case NpcActionTransferDirection.PlayerToNpc:return "Receive an item from the player's possessions";
                    case NpcActionTransferDirection.NpcToContainer:return "Store an item from the NPC's possessions in home storage";
                    case NpcActionTransferDirection.HomeContainerToNpc:return "Retrieve an item from home storage";
                    default:return option.Target.PersonId=="player"?"Give an item from the NPC's possessions to the player":"Give an item from the NPC's possessions to another person";
                }
            }
            if(option.Kind==NpcActionKind.MoveTo && option.Target.Kind==NpcActionTargetKind.Building)return "Walk to an actual local building entrance";
            if(option.Kind==NpcActionKind.Enter)return "Enter a local building through its actual door";
            if(option.Kind==NpcActionKind.Exit)return "Leave the current building through its door to the outdoors";
            return option.Kind.ToString();
        }
        public IList<NpcActionOption> Select(string output)
        {
            int id;if(output==null || !output.StartsWith("GROUP|",StringComparison.Ordinal) ||
                !int.TryParse(output.Substring(6),NumberStyles.None,CultureInfo.InvariantCulture,out id) || id<1 || id>groups.Count)return null;
            var result=new List<NpcActionOption>();foreach(var option in groups[id-1])result.Add(Copy(option));return result;
        }
        public string Grammar()
        {
            var text=new StringBuilder("root ::= \"NONE\"");
            for(int i=0;i<groups.Count;i++)text.Append(" | \"GROUP|").Append(i+1).Append('"');
            return text.Append('\n').ToString();
        }
        public string Prompt(string currentText)
        {return Prompt(currentText,null,null);}
        public string Prompt(string currentText,NpcActionDecision previousStep)
        {return Prompt(currentText,previousStep,null);}
        public string Prompt(string currentText,NpcActionDecision previousStep,string originalTask)
        {
            var text=new StringBuilder("Select the factual capability category needed by the CURRENT player request to the NPC. Polite questions and indirect requests count. Select a category by the requested effect, even when no command verb appears. Use attributed conversation history to resolve references. This is retrieval, not acceptance or an effect. Output GROUP|id or NONE if no single category fits or the intended source/recipient is ambiguous. A later decision chooses the exact native target and whether the NPC agrees. Do not infer a current request from quoted, hypothetical, reported or negated speech. Only listed categories exist. Data follows.\nAvailable native categories:\n");
            if(groups.Exists(group=>group.Exists(option=>option.Kind==NpcActionKind.Enter || option.Kind==NpcActionKind.Exit)))
                text.Append("Doorway distinction: walking to a building entrance stays outside. Enter/go inside requests need the Enter category, not walking to an entrance. Exit/go outside requests need the Exit category. Never substitute MoveTo or moving near the player for crossing a doorway.\n");
            for(int i=0;i<groups.Count;i++)
            {
                text.Append(i+1).Append(": ").Append(labels[i]).Append("; ").Append(groups[i].Count).Append(" verified target choices");
                if(i==0 && previousStep!=null && previousStep.Option!=null && labels[i].StartsWith("Hand over an item produced by an earlier task step",StringComparison.Ordinal))
                    text.Append("; this dependency uses the exact item from the previous selected step: ").Append(previousStep.Option.Label);
                text.Append('\n');
            }
            if(previousStep!=null && previousStep.Option!=null && previousStep.Option.Target!=null)
            {
                var option=previousStep.Option;
                text.Append("Previous selected step in this proposed ordered task (not yet accepted or executed): ")
                    .Append(option.Kind).Append(" — ").Append(option.Label)
                    .Append("; chosen quantity=").Append(option.Target.Quantity.ToString(CultureInfo.InvariantCulture))
                    .Append(". Use this factual step to resolve references to an item from that step. A later handover can use that exact item only after this step succeeds; do not claim it is already owned or delivered. Other listed item categories refer to their stated sources.\n");
                if(groups.Count>0 && labels[0].StartsWith("Hand over an item produced by an earlier task step",StringComparison.Ordinal))
                    text.Append("The listed handover category is a later step in this ordered request, not a claim that its item is already in inventory. The current span can be a short clause from the larger original request; read it together with the complete ordered request to resolve omitted subjects and item pronouns, while classifying only this span. Pronouns such as it or them may refer to the unique item named in the previous selected step; the typed dependency is already bound to that exact item and quantity, so the current span need not repeat the item's name. When the current span requests that later handover, classify it to the listed conditional handover category even though the item will exist only if the previous step succeeds. Do not return NONE solely because the item is not currently owned or the span uses a pronoun. This is retrieval only, not willingness, agreement, acceptance, or execution; the task binder must still use that exact produced item. Keep returning NONE for quoted, reported, hypothetical, negated, ambiguous, or absent current requests.\n");
            }
            if(!string.IsNullOrWhiteSpace(originalTask))
                text.Append("Complete ordered task (reference data only; choose for the current span below):\n")
                    .Append(originalTask).Append('\n');
            return text.Append("Current player message:\n").Append(currentText).Append("\nCategory:").ToString();
        }
    }
}

