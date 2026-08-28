using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Runtime-owned generation for one logical occupation of a reusable projectile slot.
/// Zero is reserved for an unassigned/default handle. Runtime generations are deliberately wider than
/// the protocol-326 ProjectileKey generation field so stale handles cannot alias after ordinary reuse.
/// </summary>
public readonly record struct ProjectileGeneration
{
    public ProjectileGeneration(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    public bool IsAssigned => Value != 0;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Monotonic state revision within one exact projectile generation.</summary>
public readonly record struct ProjectileRevision
{
    public ProjectileRevision(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    public bool IsAssigned => Value != 0;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Generation-safe runtime identity for one live projectile. Slot identity is deliberately distinct from
/// projectile content type and from the packed protocol ProjectileKey.
/// </summary>
public readonly record struct ProjectileHandle(ushort Slot, ProjectileGeneration Generation)
{
    public bool IsAssigned => Generation.IsAssigned;

    public override string ToString() => $"projectile:{Slot}/generation:{Generation}";
}

/// <summary>The three synchronized vanilla projectile AI state slots carried by packet 27.</summary>
public readonly record struct ProjectileAiState(float Ai0, float Ai1, float Ai2)
{
    public bool IsFinite => float.IsFinite(Ai0) && float.IsFinite(Ai1) && float.IsFinite(Ai2);
}

/// <summary>
/// Minimal protocol-neutral authoritative projectile projection. Spawner is retained as provenance required
/// to build the protocol ProjectileKey; ownership/combat semantics remain a separate gameplay concern.
/// </summary>
public readonly record struct ProjectileSnapshot(
    ProjectileHandle Handle,
    ProjectileRevision Revision,
    ProjectileTypeId Type,
    byte Spawner,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    ProjectileAiState Ai,
    ushort BannerIdToRespondTo,
    short Damage,
    float KnockBack,
    short OriginalDamage)
{
    public bool IsActive => Handle.IsAssigned && Revision.IsAssigned;
}

/// <summary>Read-only bounded snapshot boundary for authoritative live projectile state.</summary>
public interface IProjectileSnapshotReader
{
    int Capacity { get; }

    int CopyActive(Span<ProjectileSnapshot> destination);
}
