using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Immutable TerrariaServer 1.4.5.8 wall definition. <see cref="IsPresent"/> distinguishes the valid zero no-wall
/// identity from an occupied wall cell without weakening catalog validation.
/// </summary>
public readonly record struct VanillaWallDefinition(
    WallTypeId Type,
    bool IsHousingWall,
    bool IsDungeonWall,
    bool LetsLightThrough)
{
    public bool IsPresent => Type != VanillaWallIds.None;
}

/// <summary>
/// Typed definition view for all TerrariaServer 1.4.5.8 wall identities. Packed facts mirror Main.wallHouse,
/// Main.wallDungeon and Main.wallLight after Main.Initialize_TileAndNPCData2.
/// </summary>
public static class VanillaWallDefinitionCatalog
{
    public const int Count = VanillaWallIds.Count;

    private static ReadOnlySpan<ulong> HousingWords =>
    [
        0x1000FEFFEFFF1C72UL, 0xFFFFFFF03F347F1CUL, 0x05EBF3FFFFFFFFFFUL,
        0xFFEFFFFF00000000UL, 0xFFFFFFFFFFFFFFFFUL, 0x00007FFF9FFFFFFFUL
    ];

    private static ReadOnlySpan<ulong> DungeonWords =>
    [
        0x0000000000000380UL, 0x0000000FC0000000UL, 0x0000000000000000UL,
        0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL
    ];

    private static ReadOnlySpan<ulong> LightWords =>
    [
        0x0000000000200001UL, 0x00000C0000000000UL, 0x0000010001423C00UL,
        0x0020000000000000UL, 0x6800000000000000UL, 0x0000000000000000UL
    ];

    public static bool TryGet(WallTypeId type, out VanillaWallDefinition definition)
    {
        if (!VanillaWallIds.TryCreate(type.Value, out _))
        {
            definition = default;
            return false;
        }

        definition = new VanillaWallDefinition(
            type,
            Test(type, HousingWords),
            Test(type, DungeonWords),
            Test(type, LightWords));
        return true;
    }

    private static bool Test(WallTypeId type, ReadOnlySpan<ulong> words)
    {
        int value = type.Value;
        return (words[value >> 6] & (1UL << (value & 63))) != 0;
    }
}
