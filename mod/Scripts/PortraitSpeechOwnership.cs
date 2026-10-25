using System;

/// <summary>Identifies one active full-WAV playback generation for stale-cue rejection.</summary>
public readonly struct PortraitSpeechOwnerKey : IEquatable<PortraitSpeechOwnerKey>
{
    public readonly string RequestId;
    public readonly string PersonId;
    public readonly string ConversationToken;
    public readonly int ClipInstanceId;
    public readonly long Generation;

    public PortraitSpeechOwnerKey(string requestId, string personId, string conversationToken,
        int clipInstanceId, long generation)
    {
        if (string.IsNullOrWhiteSpace(requestId)) throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (string.IsNullOrWhiteSpace(personId)) throw new ArgumentException("Person ID is required.", nameof(personId));
        if (string.IsNullOrWhiteSpace(conversationToken))
            throw new ArgumentException("Conversation token is required.", nameof(conversationToken));
        if (clipInstanceId == 0) throw new ArgumentOutOfRangeException(nameof(clipInstanceId));
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));

        RequestId = requestId;
        PersonId = personId;
        ConversationToken = conversationToken;
        ClipInstanceId = clipInstanceId;
        Generation = generation;
    }

    public bool Matches(PortraitSpeechOwnerKey other)
    {
        return Generation == other.Generation && ClipInstanceId == other.ClipInstanceId &&
            string.Equals(RequestId, other.RequestId, StringComparison.Ordinal) &&
            string.Equals(PersonId, other.PersonId, StringComparison.Ordinal) &&
            string.Equals(ConversationToken, other.ConversationToken, StringComparison.Ordinal);
    }

    public bool Equals(PortraitSpeechOwnerKey other) { return Matches(other); }
    public override bool Equals(object obj) { return obj is PortraitSpeechOwnerKey && Equals((PortraitSpeechOwnerKey)obj); }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = StringComparer.Ordinal.GetHashCode(RequestId);
            hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(PersonId);
            hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(ConversationToken);
            hash = hash * 397 ^ ClipInstanceId;
            hash = hash * 397 ^ Generation.GetHashCode();
            return hash;
        }
    }
}
