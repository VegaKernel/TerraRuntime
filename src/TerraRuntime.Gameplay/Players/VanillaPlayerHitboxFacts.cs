namespace TerraRuntime.Gameplay.Players;

/// <summary>
/// Source-backed base player hitbox dimensions used by server-side Terraria 1.4.5.8 gameplay rules before
/// equipment, mount or pose-specific adjustments are applied.
/// </summary>
public static class VanillaPlayerHitboxFacts
{
    public const float BaseWidth = 20f;
    public const float BaseHeight = 42f;
}

/// <summary>Source-pinned Player dimensions after Mount.SetMount in TerrariaServer 1.4.5.8.</summary>
public static class VanillaPlayerMountHitbox1458
{
    public static (float Width, float Height) Resolve(ushort mountType) => mountType switch
    {
        55 => (14f, 14f), // RatPlayerSize
        56 => (20f, 18f), // BatPlayerSize
        61 => (8f, 14f), // PixiePlayerSize
        _ => (VanillaPlayerHitboxFacts.BaseWidth, VanillaPlayerHitboxFacts.BaseHeight + HeightBoost(mountType))
    };

    private static float HeightBoost(ushort type) => type switch
    {
        0 or 1 or 2 or 3 or 50 => 20f,
        4 => 26f,
        5 or 7 or 8 or 9 or 17 => 16f,
        6 or 13 or 15 or 16 or 18 or 19 or 20 or 21 or 22 or 24 or 25 or 26 or 27 or 28 or 29 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 38 or 39 or 51 or 53 => 10f,
        10 or 40 or 41 or 42 or 47 => 34f,
        11 or 37 or 43 => 12f,
        12 or 48 => 14f,
        14 => 6f,
        44 => 24f,
        45 => 25f,
        49 => 8f,
        62 or 63 or 64 or 65 => 4f,
        _ => 0f
    };
}
