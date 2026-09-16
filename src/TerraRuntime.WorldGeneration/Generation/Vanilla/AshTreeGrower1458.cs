using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary generation-time GrowTreeWithSettings(Tree_Ash), TerrariaServer 1.4.5.8.
/// The growth algorithm itself lives in <see cref="SettingsTreeGrower1458"/>; this type owns only the profile.</summary>
internal static class AshTreeGrower1458
{
    internal const ushort AshGrass = 633, AshTree = 634;

    // WorldGen.GrowTreeSettings.Profiles.Tree_Ash: AshTreeGroundTest is exactly 633, the wall test is the default
    // plant-growth set, height is Next(7, 13) and the crown needs four rows of padding.
    private static readonly SettingsTreeProfile1458 Profile = new(
        AshTree,
        SaplingTileType: 20,
        HeightMinimum: 7,
        HeightMaximumInclusive: 12,
        TopPadding: 4,
        IsGround: static type => type.Value == AshGrass,
        AllowsWall: TreeGrowthCatalog1458.AllowsPlantGrowth);

    public static bool TryGrow(WorldTileStore store, int x, int checkedY, IWorldGenerationVanillaRandom random) =>
        SettingsTreeGrower1458.TryGrow(store, Profile, x, checkedY, random);
}
