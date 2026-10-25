
namespace PeopleOfTheIliacBay.Core
{
    public static class ConversationHomeRequests
    {
        // +20 is the existing effective-disposition threshold for comfort with a familiar face.
        public static bool CanShareHome(int effectiveDisposition)
        { return effectiveDisposition >= 20; }
    }
}
