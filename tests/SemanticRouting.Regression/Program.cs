using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PeopleOfTheIliacBay.DFU;
using PeopleOfTheIliacBay.Core;

class Program
{
    static int failures;
    static readonly ConversationTopic[] Topics = {
        new ConversationTopic(1,"Armorers","Shops",true),
        new ConversationTopic(2,"The Rusty Anvil","Armorers",false),
        new ConversationTopic(3,"Copper Arms","Armorers",false)
    };
    static void Check(string name, bool passed)
    { Console.WriteLine((passed ? "PASS " : "FAIL ") + name); if(!passed) failures++; }
    static int Main(string[] args)
    {
        if(args.Length==1 && args[0]=="--live")return Live();
        Check("guide is a model route", ConversationKnowledge.ParseRouting("guide|-1|polite|none")?.Intent=="guide");
        Check("model-selected leaf is not replaced by named category",
            ConversationKnowledge.Parse("topic|2|neutral",Topics,"Armorers",-1)?.TopicId==2);
        Check("validated map decision accepts a free English paraphrase",
            ConversationKnowledge.Parse("map|2|neutral",Topics,"Could you put a little cross where that shop stands?",-1)?.TopicId==2);
        Check("ambiguous model result cannot authorize previous map target",
            ConversationKnowledge.Parse("unknown|-1|neutral",Topics,"Mark it on my map",2)?.Intent=="unknown");
        Check("duplicate native IDs are rejected",
            ConversationKnowledge.Parse("topic|2|neutral",new[]{Topics[1],Topics[1]},"shop",-1)==null);
        Check("protocol ID cannot have an internal space",ConversationKnowledge.ParseTopicSelection("topic| 2","topic",Topics)==null);
        Check("protocol ID cannot have a plus sign",ConversationKnowledge.ParseTopicSelection("topic|+2","topic",Topics)==null);
        Check("foreign route cannot bind map",ConversationKnowledge.ParseTopicSelection("topic|2","map",Topics)==null);
        Check("invented target rejected",ConversationKnowledge.ParseTopicSelection("map|999","map",Topics)==null);
        Check("unambiguous structured map accepted",ConversationKnowledge.ParseTopicSelection("map|2","map",Topics)?.TopicId==2);
        var wait=new NpcActionOption {Id=1,Kind=NpcActionKind.Wait,Target=new NpcActionTarget {
            Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:1",SettlementId=1,Quantity=1}};
        var follow=new NpcActionOption {Id=2,Kind=NpcActionKind.Follow,Target=new NpcActionTarget {
            Kind=NpcActionTargetKind.Person,PersonId="player",Quantity=1}};
        var sequenceOptions=new List<IList<NpcActionOption>> {new[]{wait},new[]{follow}};
        Check("negated duration cannot override the interpreted duration",NpcActionSequenceChat.ParseSemantic(
            new[]{"Wait here for two minutes, not 20 minutes.","Then accompany me."},
            sequenceOptions,new[]{"DIRECT|Wait|1|1|120","DIRECT|Follow|2|1|120"})!=null);
        Check("native duration limits remain enforced",NpcActionSequenceChat.ParseSemantic(
            new[]{"Wait a while.","Then accompany me."},sequenceOptions,
            new[]{"DIRECT|Wait|1|1|999999","DIRECT|Follow|2|1|120"})==null);
        Console.WriteLine("Failures: "+failures); return failures==0?0:1;
    }

    static int Live()
    {
        using(var client=new HttpClient {BaseAddress=new Uri("http://127.0.0.1:9011"),Timeout=TimeSpan.FromSeconds(90)})
        {
            var cases=new[]{
                ("Could you put a little cross where that shop stands?","NPC: The Rusty Anvil is an armorer nearby.","map"),
                ("No need to draw anything; just tell me where it is.","NPC: Would you like a map marker?","topic"),
                ("Would you mind showing me the way there in person?","NPC: The Rusty Anvil is an armorer nearby.","guide"),
                ("Someone asked you to lead them to a shop yesterday.","","social"),
                ("What are you doing out here?","NPC: Hello.","social"),
                ("Some clothes.","NPC: What would you like to trade?","trade"),
                ("I will hurt you if you refuse.","NPC: I cannot help with that.","social"),
                ("Could you come along behind me?","","action"),
                ("I'd rather not wander alone. Would you escort me to the armorer?","","guide"),
                ("You needn't take me there. An address will do.","NPC: I could lead you to the inn.","topic"),
                ("What errand brought you to the market today?","NPC: Hello.","social"),
                ("He threatened to break my arm; I came here to report it.","","social"),
                ("Keep your purse. I was asking where the bank is.","","topic"),
                ("Would you part with any of those boots?","NPC: You may look at my belongings.","trade"),
                ("I've changed my mind. Stop waiting for me.","NPC: I agreed to wait here.","action"),
                ("Let's arrange a meeting tomorrow afternoon.","","appointment")
            };
            foreach(var item in cases)
            {
                var completion=LocalDialogueCompletion.Classify(new LocalDialogueConnection("http://127.0.0.1:9011/completion",90000),
                    ConversationKnowledge.RoutingPrompt(item.Item1,item.Item2),item.Item2,ConversationKnowledge.RoutingGrammar(),128);
                string rendered=LocalDialogueNativeTemplate.ReadPrompt(Post(client,"/apply-template",LocalDialogueNativeTemplate.Request(completion.Prompt)));
                string response=Post(client,"/completion",LocalDialogueNativeTemplate.Payload(completion.JsonBody,rendered));
                using(var json=JsonDocument.Parse(response))
                {
                    string output=json.RootElement.GetProperty("content").GetString();
                    Console.WriteLine(JsonSerializer.Serialize(new {text=item.Item1,expected=item.Item3,output}));
                    var route=ConversationKnowledge.ParseRouting(output);
                    Check(item.Item1,route!=null && route.Intent==item.Item3 && route.SocialIntent=="none");
                }
            }
        }
        return failures==0?0:1;
    }
    static string Post(HttpClient client,string path,string body)
    {
        using(var response=client.PostAsync(path,new StringContent(body,Encoding.UTF8,"application/json")).GetAwaiter().GetResult())
        {response.EnsureSuccessStatusCode();return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();}
    }
}
