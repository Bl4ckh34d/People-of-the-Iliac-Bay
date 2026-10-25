using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcActionOfferSemanticTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Unrelated offer conversation and new request have separate nonexecuting markers", () => {
                var option=new NpcActionOption {Id=1,Kind=NpcActionKind.Wait,Label="wait here",
                    Target=new NpcActionTarget {Quantity=1}};
                var grammar=NpcActionChat.GrammarOfferResponse(new[]{option},null);
                Check.True(grammar.Contains("\"NEW_REQUEST\"") && grammar.Contains("\"NONE\""));
                Check.True(!grammar.Contains("\"CANCEL\""),"Pending offers alone cannot authorize cancellation.");
                Check.True(NpcActionChat.ParseSemantic("NEW_REQUEST",new[]{option})==null);
                Check.True(NpcActionChat.ParseSemantic("NONE",new[]{option})==null);
                var prompt=NpcActionChat.PromptPendingOfferResponse("Are you on your way home?","outdoors",option,60);
                Check.True(prompt.Contains("output NEW_REQUEST so that request can be considered separately"));
                Check.True(prompt.Contains("unrelated conversation: NONE"));
                Check.True(prompt.EndsWith("\nDecision:",StringComparison.Ordinal));
            }));
            cases.Add(("Semantic offer candidate is detached and only live for its owner session", CandidateIsDetachedAndScoped));
            cases.Add(("Paid companion option survives semantic copies and cannot become ordinary Follow",()=>{
                var paid=new NpcActionOption {Id=8,Kind=NpcActionKind.Follow,PaidCompanion=true,
                    Label="paid Follow, 120 gold for 24 game hours",
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",Quantity=1}};
                var parsed=NpcActionChat.ParseSemantic("DIRECT|Follow|8|1|86400",new[]{paid});
                Check.True(parsed!=null && parsed.Option.PaidCompanion);
                Check.Equal(86400,parsed.DurationSeconds);
                Check.True(NpcActionChat.ParseSemantic("DIRECT|Follow|8|1|300",new[]{paid})==null,
                    "Paid service has fixed one-day terms, not a short ordinary Follow duration.");
                var ordinary=new NpcActionOption {Id=7,Kind=NpcActionKind.Follow,
                    Label="follow locally",Target=NpcActionExecutor.CopyTarget(paid.Target)};
                var prompt=NpcActionChat.PromptSemantic("Can I hire you for a day?","outdoors",
                    new[]{ordinary,paid},false);
                Check.True(NpcActionChat.ParseSemantic("DIRECT|Follow|7|1|86400",new[]{ordinary,paid})==null,
                    "One-day paid duration must not extend ordinary Follow.");
                var grammar=NpcActionChat.GrammarSemantic(new[]{ordinary,paid});
                Check.True(grammar.Contains("\"DIRECT|Follow|8|1|86400\""),grammar);
                Check.True(!grammar.Contains("\"DIRECT|Follow|8|1|\" duration"),grammar);
                Check.True(NpcActionChat.ParseProposal("PROPOSE|8|86400",new[]{paid})!=null);
                Check.True(NpcActionChat.ParseSemantic("DIRECT|Follow|8|1|3600",new[]{paid})==null);
                var move=new NpcActionOption {Id=1,Kind=NpcActionKind.MoveTo,Target=NpcActionExecutor.CopyTarget(paid.Target)};
                Check.True(NpcActionSequenceChat.ParseSemantic(new[]{"come here","hire you for 24 hours"},
                    new IList<NpcActionOption>[] {new[]{move},new[]{paid}},
                    new[]{"DIRECT|MoveTo|1|1|300","DIRECT|Follow|8|1|86400"})==null,
                    "Paid hire requires its own native consent and cannot become a composed task.");
                Check.True(prompt.Contains("paid companion") && prompt.Contains("ordinary Follow"),
                    "Classifier needs explicit typed hire separation from free Follow.");
                IList<NpcActionOption> compact;int rendered;
                Check.True(NpcActionCandidateCatalog.TryCreateCompact(new[]{paid},out compact,out rendered));
                Check.True(compact[0].PaidCompanion);
                var offer=new NpcActionOffer("resident-a",4,100,parsed);
                NpcActionOption candidate;int duration;
                Check.True(offer.TryGetSemanticCandidate("resident-a",4,101,out candidate,out duration));
                Check.True(candidate.PaidCompanion);
                candidate.PaidCompanion=false;
                var forged=new NpcActionDecision {DurationSeconds=duration,Option=candidate};
                NpcActionDecision accepted;
                Check.True(!offer.TryAcceptSemantic("resident-a",4,102,forged,out accepted));
                Check.True(offer.TryGetSemanticCandidate("resident-a",4,103,out candidate,out duration));
                Check.True(candidate.PaidCompanion);
            }));
            cases.Add(("Semantic offer acceptance consumes the exact offered action once", AcceptanceIsExactAndOnce));
            cases.Add(("Closing a conversation invalidates its pending offer", ExplicitInvalidationClosesOffer));
            cases.Add(("Semantic offer rejects changed target duration and wrong owner but does not expire inside its bound session", RejectsChangesAndWrongOwner));
            cases.Add(("Semantic action prompt supports a typed cancel status", PromptSupportsCancel));
            cases.Add(("Pending-offer cancellation follows captured commitment identity and revision", OfferCancellationBindsCapturedCommitment));
            cases.Add(("Semantic action grammar fixes native quantities and legal duration bounds", GrammarMatchesNativeQuantityAndDurationContracts));
            cases.Add(("Guidance wording requests Lead but directions and NPC-only movement do not", GuidanceWordingBindsLead));
            cases.Add(("Spontaneous travel offers require a trusted familiar relationship", SpontaneousTravelNeedsRelationship));
        }

        private static NpcActionDecision Decision()
        {
            return new NpcActionDecision {DurationSeconds=45,Option=new NpcActionOption {Id=3,Kind=NpcActionKind.MoveTo,
                Label="walk to the exterior entrance",Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",Quantity=1}}};
        }

        private static NpcActionDecision Select(NpcActionOption candidate,int duration=45)
        {
            return new NpcActionDecision {DurationSeconds=duration,Option=new NpcActionOption {Id=candidate.Id,Kind=candidate.Kind,
                Label=candidate.Label,Target=NpcActionExecutor.CopyTarget(candidate.Target)}};
        }

        private static void CandidateIsDetachedAndScoped()
        {
            var offer=new NpcActionOffer("resident-a",4,100,Decision());
            NpcActionOption candidate;int duration;
            Check.True(offer.TryGetSemanticCandidate("resident-a",4,101,out candidate,out duration));
            Check.Equal(45,duration);candidate.Target.PersonId="somebody-else";
            Check.True(offer.TryGetSemanticCandidate("resident-a",4,102,out candidate,out duration));
            Check.Equal("player",candidate.Target.PersonId);
            Check.True(!offer.TryGetSemanticCandidate("resident-b",4,102,out candidate,out duration));
            Check.True(!offer.TryGetSemanticCandidate("resident-a",5,102,out candidate,out duration));
            Check.True(offer.TryGetSemanticCandidate("resident-a",4,221,out candidate,out duration),
                "A pending offer must not expire while its exact NPC conversation session remains bound.");
        }

        private static void AcceptanceIsExactAndOnce()
        {
            var offer=new NpcActionOffer("resident-a",4,100,Decision());
            NpcActionOption option;int duration;NpcActionDecision accepted;
            Check.True(offer.TryGetSemanticCandidate("resident-a",4,101,out option,out duration));
            Check.True(offer.TryAcceptSemantic("resident-a",4,102,Select(option,duration),out accepted));
            Check.Equal("player",accepted.Option.Target.PersonId);
            Check.True(!offer.TryAcceptSemantic("resident-a",4,103,Select(option,duration),out accepted));
        }

        private static void ExplicitInvalidationClosesOffer()
        {
            var offer=new NpcActionOffer("resident-a",4,100,Decision());
            NpcActionOption option;int duration;NpcActionDecision accepted;
            Check.True(offer.TryGetSemanticCandidate("resident-a",4,101,out option,out duration));
            offer.Invalidate();
            Check.True(!offer.TryGetSemanticCandidate("resident-a",4,1000000,out option,out duration));
            Check.True(!offer.TryAcceptSemantic("resident-a",4,1000000,Select(Decision().Option),out accepted));
        }

        private static void RejectsChangesAndWrongOwner()
        {
            var offer=new NpcActionOffer("resident-a",4,100,Decision());
            NpcActionOption option;int duration;NpcActionDecision accepted;
            Check.True(offer.TryGetSemanticCandidate("resident-a",4,101,out option,out duration));
            var changed=Select(option,duration);changed.Option.Target.PersonId="another-target";
            Check.True(!offer.TryAcceptSemantic("resident-a",4,102,changed,out accepted));
            Check.True(!offer.TryAcceptSemantic("resident-a",4,102,Select(option,duration+1),out accepted));
            Check.True(!offer.TryAcceptSemantic("resident-b",4,102,Select(option,duration),out accepted));
            Check.True(!offer.TryAcceptSemantic("resident-a",5,102,Select(option,duration),out accepted));
            Check.True(!offer.TryAcceptSemantic("resident-a",4,99,Select(option,duration),out accepted),
                "A clock earlier than offer creation remains invalid.");
            Check.True(offer.TryAcceptSemantic("resident-a",4,1000000,Select(option,duration),out accepted),
                "Elapsed game time alone must not invalidate an exact unconsumed offer in the same session.");
        }

        private static void PromptSupportsCancel()
        {
            var options=new List<NpcActionOption> {Decision().Option};
            Check.True(NpcActionChat.GrammarSemantic(options).Contains("\"CANCEL\""));
            Check.True(NpcActionChat.GrammarSemantic(options,true).Contains("\"CANCEL\""),
                "An active accepted commitment must keep cancellation available.");
            Check.True(!NpcActionChat.GrammarSemantic(options,false).Contains("\"CANCEL\""),
                "Without an active accepted commitment the grammar must make cancellation impossible.");
            Check.True(NpcActionChat.ParseSemantic("CANCEL",options)==null,"Cancellation must never bind as a new accepted physical action.");
        }

        private static void OfferCancellationBindsCapturedCommitment()
        {
            var options=new List<NpcActionOption> {Decision().Option};
            var noCommitments=new ConversationContextCommitment[0];
            Check.True(!NpcActionChat.GrammarSemanticForCommitments(options,noCommitments).Contains("\"CANCEL\""),
                "Offer response without active commitment must not allow cancellation.");

            var capturedTask=new ConversationContextCommitment("task:one","task","running",7);
            var commitments=new[]{capturedTask};
            Check.True(NpcActionChat.GrammarSemanticForCommitments(options,commitments).Contains("\"CANCEL\""),
                "Offer response with active task must allow cancellation.");
            Check.True(NpcActionChat.MatchesCurrentCommitment(commitments,"task","task:one",7),
                "Exact captured task identity and revision may be cancelled.");
            Check.True(!NpcActionChat.MatchesCurrentCommitment(commitments,"task","task:one",8),
                "Task progress after capture must invalidate delayed cancellation.");
            Check.True(!NpcActionChat.MatchesCurrentCommitment(commitments,"task","task:two",1),
                "A newer task must not inherit older cancellation consent.");
            Check.True(!NpcActionChat.MatchesCurrentCommitment(commitments,"action-task","task:one",7),
                "Task and action commitments must not be interchangeable.");

            var capturedAction=new[]{new ConversationContextCommitment("action:one","action-task","executing",4,NpcActionKind.Follow)};
            Check.True(NpcActionChat.MatchesCurrentCommitment(capturedAction,"action-task","action:one",4,NpcActionKind.Follow));
            Check.True(!NpcActionChat.MatchesCurrentCommitment(capturedAction,"action-task","action:one",5,NpcActionKind.Follow));
            Check.True(!NpcActionChat.MatchesCurrentCommitment(capturedAction,"action-task","action:one",4,NpcActionKind.Wait));
            var actionWithoutKind=new[]{new ConversationContextCommitment("action:one","action-task","executing",4)};
            Check.True(!NpcActionChat.MatchesCurrentCommitment(actionWithoutKind,"action-task","action:one",4,NpcActionKind.Follow),
                "An action snapshot without exact action kind must not authorize cancellation.");
        }

        private static void GrammarMatchesNativeQuantityAndDurationContracts()
        {
            var wait=Decision().Option;wait.Id=2;wait.Kind=NpcActionKind.Wait;wait.Target.Quantity=1;
            var grammar=NpcActionChat.GrammarSemantic(new[]{wait},false);
            Check.True(grammar.Contains("\"DIRECT|Wait|2|1|\" duration"),grammar);
            Check.True(!grammar.Contains("\"DIRECT|Wait|2|\" quantity"),grammar);
            Check.True(grammar.Contains("duration ::= \"1\" [0-9] | [2-9] [0-9] | [1-8] [0-9]{2} | \"900\""),grammar);
            Check.True(!grammar.Contains("duration ::= [0-9]{1,3}"),grammar);
            Check.True(NpcActionChat.ParseSemantic("DIRECT|Wait|2|1|10",new[]{wait})!=null);
            Check.True(NpcActionChat.ParseSemantic("DIRECT|Wait|2|10|300",new[]{wait})==null,
                "A fixed native quantity must not accept an arbitrary model quantity.");
            Check.True(NpcActionChat.ParseSemantic("DIRECT|Wait|2|1|9",new[]{wait})==null);
            Check.True(NpcActionChat.ParseSemantic("DIRECT|Wait|2|1|901",new[]{wait})==null);

            var stack=new NpcActionOption {Id=44,Kind=NpcActionKind.Transfer,MaximumQuantity=7,
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.Item,ItemId="42",Quantity=1}};
            var stackGrammar=NpcActionChat.GrammarSemantic(new[]{stack},false);
            Check.True(stackGrammar.Contains("\"DIRECT|Transfer|44|\" quantity \"|\" duration"),stackGrammar);
            Check.True(stackGrammar.Contains("quantity ::= ([1-9] [0-9]{0,5} | \"1000000\")"),stackGrammar);
            Check.True(NpcActionChat.ParseSemantic("DIRECT|Transfer|44|6|300",new[]{stack})!=null,
                "Grammar bounds must preserve valid stack quantities inside the native candidate limit.");
            Check.True(NpcActionChat.ParseSemantic("DIRECT|Transfer|44|8|300",new[]{stack})==null,
                "Native parsing must continue enforcing each stack candidate's exact maximum.");
        }

        private static void GuidanceWordingBindsLead()
        {
            var lead=new NpcActionOption {Id=7,Kind=NpcActionKind.Lead,Target=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="map:1",Quantity=1}};
            Check.True(NpcActionChat.ParseSemantic("DIRECT|Lead|7|1|300",new[]{lead})!=null);
            Check.True(NpcActionChat.ParseSemantic("DIRECT|MoveTo|7|1|300",new[]{lead})==null);

        }

        private static void SpontaneousTravelNeedsRelationship()
        {
            Check.True(!NpcConversationFavorPolicy.MaySpontaneouslyOfferTravel(false,false,100,false,0,0,0,false),
                "Unknown familiarity must never authorize an unsolicited walking offer.");
            Check.True(!NpcConversationFavorPolicy.MaySpontaneouslyOfferTravel(true,false,100,false,0,0,0,false),
                "A verified stranger must not volunteer a time-consuming escort.");
            Check.True(!NpcConversationFavorPolicy.MaySpontaneouslyOfferTravel(true,true,59,true,80,0,0,false),
                "Familiarity alone is not a strong positive relationship.");
            Check.True(!NpcConversationFavorPolicy.MaySpontaneouslyOfferTravel(true,true,70,false,0,0,0,false),
                "Unknown trust must not authorize unsolicited travel.");
            Check.True(NpcConversationFavorPolicy.MaySpontaneouslyOfferTravel(true,true,70,true,70,10,10,false));
            Check.True(!NpcConversationFavorPolicy.MaySpontaneouslyOfferTravel(true,true,70,true,70,10,10,true),
                "A higher-priority active concern suppresses unsolicited travel.");
        }
    }
}
