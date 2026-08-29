namespace TerraRuntime.Contracts.Gameplay;

/// <summary>Named TerrariaServer 1.4.5.8 projectile AI-style identities currently implemented by TerraRuntime.</summary>
public static class VanillaProjectileAiStyles
{
    public static readonly ProjectileAiStyleId Arrow = new(1);
    public static readonly ProjectileAiStyleId Thrown = new(2);
    public static readonly ProjectileAiStyleId Boomerang = new(3);
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

    private static readonly VanillaProjectileDefinition GreenLaserDefinition = new(
        Width: 4,
        Height: 4,
        AiStyle: VanillaProjectileAiStyles.Arrow,
        TileCollide: true,
        IgnoreWater: false,
        CanCutTiles: true,
        CollisionWidth: 4,
        CollisionHeight: 4);

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

        if (type == VanillaProjectileIds.Bullet)
        {
            definition = BulletDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.GreenLaser)
        {
            definition = GreenLaserDefinition;
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

        definition = default;
        return false;
    }
}
