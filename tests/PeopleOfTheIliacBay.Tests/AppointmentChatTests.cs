using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class AppointmentChatTests
    {
        private static List<ConversationTopic> Places()
        {
            return new List<ConversationTopic> {
                new ConversationTopic(42, "The Copper Kettle", "Taverns / The Copper Kettle", false),
                new ConversationTopic(5, "Taverns", "Taverns", true),
                new ConversationTopic(17, "The Fox and Firkin", "Taverns / The Fox and Firkin", false)
            };
        }

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Semantic appointments bind invitations without legacy wording gate", () => {
                const string request="Tomorrow, 16:30 at the Copper Kettle: shall we spend an hour together?";
                Check.True(SemanticAppointmentChat.Prompt(request,"","",Places(),765,new AppointmentCalendar(405,1,5),0).Contains(request));
                var choice=SemanticAppointmentChat.Parse("DIRECT|BOOK|1|990|60|42|1",Places(),765,0);
                Check.Equal("BOOK",choice.Kind); Check.Equal(990,choice.MinuteOfDay);
                Check.Equal(42,choice.BuildingKey);
            }));
            cases.Add(("Semantic appointments reject non-direct output and invalid game references", () => {
                foreach(var output in new[]{"BOOK|1|990|60|42|1","REPORTED|BOOK|1|990|60|42|1",
                    "DIRECT|BOOK|1|990|60|999|1","DIRECT|BOOK|1|990|60|5|1",
                    "DIRECT|BOOK|366|990|60|42|1","DIRECT|BOOK|1|1440|60|42|1",
                    "DIRECT|BOOK|1|990|14|42|1","DIRECT|BOOK|1|990|60|42|4",
                    "DIRECT|BOOK|1|990|60|42|1\nDo it!","DIRECT|CANCEL|0"})
                    Check.True(SemanticAppointmentChat.Parse(output,Places(),765,0)==null,output);
                Check.Equal("CLARIFY",SemanticAppointmentChat.Parse("DIRECT|BOOK|0|700|60|42|1",Places(),765,0).Kind);
                Check.Equal("CANCEL",SemanticAppointmentChat.Parse("DIRECT|CANCEL|1",Places(),765,2).Kind);
                Check.True(SemanticAppointmentChat.Parse("DIRECT|CANCEL|2",Places(),765,2)==null);
                Check.True(SemanticAppointmentChat.Parse("DIRECT|BOOK|1|990|60|42|1",
                    new[]{Places()[0],Places()[0]},765,0)==null);
            }));
            cases.Add(("Semantic appointment prompt preserves original speech and bounded native choices", () => {
                string prompt=SemanticAppointmentChat.Prompt("Could tomorrow's fourth bell find us sharing a table?","Player: At the Copper Kettle.\nNPC: What time?",
                    "Actual current buildingkey=42. Disposition 80.",Places(),765,new AppointmentCalendar(405,1,5),2);
                Check.True(prompt.Contains("fourth bell")&&prompt.Contains("Player: At the Copper Kettle."));
                Check.True(prompt.Contains("negated")&&prompt.Contains("CLARIFY")&&prompt.Contains("30 days"));
                string grammar=SemanticAppointmentChat.GrammarFor(Places(),2);
                Check.True(grammar.Contains("42")&&grammar.Contains("17")&&!grammar.Contains("999"));
                string empty=SemanticAppointmentChat.GrammarFor(new ConversationTopic[0],0);
                Check.True(!empty.Contains("BOOK")&&!empty.Contains("CANCEL"));
            }));
        }
    }
}
