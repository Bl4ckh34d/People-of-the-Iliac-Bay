using System;
using System.Collections.Generic;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public enum ConversationContextToolId { BrowseTopics, ReadTopic, ReadOwnInventory, ReadPlan, RecallMemory, ReadMoodCauses }
    public enum ConversationContextToolStatus { Ok, NoResults, Unavailable, InvalidRequest, StalePage }

    public sealed class ConversationContextToolRequest
    {
        public ConversationContextToolId Tool;
        public string Query, Category, ExpectedOwnerId;
        public int Page, PageSize;
        // -1 means no cursor revision; page zero is the only valid uncursored page.
        public long ExpectedRevision=-1;
    }

    public sealed class ConversationContextInventoryItem
    {
        public string Name, Category, Properties;
        public int Quantity;
    }

    public sealed class ConversationContextToolRow
    {
        public string Id, Category, Name, Properties;
        public int Quantity;
    }

    public sealed class ConversationContextToolResult
    {
        public ConversationContextToolId Tool;
        public ConversationContextToolStatus Status;
        public string OwnerId, AudienceId;
        public long Revision;
        public int Page, PageSize;
        public bool HasMore;
        public List<ConversationContextToolRow> Rows=new List<ConversationContextToolRow>();
    }

    // Bounded, detached read results. This layer applies filtering and paging;
    // native ownership, current-speaker and source freshness checks stay runtime-owned.
    public static class ConversationContextTools
    {
        public const int DefaultPageSize=12, MaximumPageSize=20, MaximumPage=512, MaximumContextCharacters=3000;
        public const int MaximumQueryLength=160, MaximumCategoryLength=80;
        public const int MaximumKnowledgeTopics=24;

        public static IList<NpcDisclosurePublicTopic> PublicKnowledgeTopics(NpcKnowledgeSnapshot snapshot)
        {
            return snapshot==null?new List<NpcDisclosurePublicTopic>():snapshot.PublicTopicMetadata(MaximumKnowledgeTopics);
        }

        public static ConversationContextToolResult ReadNpcKnowledgeTopic(NpcKnowledgeSnapshot snapshot,
            string actorId,string audienceId,ConversationContextToolRequest request)
        {
            if(request==null || request.Tool!=ConversationContextToolId.ReadTopic || !ValidRequest(request) ||
                request.PageSize>1 || string.IsNullOrWhiteSpace(request.Query) ||
                request.Query.Length>128 || string.IsNullOrWhiteSpace(actorId) || actorId.Length>240 || snapshot==null)
                return Invalid(ConversationContextToolId.ReadTopic);
            if(!string.Equals(snapshot.ActorId,actorId,StringComparison.Ordinal) ||
                !string.Equals(audienceId,NpcDisclosurePolicy.PlayerAudienceId,StringComparison.Ordinal))
                return Unavailable(ConversationContextToolId.ReadTopic,actorId);
            var cursor=ValidateCursor(request,actorId,snapshot.Revision);
            if(cursor!=ConversationContextToolStatus.Ok)
                return Result(ConversationContextToolId.ReadTopic,cursor,actorId,snapshot.Revision,request);
            if(request.Page!=0)
                return Result(ConversationContextToolId.ReadTopic,ConversationContextToolStatus.StalePage,actorId,snapshot.Revision,request);
            var result=Result(ConversationContextToolId.ReadTopic,ConversationContextToolStatus.NoResults,
                actorId,snapshot.Revision,request);
            result.AudienceId=NpcDisclosurePolicy.PlayerAudienceId;
            result.PageSize=1;
            NpcDisclosureToolRead read;
            if(!NpcDisclosurePolicy.TryReadPublicToolTopic(snapshot,actorId,audienceId,request.Query,out read))
                return result;
            result.Status=ConversationContextToolStatus.Ok;
            result.Rows.Add(new ConversationContextToolRow {Id=Clean(read.PropositionId,64),
                Category=read.EpistemicKind.ToString(),Name=Clean(read.ValueText,128),
                Properties=Clean(read.AttributionId,160)});
            return result;
        }

        public static ConversationContextToolResult BrowseTopics(IList<ConversationTopic> topics,string ownerId,long revision,
            ConversationContextToolRequest request)
        {
            if(request==null || request.Tool!=ConversationContextToolId.BrowseTopics || !ValidRequest(request) ||
                string.IsNullOrWhiteSpace(ownerId) || ownerId.Length>240 || revision<0)
                return Invalid(ConversationContextToolId.BrowseTopics);
            if(topics==null)return Unavailable(ConversationContextToolId.BrowseTopics,ownerId);
            var cursor=ValidateCursor(request,ownerId,revision);
            if(cursor!=ConversationContextToolStatus.Ok)return Result(ConversationContextToolId.BrowseTopics,cursor,ownerId,revision,request);

            var matches=new List<ConversationContextToolRow>();
            foreach(var topic in topics)
            {
                if(topic==null)continue;
                string category=Clean(topic.Path,MaximumCategoryLength);
                string name=Clean(topic.Caption,160);
                if(!Matches(category,name,request.Query,request.Category))continue;
                matches.Add(new ConversationContextToolRow {Id=topic.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Category=category,Name=name,Properties=topic.Group?"group":"topic"});
            }
            return Page(ConversationContextToolId.BrowseTopics,ownerId,revision,request,matches,false);
        }

        public static ConversationContextToolResult PageInventory(string personId,long revision,
            IList<ConversationContextInventoryItem> items,ConversationContextToolRequest request)
        {
            if(request==null || request.Tool!=ConversationContextToolId.ReadOwnInventory || !ValidRequest(request) ||
                string.IsNullOrWhiteSpace(personId) || personId.Length>240 || revision<0)
                return Invalid(ConversationContextToolId.ReadOwnInventory);
            if(items==null)return Unavailable(ConversationContextToolId.ReadOwnInventory,personId);
            var cursor=ValidateCursor(request,personId,revision);
            if(cursor!=ConversationContextToolStatus.Ok)return Result(ConversationContextToolId.ReadOwnInventory,cursor,personId,revision,request);

            var matches=new List<ConversationContextToolRow>();
            foreach(var item in items)
            {
                if(item==null || item.Quantity<1)continue;
                string category=Clean(item.Category,MaximumCategoryLength);
                string name=Clean(item.Name,160);
                string properties=Clean(item.Properties,160);
                if(string.IsNullOrWhiteSpace(name) || !Matches(category,name+" "+properties,request.Query,request.Category))continue;
                matches.Add(new ConversationContextToolRow {Category=category,Name=name,Quantity=item.Quantity,Properties=properties});
            }
            return Page(ConversationContextToolId.ReadOwnInventory,personId,revision,request,matches,true);
        }

        public static ConversationContextToolResult Unavailable(ConversationContextToolId tool,string ownerId)
        {return new ConversationContextToolResult {Tool=tool,Status=ConversationContextToolStatus.Unavailable,OwnerId=Clean(ownerId,240)};}

        public static string Format(ConversationContextToolResult result)
        {
            if(result==null)return "Requested information is unavailable.";
            var text=new StringBuilder();
            if(result.Status==ConversationContextToolStatus.Unavailable)return "This information is not available for the current speaker.";
            if(result.Status==ConversationContextToolStatus.InvalidRequest)return "The information request is invalid or outside its bounds.";
            if(result.Status==ConversationContextToolStatus.StalePage)return "The information changed or the page belongs to a different speaker. Start a new query.";
            if(result.Status==ConversationContextToolStatus.NoResults)
                return result.Tool==ConversationContextToolId.BrowseTopics?"No matching topic metadata was found.":
                    result.Tool==ConversationContextToolId.ReadTopic?"No public statement is available for that topic.":
                    "No matching current inventory items were found.";
            if(result.Tool==ConversationContextToolId.ReadOwnInventory && result.Rows.Count==0)
                return "Current personal inventory contains no items.";
            if(result.Tool==ConversationContextToolId.ReadTopic) text.Append("One actor-known public statement");
            else text.Append(result.Tool==ConversationContextToolId.BrowseTopics?"Matching topic metadata":"Current personal inventory");
            text.Append(" (page ").Append(result.Page+1).Append("): ");
            int written=0;bool omitted=false;
            for(int i=0;i<result.Rows.Count;i++)
            {
                var row=result.Rows[i];
                var line=new StringBuilder();
                if(!string.IsNullOrEmpty(row.Id))line.Append(row.Id).Append(" | ");
                if(!string.IsNullOrEmpty(row.Category))line.Append(row.Category).Append(" | ");
                line.Append(row.Name);
                if(row.Quantity>0)line.Append(" x ").Append(row.Quantity);
                if(!string.IsNullOrEmpty(row.Properties))line.Append(" ( ").Append(row.Properties).Append(" )");
                int separator=written>0?2:0;
                if(text.Length+line.Length+separator>MaximumContextCharacters){omitted=true;break;}
                if(separator>0)text.Append("; ");
                text.Append(line);
                written++;
            }
            if(result.HasMore||omitted)
            {
                const string more=" More results are available on the next page.";
                if(text.Length+more.Length<=MaximumContextCharacters)text.Append(more);
            }
            if(text.Length>MaximumContextCharacters)text.Length=MaximumContextCharacters;
            return text.ToString();
        }

        private static ConversationContextToolResult Page(ConversationContextToolId tool,string ownerId,long revision,
            ConversationContextToolRequest request,List<ConversationContextToolRow> matches,bool inventory)
        {
            int size=PageSize(request.PageSize);
            int start=request.Page*size;
            int count=start>=matches.Count?0:Math.Min(size,matches.Count-start);
            bool filtered=!string.IsNullOrWhiteSpace(request.Query)||!string.IsNullOrWhiteSpace(request.Category);
            var result=Result(tool,matches.Count==0&&(!inventory||filtered)?ConversationContextToolStatus.NoResults:ConversationContextToolStatus.Ok,
                ownerId,revision,request);
            result.PageSize=size;result.HasMore=start+count<matches.Count;
            for(int i=0;i<count;i++)
            {
                var row=matches[start+i];
                result.Rows.Add(new ConversationContextToolRow {Id=Clean(row.Id,64),Category=Clean(row.Category,MaximumCategoryLength),
                    Name=Clean(row.Name,160),Quantity=row.Quantity,Properties=Clean(row.Properties,160)});
            }
            return result;
        }

        private static ConversationContextToolStatus ValidateCursor(ConversationContextToolRequest request,string ownerId,long revision)
        {
            if(request.Page==0)
                return request.ExpectedRevision==-1 && string.IsNullOrEmpty(request.ExpectedOwnerId)?
                    ConversationContextToolStatus.Ok:ConversationContextToolStatus.StalePage;
            if(string.IsNullOrWhiteSpace(request.ExpectedOwnerId)||request.ExpectedRevision<0)
                return ConversationContextToolStatus.InvalidRequest;
            return request.ExpectedOwnerId==ownerId && request.ExpectedRevision==revision?
                ConversationContextToolStatus.Ok:ConversationContextToolStatus.StalePage;
        }

        private static bool ValidRequest(ConversationContextToolRequest request)
        {
            return request.Page>=0 && request.Page<=MaximumPage && request.PageSize>=0 && request.PageSize<=MaximumPageSize &&
                request.ExpectedRevision>=-1 && (request.Query==null || request.Query.Length<=MaximumQueryLength) &&
                (request.Category==null || request.Category.Length<=MaximumCategoryLength) &&
                (request.ExpectedOwnerId==null || request.ExpectedOwnerId.Length<=240);
        }

        private static bool Matches(string category,string name,string query,string categoryFilter)
        {
            if(!string.IsNullOrEmpty(categoryFilter) && !category.Equals(categoryFilter,StringComparison.OrdinalIgnoreCase) &&
                !category.StartsWith(categoryFilter+" / ",StringComparison.OrdinalIgnoreCase))return false;
            return string.IsNullOrWhiteSpace(query) || name.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0 ||
                category.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0;
        }

        private static int PageSize(int requested){return requested==0?DefaultPageSize:requested;}
        private static string Clean(string value,int maximum)
        {
            if(string.IsNullOrWhiteSpace(value))return string.Empty;
            var text=new StringBuilder(value.Length);bool space=false;
            foreach(char c in value)
            {
                if(char.IsControl(c)||char.IsWhiteSpace(c)){if(!space && text.Length>0)text.Append(' ');space=true;}
                else {text.Append(c);space=false;}
                if(text.Length>=maximum)break;
            }
            return text.ToString().Trim();
        }

        private static ConversationContextToolResult Invalid(ConversationContextToolId tool)
        {return new ConversationContextToolResult {Tool=tool,Status=ConversationContextToolStatus.InvalidRequest};}
        private static ConversationContextToolResult Result(ConversationContextToolId tool,ConversationContextToolStatus status,
            string ownerId,long revision,ConversationContextToolRequest request)
        {return new ConversationContextToolResult {Tool=tool,Status=status,OwnerId=Clean(ownerId,240),Revision=revision,
            Page=request.Page,PageSize=PageSize(request.PageSize)};}
    }
}
