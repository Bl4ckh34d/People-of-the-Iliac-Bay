using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcActionSequenceChatTests
    {
        private static NpcActionDecision Semantic(string output,IList<NpcActionOption> options) { var method=typeof(NpcActionChat).GetMethod("ParseSemantic"); return method==null?null:(NpcActionDecision)method.Invoke(null,new object[]{output,options}); }
        private const string Request="Wait here for 10 seconds, then follow me for 10 seconds.";
        private static NpcActionOption Option(int id,NpcActionKind kind){return new NpcActionOption{Id=id,Kind=kind,Label=kind.ToString(),Target=new NpcActionTarget{Kind=NpcActionTargetKind.Person,PersonId="player"}};}
        private static string PromptHash(string prompt)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(prompt))).Replace("-","").ToLowerInvariant();
        }
        private static List<NpcActionDecision> TypedSequence(IList<string> spans,IList<IList<NpcActionOption>> options,IList<string> outputs)
        {
            var typed=new List<string>();
            for(int i=0;i<outputs.Count;i++)
            {
                var fields=outputs[i].Split('|');int id;
                if(fields.Length!=3 || fields[0]!="ACCEPT" || !int.TryParse(fields[1],out id) || i>=options.Count)
                {typed.Add(outputs[i]);continue;}
                NpcActionOption option=null;
                foreach(var candidate in options[i])if(candidate.Id==id)option=candidate;
                typed.Add(option==null?"NONE":"DIRECT|"+option.Kind+"|"+id+"|"+option.Target.Quantity+"|"+fields[2]);
            }
            return NpcActionSequenceChat.ParseSemantic(spans,options,typed);
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Legacy action prompts stay byte-for-byte stable when interpretation-only mode is off",()=>{
                var wait=Option(711,NpcActionKind.Wait);wait.Label="wait at the exact native point";wait.Target.Quantity=1;
                var conditional=Option(712,NpcActionKind.Transfer);conditional.ItemSourceStep=1;conditional.Label="transfer exact prior purchase";
                var method=typeof(NpcActionChat).GetMethod("PromptSemanticTaskStep");
                string stepContext="Ordered request: Buy arrows, then give them to me. Previous selected step is purchase.";
                string offer=NpcActionChat.PromptOfferResponse("Yes, please.","Current offer remains pending.",wait,60);
                Check.Equal("42036975f0cac83c6fa66cd267a9ebc1f99e318026bf0eb52c0f709cfc3774bd",
                    PromptHash(NpcActionChat.PromptSemantic("wait here","context",new[]{wait})));
                Check.Equal("0f75ea9167f2a41e7897cb9a23808313edbd44a271a614e07d50864d24bff828",
                    PromptHash(NpcActionChat.PromptSemantic("stop","phase=Executing",new[]{wait},true)));
                Check.Equal("bc4e03200c5177216809ffeeea88c38abe8bd098d5c245395e621e4bd42e47a0",
                    PromptHash((string)method.Invoke(null,new object[]{"give it to me?",stepContext,new[]{conditional},false})));
                Check.Equal("3c4d68d91df178b5d1735706304c94f4a87b0477c4aca2f62d5764e49413431a",PromptHash(offer));
            }));
            cases.Add(("Interpretation-only prompts propose matched actions and leave willingness to later gates",()=>{
                var wait=Option(721,NpcActionKind.Wait);wait.Label="wait at exact native point";
                var semantic=typeof(NpcActionChat).GetMethod("PromptSemantic",new[]{typeof(string),typeof(string),typeof(IList<NpcActionOption>),typeof(bool),typeof(bool)});
                Check.True(semantic!=null,"PromptSemantic needs an optional interpretation-only mode.");
                string single=(string)semantic.Invoke(null,new object[]{"Would you wait here?","stern and unwilling",new[]{wait},false,true});
                Check.True(single.Contains("regardless of the NPC's personal desire"),single);
                Check.True(single.Contains("unaccepted proposal") && single.Contains("D3 evaluates willingness"),single);
                Check.True(single.Contains("DECLINE means no proposal was selected") && !single.Contains("DECLINE only when unwilling"),single);
                Check.True(!single.Contains("willingly agrees") && !single.Contains("Consider personality, disposition"),single);
                foreach(string marker in new[]{"NONE","negated","reported","hypothetical","conditional","CANCEL"})
                    Check.True(single.Contains(marker),"Interpretation-only mode must preserve "+marker+" semantics.\n"+single);
                Check.True(single.Contains("DIRECT|Kind|optionId|quantity|durationSeconds") && single.Contains("wait at exact native point"),single);
                Check.True(single.Contains("Copy Kind and optionId exactly") && single.Contains("Quantity must be the listed fixed quantity or within its range") &&
                    single.Contains("Duration is 10..900 seconds") && single.Contains("Preserve the selected native candidate's exact target"),single);
                Check.True(single.Contains("Wait|721 quantity=1 — wait at exact native point"),single);
                Check.True(single.Contains("no active accepted commitment") && single.Contains("never output CANCEL"),single);

                var dependent=Option(722,NpcActionKind.Transfer);dependent.ItemSourceStep=1;
                dependent.Target.Quantity=2;dependent.Target.ItemId="native-item-77";
                dependent.Label="transfer exact item from previous purchase only after it succeeds";
                var taskStep=typeof(NpcActionChat).GetMethod("PromptSemanticTaskStep",new[]{typeof(string),typeof(string),typeof(IList<NpcActionOption>),typeof(bool)});
                Check.True(taskStep!=null,"Task-step prompt needs optional interpretation-only mode.");
                string task=(string)taskStep.Invoke(null,new object[]{"give it to me?","Ordered task: buy arrows, then give them to me.",new[]{dependent},true});
                Check.True(task.Contains("unaccepted proposal") && task.Contains("regardless of the NPC's personal desire"),task);
                Check.True(task.Contains("Transfer|722 quantity=2 conditional on step 1 succeeding") &&
                    task.Contains("exact item from a previous selected step") && task.Contains("transfer exact item from previous purchase only after it succeeds"),task);
                Check.True(task.Contains("Propose the matching step independently; whole-task willingness remains for later D3 evaluation"),task);
                Check.True(!task.Contains("Assess willingness to agree") && !task.Contains("assess this NPC willingness"),task);
                Check.True(task.Contains("Task-step scope:") && task.Contains("Do not output NONE merely because the complete request has other steps"),task);
                Check.True(task.Contains("Output NONE if no listed candidate preserves the requested target"),task);
            }));
            cases.Add(("Interpretation-only offer response checks player assent without renewed NPC willingness",()=>{
                var offered=Option(731,NpcActionKind.Follow);offered.Label="follow player";
                var method=typeof(NpcActionChat).GetMethod("PromptOfferResponse",new[]{typeof(string),typeof(string),typeof(NpcActionOption),typeof(int),typeof(bool)});
                Check.True(method!=null,"Offer response needs optional interpretation-only mode.");
                string prompt=(string)method.Invoke(null,new object[]{"Yes, please.","NPC is now reluctant.",offered,90,true});
                Check.True(prompt.Contains("player currently accepts that exact offer") && !prompt.Contains("NPC still willingly agrees"),prompt);
                Check.True(prompt.Contains("do not evaluate renewed NPC willingness") && prompt.Contains("separate D3 gate"),prompt);
                Check.True(prompt.Contains("DECLINE when the player rejects the pending offer") && !prompt.Contains("NPC withdraws it"),prompt);
                Check.True(prompt.Contains("Output CANCEL only when an active accepted action or task exists and the current player message directly requests stopping that commitment"),prompt);
                Check.True(prompt.Contains("Never use CANCEL for a pending offer; rejecting a pending offer is DECLINE"),prompt);
                Check.True(prompt.Contains("quoted, reported, negated, conditional, hypothetical, or past cancellation language"),prompt);
                Check.True(prompt.Contains("DIFFERENT concrete action") && prompt.Contains("reported/quoted/hypothetical/negated acceptance"),prompt);
            }));
            cases.Add(("Task-step semantic prompt scopes multi-action rejection to the exact current span",()=>{
                const string full="Wait here for 10 seconds, then follow me for 10 seconds.";
                const string span="Wait here for 10 seconds, ";
                const string scope="Task-step scope: The unchanged complete request and other original spans are reference context for resolving meaning and preserving order. Classify only the exact current player span below as one requested effect, and assess this NPC willingness only for that span. Do not output NONE merely because the complete request has other steps. Output NONE if this current span requests no matching listed action, is ambiguous or unsupported, is quoted, reported, negated or hypothetical, or itself requires multiple actions. A step decision is not partial task acceptance; runtime saves no task unless every exact span receives a valid decision.";
                const string oldRule="Output NONE when there is no matching current request, or when several actions are needed.";
                const string stepRule="Output NONE when this exact current step has no matching listed action, or when this step itself requires several actions.";
                var wait=Option(2,NpcActionKind.Wait);
                string single=NpcActionChat.PromptSemantic(full,"",new[]{wait});
                Check.True(single.Contains(oldRule),"Single-action prompt must keep its established multi-effect rule.");
                Check.True(!single.Contains(scope),"Single-action prompt must not receive task-step scope instructions.");

                var method=typeof(NpcActionChat).GetMethod("PromptSemanticTaskStep");
                Check.True(method!=null,"Ordered task steps need an explicit scoped semantic prompt API.");
                string context="The unchanged complete request is: "+full+"\nOrdered original-text spans:\n1: "+span+"\n2: then follow me for 10 seconds.\nCurrent required step: 1 of 2.";
                string taskStep=(string)method.Invoke(null,new object[]{span,context,new[]{wait},false});
                Check.True(taskStep.Contains(stepRule),"Task-step prompt must scope the multi-action rule to its current span.");
                Check.True(!taskStep.Contains(oldRule),"Task-step prompt must remove the whole-request multi-action veto.");
                Check.True(taskStep.Contains(scope),"Task-step prompt must preserve the experimentally tested scope instruction exactly.");
                Check.True(taskStep.Contains(full) && taskStep.EndsWith("Current player message:\n"+span+"\nDecision:",StringComparison.Ordinal),
                    "Task-step prompt must keep the whole request as context and the exact span as the final classification target.");

                var dependent=Option(3,NpcActionKind.Transfer);dependent.ItemSourceStep=1;
                dependent.Label="hand over the exact item from the prior purchase only after that step succeeds";
                string conditional=(string)method.Invoke(null,new object[]{"give it to me?",
                    "The unchanged complete request is: Buy the arrows, then give them to me. Previous selected step is a purchase.",new[]{dependent},false});
                Check.True(conditional.Contains("Assess willingness to agree to the whole ordered request."),
                    "Conditional handover must preserve whole-task willingness framing.");
                Check.True(conditional.Contains("A step marked conditional depends on the exact item from a previous selected step"),
                    "Conditional handover must stay explicitly dependent on the exact earlier result.");
            }));
            cases.Add(("Candidate categories retain later native items and buildings within bounded target pages",()=>{
                var type=typeof(NpcActionChat).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcActionCandidateCatalog");Check.True(type!=null);
                var candidates=new List<NpcActionOption>();
                for(int i=1;i<=4;i++)candidates.Add(new NpcActionOption {Id=i,Kind=NpcActionKind.MoveTo,Label="building "+i,
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.Building,SettlementId=1,BuildingKey=i}});
                for(int i=1;i<=3;i++){var item=Option(i+10,NpcActionKind.Transfer);item.Target.ItemId=i.ToString();candidates.Add(item);}
                var catalog=Activator.CreateInstance(type,new object[]{candidates});
                var select=type.GetMethod("Select");
                var buildings=(IList<NpcActionOption>)select.Invoke(catalog,new object[]{"GROUP|1"});
                var items=(IList<NpcActionOption>)select.Invoke(catalog,new object[]{"GROUP|2"});
                Check.Equal(4,buildings.Count);Check.Equal(4,buildings[3].Target.BuildingKey);
                Check.Equal(3,items.Count);Check.Equal("3",items[2].Target.ItemId);
                Check.True(select.Invoke(catalog,new object[]{"GROUP|3"})==null);
                Check.True(select.Invoke(catalog,new object[]{"GROUP|1|extra"})==null);
                buildings[3].Target.BuildingKey=99;Check.Equal(4,candidates[3].Target.BuildingKey);
            }));
            cases.Add(("Bounded candidate catalog retains the exact prior-step handover when full",()=>{
                var candidates=new List<NpcActionOption>();
                for(int i=1;i<=NpcActionCandidateCatalog.MaximumCandidates;i++)
                    candidates.Add(new NpcActionOption {Id=i,Kind=NpcActionKind.MoveTo,Label="verified building "+i,
                        Target=new NpcActionTarget {Kind=NpcActionTargetKind.Building,SettlementId=1,BuildingKey=i}});
                var purchase=Option(900,NpcActionKind.Purchase);
                purchase.Label="buy 1 x Steel Tanto from verified shop stock";
                purchase.Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=2,
                    PersonId="seller",ContainerId="stock",ItemId="42",Quantity=1};
                var handover=NpcActionSequenceChat.AcquiredItemHandoverSemantic(0,
                    new NpcActionDecision {Option=purchase,DurationSeconds=300});
                Check.True(handover!=null);
                candidates.Add(handover);
                var catalog=new NpcActionCandidateCatalog(candidates);
                string priorPrompt=catalog.Prompt("give it to me?",new NpcActionDecision {Option=purchase,DurationSeconds=300});
                Check.True(priorPrompt.Contains("Purchase — buy 1 x Steel Tanto"));
                Check.True(priorPrompt.Contains("not yet accepted or executed"));
                Check.True(priorPrompt.Contains("Hand over an item produced by an earlier task step after that step succeeds"));
                Check.True(!priorPrompt.Contains("42"),"Retrieval context must not expose native item UID.");
                bool retained=false;
                for(int group=1;group<=NpcActionCandidateCatalog.MaximumCandidates;group++)
                {
                    var selected=catalog.Select("GROUP|"+group);
                    if(selected==null)break;
                    foreach(var option in selected)if(option.ItemSourceStep==1)retained=true;
                }
                Check.True(retained,"A dependency appended after the bounded local candidates was dropped from model retrieval.");
            }));
            cases.Add(("Compact action candidates include the complete eligible set only within count and rendered-row bounds",new Action(()=>{
                var catalogType=typeof(NpcActionCandidateCatalog);
                var compactMethod=catalogType.GetMethod("TryCreateCompact");
                Check.True(compactMethod!=null,"Compact path needs an explicit all-or-fallback helper.");
                var maximumCountField=catalogType.GetField("MaximumCompactCandidates");
                var maximumCharactersField=catalogType.GetField("MaximumCompactRenderedCharacters");
                Check.True(maximumCountField!=null && maximumCharactersField!=null,"Compact path needs explicit strict limits.");
                int maximumCount=(int)maximumCountField.GetValue(null);
                int maximumCharacters=(int)maximumCharactersField.GetValue(null);
                var renderMethod=typeof(NpcActionChat).GetMethod("RenderSemanticCandidateRows");
                Check.True(renderMethod!=null,"Budget must use the exact rows shown to the willingness classifier.");
                Func<IList<NpcActionOption>,string> render=items=>(string)renderMethod.Invoke(null,new object[]{items});
                Func<IList<NpcActionOption>,Tuple<bool,IList<NpcActionOption>,int>> compact=items=>{
                    var args=new object[]{items,null,0};
                    bool allowed=(bool)compactMethod.Invoke(null,args);
                    return Tuple.Create(allowed,(IList<NpcActionOption>)args[1],(int)args[2]);
                };

                var first=Option(31,NpcActionKind.Wait);first.Label="wait at verified point";first.MaximumQuantity=8;
                var second=Option(32,NpcActionKind.Follow);second.Label="follow the player";
                var source=new List<NpcActionOption>{first,second};
                var result=compact(source);
                Check.True(result.Item1);
                Check.Equal(2,result.Item2.Count);
                Check.Equal(31,result.Item2[0].Id);Check.Equal(32,result.Item2[1].Id);
                Check.Equal(8,result.Item2[0].MaximumQuantity);Check.Equal(0,result.Item2[0].ItemSourceStep);
                Check.Equal(render(source),render(result.Item2));Check.Equal(result.Item3,render(source).Length);
                result.Item2[0].Target.PersonId="changed";result.Item2[0].Label="changed";
                Check.Equal("player",first.Target.PersonId);Check.Equal("wait at verified point",first.Label);

                var exact=Option(40,NpcActionKind.Wait);exact.Label="";
                int emptyLabelCharacters=render(new[]{exact}).Length;
                exact.Label=new string('x',maximumCharacters-emptyLabelCharacters);
                var exactResult=compact(new[]{exact});
                Check.True(exactResult.Item1);Check.Equal(maximumCharacters,exactResult.Item3);
                exact.Label+="x";Check.True(!compact(new[]{exact}).Item1,"One character over budget must use category lookup.");

                var sixteen=new List<NpcActionOption>();for(int i=0;i<maximumCount;i++)sixteen.Add(Option(100+i,NpcActionKind.Wait));
                Check.True(compact(sixteen).Item1);
                sixteen.Add(Option(200,NpcActionKind.Wait));
                var tooMany=compact(sixteen);Check.True(!tooMany.Item1);Check.True(tooMany.Item2==null,"Do not return a truncated prefix.");
                Check.True(!compact(null).Item1);Check.True(!compact(new List<NpcActionOption>()).Item1);
                Check.True(!compact(new NpcActionOption[]{Option(60,NpcActionKind.Wait),null}).Item1,
                    "Malformed candidates must fall back instead of silently disappearing.");
                var missingTarget=new NpcActionOption{Id=61,Kind=NpcActionKind.Wait};
                Check.True(!compact(new[]{Option(62,NpcActionKind.Wait),missingTarget}).Item1);
                var dependency=Option(63,NpcActionKind.Transfer);dependency.ItemSourceStep=1;
                Check.True(!compact(new[]{dependency}).Item1,"Sequence dependencies remain on category lookup.");
                var duplicate=new List<NpcActionOption>{Option(70,NpcActionKind.Wait),Option(70,NpcActionKind.Follow)};
                Check.True(!compact(duplicate).Item1,"Ambiguous native IDs must fail closed to category lookup.");
            })));
            cases.Add(("Compact candidate policy supports dependency-free sequence steps and preserves native bindings",new Action(()=>{
                var method=typeof(NpcActionCandidateCatalog).GetMethod("TryCreateCompactForStep");
                Check.True(method!=null,"Sequence selection needs a testable compact-policy entry point.");
                Func<IList<NpcActionOption>,int,NpcActionDecision,Tuple<bool,IList<NpcActionOption>,int>> compact= (items,step,prior)=>{
                    var args=new object[]{items,step,prior,null,0};
                    bool allowed=(bool)method.Invoke(null,args);
                    return Tuple.Create(allowed,(IList<NpcActionOption>)args[3],(int)args[4]);
                };
                var wait=Option(501,NpcActionKind.Wait);wait.Label="wait at captured point";wait.Target.PersonId="native-player-id";
                var follow=Option(502,NpcActionKind.Follow);follow.Label="follow this player";follow.Target.PersonId="player";
                var first=compact(new[]{wait},1,null);
                Check.True(first.Item1,"First sequence step can use complete compact set.");
                Check.Equal(501,first.Item2[0].Id);Check.Equal("native-player-id",first.Item2[0].Target.PersonId);
                first.Item2[0].Target.PersonId="mutated";Check.Equal("native-player-id",wait.Target.PersonId);

                var previous=new NpcActionDecision{Option=wait,DurationSeconds=10};
                var second=compact(new[]{follow},2,previous);
                Check.True(second.Item1,"Dependency-free later step can use compact set when prior step is bound.");
                Check.Equal(502,second.Item2[0].Id);Check.Equal(NpcActionKind.Follow,second.Item2[0].Kind);
                Check.Equal("player",second.Item2[0].Target.PersonId);Check.Equal(1,second.Item2.Count);
                Check.True(!compact(new[]{follow},2,null).Item1,"Later sequence step without prior binding must fail closed.");
                Check.True(!compact(new[]{follow},NpcTaskContract.MaximumSteps+1,previous).Item1,
                    "Out-of-range sequence step must retain category path.");
                var dependency=Option(503,NpcActionKind.Transfer);dependency.ItemSourceStep=1;
                Check.True(!compact(new[]{dependency},2,previous).Item1,
                    "A handover dependency must retain category lookup even when prior step exists.");
            })));
            cases.Add(("Category prompt places full chain context before the final current span",()=>{
                var candidates=new List<NpcActionOption>{Option(1,NpcActionKind.Wait)};
                var catalog=new NpcActionCandidateCatalog(candidates);
                const string full="Go to the corner, wait there for one minute, then return to me.";
                const string current="wait there for one minute";
                string prompt=catalog.Prompt(current,null,full);
                int fullIndex=prompt.IndexOf("Complete ordered task (reference data only; choose for the current span below):",StringComparison.Ordinal);
                int fullTextIndex=prompt.IndexOf(full,fullIndex,StringComparison.Ordinal);
                int currentIndex=prompt.LastIndexOf("Current player message:\n"+current+"\nCategory:",StringComparison.Ordinal);
                Check.True(fullIndex>=0 && currentIndex>fullIndex,"The full task must precede the sole current-step selection target. indices="+fullIndex+","+currentIndex+"\n"+prompt);
                Check.True(fullTextIndex>=fullIndex && prompt.IndexOf(full,fullTextIndex+full.Length,StringComparison.Ordinal)<0,"The original whole chain appears once as context.");
                Check.True(prompt.EndsWith("Current player message:\n"+current+"\nCategory:",StringComparison.Ordinal),"Category response stub must remain last.");
            }));
            cases.Add(("Semantic action routing preserves offer assent and cancellation without intercepting unrelated social turns",()=>{
                var type=typeof(NpcActionChat).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcActionRouting");Check.True(type!=null);
                var method=type.GetMethod("Choose");
                Check.Equal("Offer",method.Invoke(null,new object[]{"social",true,false}).ToString());
                Check.Equal("Single",method.Invoke(null,new object[]{"action",false,true}).ToString());
                Check.Equal("None",method.Invoke(null,new object[]{"social",false,true}).ToString());
                Check.Equal("Decompose",method.Invoke(null,new object[]{"action",false,false}).ToString());
                Check.Equal("Offer",method.Invoke(null,new object[]{"action",true,true}).ToString());
            }));
            cases.Add(("NONE decomposition after action intent falls back to single willingness while invalid output fails closed and chains stay ordered",()=>{
                var routing=typeof(NpcActionChat).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcActionRouting");Check.True(routing!=null);
                var after= routing.GetMethod("AfterDecomposition");Check.True(after!=null,"A valid decomposition NONE needs a single-action willingness fallback.");
                const string politeWait="Would you mind waiting here?";
                var none=NpcActionDecomposition.Parse("NONE",politeWait);
                Check.Equal("Single",after.Invoke(null,new object[]{none}).ToString());
                var wait=new NpcActionOption {Id=7,Kind=NpcActionKind.Wait,Label="wait at this observed position",
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="map:1",X=1,Y=0,Z=2}};
                Check.True(NpcActionChat.ParseSemantic("NONE",new[]{wait})==null,
                    "Routing to the willingness classifier does not itself accept or commit an action.");

                Check.True(NpcActionDecomposition.Parse("MAYBE",politeWait)==null);
                Check.Equal("None",after.Invoke(null,new object[]{null}).ToString());

                const string chainText="Wait here for 10 seconds, then follow me for 10 seconds.";
                var chain=NpcActionDecomposition.Parse("CHAIN|Wait here for 10 seconds|follow me for 10 seconds",chainText);
                Check.True(chain!=null);
                Check.Equal("Decompose",after.Invoke(null,new object[]{chain}).ToString());
                Check.Equal(2,chain.Clauses.Count);
                Check.Equal("Wait here for 10 seconds, then ",chain.Clauses[0]);
                Check.Equal("follow me for 10 seconds.",chain.Clauses[1]);
            }));
            cases.Add(("Semantic acquired item handover binds selected quantity without a pronoun template",()=>{
                var method=typeof(NpcActionSequenceChat).GetMethod("AcquiredItemHandoverSemantic");Check.True(method!=null);
                var purchase=Option(7,NpcActionKind.Purchase);
                purchase.Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=2,
                    PersonId="seller",ContainerId="stock",ItemId="42",Quantity=3,QuotedGold=9};
                var selected=new NpcActionDecision {Option=purchase,DurationSeconds=300};
                var handover=(NpcActionOption)method.Invoke(null,new object[]{0,selected});
                Check.True(handover!=null);Check.Equal(1,handover.ItemSourceStep);Check.Equal(3,handover.Target.Quantity);
                Check.Equal("player",handover.Target.PersonId);Check.Equal("42",handover.Target.ItemId);
                handover.Target.Quantity=1;Check.Equal(3,purchase.Target.Quantity);
                purchase.Kind=NpcActionKind.Wait;Check.True(method.Invoke(null,new object[]{0,selected})==null);
            }));
            cases.Add(("Semantic decision context treats acquired-item handover as conditional future work",()=>{
                var dependent=Option(81,NpcActionKind.Transfer);dependent.ItemSourceStep=1;
                dependent.Label="hand over the exact item acquired by the preceding purchase, only after that step succeeds";
                var conditional=NpcActionChat.PromptSemantic("give it to me?","Previous selected step is a Steel Tanto purchase.",new[]{dependent});
                Check.True(conditional.Contains("conditional") && conditional.Contains("previous") && conditional.Contains("succeed"),conditional);
                Check.True(!conditional.Contains("Currently executable candidates:"),"A conditional future handover must not be presented as executable now.");
                Check.True(conditional.Contains("willingly agrees"),"Candidate framing must not imply consent.");
                var ordinary=NpcActionChat.PromptSemantic("wait here","",new[]{Option(82,NpcActionKind.Wait)});
                Check.True(ordinary.Contains("Currently executable candidates:"),"Ordinary current actions keep their existing candidate framing.");
            }));
            cases.Add(("Semantic action cancellation is separate from new-action willingness and empty target availability",()=>{
                var signature=new[]{typeof(string),typeof(string),typeof(IList<NpcActionOption>),typeof(bool),typeof(bool)};
                var method=typeof(NpcActionChat).GetMethod("PromptSemantic",signature);
                Check.True(method!=null,"PromptSemantic must receive authoritative active-commitment state.");
                var none=new List<NpcActionOption>();
                string active=(string)method.Invoke(null,new object[]{"You can stop now.","Wait; phase=Executing.",none,true,false});
                Check.True(active.Contains("active accepted commitment"),active);
                Check.True(active.Contains("Evaluate cancellation before new-action willingness"),active);
                Check.True(active.Contains("Cancellation does not require a new action target"),active);
                Check.True(active.Contains("DECLINE only when unwilling to perform a new listed action"),active);
                Check.True(active.Contains("quoted, reported, negated, conditional, hypothetical, or past"),active);
                Check.True(!active.Contains("No verified local action target is currently available"),active);
                string idle=(string)method.Invoke(null,new object[]{"You can stop now.","",none,false,false});
                Check.True(idle.Contains("no active accepted commitment"),idle);
                Check.True(idle.Contains("never output CANCEL"),idle);
            }));
            cases.Add(("Semantic stack quantity binds within verified bounds without changing the candidate",()=>{
                var option=Option(44,NpcActionKind.Transfer);option.MaximumQuantity=7;
                option.Target.ItemId="42";
                var decision=Semantic("DIRECT|Transfer|44|6|300",new[]{option});
                Check.True(decision!=null);Check.Equal(6,decision.Option.Target.Quantity);
                Check.Equal(1,option.Target.Quantity);Check.Equal(0,decision.Option.MaximumQuantity);
                foreach(string output in new[]{"DIRECT|Transfer|44|0|300","DIRECT|Transfer|44|8|300","DIRECT|Transfer|44|1|0",
                    "DIRECT|Transfer|44|1|9","DIRECT|Transfer|44|1|901","DIRECT|4|44|1|300","DIRECT|Transfer|44|1|300\nDIRECT|Transfer|44|1|300"})
                    Check.True(Semantic(output,new[]{option})==null,output);
                option.MaximumQuantity=0;Check.True(Semantic("DIRECT|Transfer|44|6|300",new[]{option})==null);
                option.MaximumQuantity=7;option.Kind=NpcActionKind.Follow;
                Check.True(Semantic("DIRECT|Follow|44|6|300",new[]{option})==null);
            }));
            cases.Add(("Semantic sequence preserves paraphrases while enforcing order and exact options",()=>{
                var method=typeof(NpcActionSequenceChat).GetMethod("ParseSemantic");Check.True(method!=null);
                var clauses=new[]{"Keep me company right where you are for a minute", "I'd appreciate your company on my walk"};
                var options=new List<IList<NpcActionOption>>{new[]{Option(1,NpcActionKind.Wait)},new[]{Option(2,NpcActionKind.Follow)}};
                var outputs=new[]{"DIRECT|Wait|1|1|60","DIRECT|Follow|2|1|300"};
                var decisions=(List<NpcActionDecision>)method.Invoke(null,new object[]{clauses,options,outputs});
                Check.True(decisions!=null);Check.Equal(2,decisions.Count);Check.Equal(60,decisions[0].DurationSeconds);
                outputs[1]="REPORTED";
                Check.True(method.Invoke(null,new object[]{clauses,options,outputs})==null);
                outputs[1]="DIRECT|Follow|2|1|300";options[0]=new[]{Option(1,NpcActionKind.Follow)};outputs[0]="DIRECT|Follow|1|1|60";
                Check.True(method.Invoke(null,new object[]{clauses,options,outputs})==null,"Follow cannot precede another step.");
            }));
            cases.Add(("Semantic later Wait must select an option already bound to the preceding MoveTo destination",new Action(()=>{
                var move=new NpcActionOption {Id=1,Kind=NpcActionKind.MoveTo,Label="move to the north gate",Target=new NpcActionTarget {
                    Kind=NpcActionTargetKind.Building,SettlementId=7,BuildingKey=91,Quantity=1}};
                var currentPositionWait=new NpcActionOption {Id=2,Kind=NpcActionKind.Wait,Label="wait at the south bridge",Target=new NpcActionTarget {
                    Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="map:7",X=12,Y=0,Z=18,Quantity=1}};
                var clauses=new[]{"Go to the north gate", "Wait at the south bridge for 30 seconds"};
                var options=new List<IList<NpcActionOption>> {new[]{move},new[]{currentPositionWait}};
                var outputs=new[]{"DIRECT|MoveTo|1|1|300","DIRECT|Wait|2|1|30"};
                var decisions=NpcActionSequenceChat.ParseSemantic(clauses,options,outputs);
                Check.True(decisions==null,"An explicit different-location Wait must not be silently rewritten to the MoveTo destination.");

                NpcActionDecision selectedMove;
                Check.True(NpcActionIntent.TryBind(outputs[0],options[0],out selectedMove));
                var augmentedOptions=NpcActionSequenceChat.BindWaitOptionsAfterMoveTo(selectedMove,new[]{currentPositionWait});
                Check.Equal(2,augmentedOptions.Count);
                Check.Equal(2,augmentedOptions[0].Id);
                Check.True(NpcTaskContract.TargetMatches(currentPositionWait.Target,augmentedOptions[0].Target),
                    "The south-bridge target must not be rewritten to the north-gate destination.");
                Check.True(augmentedOptions[1].Id!=augmentedOptions[0].Id,"Bound destination needs its own option identity.");
                var augmented=new List<IList<NpcActionOption>> {new[]{move},augmentedOptions};
                Check.True(NpcActionSequenceChat.ParseSemantic(clauses,augmented,outputs)==null,
                    "Selecting the original south-bridge option must remain rejected after destination augmentation.");
                clauses[1]="Wait there for 30 seconds";
                outputs[1]="DIRECT|Wait|"+augmentedOptions[1].Id+"|1|30";
                decisions=NpcActionSequenceChat.ParseSemantic(clauses,augmented,outputs);
                Check.True(decisions!=null && NpcTaskContract.TargetMatches(move.Target,decisions[1].Option.Target),
                    "A separate selected option explicitly bound to the preceding destination should be accepted.");
                Check.True(NpcActionSequenceChat.BindWaitOptionsAfterMoveTo(
                    new NpcActionDecision {Option=new NpcActionOption {Kind=NpcActionKind.Follow,Target=new NpcActionTarget {
                        Kind=NpcActionTargetKind.Person,PersonId="player"}}},new[]{currentPositionWait}).Count==1,
                    "A dynamic Follow target must not add a destination-bound Wait option.");
            })));
            cases.Add(("Accepted sequence has factual fallback acknowledgment",()=>{
                var clauses=new List<string>{"Wait here for 10 seconds","follow me for 10 seconds."};
                var options=new List<IList<NpcActionOption>>{new List<NpcActionOption>{Option(1,NpcActionKind.Wait)},new List<NpcActionOption>{Option(2,NpcActionKind.Follow)}};
                var accepted=TypedSequence(clauses,options,new[]{"ACCEPT|1|10","ACCEPT|2|10"});
                string fallback=NpcActionSequenceChat.AcceptedIntentFallback(accepted);
                Check.True(!string.IsNullOrWhiteSpace(fallback));
                Check.True(fallback.Contains("agreed") && fallback.Contains("ordered task") && fallback.Contains("begin") && fallback.Contains("conversation ends"),fallback);
                Check.True(!fallback.Contains("completed") && !fallback.Contains("paid") && !fallback.Contains("arrived"),fallback);
                Check.True(NpcActionSequenceChat.AcceptedIntentFallback(null)==null);
                Check.True(NpcActionSequenceChat.AcceptedIntentFallback(new List<NpcActionDecision>())==null);
            }));
            cases.Add(("Semantic action binding accepts paraphrases only through an exact structured native option",()=>{
                var native=new NpcActionOption {Id=23,Kind=NpcActionKind.Transfer,Label="move two exact arrows from the saved shop stock to the player",
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=5,PersonId="resident:1",
                        ContainerId="stock:9",ItemId="native-uid:77",Quantity=2,QuotedGold=3}};
                // The direct-action classifier has already established that this is the player's current request.
                // Semantic binding must not need the lexical verb list to accept a paraphrase.
                var decision=Semantic("DIRECT|Transfer|23|2|300",new[]{native});
                Check.True(decision!=null);
                Check.Equal(NpcActionKind.Transfer,decision.Option.Kind);
                Check.Equal(2,decision.Option.Target.Quantity);
                Check.Equal("native-uid:77",decision.Option.Target.ItemId);
                decision.Option.Target.ItemId="mutated";
                Check.Equal("native-uid:77",native.Target.ItemId);
                foreach(string nonDirect in new[]{"NONE","NEGATED","REPORTED","HYPOTHETICAL","CONDITIONAL"})
                    Check.True(Semantic(nonDirect+"|Transfer|23|2|300",new[]{native})==null,nonDirect);
                foreach(string invalid in new[]{"DIRECT|Purchase|23|2|300","DIRECT|Transfer|24|2|300","DIRECT|Transfer|23|1|300",
                    "DIRECT|Transfer|23|2|901","DIRECT|Transfer|23|2|NaN","DIRECT|Transfer|23|2|300|extra"})
                    Check.True(Semantic(invalid,new[]{native})==null,invalid);
                Check.True(Semantic("DIRECT|Transfer|23|2|300",new[]{native,native})==null,"Duplicate IDs are ambiguous.");
            }));
            cases.Add(("Sequence purchase pronoun binds only exact preceding stock selection",()=>{
                var purchase=Option(1,NpcActionKind.Purchase);
                purchase.Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=10,
                    PersonId="seller",ContainerId="123",ItemId="42",Quantity=2,QuotedGold=9};
                var handover=NpcActionSequenceChat.PurchasedHandover(0,new[]{purchase});
                Check.True(handover!=null);Check.Equal(1,handover.ItemSourceStep);Check.Equal(2,handover.Target.Quantity);
                var clauses=new List<string>{"Buy two arrows","give them to me"};
                var parsed=TypedSequence(clauses,new List<IList<NpcActionOption>>{new[]{purchase},new[]{handover}},new[]{"ACCEPT|1|300","ACCEPT|1|300"});
                Check.True(parsed!=null);Check.Equal(1,parsed[1].Option.ItemSourceStep);
                Check.True(NpcActionSequenceChat.PurchasedHandover(0,new[]{purchase,purchase})==null);
                purchase.Target.ContainerId=null;
                Check.True(NpcActionSequenceChat.PurchasedHandover(0,new[]{purchase})==null);
            }));
            cases.Add(("Ordered seat request keeps its surveyed target and exact timed duration",()=>{
                var seat=new NpcActionOption {Id=1,Kind=NpcActionKind.TakeSeat,Label="take the surveyed seat",
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=100,BuildingKey=10,
                        ContainerId="seat:1",X=2,Y=0,Z=3}};
                var follow=Option(2,NpcActionKind.Follow);
                var clauses=new List<string>{"Take a seat for 30 seconds","follow me for 10 seconds."};
                Check.True(clauses!=null && clauses.Count==2);
                var options=new List<IList<NpcActionOption>> {new[]{seat},new[]{follow}};
                var accepted=TypedSequence(clauses,options,new[]{"ACCEPT|1|30","ACCEPT|2|10"});
                Check.True(accepted!=null);
                Check.Equal(NpcActionKind.TakeSeat,accepted[0].Option.Kind);
                Check.Equal("seat:1",accepted[0].Option.Target.ContainerId);
                Check.Equal(30,accepted[0].DurationSeconds);
                var semantic=NpcActionSequenceChat.ParseSemantic(clauses,options,
                    new[]{"DIRECT|TakeSeat|1|1|30","DIRECT|Follow|2|1|10"});
                Check.True(semantic!=null && semantic[0].DurationSeconds==30);
                Check.True(NpcActionSequenceChat.ParseSemantic(clauses,options,
                    new[]{"DIRECT|TakeSeat|1|1|99999","DIRECT|Follow|2|1|10"})==null);
            }));
            
            
            cases.Add(("Sequence chat requires all current typed decisions in order and never accepts a prefix",()=>{
                var clauses=new List<string>{"Wait here for 10 seconds","follow me for 10 seconds."};
                var options=new List<IList<NpcActionOption>>{new List<NpcActionOption>{Option(1,NpcActionKind.Wait)},new List<NpcActionOption>{Option(2,NpcActionKind.Follow)}};
                var accepted=TypedSequence(clauses,options,new[]{"ACCEPT|1|10","ACCEPT|2|10"});
                Check.True(accepted!=null && accepted.Count==2 && accepted[0].Option.Kind==NpcActionKind.Wait && accepted[1].Option.Kind==NpcActionKind.Follow);
                foreach(var output in new[]{new[]{"ACCEPT|1|10"},new[]{"ACCEPT|1|10","DECLINE"},new[]{"ACCEPT|1|10","ACCEPT|99|10"},new[]{"ACCEPT|2|10","ACCEPT|1|10"},new[]{"ACCEPT|1|9","ACCEPT|2|10"}})
                    Check.True(TypedSequence(clauses,options,output)==null);
                accepted[0].Option.Target.PersonId="changed";Check.True(options[0][0].Target.PersonId=="player");
            }));
            cases.Add(("Wait after static MoveTo binds exact destination and rejects dynamic predecessor",()=>{
                var move=new NpcActionOption {Id=1,Kind=NpcActionKind.MoveTo,Target=new NpcActionTarget {
                    Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="map:1",X=12.5,Y=0,Z=-4}};
                var wait=Option(2,NpcActionKind.Wait);
                var clauses=new List<string>{"Go to the fountain","wait here for 30 seconds."};
                Check.True(clauses!=null && clauses.Count==2);
                Check.True(TypedSequence(clauses,new List<IList<NpcActionOption>> {new[]{move},new[]{wait}},
                    new[]{"ACCEPT|1|300","ACCEPT|2|30"})==null,
                    "A raw current-position Wait candidate must not be rewritten after selection.");
                var boundWaits=NpcActionSequenceChat.BindWaitOptionsAfterMoveTo(
                    new NpcActionDecision {Option=move,DurationSeconds=300},new[]{wait});
                Check.True(boundWaits.Count==2 && boundWaits[0].Id==wait.Id &&
                    NpcTaskContract.TargetMatches(wait.Target,boundWaits[0].Target),
                    "Original Wait candidate and target identity must remain unchanged.");
                Check.True(boundWaits[1].Id!=wait.Id && NpcTaskContract.TargetMatches(move.Target,boundWaits[1].Target),
                    "The preceding destination must be a separate candidate with a distinct ID.");
                var parsed=TypedSequence(clauses,new List<IList<NpcActionOption>> {new[]{move},boundWaits},
                    new[]{"ACCEPT|1|300","ACCEPT|"+boundWaits[1].Id+"|30"});
                Check.True(parsed!=null);
                Check.True(NpcTaskContract.TargetMatches(move.Target,parsed[1].Option.Target));
                parsed[1].Option.Target.X=99;
                Check.Equal(12.5,move.Target.X);
                Check.True(TypedSequence(clauses,new List<IList<NpcActionOption>> {new[]{move},boundWaits},
                    new[]{"ACCEPT|1|300","ACCEPT|2|30"})==null,
                    "The original here option cannot be silently rebound after selection.");

                var dynamicMove=new NpcActionOption {Id=1,Kind=NpcActionKind.MoveTo,Target=new NpcActionTarget {
                    Kind=NpcActionTargetKind.Person,PersonId="player"}};
                var dynamicWaits=NpcActionSequenceChat.BindWaitOptionsAfterMoveTo(
                    new NpcActionDecision {Option=dynamicMove},new[]{wait});
                Check.True(dynamicWaits.Count==1 && dynamicWaits[0].Id==wait.Id,
                    "An unsupported predecessor keeps original candidate visible but adds no bound option.");
                Check.True(TypedSequence(clauses,new List<IList<NpcActionOption>> {new[]{dynamicMove},dynamicWaits},
                    new[]{"ACCEPT|1|300","ACCEPT|2|30"})==null);
                var buildingMove=new NpcActionOption {Id=1,Kind=NpcActionKind.MoveTo,Target=new NpcActionTarget {
                    Kind=NpcActionTargetKind.Building,SettlementId=1,BuildingKey=1234}};
                var buildingWaits=NpcActionSequenceChat.BindWaitOptionsAfterMoveTo(
                    new NpcActionDecision {Option=buildingMove},new[]{wait});
                var buildingParsed=TypedSequence(clauses,new List<IList<NpcActionOption>> {new[]{buildingMove},buildingWaits},
                    new[]{"ACCEPT|1|300","ACCEPT|"+buildingWaits[1].Id+"|30"});
                Check.True(buildingParsed!=null,"A saved building entrance is a stable MoveTo destination.");
                Check.True(NpcTaskContract.TargetMatches(buildingMove.Target,buildingParsed[1].Option.Target));
                var buildingPlan=new NpcTaskPlan {Id="wait-building:1",ProposalId="wait-building",ResidentId="resident:1",
                    AcceptedAt=100,UpdatedAt=100,DeadlineAt=500,Steps=new List<NpcTaskStep> {
                        new NpcTaskStep {Kind=NpcActionKind.MoveTo,Target=NpcActionExecutor.CopyTarget(buildingMove.Target)},
                        new NpcTaskStep {Kind=NpcActionKind.Wait,DurationSeconds=30,Target=NpcActionExecutor.CopyTarget(buildingMove.Target)}}};
                NpcTaskContract.Validate(buildingPlan);
                var follow=Option(1,NpcActionKind.Follow);
                NpcActionTarget ignored; string reason;
                Check.True(!NpcTaskPositionBinding.TryBindWaitAfterMoveTo(
                    new NpcActionDecision {Option=follow},new NpcActionDecision {Option=wait},out ignored,out reason));

                var plan=new NpcTaskPlan {Id="wait-binding:1",ProposalId="wait-binding",ResidentId="resident:1",
                    AcceptedAt=100,UpdatedAt=100,DeadlineAt=500,Steps=new List<NpcTaskStep> {
                        new NpcTaskStep {Kind=NpcActionKind.MoveTo,Target=NpcActionExecutor.CopyTarget(move.Target)},
                        new NpcTaskStep {Kind=NpcActionKind.Wait,DurationSeconds=30,Target=NpcActionExecutor.CopyTarget(move.Target)}}};
                NpcTaskContract.Validate(plan);
                var receiptId="wait-binding:1:step:0:observed";
                var completedMove=new NpcTaskStep {Kind=NpcActionKind.MoveTo,Target=NpcActionExecutor.CopyTarget(move.Target),CompletedEffectId=receiptId};
                var boundWait=new NpcTaskStep {Kind=NpcActionKind.Wait,DurationSeconds=30,Target=NpcActionExecutor.CopyTarget(move.Target)};
                var receipt=new NpcActionJob {Kind=NpcActionKind.MoveTo,Phase=NpcActionPhase.Completed,
                    Target=NpcActionExecutor.CopyTarget(move.Target),CompletedEffectId=receiptId};
                Check.True(NpcTaskPositionBinding.HasCompletedMoveToReceipt(completedMove,receipt,boundWait));
                receipt.Phase=NpcActionPhase.Cancelled;
                Check.True(!NpcTaskPositionBinding.HasCompletedMoveToReceipt(completedMove,receipt,boundWait));
                receipt.Phase=NpcActionPhase.Completed;receipt.Target.X+=1;
                Check.True(!NpcTaskPositionBinding.HasCompletedMoveToReceipt(completedMove,receipt,boundWait));
                plan.Steps[1].Target.Z+=1;
                Check.Throws<ArgumentException>(()=>NpcTaskContract.Validate(plan));
            }));
            cases.Add(("A bound Wait cannot start outside MoveTo's resolved arrival range",()=>{
                var target=new NpcActionTarget {Kind=NpcActionTargetKind.Building,SettlementId=1,BuildingKey=1234};
                string effect="move:completed";
                var previous=new NpcTaskStep {Kind=NpcActionKind.MoveTo,Target=NpcActionExecutor.CopyTarget(target),CompletedEffectId=effect};
                var wait=new NpcTaskStep {Kind=NpcActionKind.Wait,Target=NpcActionExecutor.CopyTarget(target),DurationSeconds=30};
                var action=new NpcActionJob {Kind=NpcActionKind.MoveTo,Phase=NpcActionPhase.Completed,
                    Target=NpcActionExecutor.CopyTarget(target),CompletedEffectId=effect};
                Check.True(NpcTaskPositionBinding.HasCompletedMoveToReceipt(previous,action,wait));
                Check.True(NpcTaskPositionBinding.CanStartWaitAtResolvedArrival(0.25,0.25),
                    "Exact MoveTo arrival range must permit Wait dispatch.");
                Check.True(!NpcTaskPositionBinding.CanStartWaitAtResolvedArrival(0.2501,0.25),
                    "A corrective route must not run under the Wait timer.");
                Check.True(!NpcTaskPositionBinding.CanStartWaitAtResolvedArrival(double.NaN,0.25),
                    "Invalid distance must fail closed.");
                Check.True(!NpcTaskPositionBinding.CanStartWaitAtResolvedArrival(0.1,double.NaN),
                    "Invalid resolved range must fail closed.");
                action.CompletedEffectId="unrelated";
                Check.True(!NpcTaskPositionBinding.HasCompletedMoveToReceipt(previous,action,wait),
                    "Position tolerance cannot bypass the completed-step receipt.");
            }));
            cases.Add(("Wait after Enter keeps the exact public doorway until its interior landing",()=>{
                var enter=new NpcActionOption {Id=1,Kind=NpcActionKind.Enter,Target=new NpcActionTarget {
                    Kind=NpcActionTargetKind.Building,SettlementId=1,BuildingKey=1234,DoorId="door:1:1234:0:0:0",
                    WorldSpaceId="settlement:1",DestinationSpaceId="interior:1:1234"}};
                var wait=Option(2,NpcActionKind.Wait);
                var clauses=new List<string>{"Enter the inn","wait here for 30 seconds."};
                Check.True(clauses!=null && clauses.Count==2,
                    "Enter followed by an explicit timed Wait must remain an ordered task.");
                var boundWaits=NpcActionSequenceChat.BindWaitOptionsAfterMoveTo(
                    new NpcActionDecision {Option=enter,DurationSeconds=300},new[]{wait});
                Check.True(boundWaits.Count==2 && boundWaits[1].Id!=wait.Id &&
                    NpcTaskContract.TargetMatches(enter.Target,boundWaits[1].Target),
                    "Enter must add a distinct Wait option bound to its exact native door.");
                var parsed=TypedSequence(clauses,new List<IList<NpcActionOption>> {new[]{enter},boundWaits},
                    new[]{"ACCEPT|1|300","ACCEPT|"+boundWaits[1].Id+"|30"});
                Check.True(parsed!=null && parsed.Count==2 &&
                    NpcTaskContract.TargetMatches(enter.Target,parsed[1].Option.Target),
                    "The selected Wait must retain the entered building and door identity.");
                var entering=new NpcActionJob {Id="enter-wait:0",ProposalId="enter-wait:0",ResidentId="resident:1",
                    Kind=NpcActionKind.Enter,Target=NpcActionExecutor.CopyTarget(enter.Target),Phase=NpcActionPhase.Executing,
                    Revision=2,AcceptedAt=100,UpdatedAt=105,DeadlineAt=200};
                var receipt=NpcBuildingTransitionReceipt.Create(entering,"resident:1",1,1234,enter.Target.DoorId,
                    "settlement:1","interior:1:1234",110);
                NpcActionJob completed;
                Check.True(NpcActionExecutor.Advance(entering,2,110,NpcActionObservation.BuildingTransitionSuccess(receipt),out completed),
                    "The exact native Enter receipt must complete before its Wait can be considered.");
                var prior=new NpcTaskStep {Kind=NpcActionKind.Enter,Target=NpcActionExecutor.CopyTarget(enter.Target),
                    CompletedEffectId=completed.CompletedEffectId};
                var waiting=new NpcTaskStep {Kind=NpcActionKind.Wait,Target=NpcActionExecutor.CopyTarget(enter.Target),DurationSeconds=30};
                Check.True(!NpcTaskPositionBinding.HasCompletedStaticArrivalReceipt(prior,completed,waiting),
                    "An unobserved interior must not start its bound Wait.");
                completed.BuildingTransitionReceipt.DestinationObserved=true;
                Check.True(NpcTaskPositionBinding.HasCompletedStaticArrivalReceipt(prior,completed,waiting),
                    "Only the matching observed interior landing may start its bound Wait.");
            }));
            cases.Add(("Sequence chat rejects Follow before the final step",()=>{
                var follow=Option(1,NpcActionKind.Follow);var wait=Option(2,NpcActionKind.Wait);
                Check.True(NpcActionSequenceChat.ParseSemantic(new[]{"Come with me","Stay here"},
                    new List<IList<NpcActionOption>>{new[]{follow},new[]{wait}},
                    new[]{"DIRECT|Follow|1|1|30","DIRECT|Wait|2|1|30"})==null);
            }));
            
        }
    }
}

