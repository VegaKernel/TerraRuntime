using TerraRuntime.Contracts.Gameplay;

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
/// Authoritative local simulation inputs/state required by vanilla NPC AI and movement. Collision, overlap,
/// liquid, combat and lifetime state are local runtime facts rather than packet-23 serialization details.
/// Zero LifeMax means unspecified combat state; TimeLeft == -1 means unspecified lifetime state. The store
/// resolves those sentinels on spawn and preserves existing values across updates that do not own them.
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

    /// <summary>
    /// Position captured immediately before the previous authoritative movement. AI_003 compares
    /// current X with OldPositionX to detect fighters that are stuck against world geometry.
    /// </summary>
    public float OldPositionX { get; init; }

    public float OldPositionY { get; init; }

    /// <summary>
    /// Current authoritative NPC life. LifeMax == 0 means unspecified at an ingress/update boundary;
    /// once an NPC is live the runtime store materializes a positive vanilla maximum for known types.
    /// </summary>
    public int Life { get; init; }

    public int LifeMax { get; init; }

    /// <summary>
    /// One-tick damage transient corresponding to vanilla NPC.justHit. Combat owns setting it; the
    /// authoritative AI pass consumes it and clears it so stale hit state cannot leak into later ticks.
    /// </summary>
    public bool JustHit { get; init; }

    /// <summary>
    /// Vanilla inactivity lifetime. -1 is reserved for an unspecified ingress/update value; zero is a
    /// real expired lifetime and must remain representable so CheckActive can request authoritative despawn.
    /// </summary>
    public int TimeLeft { get; init; }

    /// <summary>
    /// Result of vanilla Collision.SolidCollision at the final authoritative position of the previous
    /// world pass. AI_001 uses it together with CollideY/OldVelocityY to escape tile overlap.
    /// </summary>
    public bool SolidCollision { get; init; }

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
        LiquidContact = NpcLiquidContactKind.None,
        OldPositionX = 0f,
        OldPositionY = 0f,
        Life = 0,
        LifeMax = 0,
        JustHit = false,
        TimeLeft = -1,
        SolidCollision = false
    };

    public bool IsValid =>
        DirectionX is >= -1 and <= 1 &&
        DirectionY is >= -1 and <= 1 &&
        float.IsFinite(OldVelocityX) &&
        float.IsFinite(OldVelocityY) &&
        float.IsFinite(OldPositionX) &&
        float.IsFinite(OldPositionY) &&
        float.IsFinite(Scale) &&
        Scale > 0f &&
        ((LifeMax == 0 && Life == 0) ||
         (LifeMax > 0 && Life >= 0 && Life <= LifeMax)) &&
        TimeLeft >= -1 &&
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

    public NpcTypeId TypeIdentity => new(Type);

    public NpcNetId NetIdentity => new(NetId);
}

/// <summary>
/// Read-only bounded snapshot boundary for authoritative live NPC state.
/// </summary>
public interface INpcSnapshotReader
{
    int Capacity { get; }

    int CopyActive(Span<NpcSnapshot> destination);
}
