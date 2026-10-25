using System.Collections.Generic;

/// <summary>Bounded sidecar probe state keyed to one exact playback generation.</summary>
public sealed class PortraitSpeechLoadPolicy
{
    private const int MaximumRememberedOwners = 256;

    private sealed class Entry
    {
        public int ProbeCount;
        public float RetryAt;
        public bool InFlight;
        public bool Final;
    }

    private readonly Dictionary<PortraitSpeechOwnerKey, Entry> entries =
        new Dictionary<PortraitSpeechOwnerKey, Entry>();
    private readonly Queue<PortraitSpeechOwnerKey> insertionOrder = new Queue<PortraitSpeechOwnerKey>();

    /// <summary>Reserve one attempt. Returns false while in flight, delayed, final, or over budget.</summary>
    public bool TryBegin(PortraitSpeechOwnerKey owner, float now, int maximumProbes)
    {
        if (maximumProbes <= 0) return false;
        Entry entry = GetOrCreate(owner);
        if (entry.Final || entry.InFlight || entry.ProbeCount >= maximumProbes || now < entry.RetryAt)
        {
            if (entry.ProbeCount >= maximumProbes) entry.Final = true;
            return false;
        }

        entry.ProbeCount++;
        entry.InFlight = true;
        entry.RetryAt = 0f;
        return true;
    }

    /// <summary>Schedule another probe, or finalize once the playback-generation budget is spent.</summary>
    public void Defer(PortraitSpeechOwnerKey owner, float retryAt, int maximumProbes)
    {
        Entry entry = GetOrCreate(owner);
        entry.InFlight = false;
        if (entry.ProbeCount >= maximumProbes) entry.Final = true;
        else entry.RetryAt = retryAt;
    }

    /// <summary>Finalize this exact playback generation after success or a permanent error.</summary>
    public void MarkFinal(PortraitSpeechOwnerKey owner)
    {
        Entry entry = GetOrCreate(owner);
        entry.InFlight = false;
        entry.Final = true;
    }

    public int ProbeCount(PortraitSpeechOwnerKey owner)
    {
        Entry entry;
        return entries.TryGetValue(owner, out entry) ? entry.ProbeCount : 0;
    }

    private Entry GetOrCreate(PortraitSpeechOwnerKey owner)
    {
        Entry entry;
        if (entries.TryGetValue(owner, out entry)) return entry;

        entry = new Entry();
        entries.Add(owner, entry);
        insertionOrder.Enqueue(owner);
        while (insertionOrder.Count > MaximumRememberedOwners)
            entries.Remove(insertionOrder.Dequeue());
        return entry;
    }
}
