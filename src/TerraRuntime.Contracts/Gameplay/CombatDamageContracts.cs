using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Stable semantic origin of one gameplay damage event. The source category is deliberately
/// independent from Terraria packet IDs and from mutable runtime entity objects.
/// </summary>
public enum DamageSourceKind : byte
{
    None = 0,
    Environment = 1,
    PlayerItem = 2,
    PlayerProjectile = 3,
    NpcContact = 4,
    NpcProjectile = 5,
    Server = 6
}

/// <summary>
/// Generation-safe provenance for a damage event. Only handles required by the selected source
/// kind may be populated; mixed or stale-looking provenance is rejected by <see cref="IsValid"/>.
/// </summary>
public readonly record struct DamageSource(
    DamageSourceKind Kind,
    PlayerHandle Player,
    NpcHandle Npc,
    ProjectileHandle Projectile)
{
    public bool IsValid => Kind switch
    {
        DamageSourceKind.Environment or DamageSourceKind.Server =>
            !Player.IsAssigned && !Npc.IsAssigned && !Projectile.IsAssigned,
        DamageSourceKind.PlayerItem =>
            Player.IsAssigned && !Npc.IsAssigned && !Projectile.IsAssigned,
        DamageSourceKind.PlayerProjectile =>
            Player.IsAssigned && !Npc.IsAssigned && Projectile.IsAssigned,
        DamageSourceKind.NpcContact =>
            !Player.IsAssigned && Npc.IsAssigned && !Projectile.IsAssigned,
        DamageSourceKind.NpcProjectile =>
            !Player.IsAssigned && Npc.IsAssigned && Projectile.IsAssigned,
        _ => false
    };

    public static DamageSource Environment =>
        new(DamageSourceKind.Environment, default, default, default);

    public static DamageSource Server =>
        new(DamageSourceKind.Server, default, default, default);

    public static DamageSource FromPlayerItem(PlayerHandle player) =>
        new(DamageSourceKind.PlayerItem, player, default, default);

    public static DamageSource FromPlayerProjectile(
        PlayerHandle player,
        ProjectileHandle projectile) =>
        new(DamageSourceKind.PlayerProjectile, player, default, projectile);

    public static DamageSource FromNpcContact(NpcHandle npc) =>
        new(DamageSourceKind.NpcContact, default, npc, default);

    public static DamageSource FromNpcProjectile(
        NpcHandle npc,
        ProjectileHandle projectile) =>
        new(DamageSourceKind.NpcProjectile, default, npc, projectile);
}

/// <summary>
/// Deterministic NPC damage input after upstream source-specific modifiers such as weapon/projectile
/// scaling and random damage variation have been resolved. This slice owns NPC defense, flat armor
/// penetration, the ordinary vanilla critical multiplier and a source-resolved hit direction for
/// knockback; broader banner/buff/immunity rules remain separate gameplay work.
/// </summary>
public readonly record struct NpcDamageRequest(
    NpcHandle Target,
    DamageSource Source,
    int BaseDamage,
    int ArmorPenetration = 0,
    bool Critical = false,
    float KnockBack = 0f,
    int HitDirection = 0)
{
    public bool IsValid =>
        Target.IsAssigned &&
        Source.IsValid &&
        BaseDamage >= 0 &&
        ArmorPenetration >= 0 &&
        float.IsFinite(KnockBack) &&
        KnockBack >= 0f &&
        HitDirection is >= -1 and <= 1;
}

/// <summary>
/// Protocol-neutral projectile hit intent after a collision subsystem has selected an NPC target. Projectile
/// provenance, target selection and damage application remain distinct stages; this value performs no mutation.
/// </summary>
public readonly record struct ProjectileNpcHitIntent(
    NpcHandle Target,
    DamageSource Source,
    int BaseDamage,
    float KnockBack,
    int HitDirection)
{
    public bool IsValid =>
        Target.IsAssigned &&
        Source is { Kind: DamageSourceKind.PlayerProjectile or DamageSourceKind.NpcProjectile } &&
        Source.IsValid &&
        BaseDamage > 0 &&
        float.IsFinite(KnockBack) &&
        KnockBack >= 0f &&
        HitDirection is >= -1 and <= 1;

    public bool TryCreateDamageRequest(out NpcDamageRequest request)
    {
        if (!IsValid)
        {
            request = default;
            return false;
        }

        request = new NpcDamageRequest(
            Target,
            Source,
            BaseDamage,
            KnockBack: KnockBack,
            HitDirection: HitDirection);
        return true;
    }
}

/// <summary>
/// Immutable result of one committed NPC damage transition. ResolvedDamage is not capped to remaining
/// life; LifeLost reports the actual authoritative HP delta.
/// </summary>
public readonly record struct NpcDamageResult(
    NpcHandle Target,
    NpcRevision Revision,
    DamageSource Source,
    int SourceDamage,
    int Defense,
    int EffectiveDefense,
    int ResolvedDamage,
    int LifeBefore,
    int LifeAfter,
    bool Critical)
{
    public int LifeLost => LifeBefore - LifeAfter;

    public bool Lethal => LifeBefore > 0 && LifeAfter == 0;
}
