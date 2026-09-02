using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime;

/// <summary>
/// Runtime-owned projectile behavior implementation family. This is deliberately distinct from the
/// source-backed Terraria aiStyle: equal aiStyle values are not sufficient evidence that every type can
/// safely reuse the same TerraRuntime behavior path.
/// </summary>
internal enum VanillaProjectileBehaviorFamily : byte
{
    None = 0,
    BasicArrow = 1,
    Thrown = 2,
    Boomerang = 3,
    SkeletronSkull = 4,
    DeerclopsIceSpike = 5,
    DeerclopsRubble = 6,
    DeerclopsShadowHand = 7
}

/// <summary>
/// Explicit runtime strategy/capability metadata for one source-verified projectile type.
/// Type-specific exceptions live here rather than being scattered across AI and world-motion steppers.
/// </summary>
internal readonly record struct VanillaProjectileBehaviorProfile(
    VanillaProjectileBehaviorFamily Family,
    ProjectileAiStyleId ExpectedAiStyle,
    bool BehaviorImplemented,
    bool RequiresDefaultAi2,
    bool RejectServerOwned,
    bool ExemptFromPreAiWorldBounds);

/// <summary>
/// Runtime-owned opt-in catalog for projectile behavior reuse. Every mapping is explicit: adding a new
/// source definition with aiStyle 1/2/3 does not automatically make its behavior supported.
/// </summary>
internal static class VanillaProjectileBehaviorProfileCatalog
{
    private static readonly VanillaProjectileBehaviorProfile BasicArrowProfile = new(
        VanillaProjectileBehaviorFamily.BasicArrow,
        VanillaProjectileAiStyles.Arrow,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile GreenLaserProfile = BasicArrowProfile with
    {
        RejectServerOwned = true
    };

    private static readonly VanillaProjectileBehaviorProfile SkeletronSkullProfile = new(
        VanillaProjectileBehaviorFamily.SkeletronSkull,
        VanillaProjectileAiStyles.Arrow,
        BehaviorImplemented: true,
        RequiresDefaultAi2: false,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile ThrownProfile = new(
        VanillaProjectileBehaviorFamily.Thrown,
        VanillaProjectileAiStyles.Thrown,
        BehaviorImplemented: true,
        RequiresDefaultAi2: false,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile BoomerangProfile = new(
        VanillaProjectileBehaviorFamily.Boomerang,
        VanillaProjectileAiStyles.Boomerang,
        BehaviorImplemented: false,
        RequiresDefaultAi2: false,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: true);

    private static readonly VanillaProjectileBehaviorProfile DeerclopsIceSpikeProfile = new(
        VanillaProjectileBehaviorFamily.DeerclopsIceSpike,
        VanillaProjectileAiStyles.SharpTears,
        BehaviorImplemented: true,
        RequiresDefaultAi2: false,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile DeerclopsRubbleProfile = new(
        VanillaProjectileBehaviorFamily.DeerclopsRubble,
        VanillaProjectileAiStyles.Arrow,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile DeerclopsShadowHandProfile = new(
        VanillaProjectileBehaviorFamily.DeerclopsShadowHand,
        VanillaProjectileAiStyles.ShadowHand,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    public static bool TryGet(
        ProjectileTypeId type,
        out VanillaProjectileBehaviorProfile profile)
    {
        if (type == VanillaProjectileIds.GreenLaser)
        {
            profile = GreenLaserProfile;
            return true;
        }

        if (type == VanillaProjectileIds.SkeletronSkull)
        {
            profile = SkeletronSkullProfile;
            return true;
        }

        if (type == VanillaProjectileIds.DeerclopsIceSpike)
        {
            profile = DeerclopsIceSpikeProfile;
            return true;
        }

        if (type == VanillaProjectileIds.DeerclopsRubble)
        {
            profile = DeerclopsRubbleProfile;
            return true;
        }

        if (type == VanillaProjectileIds.DeerclopsShadowHand)
        {
            profile = DeerclopsShadowHandProfile;
            return true;
        }

        if (IsBasicArrow(type))
        {
            profile = BasicArrowProfile;
            return true;
        }

        if (IsThrown(type))
        {
            profile = ThrownProfile;
            return true;
        }

        if (type == VanillaProjectileIds.EnchantedBoomerang)
        {
            profile = BoomerangProfile;
            return true;
        }

        profile = default;
        return false;
    }

    private static bool IsBasicArrow(ProjectileTypeId type) =>
        type == VanillaProjectileIds.WoodenArrowFriendly ||
        type == VanillaProjectileIds.FireArrow ||
        type == VanillaProjectileIds.UnholyArrow ||
        type == VanillaProjectileIds.JestersArrow ||
        type == VanillaProjectileIds.Bullet ||
        type == VanillaProjectileIds.Seed ||
        type == VanillaProjectileIds.ConfettiGun ||
        type == VanillaProjectileIds.QueenBeeStinger ||
        type == VanillaProjectileIds.ConfettiMelee ||
        type == VanillaProjectileIds.BoneArrowFromMerchant ||
        type == VanillaProjectileIds.SoundGun ||
        type == VanillaProjectileIds.BoneShard;

    private static bool IsThrown(ProjectileTypeId type) =>
        type == VanillaProjectileIds.Bone ||
        type == VanillaProjectileIds.Shuriken ||
        type == VanillaProjectileIds.ThrowingKnife ||
        type == VanillaProjectileIds.PoisonedKnife ||
        type == VanillaProjectileIds.RottenEgg ||
        type == VanillaProjectileIds.StarAnise ||
        type == VanillaProjectileIds.NurseSyringeHurt ||
        type == VanillaProjectileIds.SantaBombs ||
        type == VanillaProjectileIds.BoneDagger ||
        type == VanillaProjectileIds.Waffle ||
        type == VanillaProjectileIds.MeleeBone;
}
