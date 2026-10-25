using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcSocialAvailabilityTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            

            cases.Add(("Structured busy-pause intent accepts semantic paraphrases while preserving none attribution",new Action(()=>{
                var topics=new List<ConversationTopic>();
                var paraphrase=ConversationKnowledge.Parse("social|-1|neutral|busy_pause",topics,
                    "An urgent matter came up; I should get back to the guild before dark.",-1);
                Check.True(paraphrase!=null, "semantic departure paraphrase should pass");
                Check.Equal("busy_pause",paraphrase.SocialIntent);

                foreach(string text in new[] {"Do you think I should get back to the guild?",
                    "I do not need to get back to the guild.","My sister said I should get back to the guild.",
                    "'I should get back to the guild' is what he told me.","I am not busy.","I can stay here."})
                    Check.Equal("none",ConversationKnowledge.Parse("social|-1|neutral|none",topics,text,-1).SocialIntent);
                Check.Equal("busy_pause",ConversationKnowledge.Parse("social|-1|busy_departure",topics,
                    "I have a prior commitment and need to return to the guild.",-1).SocialIntent);
                Check.True(ConversationKnowledge.Parse("social|-1|neutral|busy_pause",topics,
                    "Do you think I should get back to the guild?",-1)!=null,
                    "Parser validates structured fields; classifier owns semantic attribution");
            })));

            cases.Add(("Structured family-threat intent supports semantic paraphrases and keeps classifier attribution separate",new Action(()=>{
                var topics=new List<ConversationTopic>();
                var semantic=ConversationKnowledge.Parse("social|-1|neutral|family_threat",topics,
                    "Your family will regret what you did.",-1);
                Check.True(semantic!=null,"semantic threat should not require exact threat regex");
                Check.Equal("family_threat",semantic.SocialIntent);
                foreach(string text in new[] {"Do you think your family will pay?","I will not hurt your family.",
                    "He said your family would pay.","'Your family will regret this.'", "If your family gets in my way, I will act."})
                    Check.Equal("none",ConversationKnowledge.Parse("social|-1|neutral|none",topics,text,-1).SocialIntent);
            })));

            cases.Add(("Conversation classifier prompt scopes social intent to current player message",new Action(()=>{
                string prompt=ConversationKnowledge.Prompt("I have to get back to work.",new List<ConversationTopic>(),-1,
                    "Earlier, the NPC said: I need to leave.");
                Check.True(prompt.Contains("busy_pause"),"classifier must request structured busy_pause intent");
                Check.True(prompt.Contains("family_threat"),"classifier must request structured family_threat intent");
                Check.True(prompt.Contains("intent|topicID|tone|socialIntent"),"classifier must state four-field grammar");
                Check.True(prompt.Contains("only the current player message"),"classifier must use current turn only");
                Check.True(prompt.Contains("questions") && prompt.Contains("hypotheticals") &&
                    prompt.Contains("quoted or reported speech") && prompt.Contains("negated departure"),
                    "classifier must exclude non-current or non-assertive speech");
            })));

            cases.Add(("Structured classifier distinguishes task actions and appointments from social turns",new Action(()=>{
                var topics=new List<ConversationTopic>();
                Check.Equal("action",ConversationKnowledge.Parse("action|-1|neutral|none",topics,
                    "Please buy me a loaf of bread.",-1).Intent);
                Check.Equal("appointment",ConversationKnowledge.Parse("appointment|-1|neutral|none",topics,
                    "Let's meet at the Copper Kettle tomorrow.",-1).Intent);
                Check.True(ConversationKnowledge.Parse("appointment|4|neutral|none",topics,
                    "Let's meet tomorrow.",-1)==null);
                Check.True(ConversationKnowledge.Parse("action|-1|neutral|busy_pause",topics,
                    "I need to leave, but please buy me a loaf of bread.",-1)==null);
            })));

            cases.Add(("Classifier parser preserves legacy output and rejects malformed structured effects",new Action(()=>{
                var topics=new List<ConversationTopic>();
                var legacy=ConversationKnowledge.Parse("social|-1|neutral",topics,"Hello.",-1);
                Check.True(legacy!=null);
                Check.Equal("none",legacy.SocialIntent);
                Check.Equal("family_threat",ConversationKnowledge.Parse("social|-1|family_threat",topics,
                    "Your family will regret this.",-1).SocialIntent);
                Check.Equal("busy_pause",ConversationKnowledge.Parse("social|-1|busy_departure",topics,
                    "I have to leave.",-1).SocialIntent);

                Check.True(ConversationKnowledge.Parse("social|-1|neutral|made_up",topics,"Hello.",-1)==null);
                Check.True(ConversationKnowledge.Parse("social|-1|neutral|none|extra",topics,"Hello.",-1)==null);
                Check.True(ConversationKnowledge.Parse("action|2|neutral|none",topics,"Buy bread.",-1)==null);
                Check.True(ConversationKnowledge.Parse("appointment|2|neutral|none",topics,"Meet tomorrow.",-1)==null);
                var threatWithAction=ConversationKnowledge.Parse("action|-1|neutral|family_threat",topics,
                    "Your family will pay.",-1);
                Check.True(threatWithAction==null);
            })));

            cases.Add(("Default classifier grammar carries four structured fields",new Action(()=>{
                var connection=new LocalDialogueConnection("http://127.0.0.1:8080",1000);
                var completion=LocalDialogueCompletion.Classify(connection,"Classify this turn.");
                Check.True(completion.JsonBody.Contains("intent \\\"|\\\" number \\\"|\\\" tone \\\"|\\\" social-intent"),completion.JsonBody);
                Check.True(completion.JsonBody.Contains("\\\"appointment\\\""),completion.JsonBody);
                Check.True(completion.JsonBody.Contains("\\\"busy_pause\\\""),completion.JsonBody);
                Check.True(completion.JsonBody.Contains("\\\"family_threat\\\""),completion.JsonBody);
            })));
        }
    }
}
