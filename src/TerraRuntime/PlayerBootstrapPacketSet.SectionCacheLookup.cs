using System.Diagnostics;
using TerraRuntime.World;

namespace TerraRuntime;

internal enum SectionFrameLookupResult : byte
{
    Available = 0,
    Unavailable = 1,
    RateLimited = 2
}

internal readonly record struct SectionRebuildRequestTicket(bool Accepted, long Generation)
{
    public static SectionRebuildRequestTicket Rejected => new(false, 0);
}

public sealed partial class PlayerBootstrapPacketSet
{
    private static readonly TimeSpan SectionCacheLookupWaitTimeout = TimeSpan.FromSeconds(5);

    private readonly Dictionary<int, long> _sectionCacheFailedRebuildGenerations = new();
    private Func<WorldSectionId, SectionRebuildRequestTicket>? _sectionRebuildRequester;
    private long _sectionCacheHits;
    private long _sectionCacheMisses;
    private long _sectionCacheStaleReads;
    private long _sectionCacheWaits;
    private long _sectionCacheWaitCompletions;
    private long _sectionCacheWaitFailures;
    private long _sectionCacheWaitTimeouts;

    internal void AttachSectionRebuildRequester(Func<WorldSectionId, SectionRebuildRequestTicket> requester)
    {
        ArgumentNullException.ThrowIfNull(requester);
        if (Interlocked.CompareExchange(ref _sectionRebuildRequester, requester, null) is not null)
            throw new InvalidOperationException("A section cache rebuild requester is already attached.");
    }

    internal void DetachSectionRebuildRequester()
    {
        Volatile.Write(ref _sectionRebuildRequester, null);
        _sectionRebuildGlobalBudget.ClearActiveGenerations();
        NotifySectionCacheWaiters();
    }

    /// <summary>
    /// Resolves one packet-10 frame without performing section compression on the caller thread when the
    /// production rebuild pipeline is attached. A miss is submitted once through the pipeline and all
    /// concurrent callers wait on the same generation-specific cache publication boundary.
    /// </summary>
    internal bool TryGetOrRequestSectionFrame(
        WorldSectionId section,
        out ReadOnlyMemory<byte> frame) =>
        ResolveSectionFrame(section, out frame) == SectionFrameLookupResult.Available;

    internal SectionFrameLookupResult ResolveSectionFrame(
        WorldSectionId section,
        out ReadOnlyMemory<byte> frame)
    {
        frame = default;
        WorldFileData? world = _world;
        if (world is null)
            return SectionFrameLookupResult.Unavailable;

        int index = TerrariaSectionGeometry.ToLinearIndex(world.Header.Dimensions, section);
        lock (_sectionCacheGate)
        {
            long version = world.Tiles.GetSectionVersion(section);
            if ((version & 1L) == 0 &&
                _sectionCache.TryGetValue(index, out SectionCacheEntry cached) &&
                cached.Version == version)
            {
                Interlocked.Increment(ref _sectionCacheHits);
                frame = cached.TileSectionFrame;
                return SectionFrameLookupResult.Available;
            }

            Interlocked.Increment(ref _sectionCacheMisses);
            if (_sectionCache.ContainsKey(index))
                Interlocked.Increment(ref _sectionCacheStaleReads);
        }

        Func<WorldSectionId, SectionRebuildRequestTicket>? requester = Volatile.Read(ref _sectionRebuildRequester);
        if (requester is null)
        {
            // Startup/tests that intentionally use the packet set without the runtime pipeline retain the
            // correctness-first synchronous fallback. The production host attaches the pipeline before accept.
            if (!TryGetOrEncodeSection(section, out SectionCacheEntry entry))
                return SectionFrameLookupResult.Unavailable;

            frame = entry.TileSectionFrame;
            return SectionFrameLookupResult.Available;
        }

        SectionRebuildGlobalBudgetRequestResult requestResult = _sectionRebuildGlobalBudget.RequestDetailed(
            index,
            section,
            requester,
            out SectionRebuildRequestTicket ticket);
        if (requestResult == SectionRebuildGlobalBudgetRequestResult.GlobalRateLimited)
            return SectionFrameLookupResult.RateLimited;
        if (requestResult != SectionRebuildGlobalBudgetRequestResult.Accepted)
            return SectionFrameLookupResult.Unavailable;

        Interlocked.Increment(ref _sectionCacheWaits);
        long started = Stopwatch.GetTimestamp();
        lock (_sectionCacheGate)
        {
            while (true)
            {
                long currentVersion = world.Tiles.GetSectionVersion(section);
                if ((currentVersion & 1L) == 0 &&
                    _sectionCache.TryGetValue(index, out SectionCacheEntry current) &&
                    current.Version == currentVersion)
                {
                    Interlocked.Increment(ref _sectionCacheWaitCompletions);
                    frame = current.TileSectionFrame;
                    return SectionFrameLookupResult.Available;
                }

                if (_sectionCacheFailedRebuildGenerations.TryGetValue(index, out long failedGeneration) &&
                    failedGeneration == ticket.Generation)
                {
                    Interlocked.Increment(ref _sectionCacheWaitFailures);
                    return SectionFrameLookupResult.Unavailable;
                }

                if (Volatile.Read(ref _sectionRebuildRequester) is null)
                    return SectionFrameLookupResult.Unavailable;

                TimeSpan remaining = SectionCacheLookupWaitTimeout - Stopwatch.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero)
                {
                    Interlocked.Increment(ref _sectionCacheWaitTimeouts);
                    return SectionFrameLookupResult.Unavailable;
                }

                Monitor.Wait(_sectionCacheGate, remaining);
            }
        }
    }

    internal void NotifySectionCacheRebuildFailed(WorldSectionId section, long generation)
    {
        if (_world is null || generation <= 0)
            return;

        int index = TerrariaSectionGeometry.ToLinearIndex(_world.Header.Dimensions, section);
        lock (_sectionCacheGate)
        {
            _sectionCacheFailedRebuildGenerations[index] = generation;
            Monitor.PulseAll(_sectionCacheGate);
        }

        _sectionRebuildGlobalBudget.Complete(index, generation);
    }

    internal void NotifySectionCacheWaiters()
    {
        lock (_sectionCacheGate)
            Monitor.PulseAll(_sectionCacheGate);
    }
}
