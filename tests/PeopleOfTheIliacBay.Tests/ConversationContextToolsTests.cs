using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ConversationContextToolsTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            Action topicPaging = () => {
                var topics=new List<ConversationTopic> {
                    new ConversationTopic(3,"Crown and Anchor Inn","Where is / Location",false),
                    new ConversationTopic(8,"Local Temples","Where is / Location",true),
                    new ConversationTopic(9,"Temple of Kynareth","Where is / Location / Local Temples",false),
                    new ConversationTopic(14,"Fighters Guild","Where is / Location",false),
                };
                var request=new ConversationContextToolRequest {Tool=ConversationContextToolId.BrowseTopics,
                    Query="temple",Page=0,PageSize=1};
                var first=ConversationContextTools.BrowseTopics(topics,"npc:17",12,request);
                Check.Equal(ConversationContextToolStatus.Ok,first.Status);
                Check.Equal("8",first.Rows[0].Id);
                Check.True(first.HasMore);
                request.Page=1;request.ExpectedOwnerId="npc:17";request.ExpectedRevision=12;
                var next=ConversationContextTools.BrowseTopics(topics,"npc:17",12,request);
                Check.Equal(ConversationContextToolStatus.Ok,next.Status);
                Check.Equal("9",next.Rows[0].Id);
                Check.Equal(ConversationContextToolStatus.StalePage,
                    ConversationContextTools.BrowseTopics(topics,"npc:18",12,request).Status);
                Check.Equal(ConversationContextToolStatus.StalePage,
                    ConversationContextTools.BrowseTopics(topics,"npc:17",13,request).Status);
            };
            cases.Add(("Topic browsing filters native topic metadata and pages with owner revision binding", topicPaging));
            Action inventoryPaging = () => {
                var items=new List<ConversationContextInventoryItem> {
                    new ConversationContextInventoryItem {Name="Iron Arrow",Category="Ammunition",Quantity=7,Properties="weight 0.1"},
                    new ConversationContextInventoryItem {Name="Healing Potion",Category="Potion",Quantity=1,Properties=""},
                };
                var request=new ConversationContextToolRequest {Tool=ConversationContextToolId.ReadOwnInventory,
                    Query="arrow",PageSize=12};
                var result=ConversationContextTools.PageInventory("npc:17",5,items,request);
                Check.Equal(ConversationContextToolStatus.Ok,result.Status);
                Check.Equal(1,result.Rows.Count);
                Check.Equal("Iron Arrow",result.Rows[0].Name);
                Check.Equal(7,result.Rows[0].Quantity);
                string text=ConversationContextTools.Format(result);
                Check.True(text.Contains("Iron Arrow") && text.Contains("7"));
                Check.True(!text.Contains("UID") && !text.Contains("NativePayload"));
                items[0].Name="Changed";
                Check.Equal("Iron Arrow",result.Rows[0].Name);
            };
            cases.Add(("Own inventory query returns detached filtered item facts without item identity", inventoryPaging));
            Action emptyInventory = () => {
                var empty=ConversationContextTools.PageInventory("npc:17",0,new List<ConversationContextInventoryItem>(),
                    new ConversationContextToolRequest {Tool=ConversationContextToolId.ReadOwnInventory});
                Check.Equal(ConversationContextToolStatus.Ok,empty.Status);
                Check.Equal(0,empty.Rows.Count);
                Check.Equal(ConversationContextToolStatus.Unavailable,
                    ConversationContextTools.Unavailable(ConversationContextToolId.ReadOwnInventory,"npc:17").Status);
                Check.True(ConversationContextTools.Format(empty).Contains("no items"));
            };
            cases.Add(("Empty current inventory differs from unavailable inventory", emptyInventory));
            Action bounds = () => {
                Check.Equal(12,ConversationContextTools.DefaultPageSize);
                Check.Equal(20,ConversationContextTools.MaximumPageSize);
                Check.Equal(3000,ConversationContextTools.MaximumContextCharacters);
                var request=new ConversationContextToolRequest {Tool=ConversationContextToolId.BrowseTopics,
                    Query=new string('x',161),PageSize=21};
                Check.Equal(ConversationContextToolStatus.InvalidRequest,
                    ConversationContextTools.BrowseTopics(new List<ConversationTopic>(),"npc:17",1,request).Status);
                var topics=new List<ConversationTopic>();
                for(int i=0;i<40;i++)topics.Add(new ConversationTopic(i,"Topic "+i,"Location",false));
                var result=ConversationContextTools.BrowseTopics(topics,"npc:17",1,
                    new ConversationContextToolRequest {Tool=ConversationContextToolId.BrowseTopics,PageSize=20});
                Check.True(ConversationContextTools.Format(result).Length<=3000);
                Check.Equal(ConversationContextToolStatus.Unavailable,
                    ConversationContextTools.Unavailable(ConversationContextToolId.ReadPlan,"npc:17").Status);
                Check.Equal(ConversationContextToolStatus.Unavailable,
                    ConversationContextTools.Unavailable(ConversationContextToolId.RecallMemory,"npc:17").Status);
                Check.Equal(ConversationContextToolStatus.Unavailable,
                    ConversationContextTools.Unavailable(ConversationContextToolId.ReadTopic,"npc:17").Status);
            };
            cases.Add(("Context-tool input and formatted output stay bounded", bounds));
        }
    }
}
