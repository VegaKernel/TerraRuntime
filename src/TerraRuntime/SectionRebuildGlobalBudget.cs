using TerraRuntime.World;

namespace TerraRuntime;

internal readonly record struct SectionRebuildGlobalBudgetOptions(
    TimeSpan Window,
    int MaxUniqueRequests)
{
    /// <summary>
    /// Server-wide hard-abuse ceiling for new section-compression generations. The limit intentionally matches
    /// the byte-sized player-slot ceiling and the default on-demand pending capacity: at most one complete set of
    /// distinct pending generations may be admitted per second, regardless of configured player count.
    /// </summary>
    public static SectionRebuildGlobalBudgetOptions HardAbuse { get; } =
        new(TimeSpan.FromSeconds(1), byte.MaxValue);

    public void Validate()
    {
        if (Window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(Window));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxUniqueRequests, 1);
    }
}

internal readonly record struct SectionRebuildGlobalBudgetSnapshot(
    long Requests,
    long UniqueAdmissions,
    long DeduplicatedRequests,
    long RejectedRequests,
    int ActiveGenerations,
    int CurrentWindowUniqueAdmissions,
    int MaxUniqueRequests,
    TimeSpan Window);

/// <summary>
/// Thread-safe server-global admission budget for expensive on-demand section rebuild generations.
/// A generation is charged once when the first waiter creates it; additional waiters sharing the same
/// single-flight generation do not consume more global compression budget.
/// </summary>
internal sealed class SectionRebuildGlobalBudget
{
    private readonly object _gate = new();
    private readonly SectionRebuildGlobalBudgetOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<int, long> _activeGenerations = [];
    private long _windowStartTimestamp;
    private int _currentWindowUniqueAdmissions;
    private long _requests;
    private long _uniqueAdmissions;
    private long _deduplicatedRequests;
    private long _rejectedRequests;

    public SectionRebuildGlobalBudget(
        SectionRebuildGlobalBudgetOptions options,
        TimeProvider? timeProvider = null)
    {
        options.Validate();
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _windowStartTimestamp = _timeProvider.GetTimestamp();
    }

    public SectionRebuildGlobalBudgetSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                RefreshWindowLocked();
                return new SectionRebuildGlobalBudgetSnapshot(
                    Requests: _requests,
                    UniqueAdmissions: _uniqueAdmissions,
                    DeduplicatedRequests: _deduplicatedRequests,
                    RejectedRequests: _rejectedRequests,
                    ActiveGenerations: _activeGenerations.Count,
                    CurrentWindowUniqueAdmissions: _currentWindowUniqueAdmissions,
                    MaxUniqueRequests: _options.MaxUniqueRequests,
                    Window: _options.Window);
            }
        }
    }

    public SectionRebuildRequestTicket Request(
        int sectionIndex,
        WorldSectionId section,
        Func<WorldSectionId, SectionRebuildRequestTicket> requester)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sectionIndex);
        ArgumentNullException.ThrowIfNull(requester);

        lock (_gate)
        {
            _requests++;

            if (_activeGenerations.TryGetValue(sectionIndex, out long activeGeneration))
            {
                SectionRebuildRequestTicket duplicate = requester(section);
                if (!duplicate.Accepted)
                {
                    _activeGenerations.Remove(sectionIndex);
                    _rejectedRequests++;
                    return duplicate;
                }

                if (duplicate.Generation != activeGeneration)
                {
                    throw new InvalidOperationException(
                        "Section rebuild generation changed before the active budget generation completed.");
                }

                _deduplicatedRequests++;
                return duplicate;
            }

            RefreshWindowLocked();
            if (_currentWindowUniqueAdmissions >= _options.MaxUniqueRequests)
            {
                _rejectedRequests++;
                return SectionRebuildRequestTicket.Rejected;
            }

            _currentWindowUniqueAdmissions++;
            SectionRebuildRequestTicket ticket = requester(section);
            if (!ticket.Accepted)
            {
                _currentWindowUniqueAdmissions--;
                _rejectedRequests++;
                return ticket;
            }

            _activeGenerations[sectionIndex] = ticket.Generation;
            _uniqueAdmissions++;
            return ticket;
        }
    }

    public void Complete(int sectionIndex, long generation = 0)
    {
        if (sectionIndex < 0)
            return;

        lock (_gate)
        {
            if (!_activeGenerations.TryGetValue(sectionIndex, out long activeGeneration))
                return;

            if (generation > 0 && activeGeneration != generation)
                return;

            _activeGenerations.Remove(sectionIndex);
        }
    }

    public void ClearActiveGenerations()
    {
        lock (_gate)
            _activeGenerations.Clear();
    }

    private void RefreshWindowLocked()
    {
        long now = _timeProvider.GetTimestamp();
        if (_timeProvider.GetElapsedTime(_windowStartTimestamp, now) < _options.Window)
            return;

        _windowStartTimestamp = now;
        _currentWindowUniqueAdmissions = 0;
    }
}
