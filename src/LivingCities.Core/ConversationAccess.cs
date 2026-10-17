namespace LivingCities.Core
{
    public static class ConversationAccess
    {
        public static bool CanStart(bool reportingToGuard, bool alarmed, bool holdsGrudge)
        { return !reportingToGuard && !alarmed && !holdsGrudge; }
    }
}
