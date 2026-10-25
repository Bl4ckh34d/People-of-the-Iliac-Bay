namespace PeopleOfTheIliacBay.Core
{
    public static class ConversationAccess
    {
        public static bool CanStart(bool reportingToGuard, bool alarmed, bool holdsGrudge)
        { return CanStart(reportingToGuard, alarmed, holdsGrudge, false); }

        public static bool CanStart(bool reportingToGuard, bool alarmed, bool holdsGrudge, bool terminalHostility)
        { return CanStart(reportingToGuard, alarmed, holdsGrudge, terminalHostility, true); }

        public static bool CanStart(bool reportingToGuard, bool alarmed, bool holdsGrudge,
            bool terminalHostility, bool conversationDispositionEnabled)
        { return !reportingToGuard && !alarmed && !holdsGrudge &&
            (!conversationDispositionEnabled || !terminalHostility); }
    }
}
