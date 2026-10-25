/// <summary>Requires an explicit command-line opt-in for legacy social sentence streaming.</summary>
internal static class BreezeTtsStreamingPolicy
{
    internal static bool IsEnabled(bool voiceEnabled, bool settingEnabled,
        bool endpointConfigured, bool legacyStreamingOptIn)
    {
        return voiceEnabled && settingEnabled && endpointConfigured && legacyStreamingOptIn;
    }
}
