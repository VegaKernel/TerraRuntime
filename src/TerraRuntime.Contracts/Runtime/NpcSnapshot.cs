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
/// Liquid kind remembered from the previous authoritative collision pass. Vanilla gravity runs before
/// the new wet-collision pass and therefore consumes these persisted contact flags from the prior tick.
/// </summary>
public enum NpcLiquidContactKind : byte
{
    None = 0,
    Water = 1,
    Lava = 2,
    Honey = 3,
    Shimmer = 4
}

/// <summary>
/// Authoritative local simulation inputs/state first required by vanilla NPC AI and movement.
/// Collision and liquid flags are produced by their owning world/physics systems; AI consumes the
/// immutable pre-pass values and may change persistent flags such as NoGravity.
/// </summary>
public readonly record struct NpcSimulationState(
    int DirectionX,
    int DirectionY,
    float OldVelocityX,
    float OldVelocityY,
    bool CollideX,
    bool CollideY,
    bool Wet,
    bool NoGravity,
    bool NoTileCollide,
    float Scale)
{
    public NpcLiquidContactKind LiquidContact { get; init; }

    public static NpcSimulationState Initial => new(
        DirectionX: 0,
        DirectionY: 0,
        OldVelocityX: 0f,
        OldVelocityY: 0f,
        CollideX: false,
        CollideY: false,
        Wet: false,
        NoGravity: false,
        NoTileCollide: false,
        Scale: 1f)
    {
        LiquidContact = NpcLiquidContactKind.None
    };

    public bool IsValid =>
        DirectionX is >= -1 and <= 1 &&
        DirectionY is >= -1 and <= 1 &&
        float.IsFinite(OldVelocityX) &&
        float.IsFinite(OldVelocityY) &&
        float.IsFinite(Scale) &&
        Scale > 0f &&
        Enum.IsDefined(LiquidContact);
}

/// <summary>
/// Minimal protocol-neutral live NPC projection used to bring authoritative NPC lifecycle and AI online.
/// Type is the positive gameplay NPC type used by vanilla AI. NetId is kept separately because packet 23
/// permits negative variant ids that map back to a positive gameplay type.
/// </summary>
public readonly record struct NpcSnapshot(
    NpcHandle Handle,
    NpcRevision Revision,
    int Type,
    short NetId,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    ushort Target,
    NpcAiState Ai,
    NpcSimulationState Simulation)
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
