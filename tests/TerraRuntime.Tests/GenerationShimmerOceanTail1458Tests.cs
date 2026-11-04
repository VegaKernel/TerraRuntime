using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationShimmerOceanTail1458Tests
{
    [Theory]
    [InlineData(true, "82a4c09937fab3e1f0705b42a64d12ed367351e808b2331a3e31f46208acfc9c")]
    [InlineData(false, "322ef3d80af53868b54704e6a9a7f3a93a16e44f4143c0e433f782c886336b86")]
    public void Original_shimmer_ocean_helpers_match_full_grid_liquid_kinds_and_paint(
        bool shimmerLeft, string original)
    {
        // Direct original WorldGen.ShimmerifySideOfWorld(true) followed by
        // PaintTheSandRainbowAndSandWallsBlack, TerrariaServer 1.4.5.8, seed1458.
        // The ignored local reflection probe exercises the two original helpers.
        var workspace = new Workspace(600, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        Assert.True(workspace.TrySetSpawn(shimmerLeft ? 100 : 350, 100));
        workspace.SetVanillaSeedProfile(new(
            VanillaSpecialWorldSeed1458.DrunkWorld | VanillaSpecialWorldSeed1458.CelebrationMk10,
            VanillaSecretWorldSeed1458.None));
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++)
                Set(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        for (int x = 120; x < 180; x++)
            for (int y = 120; y < 300; y++)
                Set(x, y, default);

        Set(100, 100, Cell(1, true, 100, WorldLiquidKind.Water));
        Set(100, 101, Cell(1, true, 200, WorldLiquidKind.Lava));
        Set(100, 102, Cell(1, true, 150, WorldLiquidKind.Honey));
        Set(100, 103, Cell(1, true, 180, WorldLiquidKind.Shimmer));
        Set(100, 104, Cell(373, true));
        Set(100, 105, Cell(374, false));
        Set(100, 106, Cell(375, true));
        Set(300, 100, Cell(1, true, 100));
        Set(350, 100, Cell(1, true, 100));
        Set(100, 320, Cell(1, true, 100));
        Set(110, 100, Cell(1, true, 100, WorldLiquidKind.Lava, 7));
        Set(150, 150, Cell(53, true, wall: 187));
        Set(150, 149, Cell(165, true));
        Set(150, 148, Cell(185, true));
        Set(150, 151, Cell(165, true));
        Set(150, 152, Cell(165, true));
        Set(200, 100, Cell(396, true));
        Set(200, 99, Cell(165, true));
        Set(200, 200, Cell(397, true));
        Set(200, 199, Cell(186, true));
        Set(230, 200, Cell(1, true, wall: 187));
        Set(231, 200, Cell(1, true, wall: 216));
        Set(19, 200, Cell(1, true, wall: 187));

        Assert.NotEqual(original, Hash(workspace));
        FinalPass1458.ApplyOnlyShimmerOceanTail(workspace, TestContext.Current.CancellationToken);

        Assert.Equal(original, Hash(workspace));
        Assert.Equal(906992634, new VanillaUnifiedRandom1458(1458).Next());
        Assert.Equal(shimmerLeft ? WorldLiquidKind.Shimmer : WorldLiquidKind.Water,
            workspace.TileStore.Get(100, 100).LiquidKind);
        Assert.Equal(WorldLiquidKind.Honey, workspace.TileStore.Get(100, 102).LiquidKind);
        Assert.Equal(shimmerLeft ? WorldLiquidKind.Water : WorldLiquidKind.Shimmer,
            workspace.TileStore.Get(350, 100).LiquidKind);
        Assert.Equal(shimmerLeft ? (ushort)709 : (ushort)374, workspace.TileStore.Get(100, 105).Type);
        Assert.Equal((byte)25, workspace.TileStore.Get(230, 200).WallColor);
        Assert.Equal((byte)0, workspace.TileStore.Get(19, 200).WallColor);

        void Set(int x, int y, WorldTile tile) => workspace.TileStore.Set(x, y, in tile);
    }

    [Theory]
    [InlineData(VanillaSpecialWorldSeed1458.DrunkWorld | VanillaSpecialWorldSeed1458.CelebrationMk10, true)]
    [InlineData(VanillaSpecialWorldSeed1458.DrunkWorld, false)]
    [InlineData(VanillaSpecialWorldSeed1458.CelebrationMk10, false)]
    [InlineData(VanillaSpecialWorldSeed1458.DrunkWorld | VanillaSpecialWorldSeed1458.CelebrationMk10 | VanillaSpecialWorldSeed1458.Remix, false)]
    [InlineData(VanillaSpecialWorldSeed1458.DrunkWorld | VanillaSpecialWorldSeed1458.CelebrationMk10 | VanillaSpecialWorldSeed1458.NotTheBees, false)]
    [InlineData(VanillaSpecialWorldSeed1458.DrunkWorld | VanillaSpecialWorldSeed1458.CelebrationMk10 | VanillaSpecialWorldSeed1458.Zenith, false)]
    public void Only_shimmer_ocean_gate_matches_source_flags(VanillaSpecialWorldSeed1458 flags, bool expected) =>
        Assert.Equal(expected, FinalPass1458.HasOnlyShimmerOceanTail(new(flags, VanillaSecretWorldSeed1458.None)));

    private static WorldTile Cell(
        ushort type, bool active, byte amount = 0, WorldLiquidKind kind = WorldLiquidKind.Water, ushort wall = 0) =>
        new()
        {
            Type = type,
            Wall = wall,
            LiquidAmount = amount,
            LiquidKind = kind,
            Flags = active ? WorldTileFlags.Active : WorldTileFlags.None
        };

    private static string Hash(Workspace workspace)
    {
        var snapshot = new StringBuilder(workspace.WidthTiles * workspace.HeightTiles * 20);
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                snapshot.Append(tile.IsActive ? '1' : '0').Append(',')
                    .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append(tile.LiquidAmount).Append(';')
                    .Append((byte)tile.LiquidKind).Append(',')
                    .Append(tile.TileColor).Append(',').Append(tile.WallColor).Append(';');
            }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }
}
