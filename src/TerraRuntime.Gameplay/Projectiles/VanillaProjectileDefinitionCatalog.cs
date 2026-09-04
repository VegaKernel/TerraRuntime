using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>Named TerrariaServer 1.4.5.8 projectile AI-style identities currently implemented by TerraRuntime.</summary>
public static class VanillaProjectileAiStyles
{
    public static readonly ProjectileAiStyleId Arrow = new(1);
    public static readonly ProjectileAiStyleId Thrown = new(2);
    public static readonly ProjectileAiStyleId Boomerang = new(3);
    public static readonly ProjectileAiStyleId FallingStar = new(5);
    public static readonly ProjectileAiStyleId Fireball = new(8);
    public static readonly ProjectileAiStyleId MagicMissile = new(9);
    public static readonly ProjectileAiStyleId Bomb = new(16);
    public static readonly ProjectileAiStyleId EyeFire = new(23);
    public static readonly ProjectileAiStyleId SuperStar = new(151);
    public static readonly ProjectileAiStyleId SharpTears = new(157);
    public static readonly ProjectileAiStyleId ShadowHand = new(187);
}

/// <summary>
/// Source-backed gameplay shape needed by authoritative projectile world simulation. This is intentionally
/// smaller than Projectile.SetDefaults: fields are added only when runtime behavior actually consumes them.
/// </summary>
public readonly record struct VanillaProjectileDefinition(
    int Width,
    int Height,
    ProjectileAiStyleId AiStyle,
    bool TileCollide,
    bool IgnoreWater,
    bool CanCutTiles,
    int CollisionWidth,
    int CollisionHeight)
{
    public float CollisionOffsetX => (Width - CollisionWidth) * 0.5f;

    public float CollisionOffsetY => (Height - CollisionHeight) * 0.5f;
}

/// <summary>
/// Version-pinned TerrariaServer 1.4.5.8 projectile definitions for behavior slices TerraRuntime can simulate
/// without guessed defaults. A definition can still have explicit unsupported side-effect boundaries in the
/// runtime stepper when irreversible world effects are not yet modeled.
/// </summary>
public static class VanillaProjectileDefinitionCatalog
{
    private static readonly VanillaProjectileDefinition WoodenArrowDefinition = new(
        Width: 10,
        Height: 10,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 10,
        CollisionHeight: 10);

    private static readonly VanillaProjectileDefinition FireArrowDefinition = new(
        Width: 10,
        Height: 10,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 10,
        CollisionHeight: 10);

    private static readonly VanillaProjectileDefinition UnholyArrowDefinition = new(
        Width: 10,
        Height: 10,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 10,
        CollisionHeight: 10);

    private static readonly VanillaProjectileDefinition JestersArrowDefinition = new(
        Width: 10,
        Height: 10,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: true,
        CanCutTiles: true,
        CollisionWidth: 10,
        CollisionHeight: 10);

    private static readonly VanillaProjectileDefinition EnchantedBoomerangDefinition = new(
        Width: 22,
        Height: 22,
        AiStyle: VanillaProjectileAiStyles.Boomerang,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 10,
        CollisionHeight: 10);

    private static readonly VanillaProjectileDefinition BulletDefinition = new(
        Width: 4,
        Height: 4,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 4,
        CollisionHeight: 4);

    private static readonly VanillaProjectileDefinition RocketFamilyDefinition = new(
        Width: 14,
        Height: 14,
        AiStyle: VanillaProjectileAiStyles.Bomb,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 14,
        CollisionHeight: 14);

    private static readonly VanillaProjectileDefinition MagicMissileDefinition = new(
        Width: 32,
        Height: 32,
        AiStyle: VanillaProjectileAiStyles.MagicMissile,
        TileCollide: true,
        IgnoreWater: true,
        CanCutTiles: true,
        CollisionWidth: 4,
        CollisionHeight: 4);

    private static readonly VanillaProjectileDefinition FlamelashDefinition = new(
        Width: 32,
        Height: 32,
        AiStyle: VanillaProjectileAiStyles.MagicMissile,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 4,
        CollisionHeight: 4);

    private static readonly VanillaProjectileDefinition RainbowRodBulletDefinition = new(
        Width: 32,
        Height: 32,
        AiStyle: VanillaProjectileAiStyles.MagicMissile,
        TileCollide: true,
        IgnoreWater: true,
        CanCutTiles: true,
        CollisionWidth: 4,
        CollisionHeight: 4);

    private static readonly VanillaProjectileDefinition GreenLaserDefinition = new(
        Width: 4,
        Height: 4,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 4,
        CollisionHeight: 4);

    private static readonly VanillaProjectileDefinition SkeletronSkullDefinition = new(
        Width: 26,
        Height: 26,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 26,
        CollisionHeight: 26);

    private static readonly VanillaProjectileDefinition QueenBeeStingerDefinition = new(
        Width: 10,
        Height: 10,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 10,
        CollisionHeight: 10);

    private static readonly VanillaProjectileDefinition WallOfFleshEyeLaserDefinition = new(
        Width: 4,
        Height: 4,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: false,
        CollisionWidth: 4,
        CollisionHeight: 4);

    private static readonly VanillaProjectileDefinition ProbePinkLaserDefinition = new(
        Width: 4,
        Height: 4,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 4,
        CollisionHeight: 4);

    private static readonly VanillaProjectileDefinition RetinazerDeathLaserDefinition = new(
        Width: 4,
        Height: 4,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 4,
        CollisionHeight: 4);

    private static readonly VanillaProjectileDefinition GolemFireballDefinition = new(
        Width: 16,
        Height: 16,
        AiStyle: VanillaProjectileAiStyles.Fireball,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 16,
        CollisionHeight: 16);

    private static readonly VanillaProjectileDefinition SpazmatismCursedFlameDefinition = new(
        Width: 16,
        Height: 16,
        AiStyle: VanillaProjectileAiStyles.Fireball,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 16,
        CollisionHeight: 16);

    private static readonly VanillaProjectileDefinition SpazmatismEyeFireDefinition = new(
        Width: 6,
        Height: 6,
        AiStyle: VanillaProjectileAiStyles.EyeFire,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 6,
        CollisionHeight: 6);

    private static readonly VanillaProjectileDefinition PhantasmalBoltDefinition = new(
        Width: 8,
        Height: 8,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: false,
        IgnoreWater: true,
        CanCutTiles: true,
        CollisionWidth: 8,
        CollisionHeight: 8);

    private static readonly VanillaProjectileDefinition CultistFireballDefinition = new(
        Width: 40,
        Height: 40,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: true,
        CanCutTiles: true,
        CollisionWidth: 40,
        CollisionHeight: 40);

    private static readonly VanillaProjectileDefinition AncientDoomProjectileDefinition = new(
        Width: 16,
        Height: 16,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: true,
        CanCutTiles: true,
        CollisionWidth: 16,
        CollisionHeight: 16);

    private static readonly VanillaProjectileDefinition QueenSlimeGelAttackDefinition = new(
        Width: 12,
        Height: 12,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 12,
        CollisionHeight: 12);

    private static readonly VanillaProjectileDefinition GolemEyeBeamDefinition = new(
        Width: 8,
        Height: 8,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: false,
        IgnoreWater: true,
        CanCutTiles: true,
        CollisionWidth: 8,
        CollisionHeight: 8);

    private static readonly VanillaProjectileDefinition PlanteraSeedDefinition = new(
        Width: 14,
        Height: 14,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 14,
        CollisionHeight: 14);

    private static readonly VanillaProjectileDefinition SuperStarDefinition = new(
        Width: 24,
        Height: 24,
        AiStyle: VanillaProjectileAiStyles.SuperStar,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 24,
        CollisionHeight: 24);

    private static readonly VanillaProjectileDefinition StarCannonStarDefinition = new(
        Width: 18,
        Height: 18,
        AiStyle: VanillaProjectileAiStyles.FallingStar,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 18,
        CollisionHeight: 18);

    private static readonly VanillaProjectileDefinition BoneDefinition = new(
        Width: 16,
        Height: 16,
        AiStyle: VanillaProjectileAiStyles.Thrown,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 16,
        CollisionHeight: 16);

    private static readonly VanillaProjectileDefinition ShurikenDefinition = new(
        Width: 22,
        Height: 22,
        AiStyle: VanillaProjectileAiStyles.Thrown,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 6,
        CollisionHeight: 6);

    private static readonly VanillaProjectileDefinition ThrowingKnifeDefinition = new(
        Width: 12,
        Height: 12,
        AiStyle: VanillaProjectileAiStyles.Thrown,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 12,
        CollisionHeight: 12);

    private static readonly VanillaProjectileDefinition SeedDefinition = new(
        Width: 8,
        Height: 8,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 8,
        CollisionHeight: 8);

    private static readonly VanillaProjectileDefinition PoisonedKnifeDefinition = new(
        Width: 12,
        Height: 12,
        AiStyle: VanillaProjectileAiStyles.Thrown,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 12,
        CollisionHeight: 12);

    private static readonly VanillaProjectileDefinition ConfettiDefinition = new(
        Width: 10,
        Height: 10,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 10,
        CollisionHeight: 10);

    private static readonly VanillaProjectileDefinition RottenEggDefinition = new(
        Width: 12,
        Height: 14,
        AiStyle: VanillaProjectileAiStyles.Thrown,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 12,
        CollisionHeight: 14);

    private static readonly VanillaProjectileDefinition StarAniseDefinition = new(
        Width: 22,
        Height: 22,
        AiStyle: VanillaProjectileAiStyles.Thrown,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 22,
        CollisionHeight: 22);

    private static readonly VanillaProjectileDefinition BoneArrowFromMerchantDefinition = new(
        Width: 10,
        Height: 10,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 10,
        CollisionHeight: 10);

    private static readonly VanillaProjectileDefinition NurseSyringeHurtDefinition = new(
        Width: 10,
        Height: 10,
        AiStyle: VanillaProjectileAiStyles.Thrown,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 10,
        CollisionHeight: 10);

    private static readonly VanillaProjectileDefinition SantaBombsDefinition = new(
        Width: 10,
        Height: 10,
        AiStyle: VanillaProjectileAiStyles.Thrown,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 10,
        CollisionHeight: 10);

    private static readonly VanillaProjectileDefinition WaffleDefinition = new(
        Width: 18,
        Height: 18,
        AiStyle: VanillaProjectileAiStyles.Thrown,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 18,
        CollisionHeight: 18);

    private static readonly VanillaProjectileDefinition SoundGunDefinition = new(
        Width: 66,
        Height: 66,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: false,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 66,
        CollisionHeight: 66);

    private static readonly VanillaProjectileDefinition MeleeBoneDefinition = new(
        Width: 16,
        Height: 16,
        AiStyle: VanillaProjectileAiStyles.Thrown,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 16,
        CollisionHeight: 16);

    private static readonly VanillaProjectileDefinition BoneShardDefinition = new(
        Width: 6,
        Height: 6,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 6,
        CollisionHeight: 6);

    private static readonly VanillaProjectileDefinition BoneDaggerDefinition = new(
        Width: 22,
        Height: 22,
        AiStyle: VanillaProjectileAiStyles.Thrown,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 10,
        CollisionHeight: 10);

    private static readonly VanillaProjectileDefinition DeerclopsIceSpikeDefinition = new(
        Width: 32,
        Height: 32,
        AiStyle: VanillaProjectileAiStyles.SharpTears,
        TileCollide: false,
        IgnoreWater: true,
        CanCutTiles: true,
        CollisionWidth: 32,
        CollisionHeight: 32);

    private static readonly VanillaProjectileDefinition DeerclopsRubbleDefinition = new(
        Width: 32,
        Height: 32,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: false,
        IgnoreWater: true,
        CanCutTiles: true,
        CollisionWidth: 32,
        CollisionHeight: 32);

    private static readonly VanillaProjectileDefinition DeerclopsShadowHandDefinition = new(
        Width: 40,
        Height: 40,
        AiStyle: VanillaProjectileAiStyles.ShadowHand,
        TileCollide: false,
        IgnoreWater: true,
        CanCutTiles: true,
        CollisionWidth: 40,
        CollisionHeight: 40);

    public static bool TryGet(ProjectileTypeId type, out VanillaProjectileDefinition definition)
    {
        if (type == VanillaProjectileIds.WoodenArrowFriendly)
        {
            definition = WoodenArrowDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.FireArrow)
        {
            definition = FireArrowDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.UnholyArrow)
        {
            definition = UnholyArrowDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.JestersArrow)
        {
            definition = JestersArrowDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.EnchantedBoomerang)
        {
            definition = EnchantedBoomerangDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.Bullet || type == VanillaProjectileIds.SilverBullet)
        {
            definition = BulletDefinition;
            return true;
        }

        if (type.Value is >= 133 and <= 144)
        {
            definition = RocketFamilyDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.MagicMissile)
        {
            definition = MagicMissileDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.Flamelash)
        {
            definition = FlamelashDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.RainbowRodBullet)
        {
            definition = RainbowRodBulletDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.GreenLaser)
        {
            definition = GreenLaserDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.SkeletronSkull)
        {
            definition = SkeletronSkullDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.QueenBeeStinger)
        {
            definition = QueenBeeStingerDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.WallOfFleshEyeLaser)
        {
            definition = WallOfFleshEyeLaserDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.ProbePinkLaser)
        {
            definition = ProbePinkLaserDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.RetinazerDeathLaser)
        {
            definition = RetinazerDeathLaserDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.GolemFireball)
        {
            definition = GolemFireballDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.SpazmatismCursedFlame)
        {
            definition = SpazmatismCursedFlameDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.SpazmatismEyeFire)
        {
            definition = SpazmatismEyeFireDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.PhantasmalBolt)
        {
            definition = PhantasmalBoltDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.CultistBossFireBall || type == VanillaProjectileIds.CultistBossFireBallClone)
        {
            definition = CultistFireballDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.AncientDoomProjectile)
        {
            definition = AncientDoomProjectileDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.QueenSlimeGelAttack)
        {
            definition = QueenSlimeGelAttackDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.GolemEyeBeam)
        {
            definition = GolemEyeBeamDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.PlanteraSeed || type == VanillaProjectileIds.PlanteraPoisonSeed)
        {
            definition = PlanteraSeedDefinition;
            return true;
        }
        if (type == VanillaProjectileIds.SuperStar)
        {
            definition = SuperStarDefinition;
            return true;
        }
        if (type == VanillaProjectileIds.StarCannonStar)
        {
            definition = StarCannonStarDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.Bone)
        {
            definition = BoneDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.Shuriken)
        {
            definition = ShurikenDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.ThrowingKnife)
        {
            definition = ThrowingKnifeDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.Seed)
        {
            definition = SeedDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.PoisonedKnife)
        {
            definition = PoisonedKnifeDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.ConfettiGun ||
            type == VanillaProjectileIds.ConfettiMelee)
        {
            definition = ConfettiDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.RottenEgg)
        {
            definition = RottenEggDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.StarAnise)
        {
            definition = StarAniseDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.BoneArrowFromMerchant)
        {
            definition = BoneArrowFromMerchantDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.NurseSyringeHurt)
        {
            definition = NurseSyringeHurtDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.SantaBombs)
        {
            definition = SantaBombsDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.BoneDagger)
        {
            definition = BoneDaggerDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.Waffle)
        {
            definition = WaffleDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.SoundGun)
        {
            definition = SoundGunDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.MeleeBone)
        {
            definition = MeleeBoneDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.BoneShard)
        {
            definition = BoneShardDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.DeerclopsIceSpike)
        {
            definition = DeerclopsIceSpikeDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.DeerclopsRubble)
        {
            definition = DeerclopsRubbleDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.DeerclopsShadowHand)
        {
            definition = DeerclopsShadowHandDefinition;
            return true;
        }

        definition = default;
        return false;
    }
}
