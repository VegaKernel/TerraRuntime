using System.Numerics;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Tracks symmetric player-to-player visibility membership with section-based hysteresis.
/// Membership is runtime state only: it does not itself suppress packets or emit resync traffic.
/// </summary>
internal sealed class RuntimePlayerVisibilityTracker
{
    private const int MaxPlayerSlots = 256;
    private const int WordsPerPlayer = MaxPlayerSlots / 64;

    private readonly RuntimePlayerSpatialIndex _players;
    private readonly ulong[] _visible = new ulong[MaxPlayerSlots * WordsPerPlayer];
    private readonly int _enterRadiusSections;
    private readonly int _leaveRadiusSections;
    private int _visiblePairs;
    private long _refreshes;
    private long _enterTransitions;
    private long _leaveTransitions;

    public RuntimePlayerVisibilityTracker(
        RuntimePlayerSpatialIndex players,
        int enterRadiusSections,
        int leaveRadiusSections)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentOutOfRangeException.ThrowIfNegative(enterRadiusSections);
        ArgumentOutOfRangeException.ThrowIfLessThan(leaveRadiusSections, enterRadiusSections);

        _players = players;
        _enterRadiusSections = enterRadiusSections;
        _leaveRadiusSections = leaveRadiusSections;
    }

    public RuntimePlayerVisibilitySnapshot Snapshot => new(
        _visiblePairs,
        _refreshes,
        _enterTransitions,
        _leaveTransitions,
        _enterRadiusSections,
        _leaveRadiusSections);

    public bool IsVisible(PlayerSlotId observer, PlayerSlotId subject)
    {
        if (observer == subject)
            return false;

        return IsSet(observer.Value, subject.Value);
    }

    public RuntimePlayerVisibilityUpdate Refresh(
        PlayerSlotId subject,
        Span<PlayerSlotId> entered,
        Span<PlayerSlotId> left)
    {
        ValidateTransitionBuffers(entered, left);
        _refreshes++;

        if (!_players.TryGetSection(subject, out WorldSectionId subjectSection))
            return ClearSubject(subject, left);

        int stayedCount = 0;
        int leftCount = 0;
        int rowBase = subject.Value * WordsPerPlayer;

        for (int wordIndex = 0; wordIndex < WordsPerPlayer; wordIndex++)
        {
            ulong word = _visible[rowBase + wordIndex];
            while (word != 0)
            {
                int bit = BitOperations.TrailingZeroCount(word);
                int peerValue = checked((wordIndex * 64) + bit);
                var peer = new PlayerSlotId(checked((byte)peerValue));

                bool remainsVisible = _players.TryGetSection(peer, out WorldSectionId peerSection) &&
                    SectionDistance(subjectSection, peerSection) <= _leaveRadiusSections;
                if (remainsVisible)
                {
                    stayedCount++;
                }
                else
                {
                    ClearPair(subject.Value, peer.Value);
                    left[leftCount++] = peer;
                    _visiblePairs--;
                    _leaveTransitions++;
                }

                word &= word - 1;
            }
        }

        Span<PlayerSlotId> nearby = stackalloc PlayerSlotId[MaxPlayerSlots];
        int nearbyCount = _players.CollectNearbyPlayers(
            subject,
            _enterRadiusSections,
            nearby,
            includeSubject: false);
        int enteredCount = 0;

        for (int i = 0; i < nearbyCount; i++)
        {
            PlayerSlotId peer = nearby[i];
            if (IsSet(subject.Value, peer.Value))
                continue;

            SetPair(subject.Value, peer.Value);
            entered[enteredCount++] = peer;
            _visiblePairs++;
            _enterTransitions++;
        }

        return new RuntimePlayerVisibilityUpdate(enteredCount, stayedCount, leftCount);
    }

    public RuntimePlayerVisibilityUpdate Remove(
        PlayerSlotId subject,
        Span<PlayerSlotId> left)
    {
        if (left.Length < MaxPlayerSlots)
        {
            throw new ArgumentException(
                $"Destination must have room for all {MaxPlayerSlots} possible player slots.",
                nameof(left));
        }

        return ClearSubject(subject, left);
    }

    private RuntimePlayerVisibilityUpdate ClearSubject(
        PlayerSlotId subject,
        Span<PlayerSlotId> left)
    {
        int leftCount = 0;
        int rowBase = subject.Value * WordsPerPlayer;

        for (int wordIndex = 0; wordIndex < WordsPerPlayer; wordIndex++)
        {
            ulong word = _visible[rowBase + wordIndex];
            while (word != 0)
            {
                int bit = BitOperations.TrailingZeroCount(word);
                int peerValue = checked((wordIndex * 64) + bit);
                var peer = new PlayerSlotId(checked((byte)peerValue));

                ClearPair(subject.Value, peer.Value);
                left[leftCount++] = peer;
                _visiblePairs--;
                _leaveTransitions++;
                word &= word - 1;
            }
        }

        return new RuntimePlayerVisibilityUpdate(0, 0, leftCount);
    }

    private bool IsSet(byte observer, byte subject)
    {
        int wordIndex = subject / 64;
        int bit = subject % 64;
        int rowBase = observer * WordsPerPlayer;
        return (_visible[rowBase + wordIndex] & (1UL << bit)) != 0;
    }

    private void SetPair(byte first, byte second)
    {
        Set(first, second);
        Set(second, first);
    }

    private void ClearPair(byte first, byte second)
    {
        Clear(first, second);
        Clear(second, first);
    }

    private void Set(byte observer, byte subject)
    {
        int wordIndex = subject / 64;
        int bit = subject % 64;
        int rowBase = observer * WordsPerPlayer;
        _visible[rowBase + wordIndex] |= 1UL << bit;
    }

    private void Clear(byte observer, byte subject)
    {
        int wordIndex = subject / 64;
        int bit = subject % 64;
        int rowBase = observer * WordsPerPlayer;
        _visible[rowBase + wordIndex] &= ~(1UL << bit);
    }

    private static int SectionDistance(WorldSectionId first, WorldSectionId second) =>
        Math.Max(Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y));

    private static void ValidateTransitionBuffers(
        Span<PlayerSlotId> entered,
        Span<PlayerSlotId> left)
    {
        if (entered.Length < MaxPlayerSlots)
        {
            throw new ArgumentException(
                $"Destination must have room for all {MaxPlayerSlots} possible player slots.",
                nameof(entered));
        }

        if (left.Length < MaxPlayerSlots)
        {
            throw new ArgumentException(
                $"Destination must have room for all {MaxPlayerSlots} possible player slots.",
                nameof(left));
        }
    }
}

internal readonly record struct RuntimePlayerVisibilityUpdate(
    int Entered,
    int Stayed,
    int Left);

internal readonly record struct RuntimePlayerVisibilitySnapshot(
    int VisiblePairs,
    long Refreshes,
    long EnterTransitions,
    long LeaveTransitions,
    int EnterRadiusSections,
    int LeaveRadiusSections);
