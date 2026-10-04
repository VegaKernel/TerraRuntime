using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

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

    // Only populated on an isolated death preview, from its retained context sample.
    internal bool HasGoodWorldSpawnContext { get; private set; }

    internal bool IsSpawnRandomSource(VanillaUnifiedRandom1458 source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _spawnRandom is SystemVanillaNpcRandom adapter && ReferenceEquals(adapter.SourceRandom, source);
    }

    // Death admission needs the real allocation/protection/generation rules without publishing child NPCs.
    // The trusted context provider is sampled once; the preview never retains a callback into live state.
    // Application must recheck the source NPC revision and shared RNG after this sample before accepting.
    internal RuntimeNpcStore CreateDeathPreview(IVanillaNpcRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var context = _spawnContext?.Invoke();
        var preview = new RuntimeNpcStore(Capacity);
        _slots.CopyTo(preview._slots, 0);
        preview._activeCount = _activeCount;
        preview._spawnRandom = random;
        if (context is { } captured)
        {
            preview._spawnContext = () => captured;
            preview.HasGoodWorldSpawnContext = captured.GoodWorld;
        }
        return preview;
    }
}
