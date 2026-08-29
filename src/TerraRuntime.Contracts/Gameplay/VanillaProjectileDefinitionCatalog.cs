namespace TerraRuntime.Contracts.Gameplay;

/// <summary>Named TerrariaServer 1.4.5.8 projectile AI-style identities currently implemented by TerraRuntime.</summary>
public static class VanillaProjectileAiStyles
{
    public static readonly ProjectileAiStyleId Arrow = new(1);
    public static readonly ProjectileAiStyleId Thrown = new(2);
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
    int CollisionWidth,
    int CollisionHeight)
{
    public float CollisionOffsetX => (Width - CollisionWidth) * 0.5f;

    public float CollisionOffsetY => (Height - CollisionHeight) * 0.5f;
}

/// <summary>
/// Version-pinned TerrariaServer 1.4.5.8 projectile definitions for behavior slices that TerraRuntime can
/// currently simulate end-to-end. Unsupported vanilla IDs deliberately do not receive guessed defaults.
/// </summary>
public static class VanillaProjectileDefinitionCatalog
{
    private static readonly VanillaProjectileDefinition ShurikenDefinition = new(
        Width: 22,
        Height: 22,
        AiStyle: VanillaProjectileAiStyles.Thrown,
        TileCollide: true,
        IgnoreWater: false,
        CollisionWidth: 6,
        CollisionHeight: 6);

    private static readonly VanillaProjectileDefinition ThrowingKnifeDefinition = new(
        Width: 12,
        Height: 12,
        AiStyle: VanillaProjectileAiStyles.Thrown,
        TileCollide: true,
        IgnoreWater: false,
        CollisionWidth: 12,
        CollisionHeight: 12);

    private static readonly VanillaProjectileDefinition PoisonedKnifeDefinition = new(
        Width: 12,
        Height: 12,
        AiStyle: VanillaProjectileAiStyles.Thrown,
        TileCollide: true,
        IgnoreWater: false,
        CollisionWidth: 12,
        CollisionHeight: 12);

    private static readonly VanillaProjectileDefinition BoneDaggerDefinition = new(
        Width: 22,
        Height: 22,
        AiStyle: VanillaProjectileAiStyles.Thrown,
        TileCollide: true,
        IgnoreWater: false,
        CollisionWidth: 10,
        CollisionHeight: 10);

    public static bool TryGet(ProjectileTypeId type, out VanillaProjectileDefinition definition)
    {
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

        if (type == VanillaProjectileIds.PoisonedKnife)
        {
            definition = PoisonedKnifeDefinition;
            return true;
        }

        if (type == VanillaProjectileIds.BoneDagger)
        {
            definition = BoneDaggerDefinition;
            return true;
        }

        definition = default;
        return false;
    }
}
