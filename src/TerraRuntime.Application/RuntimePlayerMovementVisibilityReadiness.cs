using System.Numerics;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime;

/// <summary>
/// Tracks whether an observer has received a current movement baseline for a visible subject.
/// Readiness is directional because one recipient queue may accept a resync while the reverse
/// direction is unavailable or backpressured.
/// </summary>
internal sealed class RuntimePlayerMovementVisibilityReadiness
{
    private const int MaxPlayerSlots = 256;
    private const int WordsPerPlayer = MaxPlayerSlots / 64;

    private readonly ulong[] _ready = new ulong[MaxPlayerSlots * WordsPerPlayer];
    private int _readyDirections;
    private long _marks;
    private long _clears;

    public RuntimePlayerMovementVisibilityReadinessSnapshot Snapshot =>
        new(_readyDirections, _marks, _clears);

    public bool IsReady(PlayerSlotId observer, PlayerSlotId subject)
    {
        if (observer == subject)
            return false;

        return IsSet(observer.Value, subject.Value);
    }

    public bool MarkReady(PlayerSlotId observer, PlayerSlotId subject)
    {
        if (observer == subject || IsSet(observer.Value, subject.Value))
            return false;

        Set(observer.Value, subject.Value);
        _readyDirections++;
        _marks++;
        return true;
    }

    public int ClearPair(PlayerSlotId first, PlayerSlotId second)
    {
        if (first == second)
            return 0;

        int cleared = 0;
        if (Clear(first.Value, second.Value))
            cleared++;
        if (Clear(second.Value, first.Value))
            cleared++;

        _readyDirections -= cleared;
        _clears += cleared;
        return cleared;
    }

    public int ClearPlayer(PlayerSlotId player)
    {
        int cleared = 0;
        int rowBase = player.Value * WordsPerPlayer;

        for (int wordIndex = 0; wordIndex < WordsPerPlayer; wordIndex++)
        {
            ulong rowWord = _ready[rowBase + wordIndex];
            cleared += BitOperations.PopCount(rowWord);
            _ready[rowBase + wordIndex] = 0;
        }

        for (int observer = 0; observer < MaxPlayerSlots; observer++)
        {
            if (observer == player.Value)
                continue;

            if (Clear(checked((byte)observer), player.Value))
                cleared++;
        }

        _readyDirections -= cleared;
        _clears += cleared;
        return cleared;
    }

    private bool IsSet(byte observer, byte subject)
    {
        int wordIndex = subject / 64;
        int bit = subject % 64;
        int rowBase = observer * WordsPerPlayer;
        return (_ready[rowBase + wordIndex] & (1UL << bit)) != 0;
    }

    private void Set(byte observer, byte subject)
    {
        int wordIndex = subject / 64;
        int bit = subject % 64;
        int rowBase = observer * WordsPerPlayer;
        _ready[rowBase + wordIndex] |= 1UL << bit;
    }

    private bool Clear(byte observer, byte subject)
    {
        int wordIndex = subject / 64;
        int bit = subject % 64;
        int rowBase = observer * WordsPerPlayer;
        ulong mask = 1UL << bit;
        int index = rowBase + wordIndex;
        if ((_ready[index] & mask) == 0)
            return false;

        _ready[index] &= ~mask;
        return true;
    }
}

internal readonly record struct RuntimePlayerMovementVisibilityReadinessSnapshot(
    int ReadyDirections,
    long Marks,
    long Clears);
