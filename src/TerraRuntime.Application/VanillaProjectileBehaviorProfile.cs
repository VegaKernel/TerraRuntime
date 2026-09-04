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
    DeerclopsShadowHand = 7,
    Bomb = 8,
    HostileStraightArrow = 9,
    PlanteraSeed = 10,
    GolemFireball = 11,
    ControlledMagicMissile = 12,
    SpazmatismCursedFlame = 13,
    SpazmatismEyeFire = 14,
    HostileStraightNoGravity = 15,
    CultistFireball = 16,
    QueenSlimeGel = 17,
    SkeletronPrimeBomb = 18,
    PlanteraThornBall = 19,
    PhantasmalEye = 20,
    PhantasmalSphere = 21,
    FairyQueenLance = 22,
    FairyQueenSunDance = 23,
    PhantasmalDeathray = 24,
    HallowBossRainbowStreak = 25,
    HallowBossLastingRainbow = 26,
    HallowBossDeathAurora = 27,
    QueenSlimeSmash = 28,
    Sharknado = 29,
    SharknadoBolt = 30
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

    private static readonly VanillaProjectileBehaviorProfile HostileStraightArrowProfile = new(
        VanillaProjectileBehaviorFamily.HostileStraightArrow,
        VanillaProjectileAiStyles.Arrow,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile PlanteraSeedProfile = new(
        VanillaProjectileBehaviorFamily.PlanteraSeed,
        VanillaProjectileAiStyles.Arrow,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile GolemFireballProfile = new(
        VanillaProjectileBehaviorFamily.GolemFireball,
        VanillaProjectileAiStyles.Fireball,
        BehaviorImplemented: true,
        RequiresDefaultAi2: false,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile ControlledMagicMissileProfile = new(
        VanillaProjectileBehaviorFamily.ControlledMagicMissile,
        VanillaProjectileAiStyles.MagicMissile,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: true,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile SpazmatismCursedFlameProfile = new(
        VanillaProjectileBehaviorFamily.SpazmatismCursedFlame,
        VanillaProjectileAiStyles.Fireball,
        BehaviorImplemented: true,
        RequiresDefaultAi2: false,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile SpazmatismEyeFireProfile = new(
        VanillaProjectileBehaviorFamily.SpazmatismEyeFire,
        VanillaProjectileAiStyles.EyeFire,
        BehaviorImplemented: true,
        RequiresDefaultAi2: false,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile HostileStraightNoGravityProfile = new(
        VanillaProjectileBehaviorFamily.HostileStraightNoGravity,
        VanillaProjectileAiStyles.Arrow,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile CultistFireballProfile = new(
        VanillaProjectileBehaviorFamily.CultistFireball,
        VanillaProjectileAiStyles.Arrow,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile QueenSlimeGelProfile = new(
        VanillaProjectileBehaviorFamily.QueenSlimeGel,
        VanillaProjectileAiStyles.Arrow,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile ThrownProfile = new(
        VanillaProjectileBehaviorFamily.Thrown,
        VanillaProjectileAiStyles.Thrown,
        BehaviorImplemented: true,
        RequiresDefaultAi2: false,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile BombProfile = new(
        VanillaProjectileBehaviorFamily.Bomb,
        VanillaProjectileAiStyles.Bomb,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile SkeletronPrimeBombProfile = new(
        VanillaProjectileBehaviorFamily.SkeletronPrimeBomb,
        VanillaProjectileAiStyles.Bomb,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile PlanteraThornBallProfile = new(
        VanillaProjectileBehaviorFamily.PlanteraThornBall,
        VanillaProjectileAiStyles.BouncyBall,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile SharknadoProfile = new(
        VanillaProjectileBehaviorFamily.Sharknado,
        VanillaProjectileAiStyles.Sharknado,
        BehaviorImplemented: true,
        RequiresDefaultAi2: false,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile SharknadoBoltProfile = new(
        VanillaProjectileBehaviorFamily.SharknadoBolt,
        VanillaProjectileAiStyles.SharknadoBolt,
        BehaviorImplemented: true,
        RequiresDefaultAi2: false,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile PhantasmalEyeProfile = new(
        VanillaProjectileBehaviorFamily.PhantasmalEye,
        VanillaProjectileAiStyles.PhantasmalEye,
        BehaviorImplemented: true,
        RequiresDefaultAi2: false,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile PhantasmalSphereProfile = new(
        VanillaProjectileBehaviorFamily.PhantasmalSphere,
        VanillaProjectileAiStyles.PhantasmalSphere,
        BehaviorImplemented: true,
        RequiresDefaultAi2: false,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile PhantasmalDeathrayProfile = new(
        VanillaProjectileBehaviorFamily.PhantasmalDeathray,
        VanillaProjectileAiStyles.PhantasmalDeathray,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile HallowBossRainbowStreakProfile = new(
        VanillaProjectileBehaviorFamily.HallowBossRainbowStreak,
        VanillaProjectileAiStyles.HallowBossRainbowStreak,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile HallowBossLastingRainbowProfile = new(
        VanillaProjectileBehaviorFamily.HallowBossLastingRainbow,
        VanillaProjectileAiStyles.HallowBossRainbowTrail,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile HallowBossDeathAuroraProfile = new(
        VanillaProjectileBehaviorFamily.HallowBossDeathAurora,
        VanillaProjectileAiStyles.None,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile QueenSlimeSmashProfile = new(
        VanillaProjectileBehaviorFamily.QueenSlimeSmash,
        VanillaProjectileAiStyles.QueenSlimeSmash,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile FairyQueenLanceProfile = new(
        VanillaProjectileBehaviorFamily.FairyQueenLance,
        VanillaProjectileAiStyles.FairyQueenLance,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile FairyQueenSunDanceProfile = new(
        VanillaProjectileBehaviorFamily.FairyQueenSunDance,
        VanillaProjectileAiStyles.FairyQueenSunDance,
        BehaviorImplemented: true,
        RequiresDefaultAi2: true,
        RejectServerOwned: false,
        ExemptFromPreAiWorldBounds: false);

    private static readonly VanillaProjectileBehaviorProfile BoomerangProfile = new(
        VanillaProjectileBehaviorFamily.Boomerang,
        VanillaProjectileAiStyles.Boomerang,
        BehaviorImplemented: true,
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

        if (IsHostileStraightArrow(type))
        {
            profile = HostileStraightArrowProfile;
            return true;
        }

        if (type == VanillaProjectileIds.PlanteraSeed || type == VanillaProjectileIds.PlanteraPoisonSeed)
        {
            profile = PlanteraSeedProfile;
            return true;
        }

        if (type == VanillaProjectileIds.PlanteraThornBall)
        {
            profile = PlanteraThornBallProfile;
            return true;
        }

        if (type == VanillaProjectileIds.Sharknado || type == VanillaProjectileIds.Cthulunado)
        {
            profile = SharknadoProfile;
            return true;
        }

        if (type == VanillaProjectileIds.SharknadoBolt)
        {
            profile = SharknadoBoltProfile;
            return true;
        }

        if (type == VanillaProjectileIds.PhantasmalEye)
        {
            profile = PhantasmalEyeProfile;
            return true;
        }

        if (type == VanillaProjectileIds.PhantasmalSphere)
        {
            profile = PhantasmalSphereProfile;
            return true;
        }

        if (type == VanillaProjectileIds.PhantasmalDeathray)
        {
            profile = PhantasmalDeathrayProfile;
            return true;
        }

        if (type == VanillaProjectileIds.HallowBossRainbowStreak)
        {
            profile = HallowBossRainbowStreakProfile;
            return true;
        }

        if (type == VanillaProjectileIds.HallowBossLastingRainbow)
        {
            profile = HallowBossLastingRainbowProfile;
            return true;
        }

        if (type == VanillaProjectileIds.HallowBossDeathAurora)
        {
            profile = HallowBossDeathAuroraProfile;
            return true;
        }

        if (type == VanillaProjectileIds.QueenSlimeSmash)
        {
            profile = QueenSlimeSmashProfile;
            return true;
        }

        if (type == VanillaProjectileIds.FairyQueenLance)
        {
            profile = FairyQueenLanceProfile;
            return true;
        }

        if (type == VanillaProjectileIds.FairyQueenSunDance)
        {
            profile = FairyQueenSunDanceProfile;
            return true;
        }

        if (type == VanillaProjectileIds.GolemFireball)
        {
            profile = GolemFireballProfile;
            return true;
        }

        if (type == VanillaProjectileIds.SpazmatismCursedFlame)
        {
            profile = SpazmatismCursedFlameProfile;
            return true;
        }

        if (type == VanillaProjectileIds.SpazmatismEyeFire)
        {
            profile = SpazmatismEyeFireProfile;
            return true;
        }

        if (type == VanillaProjectileIds.PhantasmalBolt || type == VanillaProjectileIds.AncientDoomProjectile)
        {
            profile = HostileStraightNoGravityProfile;
            return true;
        }

        if (type == VanillaProjectileIds.CultistBossFireBall || type == VanillaProjectileIds.CultistBossFireBallClone)
        {
            profile = CultistFireballProfile;
            return true;
        }

        if (type == VanillaProjectileIds.QueenSlimeGelAttack)
        {
            profile = QueenSlimeGelProfile;
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

        if (type == VanillaProjectileIds.MagicMissile ||
            type == VanillaProjectileIds.Flamelash ||
            type == VanillaProjectileIds.RainbowRodBullet)
        {
            profile = ControlledMagicMissileProfile;
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

        if (type == VanillaProjectileIds.SkeletronPrimeBomb)
        {
            profile = SkeletronPrimeBombProfile;
            return true;
        }

        if (type.Value is >= 133 and <= 144)
        {
            profile = BombProfile;
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

    private static bool IsHostileStraightArrow(ProjectileTypeId type) =>
        type == VanillaProjectileIds.WallOfFleshEyeLaser ||
        type == VanillaProjectileIds.ProbePinkLaser ||
        type == VanillaProjectileIds.RetinazerDeathLaser ||
        type == VanillaProjectileIds.GolemEyeBeam;

    private static bool IsBasicArrow(ProjectileTypeId type) =>
        type == VanillaProjectileIds.WoodenArrowFriendly ||
        type == VanillaProjectileIds.FireArrow ||
        type == VanillaProjectileIds.UnholyArrow ||
        type == VanillaProjectileIds.JestersArrow ||
        type == VanillaProjectileIds.Bullet ||
        type == VanillaProjectileIds.SilverBullet ||
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
