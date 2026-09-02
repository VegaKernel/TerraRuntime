using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Mutable state accepted by the authoritative projectile store. Type is the vanilla client-visible
/// presentation identity for the current protocol; future custom archetype identity stays separate from
/// this field. Packet presence flags and packed ProjectileKey representation remain outside Core.
/// </summary>
public readonly record struct ProjectileStateUpdate(
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
    short OriginalDamage);

/// <summary>
/// Runtime-only liquid contact flags carried by Terraria Projectile across updates. Generic wet contact can
/// clear on exit, while the liquid-kind flags are raised by the current collision probes and persist until a
/// source-backed behavior explicitly clears them. None of these fields belong to packet 27.
/// </summary>
public readonly record struct ProjectileLiquidState(
    bool Wet,
    bool LavaWet,
    bool HoneyWet,
    bool ShimmerWet);

/// <summary>
/// Runtime-owned lifecycle fields initialized by vanilla Projectile.SetDefaults and intentionally absent
/// from packet 27. They remain authoritative server state so allocation and later simulation do not infer
/// gameplay lifetime or liquid history from network traffic.
/// </summary>
public readonly record struct ProjectileLifecycleState(
    int TimeLeft,
    bool NetImportant,
    ProjectileLiquidState Liquid = default)
{
    public bool IsInitialized => TimeLeft > 0;

    /// <summary>Projectile.oldVelocity captured at the source-equivalent update boundary.</summary>
    public float OldVelocityX { get; init; }

    public float OldVelocityY { get; init; }

    /// <summary>Vanilla Projectile.reflected. A reflected generation cannot be reflected again.</summary>
    public bool Reflected { get; init; }

    /// <summary>Runtime-only authoritative penetrate override written by NPC.ReflectProjectile.</summary>
    public int? PenetrateOverride { get; init; }
}

/// <summary>
/// Bounded single-writer authoritative projectile lifecycle state. TerrariaServer 1.4.5.8 normally scans
/// physical slots 0..999. When all are occupied it replaces the non-netImportant projectile with the lowest
/// timeLeft; if every normal slot is netImportant, slot 1000 is the real overflow/fallback physical slot.
/// Protocol ProjectileKey also addresses indices 0..1000, while runtime generations stay wider than the
/// 14-bit wire generation so stale handles do not alias after ordinary reuse.
/// </summary>
public sealed partial class RuntimeProjectileStore
{
    public const ushort MaximumVanillaPhysicalSlot = 999;
    public const int VanillaPhysicalSlotCount = MaximumVanillaPhysicalSlot + 1;
    public const ushort VanillaOverflowSlot = 1000;
    public const ushort MaximumProtocolIndex = VanillaOverflowSlot;
    public const int MaximumProtocolAddressableCapacity = MaximumProtocolIndex + 1;

    private const int VanillaOldestProjectileSentinelTimeLeft = 9_999_999;

    private readonly SlotState[] _slots;
    private readonly IProjectileStateCommitSink? _commitSink;
    private int _activeCount;

    public RuntimeProjectileStore(
        int capacity = MaximumProtocolAddressableCapacity,
        IProjectileStateCommitSink? commitSink = null)
    {
        if (capacity <= 0 || capacity > MaximumProtocolAddressableCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _slots = new SlotState[capacity];
        _commitSink = commitSink;
    }

    public int Capacity => _slots.Length;

    public int ActiveCount => _activeCount;
}
