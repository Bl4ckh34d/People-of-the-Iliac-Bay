namespace PeopleOfTheIliacBay.Core
{
    public static class AgenticDialogueUiPolicy
    {
        // The free-text LLM owns conversational tone while available. The
        // optional decision planner does not own this presentation policy.
        public static bool SuppressNativeToneChooser(bool freeTextEnabled, bool dialogueClientAvailable)
        { return freeTextEnabled && dialogueClientAvailable; }
    }
}
