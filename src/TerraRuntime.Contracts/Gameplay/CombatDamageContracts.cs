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
    int HitDirection,
    int ArmorPenetration = 0,
    bool Critical = false)
{
    public bool IsValid =>
        Target.IsAssigned &&
        Source is { Kind: DamageSourceKind.PlayerProjectile or DamageSourceKind.NpcProjectile } &&
        Source.IsValid &&
        BaseDamage > 0 &&
        ArmorPenetration >= 0 &&
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
            ArmorPenetration: ArmorPenetration,
            Critical: Critical,
            KnockBack: KnockBack,
            HitDirection: HitDirection);
        return true;
    }
}

/// <summary>
/// Immutable result of one committed NPC damage transition. ResolvedDamage is not capped to remaining
/// life. LifeBefore/LifeAfter describe the strike arithmetic before any source-backed checkDead survival
/// transition; DeathIntercepted marks that the NPC remained active instead of entering ordinary death finalization.
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
    bool Critical,
    bool DeathIntercepted = false)
{
    public int LifeLost => LifeBefore - LifeAfter;

    public bool Lethal => LifeBefore > 0 && LifeAfter == 0 && !DeathIntercepted;
}


/// <summary>
/// Server-owned source context for one player attack before damage is calculated. Selected item identity and prefix are
/// authoritative inputs; packet-reported final damage/crit are deliberately absent. Projectile attacks retain their
/// generation-safe projectile provenance in <see cref="Source"/> while still naming the selected source weapon.
/// </summary>
public readonly record struct AttackContext(
    PlayerHandle Attacker,
    DamageSource Source,
    ItemTypeId Weapon,
    PrefixId Prefix,
    bool Pvp)
{
    public bool IsValid =>
        Attacker.IsAssigned && Source.IsValid && Source.Player == Attacker && !Weapon.IsNone &&
        Source.Kind is DamageSourceKind.PlayerItem or DamageSourceKind.PlayerProjectile;
}

/// <summary>
/// Source-calculation output before any target-specific mitigation. This is the authoritative boundary between
/// AttackContext/source validation and defense/endurance/immunity handling; client-reported final damage never enters it.
/// </summary>
public readonly record struct AuthoritativeAttackDamage(
    DamageSource Source,
    int Damage,
    int ArmorPenetration,
    bool Critical,
    float KnockBack,
    int HitDirection)
{
    public bool IsValid =>
        Source.IsValid && Damage >= 0 && ArmorPenetration >= 0 &&
        float.IsFinite(KnockBack) && KnockBack >= 0f && HitDirection is >= -1 and <= 1;
}

/// <summary>Target-side facts selected from authoritative state before HP mutation.</summary>
public readonly record struct TargetMitigation(
    int Defense,
    int EffectiveDefense,
    float Endurance,
    bool Immune,
    bool Dodged,
    bool NoKnockback)
{
    public bool IsValid =>
        EffectiveDefense <= Math.Max(Defense, 0) &&
        float.IsFinite(Endurance) && Endurance is >= 0f and <= 1f;
}

/// <summary>Final result of attack calculation -> target mitigation, still prior to actual HP mutation.</summary>
public readonly record struct FinalDamageToHp(
    int Damage,
    TargetMitigation Mitigation)
{
    public bool IsValid => Damage >= 0 && Mitigation.IsValid;
}
