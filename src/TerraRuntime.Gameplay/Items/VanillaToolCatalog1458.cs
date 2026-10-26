using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

public readonly record struct ToolDefinition1458(ItemTypeId Item, PrefixId Prefix,
    int Pick, int Axe, int Hammer, int UseTime, int UseAnimation, int UseStyle, int TileBoost, bool Channel, bool AutoReuse, bool UseTurn, bool Melee, bool NoMelee, bool NoUseGraphic, int Shoot, float ShootSpeed, int Damage, float KnockBack, float Scale, bool IsDrill, bool IsChainsaw)
{
    public int Crit { get; init; }
    public bool NoMeleeSpeedBonus { get; init; }
}

public readonly record struct ToolLaunchStats1458(ProjectileTypeId Projectile,
    int Damage, float KnockBack, float Speed);

/// <summary>All retained mining tools and their stored prefix fields, not use authorization.</summary>
public static class VanillaToolCatalog1458
{
    public const int Count = 106;
    public static bool TryGet(ItemTypeId item, out ToolDefinition1458 definition)
    {
        definition = item.Value switch
        {
            1 => new(item, default, 40, 0, 0, 13, 20, 1, 0, false, true, true, true, false, false, 0, 0.0f, 5, 2.0f, 1.0f, false, false),
            7 => new(item, default, 0, 0, 40, 20, 30, 1, 0, false, true, true, true, false, false, 0, 0.0f, 7, 5.5f, 1.0f, false, false),
            10 => new(item, default, 0, 9, 0, 19, 27, 1, 0, false, true, true, true, false, false, 0, 0.0f, 5, 4.5f, 1.0f, false, false),
            45 => new(item, default, 0, 15, 0, 15, 30, 1, 0, false, true, false, true, false, false, 0, 0.0f, 20, 6.0f, 1.0f, false, false),
            103 => new(item, default, 65, 0, 0, 15, 20, 1, 0, false, true, true, true, false, false, 0, 0.0f, 9, 3.0f, 1.0f, false, false),
            104 => new(item, default, 0, 0, 55, 19, 45, 1, 0, false, true, false, true, false, false, 0, 0.0f, 24, 6.0f, 1.0f, false, false),
            122 => new(item, default, 100, 0, 0, 18, 23, 1, 0, false, true, true, true, false, false, 0, 0.0f, 12, 2.0f, 1.0f, false, false),
            196 => new(item, default, 0, 0, 25, 25, 37, 1, -1, false, true, true, true, false, false, 0, 0.0f, 2, 5.5f, 1.0f, false, false),
            204 => new(item, default, 0, 20, 60, 16, 30, 1, 0, false, true, true, true, false, false, 0, 0.0f, 20, 7.0f, 1.0f, false, false),
            217 => new(item, default, 0, 30, 70, 14, 27, 1, 0, false, true, true, true, false, false, 0, 0.0f, 20, 7.0f, 1.0f, false, false),
            367 => new(item, default, 0, 0, 80, 14, 27, 1, 0, false, true, true, true, false, false, 0, 0.0f, 26, 7.5f, 1.0f, false, false),
            383 => new(item, default, 0, 14, 0, 7, 15, 5, -1, true, false, false, true, true, true, 57, 40.0f, 23, 2.75f, 1.0f, false, true),
            384 => new(item, default, 0, 17, 0, 6, 15, 5, -1, true, false, false, true, true, true, 58, 40.0f, 29, 3.0f, 1.0f, false, true),
            385 => new(item, default, 110, 0, 0, 7, 15, 5, -1, true, false, false, true, true, true, 59, 32.0f, 10, 0.5f, 1.0f, true, false),
            386 => new(item, default, 150, 0, 0, 6, 15, 5, -1, true, false, false, true, true, true, 60, 32.0f, 15, 0.5f, 1.0f, true, false),
            387 => new(item, default, 0, 20, 0, 4, 15, 5, -1, true, false, false, true, true, true, 61, 40.0f, 33, 4.5f, 1.0f, false, true),
            388 => new(item, default, 180, 0, 0, 4, 15, 5, -1, true, false, false, true, true, true, 62, 32.0f, 20, 0.5f, 1.0f, true, false),
            579 => new(item, default, 200, 22, 0, 4, 15, 5, -1, true, false, false, true, true, true, 107, 36.0f, 35, 4.75f, 1.0f, true, true),
            654 => new(item, default, 0, 0, 40, 20, 30, 1, 0, false, true, true, true, false, false, 0, 0.0f, 7, 5.5f, 1.0f, false, false),
            657 => new(item, default, 0, 0, 35, 23, 33, 1, 0, false, true, true, true, false, false, 0, 0.0f, 4, 5.5f, 1.0f, false, false),
            660 => new(item, default, 0, 0, 55, 19, 29, 1, 0, false, true, true, true, false, false, 0, 0.0f, 10, 5.5f, 1.0f, false, false),
            776 => new(item, default, 110, 0, 0, 13, 25, 1, 0, false, true, true, true, false, false, 0, 0.0f, 10, 5.0f, 1.0f, false, false),
            777 => new(item, default, 150, 0, 0, 10, 25, 1, 0, false, true, true, true, false, false, 0, 0.0f, 15, 5.0f, 1.0f, false, false),
            778 => new(item, default, 180, 0, 0, 8, 25, 1, 0, false, true, true, true, false, false, 0, 0.0f, 20, 5.0f, 1.0f, false, false),
            787 => new(item, default, 0, 0, 85, 14, 27, 1, 0, false, true, true, true, false, false, 0, 0.0f, 26, 7.5f, 1.1f, false, false),
            797 => new(item, default, 0, 0, 55, 19, 40, 1, 0, false, true, false, true, false, false, 0, 0.0f, 23, 6.0f, 1.0f, false, false),
            798 => new(item, default, 70, 0, 0, 14, 22, 1, 0, false, true, true, true, false, false, 0, 0.0f, 12, 3.5f, 1.0f, false, false),
            799 => new(item, default, 0, 15, 0, 15, 32, 1, 0, false, true, false, true, false, false, 0, 0.0f, 22, 6.0f, 1.0f, false, false),
            882 => new(item, default, 35, 0, 0, 16, 25, 1, 0, false, true, true, true, false, false, 0, 0.0f, 4, 2.0f, 1.0f, false, false),
            922 => new(item, default, 0, 0, 40, 20, 30, 1, 0, false, true, true, true, false, false, 0, 0.0f, 7, 5.5f, 1.0f, false, false),
            990 => new(item, default, 200, 22, 0, 7, 25, 1, 0, false, true, true, true, false, false, 0, 0.0f, 35, 4.75f, 1.1f, false, false),
            991 => new(item, default, 0, 14, 0, 13, 35, 1, 0, false, true, true, true, false, false, 0, 0.0f, 33, 5.0f, 1.1f, false, false),
            992 => new(item, default, 0, 17, 0, 10, 35, 1, 0, false, true, true, true, false, false, 0, 0.0f, 39, 6.0f, 1.1f, false, false),
            993 => new(item, default, 0, 20, 0, 8, 35, 1, 0, false, true, true, true, false, false, 0, 0.0f, 43, 7.0f, 1.1f, false, false),
            1188 => new(item, default, 130, 0, 0, 12, 25, 1, 0, false, true, true, true, false, false, 0, 0.0f, 12, 5.0f, 1.0f, false, false),
            1189 => new(item, default, 130, 0, 0, 7, 15, 5, -1, true, false, false, true, true, true, 213, 32.0f, 12, 0.5f, 1.0f, true, false),
            1190 => new(item, default, 0, 15, 0, 7, 15, 5, -1, true, false, false, true, true, true, 214, 40.0f, 26, 2.9f, 1.0f, false, true),
            1195 => new(item, default, 165, 0, 0, 9, 25, 1, 0, false, true, true, true, false, false, 0, 0.0f, 17, 5.0f, 1.0f, false, false),
            1196 => new(item, default, 165, 0, 0, 5, 15, 5, -1, true, false, false, true, true, true, 216, 32.0f, 17, 0.5f, 1.0f, true, false),
            1197 => new(item, default, 0, 18, 0, 5, 15, 5, -1, true, false, false, true, true, true, 217, 40.0f, 31, 3.75f, 1.0f, false, true),
            1202 => new(item, default, 190, 0, 0, 7, 25, 1, 0, false, true, true, true, false, false, 0, 0.0f, 27, 5.0f, 1.0f, false, false),
            1203 => new(item, default, 190, 0, 0, 4, 15, 5, -1, true, false, false, true, true, true, 219, 32.0f, 27, 0.5f, 1.0f, true, false),
            1204 => new(item, default, 0, 21, 0, 4, 15, 5, -1, true, false, false, true, true, true, 220, 40.0f, 34, 4.6f, 1.0f, false, true),
            1222 => new(item, default, 0, 15, 0, 12, 35, 1, 0, false, true, true, true, false, false, 0, 0.0f, 36, 5.5f, 1.1f, false, false),
            1223 => new(item, default, 0, 18, 0, 9, 35, 1, 0, false, true, true, true, false, false, 0, 0.0f, 41, 6.5f, 1.1f, false, false),
            1224 => new(item, default, 0, 21, 0, 7, 35, 1, 0, false, true, true, true, false, false, 0, 0.0f, 44, 7.5f, 1.1f, false, false),
            1230 => new(item, default, 200, 0, 0, 7, 25, 1, 1, false, true, true, true, false, false, 0, 0.0f, 40, 5.0f, 1.0f, false, false),
            1231 => new(item, default, 200, 0, 0, 4, 15, 5, 0, true, false, false, true, true, true, 223, 40.0f, 35, 1.0f, 1.0f, true, false),
            1232 => new(item, default, 0, 23, 0, 4, 15, 5, 0, true, false, false, true, true, true, 224, 46.0f, 50, 4.6f, 1.0f, false, true),
            1233 => new(item, default, 0, 23, 0, 7, 30, 1, 1, false, true, true, true, false, false, 0, 0.0f, 70, 7.0f, 1.15f, false, false),
            1234 => new(item, default, 0, 0, 90, 14, 35, 1, 1, false, true, true, true, false, false, 0, 0.0f, 80, 8.0f, 1.0f, false, false),
            1262 => new(item, default, 0, 0, 90, 4, 15, 5, 0, true, false, false, true, true, true, 252, 46.0f, 45, 5.2f, 1.0f, false, false),
            1294 => new(item, default, 210, 25, 0, 6, 16, 1, 1, false, true, true, true, false, false, 0, 0.0f, 34, 5.5f, 1.15f, false, false),
            1305 => new(item, default, 0, 35, 100, 7, 23, 1, 1, false, true, false, true, false, false, 0, 0.0f, 72, 7.25f, 1.15f, false, false),
            1320 => new(item, default, 55, 0, 0, 11, 19, 1, 0, false, true, true, true, false, false, 0, 0.0f, 8, 3.0f, 1.0f, false, false),
            1506 => new(item, default, 200, 0, 0, 8, 24, 1, 3, false, true, true, true, false, false, 0, 0.0f, 32, 5.25f, 1.0f, false, false),
            1507 => new(item, default, 0, 30, 90, 8, 28, 1, 3, false, true, true, true, false, false, 0, 0.0f, 60, 7.0f, 1.05f, false, false),
            1917 => new(item, default, 55, 0, 0, 16, 20, 1, 0, false, true, true, true, false, false, 0, 0.0f, 7, 2.5f, 1.0f, false, false),
            2176 => new(item, default, 200, 25, 0, 4, 12, 1, -1, false, true, true, true, false, false, 0, 0.0f, 45, 6.0f, 1.0f, false, false),
            2320 => new(item, default, 0, 0, 70, 14, 24, 1, 0, false, true, false, true, false, false, 0, 0.0f, 24, 6.0f, 1.05f, false, false),
            2341 => new(item, default, 59, 0, 0, 13, 22, 1, 0, false, true, true, true, false, false, 0, 0.0f, 16, 3.0f, 1.15f, false, false),
            2342 => new(item, default, 0, 14, 0, 4, 15, 5, -1, true, false, false, true, true, true, 369, 48.0f, 13, 2.25f, 1.0f, false, true),
            2516 => new(item, default, 0, 0, 35, 23, 33, 1, 0, false, true, true, true, false, false, 0, 0.0f, 4, 5.5f, 1.0f, false, false),
            2746 => new(item, default, 0, 0, 35, 23, 33, 1, 0, false, true, true, true, false, false, 0, 0.0f, 4, 5.5f, 1.0f, false, false),
            2774 => new(item, default, 225, 0, 0, 2, 15, 5, 2, true, false, false, true, true, true, 428, 32.0f, 50, 0.5f, 1.0f, true, false),
            2776 => new(item, default, 225, 0, 0, 6, 12, 1, 4, false, true, true, true, false, false, 0, 0.0f, 80, 5.5f, 1.0f, false, false),
            2779 => new(item, default, 225, 0, 0, 2, 15, 5, 2, true, false, false, true, true, true, 430, 32.0f, 50, 0.5f, 1.0f, true, false),
            2781 => new(item, default, 225, 0, 0, 6, 12, 1, 4, false, true, true, true, false, false, 0, 0.0f, 80, 5.5f, 1.0f, false, false),
            2784 => new(item, default, 225, 0, 0, 2, 15, 5, 2, true, false, false, true, true, true, 432, 32.0f, 50, 0.5f, 1.0f, true, false),
            2786 => new(item, default, 225, 0, 0, 6, 12, 1, 4, false, true, true, true, false, false, 0, 0.0f, 80, 5.5f, 1.0f, false, false),
            2798 => new(item, default, 230, 0, 0, 6, 25, 5, 11, true, false, false, true, true, true, 445, 36.0f, 35, 4.75f, 1.0f, false, false),
            3098 => new(item, default, 0, 30, 0, 4, 15, 5, -1, true, false, false, true, true, true, 509, 48.0f, 120, 8.0f, 1.0f, false, true),
            3464 => new(item, default, 225, 0, 0, 2, 15, 5, 2, true, false, false, true, true, true, 609, 32.0f, 50, 0.5f, 1.0f, true, false),
            3466 => new(item, default, 225, 0, 0, 6, 12, 1, 4, false, true, true, true, false, false, 0, 0.0f, 80, 5.5f, 1.0f, false, false),
            3481 => new(item, default, 0, 0, 59, 21, 27, 1, 0, false, true, true, true, false, false, 0, 0.0f, 10, 5.5f, 1.0f, false, false),
            3482 => new(item, default, 0, 12, 0, 17, 25, 1, 0, false, true, true, true, false, false, 0, 0.0f, 8, 4.5f, 1.0f, false, false),
            3485 => new(item, default, 59, 0, 0, 15, 19, 1, 0, false, true, true, true, false, false, 0, 0.0f, 7, 2.0f, 1.05f, false, false),
            3487 => new(item, default, 0, 0, 50, 25, 28, 1, 0, false, true, true, true, false, false, 0, 0.0f, 9, 5.5f, 1.0f, false, false),
            3488 => new(item, default, 0, 11, 0, 18, 26, 1, 0, false, true, true, true, false, false, 0, 0.0f, 7, 4.5f, 1.0f, false, false),
            3491 => new(item, default, 50, 0, 0, 19, 21, 1, 0, false, true, true, true, false, false, 0, 0.0f, 6, 2.0f, 1.05f, false, false),
            3493 => new(item, default, 0, 0, 43, 19, 29, 1, 0, false, true, true, true, false, false, 0, 0.0f, 8, 5.5f, 1.0f, false, false),
            3494 => new(item, default, 0, 10, 0, 19, 28, 1, 0, false, true, true, true, false, false, 0, 0.0f, 6, 4.5f, 1.0f, false, false),
            3497 => new(item, default, 43, 0, 0, 12, 19, 1, 0, false, true, true, true, false, false, 0, 0.0f, 6, 2.0f, 1.025f, false, false),
            3499 => new(item, default, 0, 0, 38, 21, 31, 1, 0, false, true, true, true, false, false, 0, 0.0f, 6, 5.5f, 1.0f, false, false),
            3500 => new(item, default, 0, 8, 0, 20, 28, 1, 0, false, true, true, true, false, false, 0, 0.0f, 4, 4.5f, 1.0f, false, false),
            3503 => new(item, default, 35, 0, 0, 14, 21, 1, 0, false, true, true, true, false, false, 0, 0.0f, 5, 2.0f, 0.95f, false, false),
            3505 => new(item, default, 0, 0, 35, 23, 33, 1, -1, false, true, true, true, false, false, 0, 0.0f, 4, 5.5f, 1.0f, false, false),
            3506 => new(item, default, 0, 7, 0, 21, 30, 1, -1, false, true, true, true, false, false, 0, 0.0f, 3, 4.5f, 1.0f, false, false),
            3509 => new(item, default, 35, 0, 0, 15, 23, 1, -1, false, true, true, true, false, false, 0, 0.0f, 4, 2.0f, 0.9f, false, false),
            3511 => new(item, default, 0, 0, 45, 19, 29, 1, 0, false, true, true, true, false, false, 0, 0.0f, 9, 5.5f, 1.0f, false, false),
            3512 => new(item, default, 0, 10, 0, 18, 26, 1, 0, false, true, true, true, false, false, 0, 0.0f, 6, 4.5f, 1.0f, false, false),
            3515 => new(item, default, 45, 0, 0, 11, 19, 1, 0, false, true, true, true, false, false, 0, 0.0f, 6, 2.0f, 1.05f, false, false),
            3517 => new(item, default, 0, 0, 55, 23, 28, 1, 0, false, true, true, true, false, false, 0, 0.0f, 9, 5.5f, 1.0f, false, false),
            3518 => new(item, default, 0, 11, 0, 18, 26, 1, 0, false, true, true, true, false, false, 0, 0.0f, 7, 4.5f, 1.0f, false, false),
            3521 => new(item, default, 55, 0, 0, 17, 20, 1, 0, false, true, true, true, false, false, 0, 0.0f, 6, 2.0f, 1.05f, false, false),
            3522 => new(item, default, 0, 30, 100, 7, 28, 1, 4, false, true, true, true, false, false, 0, 0.0f, 60, 7.0f, 1.0f, false, false),
            3523 => new(item, default, 0, 30, 100, 7, 28, 1, 4, false, true, true, true, false, false, 0, 0.0f, 60, 7.0f, 1.0f, false, false),
            3524 => new(item, default, 0, 30, 100, 7, 28, 1, 4, false, true, true, true, false, false, 0, 0.0f, 60, 7.0f, 1.0f, false, false),
            3525 => new(item, default, 0, 30, 100, 7, 28, 1, 4, false, true, true, true, false, false, 0, 0.0f, 60, 7.0f, 1.0f, false, false),
            4059 => new(item, default, 55, 0, 0, 14, 18, 1, 0, false, true, true, true, false, false, 0, 0.0f, 8, 4.0f, 1.0f, false, false),
            4317 => new(item, default, 0, 30, 80, 11, 27, 1, 0, false, true, false, true, false, false, 0, 0.0f, 30, 7.0f, 1.0f, false, false),
            4711 => new(item, default, 0, 0, 0, 14, 22, 1, 0, false, true, true, true, false, false, 0, 0.0f, 12, 3.5f, 1.0f, false, false),
            5095 => new(item, default, 0, 30, 0, 15, 15, 1, 0, false, true, false, true, false, false, 0, 0.0f, 27, 5.0f, 1.2f, false, false) { Crit = 10 },
            5283 => new(item, default, 0, 0, 45, 20, 30, 1, 0, false, true, true, true, false, false, 0, 0.0f, 9, 5.5f, 1.0f, false, false),
            5295 => new(item, default, 0, 25, 0, 12, 24, 1, 0, false, true, false, true, false, false, 0, 0.0f, 20, 5.0f, 1.0f, false, false),
            5519 => new(item, default, 0, 30, 0, 20, 20, 1, 0, false, false, false, true, true, true, 1052, 10.5f, 20, 5.0f, 1.0f, false, false),
            _ => default
        };
        return !definition.Item.IsNone;
    }

    // Eight original variant contexts prove only Remix's5283 branch changes
    // these tool defaults. Context remains explicit and never inferred from OS.
    public static bool TryResolve(ItemTypeId item, PrefixId prefix, bool remixWorld,
        bool windowsItemPrefixArithmetic, out ToolDefinition1458 definition)
    {
        if (!TryGet(item, out definition)) return false;
        if (item.Value == 5283 && remixWorld)
            definition = definition with { Hammer = 35, Damage = 4 };
        if (prefix.Value == 0) return true;
        if ((uint)prefix.Value >= VanillaItemPrefixTable1458.PrefixCount ||
            !VanillaItemPrefixTable1458.TryGet(item, out var acceptance) ||
            !VanillaItemPrefixTable1458.TryGetStatModifiers(prefix, out var modifiers)) return false;
        acceptance = acceptance with
        {
            Damage = (short)definition.Damage,
            UseAnimation = (byte)definition.UseAnimation
        };
        if (!VanillaItemPrefixTable1458.Accepts(acceptance, prefix.Value,
            windowsItemPrefixArithmetic)) return false;
        float size = SizeMultipliers[prefix.Value];
        if (!float.IsFinite(size)) return false;
        definition = definition with
        {
            Prefix = prefix,
            Crit = definition.Crit + modifiers.CritBonus,
            Damage = Round(definition.Damage, modifiers.DamageMultiplier, windowsItemPrefixArithmetic),
            UseTime = Round(definition.UseTime, modifiers.SpeedMultiplier, windowsItemPrefixArithmetic),
            UseAnimation = Round(definition.UseAnimation, modifiers.SpeedMultiplier, windowsItemPrefixArithmetic),
            ShootSpeed = definition.ShootSpeed * modifiers.ShootSpeedMultiplier,
            KnockBack = definition.KnockBack * modifiers.KnockBackMultiplier,
            Scale = definition.Scale * size
        };
        return true;
    }

    private static int Round(int value, float multiplier, bool windows) =>
        (int)Math.Round(windows ? value * (double)multiplier : value * multiplier);

    public static bool TryGetLaunchStats(in ToolDefinition1458 tool, float meleeDamage,
        float meleeSpeed, bool knockBackGlove, bool knockBackBuff, bool windowsArithmetic,
        out ToolLaunchStats1458 launch)
    {
        launch = default;
        if (tool.Shoot <= 0 || tool.Damage < 0 || !float.IsFinite(meleeDamage) || meleeDamage < 0f ||
            !float.IsFinite(meleeSpeed) || meleeSpeed <= 0f ||
            !float.IsFinite(tool.KnockBack) || tool.KnockBack < 0f ||
            !float.IsFinite(tool.ShootSpeed) || tool.ShootSpeed < 0f) return false;
        double damage = windowsArithmetic
            ? tool.Damage * (double)meleeDamage + 5E-06f
            : tool.Damage * meleeDamage + 5E-06f;
        if (damage >= int.MaxValue) return false;
        int storedDamage = tool.Damage <= 0 ? tool.Damage : (int)damage;
        float knockback = tool.KnockBack;
        if (knockBackGlove) knockback *= 2f;
        if (knockBackBuff) knockback *= 1.5f;
        // All24 retained tool-shot types have the actual source velocity-scaling
        // exemption flag FALSE; this is independent of the item animation flag.
        float speed = tool.ShootSpeed / meleeSpeed;
        if (!float.IsFinite(speed) || !float.IsFinite(knockback)) return false;
        launch = new(new ProjectileTypeId(tool.Shoot), storedDamage, knockback, speed);
        return true;
    }

    // NaN marks prefixes no retained tool accepts in the actual0..97 corpus.
    private static ReadOnlySpan<float> SizeMultipliers =>
    [
        1.0f, 1.12f, 1.18f, 1.05f, 1.1f, 1.0f, 1.0f, 0.82f, 0.87f, 0.9f, 1.0f, 0.9f,
        1.1f, 1.1f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,
        1.0f, 1.0f, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, 1.0f, float.NaN, float.NaN, float.NaN, float.NaN,
        1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,
        1.0f, 1.0f, 1.0f, 1.0f, float.NaN, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,
        1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,
        1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.1f, 1.0f, float.NaN,
        1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,
        1.0f, 1.0f,
    ];
}
