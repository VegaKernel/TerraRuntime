namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Runtime-owned generation for one logical occupation of a reusable NPC slot.
/// Zero is reserved for an unassigned/default handle. This is deliberately wider than the
/// protocol generation byte so stale runtime handles cannot alias after ordinary slot reuse.
/// </summary>
public readonly record struct NpcGeneration
{
    public NpcGeneration(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    public bool IsAssigned => Value != 0;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Monotonic state revision within one exact NPC generation.
/// </summary>
public readonly record struct NpcRevision
{
    public NpcRevision(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    public bool IsAssigned => Value != 0;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Generation-safe identity for one live NPC occupying a protocol-addressable slot.
/// </summary>
public readonly record struct NpcHandle(byte Slot, NpcGeneration Generation)
{
    public bool IsAssigned => Generation.IsAssigned;

    public override string ToString() => $"npc:{Slot}/generation:{Generation}";
}

/// <summary>
/// The four synchronized vanilla NPC AI state slots carried by packet 23.
/// Local-only vanilla AI state is intentionally not modeled until source-backed behavior needs it.
/// </summary>
public readonly record struct NpcAiState(float Ai0, float Ai1, float Ai2, float Ai3)
{
    public bool IsFinite =>
        float.IsFinite(Ai0) &&
        float.IsFinite(Ai1) &&
        float.IsFinite(Ai2) &&
        float.IsFinite(Ai3);
}

/// <summary>
/// Minimal protocol-neutral live NPC projection used to bring authoritative NPC lifecycle and AI online.
/// Additional gameplay state should be added only when a source-backed behavior requires it.
/// </summary>
public readonly record struct NpcSnapshot(
    NpcHandle Handle,
    NpcRevision Revision,
    short NetId,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    ushort Target,
    NpcAiState Ai)
{
    public bool IsActive => Handle.IsAssigned && Revision.IsAssigned;
}

/// <summary>
/// Read-only bounded snapshot boundary for authoritative live NPC state.
/// </summary>
public interface INpcSnapshotReader
{
    int Capacity { get; }

    int CopyActive(Span<NpcSnapshot> destination);
}
