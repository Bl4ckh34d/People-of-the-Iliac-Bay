using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // Shared exact lexical feature extraction for the scan and compact index.
    internal static class ConversationFeatureExtractor
    {
        private static readonly HashSet<string> Ignored=new HashSet<string>((
            "a an am is it its he him his she her hers they them their we us our ours i me my mine you your yours " +
            "what when where how who which have has had was were are the and or of to in on at for as by be been " +
            "that this those these with from do does did can could would should will shall not yes no " +
            "remember recall remind about tell told say said name named call called earlier before again please").Split(' '));
        private static readonly Dictionary<string,string> Concepts=BuildConcepts();

        internal sealed class TextSpan
        {
            internal readonly int Index;
            internal readonly string Value;
            internal TextSpan(int index,string value){Index=index;Value=value;}
        }

        // Lexical retrieval only: segmentation never authorizes a game action.
        internal static IEnumerable<TextSpan> MatchTokens(string text)
        {
            text=text??string.Empty;
            int start=-1;
            for(int i=0;i<=text.Length;i++)
            {
                if(i<text.Length && (char.IsLetter(text[i]) || char.IsNumber(text[i])))
                {if(start<0)start=i;continue;}
                if(start>=0 && i-start>=2)yield return new TextSpan(start,text.Substring(start,i-start));
                start=-1;
            }
        }

        internal static IEnumerable<TextSpan> Clauses(string text)
        {
            int start=0;
            for(int i=0;i<text.Length;i++)
            {
                if(text[i]!='.' && text[i]!='!' && text[i]!='?')continue;
                if(i>start)yield return new TextSpan(start,text.Substring(start,i-start+1));
                start=i+1;
            }
            if(start<text.Length)yield return new TextSpan(start,text.Substring(start));
        }

        internal static HashSet<string> Extract(string text)
        {
            var features=new HashSet<string>(System.StringComparer.Ordinal);
            foreach(var match in MatchTokens(text))
            {
                string word=match.Value.ToLowerInvariant();
                if(Ignored.Contains(word)) continue;
                string stem=Stem(word);
                features.Add("w:"+word);features.Add("s:"+stem);
                string concept;
                if(Concepts.TryGetValue(stem,out concept))features.Add("c:"+concept);
            }
            return features;
        }

        private static string Stem(string word)
        {
            if(word.Length>4&&word.EndsWith("ies",System.StringComparison.Ordinal))word=word.Substring(0,word.Length-3)+"y";
            else if(word.Length>4&&word.EndsWith("s",System.StringComparison.Ordinal)&&!word.EndsWith("ss",System.StringComparison.Ordinal))word=word.Substring(0,word.Length-1);
            if(word.Length>5&&word.EndsWith("ing",System.StringComparison.Ordinal))word=word.Substring(0,word.Length-3);
            else if(word.Length>4&&word.EndsWith("ed",System.StringComparison.Ordinal))word=word.Substring(0,word.Length-2);
            if(word.Length>3&&word.EndsWith("e",System.StringComparison.Ordinal))word=word.Substring(0,word.Length-1);
            return word;
        }

        private static Dictionary<string,string> BuildConcepts()
        {
            var result=new Dictionary<string,string>(System.StringComparer.Ordinal);
            string[] groups={"horse mare stallion steed mount pony","inn tavern hostel lodging","parcel package bundle",
                "occupation profession job work trade","buy bought purchase purchased","sell sold sale","thief thieves robber burglar",
                "take took taken steal stole stolen theft","home house residence live lived dwelling","sick ill illness disease",
                "heal healed cure cured remedy medicine","travel journey trip voyage","lost missing misplaced",
                "hide hid hidden conceal concealed","meet met meeting encounter","weapon sword blade dagger","money gold coin coins payment"};
            foreach(string group in groups)
            {
                string[] words=group.Split(' ');
                foreach(string word in words)result[Stem(word)]=words[0];
            }
            return result;
        }
    }
}
