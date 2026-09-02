using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public readonly record struct NpcStateUpdate(
    int Type,
    short NetId,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    ushort Target,
    NpcAiState Ai,
    NpcSimulationState Simulation);

/// <summary>
/// Generation-safe authoritative NPC slot store. This type owns storage identity, revision ordering,
/// active-slot accounting and commit publication. Vanilla spawn/combat/lifetime defaults are resolved by
/// <see cref="RuntimeNpcStateOwnershipPolicy"/> so the slot store stays independent from content catalogs.
/// </summary>
public sealed partial class RuntimeNpcStore
{
    public const int MaximumAddressableCapacity = byte.MaxValue + 1;

    private readonly SlotState[] _slots;
    private readonly INpcStateCommitSink? _commitSink;
    private int _activeCount;

    public RuntimeNpcStore(int capacity = MaximumAddressableCapacity, INpcStateCommitSink? commitSink = null)
    {
        if (capacity <= 0 || capacity > MaximumAddressableCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _slots = new SlotState[capacity];
        _commitSink = commitSink;
    }

    public int Capacity => _slots.Length;
    public int ActiveCount => _activeCount;
}
