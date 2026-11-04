using TerraRuntime.World;
namespace TerraRuntime.WorldGeneration.Vanilla;

internal static class PassCatalog1458
{
    public const string SourceVersion = "TerrariaServer 1.4.5.8";
    public const string AddPassesSha256 = "72e757af1fb0a7b565d397bbb0f9fd1d32a2960838ba636c593e122645ab9672";
    public const string RegistrationExpressionSequenceSha256 = "8346fd9162166d740f6cf1b61b338391c538b3fe95af908d8bfaa3a98fdf94bd";
    public const string ResolvedPassNameSequenceSha256 = "1654faeb1831d2c69df8e358664e9152af8ad22c2e3a0c315a772862f4064df5";
    public const string DisablePassesForSpecialSeedsSha256 = "527b2d7b7e5924bcb1ede4e09a371f67e0bf8ccf7bb8516fdc712e58600782f0";

    private static readonly string[] PassNames =
    [
        "Terrain",
        "Jungle",
        "Skyblock",
        "Dunes",
        "Ocean Sand",
        "Sand Patches",
        "Tunnels",
        "Mount Caves",
        "Dirt Wall Backgrounds",
        "Rocks In Dirt",
        "Dirt In Rocks",
        "Clay",
        "Small Holes",
        "Dirt Layer Caves",
        "Rock Layer Caves",
        "Surface Caves",
        "Wavy Caves",
        "Generate Ice Biome",
        "Grass",
        "Jungle",
        "Mud Caves To Grass",
        "Full Desert",
        "Mushroom Patches",
        "Marble",
        "Granite",
        "Floating Islands",
        "Dirt To Mud",
        "Silt",
        "Shinies",
        "Webs",
        "Underworld",
        "Corruption",
        "Lakes",
        "Slush",
        "Dual Dungeons Dither Snake",
        "Dungeon",
        "Mountain Caves",
        "Beaches",
        "Gems",
        "Gravitating Sand",
        "Create Ocean Caves",
        "Shimmer",
        "Clean Up Dirt",
        "Pyramids",
        "Dirt Rock Wall Runner",
        "Living Trees",
        "Wood Tree Walls",
        "Altars",
        "Wet Jungle",
        "Jungle Temple",
        "Hives",
        "Jungle Chests",
        "Settle Liquids",
        "Remove Water From Sand",
        "Oasis",
        "Shell Piles",
        "Smooth World",
        "Waterfalls",
        "Ice",
        "Wall Variety",
        "Life Crystals",
        "Statues",
        "Buried Chests",
        "Surface Chests",
        "Jungle Chests Placement",
        "Water Chests",
        "Spider Caves",
        "Gem Caves",
        "Moss",
        "Temple",
        "Cave Walls",
        "Jungle Trees",
        "Floating Island Houses",
        "Quick Cleanup",
        "Pots",
        "Hellforge",
        "Spreading Grass",
        "Surface Ore and Stone",
        "Place Fallen Log",
        "Traps",
        "Piles",
        "Spawn Point",
        "Grass Wall",
        "Guide",
        "Sunflowers",
        "Planting Trees",
        "Herbs",
        "Dye Plants",
        "Webs And Honey",
        "Weeds",
        "Glowing Mushrooms and Jungle Plants",
        "Jungle Plants",
        "Vines",
        "Flowers",
        "Mushrooms",
        "Gems In Ice Biome",
        "Random Gems",
        "Moss Grass",
        "Muds Walls In Jungle",
        "Larva",
        "Micro Biomes",
        "Settle Liquids Again",
        "Cactus, Palm Trees, & Coral",
        "Tile Cleanup",
        "Lihzahrd Altars",
        "Water Plants",
        "Stalac",
        "Remove Broken Traps",
        "Final Cleanup",
    ];

    private static readonly string[] DualDungeonDisabledPassNames =
    [
        "Generate Ice Biome",
        "Full Desert",
        "Jungle",
        "Jungle Chests",
        "Jungle Chests Placement",
        "Hives",
        "Larva",
        "Jungle Temple",
        "Temple",
        "Lihzahrd Altars",
        "Corruption",
        "Shimmer",
    ];

    private static readonly Dictionary<string, string> OrdinaryReportNames = CreateOrdinaryReportNames();

    internal static bool TryGetOrdinaryReportName(TerraRuntime.Contracts.Gameplay.WorldGenerationPassId id, out string? name) =>
        OrdinaryReportNames.TryGetValue(id.Value, out name);

    private static Dictionary<string, string> CreateOrdinaryReportNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in PassNames)
        {
            // These registrations are conditional in WorldGen.AddGenerationPasses. The ordinary plan's
            // Dither entry is only a compatibility barrier; it does not appear in WorldManifest.
            if (name is "Skyblock" or "Dual Dungeons Dither Snake") continue;
            string id = "terraria:1.4.5.8/" + new string(name.Where(char.IsAsciiLetterOrDigit).ToArray());
            // The source display name has a lowercase conjunction; the existing typed pass ID uses PascalCase.
            if (name == "Glowing Mushrooms and Jungle Plants")
                id = "terraria:1.4.5.8/GlowingMushroomsAndJunglePlants";
            else if (name == "Surface Ore and Stone")
                id = "terraria:1.4.5.8/SurfaceOreAndStone";
            names.TryAdd(id, name); // The extra-Jungle registration shares the same original name.
        }
        return names;
    }

    public static ReadOnlySpan<string> SourceOrderBeforeSpecialSeedFiltering => PassNames;

    public static ReadOnlySpan<string> DisabledForDualDungeons => DualDungeonDisabledPassNames;

    public static bool IsDisabledForDualDungeons(string passName)
    {
        ArgumentNullException.ThrowIfNull(passName);

        foreach (string disabledPassName in DualDungeonDisabledPassNames)
        {
            if (string.Equals(passName, disabledPassName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
