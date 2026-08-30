using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Contracts.Runtime;

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

public readonly record struct NpcHandle(byte Slot, NpcGeneration Generation)
{
    public bool IsAssigned => Generation.IsAssigned;
    public override string ToString() => $"npc:{Slot}/generation:{Generation}";
}

public readonly record struct NpcAiState(float Ai0, float Ai1, float Ai2, float Ai3)
{
    public bool IsFinite =>
        float.IsFinite(Ai0) &&
        float.IsFinite(Ai1) &&
        float.IsFinite(Ai2) &&
        float.IsFinite(Ai3);
}

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
/// liquid, combat, presentation-facing direction and lifetime state are runtime facts rather than packet details.
/// Zero LifeMax means unspecified combat state; TimeLeft == -1 means unspecified lifetime state. Runtime-owned
/// state policy resolves those sentinels on spawn and preserves existing values across updates that do not own them.
/// Server-only vanilla localAI and transient vulnerability/presentation flags live here so one NPC revision commits
/// the complete authoritative AI transition atomically instead of advancing side dictionaries independently.
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

    public float OldPositionX { get; init; }

    public float OldPositionY { get; init; }

    public int Life { get; init; }

    public int LifeMax { get; init; }

    public bool JustHit { get; init; }

    public int TimeLeft { get; init; }

    /// <summary>
    /// Vanilla NPC.spriteDirection. NPC construction starts at -1. Unlike movement direction this is not
    /// automatically changed by TargetClosest; AI styles update it only at their own source-backed points.
    /// Zero is accepted for ingress compatibility but ordinary verified types materialize/use -1 or +1.
    /// </summary>
    public int SpriteDirection { get; init; }

    public bool SolidCollision { get; init; }

    /// <summary>
    /// Server-only vanilla NPC.localAI[0..3]. These slots are deliberately separate from wire-visible ai[0..3]
    /// but share the same authoritative revision so rejected or stale NPC updates cannot advance local timers.
    /// </summary>
    public NpcAiState LocalAi { get; init; }

    /// <summary>Vanilla NPC.hide-style presentation state owned by authoritative boss transitions.</summary>
    public bool Hidden { get; init; }

    /// <summary>Authoritative damage gate for vanilla transitions such as King Slime teleport disappearance.</summary>
    public bool DontTakeDamage { get; init; }

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
        SpriteDirection = -1,
        SolidCollision = false,
        LocalAi = default,
        Hidden = false,
        DontTakeDamage = false
    };

    public bool IsValid =>
        DirectionX is >= -1 and <= 1 &&
        DirectionY is >= -1 and <= 1 &&
        SpriteDirection is >= -1 and <= 1 &&
        float.IsFinite(OldVelocityX) &&
        float.IsFinite(OldVelocityY) &&
        float.IsFinite(OldPositionX) &&
        float.IsFinite(OldPositionY) &&
        float.IsFinite(Scale) &&
        Scale > 0f &&
        LocalAi.IsFinite &&
        ((LifeMax == 0 && Life == 0) ||
         (LifeMax > 0 && Life >= 0 && Life <= LifeMax)) &&
        TimeLeft >= -1 &&
        Enum.IsDefined(LiquidContact);
}

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

public interface INpcSnapshotReader
{
    int Capacity { get; }
    int CopyActive(Span<NpcSnapshot> destination);
}
