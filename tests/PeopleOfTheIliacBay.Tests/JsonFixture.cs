using System.Text.Json.Nodes;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class JsonFixture
    {
        internal static string WithoutProperty(string json,string property)
        {
            var root=JsonNode.Parse(json);
            Remove(root,property);
            return root.ToJsonString();
        }
        private static void Remove(JsonNode node,string property)
        {
            if(node is JsonObject obj)
            {
                obj.Remove(property);
                foreach(var pair in obj)Remove(pair.Value,property);
            }
            else if(node is JsonArray array)foreach(var item in array)Remove(item,property);
        }
    }
}
