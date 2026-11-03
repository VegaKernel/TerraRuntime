using System.Reflection;
using System.Text.Json;
using System.IO.Compression;
using TerraRuntime.Core;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class GenerationSurfaceTrees1458Tests
{
    private static JsonDocument Read() { using var stream = typeof(GenerationSurfaceTrees1458Tests).Assembly.GetManifestResourceStream("GenerationSurfaceTrees1458")!; using var gzip = new GZipStream(stream, CompressionMode.Decompress); return JsonDocument.Parse(gzip); }
    public static IEnumerable<object[]> OriginalRows() { using var d = Read(); for (int i = 0; i < d.RootElement.GetArrayLength(); i++) yield return new object[] { i }; }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Surface_tree_growth_matches_actual_original_return_grid_and_full_random(int id) { using var data = Read(); var row = data.RootElement[id]; int mode = row.GetProperty("mode").GetInt32(); string family = row.GetProperty("family").GetString()!; var w = Create(family, mode); var random = new RandomAdapter(row.GetProperty("seed").GetInt32()); var framing = new GenerationTileFraming1458(w.TileStore, random, w, useWorldGenerationSolidity: true); int x = mode == 28 ? 1 : mode == 29 ? 94 : 48, floor = mode == 30 ? 50 : 100, probe = mode >= 17 && mode <= 20 || mode == 31 ? floor - 2 : floor; bool result = family switch { "epic" => TreeGrower1458.TryGrowEpic(w.TileStore, x, probe, random, framing), "ordinary" => TreeGrower1458.TryGrow(w.TileStore, x, probe, random, framing: framing), "palm" => PalmTreeGrower1458.TryGrow(w.TileStore, x, probe, random, framing), "sakura" => SettingsTreeGrower1458.TryGrow(w.TileStore, SettingsTreeGrower1458.VanityTree(596), x, probe, random, framing), _ => SettingsTreeGrower1458.TryGrow(w.TileStore, SettingsTreeGrower1458.VanityTree(616), x, probe, random, framing) }; Assert.Equal(row.GetProperty("returned").GetBoolean(), result); Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(w)); Assert.Equal(row.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', row.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32())), random.State()); Assert.Equal(row.GetProperty("next").GetInt32(), random.Next()); }
    private static Workspace Create(string family, int mode)
    {
        const int W = 96, H = 180; var w = new Workspace(W, H); w.InitializeVanillaGenerationSolidity(); int x = mode == 28 ? 1 : mode == 29 ? W - 2 : 48, floor = mode == 30 ? 50 : 100, probe = floor; ushort ground = (ushort)(family == "palm" ? 53 : 2), sapling = (ushort)(family == "sakura" ? 595 : family == "willow" ? 615 : 20);
        for (int column = x - 1; column <= x + 1; column++) At(column, floor) = new() { Flags = WorldTileFlags.Active, Type = ground };
        if (mode == 1) At(x, floor).Flags |= WorldTileFlags.Inactive; if (mode == 2) At(x, floor).TrySetSourceShape(true, 0); if (mode == 3) At(x, floor).TrySetSourceShape(false, 1); if (mode == 4) At(x, floor).TrySetSourceShape(false, 3); if (mode == 5) At(x, floor).Flags &= ~WorldTileFlags.Active; if (mode == 6) At(x, floor).Type = 1;
        if (mode == 7) At(x, floor - 1).LiquidAmount = 128; if (mode == 8) At(x - 1, floor - 1).LiquidAmount = 128; if (mode == 9) At(x + 1, floor - 1).LiquidAmount = 128; if (mode == 10) At(x, floor - 1).Wall = 1; if (mode == 11) At(x, floor - 1).Wall = 63;
        if (mode == 12 || mode == 14) At(x - 1, floor).Flags &= ~WorldTileFlags.Active; if (mode == 13 || mode == 14) At(x + 1, floor).Flags &= ~WorldTileFlags.Active; if (mode == 15) At(x - 1, floor).TrySetSourceShape(false, 1); if (mode == 16) At(x - 1, floor).Flags |= WorldTileFlags.Inactive;
        if (mode >= 17 && mode <= 20 || mode == 31) { probe = floor - 2; for (int row = probe; row < floor; row++) At(x, row) = new() { Flags = WorldTileFlags.Active, Type = (ushort)(mode == 18 ? 590 : mode == 31 ? 595 : sapling) }; if (mode == 19) At(x, probe).Flags &= ~WorldTileFlags.Active; if (mode == 20) At(x, probe + 1).Flags &= ~WorldTileFlags.Active; }
        if (mode == 21 || mode == 22) At(x + 2, floor - (mode == 21 ? 40 : 10)) = new() { Flags = WorldTileFlags.Active, Type = 1 };
        if (mode == 23 || mode == 24 || mode == 27) { At(x, floor - 5) = new() { Flags = WorldTileFlags.Active, Type = (ushort)(mode == 24 ? 590 : 3) }; if (mode == 27) At(x, floor - 5).Flags |= WorldTileFlags.Inactive; }
        if (mode == 25 || mode == 26) { for (int column = x - 4; column <= x + 4; column++) for (int row = floor - 60; row <= floor + 3; row++) { if (column < 0 || column >= W || row < 0) continue; ref var cell = ref At(column, row); cell.TileColor = 9; cell.WallColor = 5; cell.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall; } At(x, floor).TileColor = 7; }
        if (mode >= 32 && mode <= 34) for (int column = x - 1; column <= x + 1; column++) At(column, floor).Type = (ushort)(mode == 32 ? 477 : mode == 33 ? 492 : 633);
        if (mode == 35) At(x - 1, floor - 1).LiquidKind = WorldLiquidKind.Shimmer; if (mode == 36) At(x + 1, floor).Type = 1; if (mode == 37) At(x, floor - 1).TrySetSourceShape(true, 3); if (mode == 38) At(x, floor - 1).Wall = 2; if (mode == 39) At(x, floor).LiquidAmount = 128;
        return w; ref WorldTile At(int column, int row) => ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(column, row)];
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom { private readonly VanillaUnifiedRandom1458 random = new(seed); public string State() { const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic; return typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random) + ":" + string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random)!); } public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] b) => random.NextBytes(b); }
}
