using System.Text;

namespace PeopleOfTheIliacBay.DFU
{
    // Display metadata must never turn an otherwise valid physical transfer into
    // a refusal. The native UID remains the item identity.
    internal static class NativeItemReceiptLabel
    {
        internal static string Capture(string nativeName)
        {
            const int maximum=240;
            var label=new StringBuilder(maximum);
            bool space=false;
            if(nativeName!=null)foreach(char character in nativeName)
            {
                if(char.IsWhiteSpace(character) || char.IsControl(character))
                {space=label.Length>0;continue;}
                if(space && label.Length<maximum)label.Append(' ');
                space=false;
                if(label.Length>=maximum)break;
                label.Append(character);
            }
            string result=label.ToString().TrimEnd();
            return result.Length==0?"unidentified item":result;
        }
    }
}
