using TerraRuntime.Core;
using TerraRuntime.WorldGeneration.Runtime;
using System.Text.Json;
using System.IO.Compression;
using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;
namespace TerraRuntime.Tests;

// Official GrowTree with both generation flags set, fresh proper Main metadata and two identical captures.
// Surface99/100/101 straddles the strict Remix bare-branch boundary at ground100.
public sealed class GenerationRemixBareTree1458Tests
{
    private static JsonDocument Read()
    {
        using var raw = typeof(GenerationRemixBareTree1458Tests).Assembly.GetManifestResourceStream("GenerationRemixBareTree1458")!;
        using var zip = new GZipStream(raw, CompressionMode.Decompress);
        return JsonDocument.Parse(zip);
    }
    public static IEnumerable<object[]> Rows()
    {
        using var data = Read();
        for (int i = 0; i < data.RootElement.GetArrayLength(); i++)
            yield return new object[] { i };
    }
    [Theory]
    [MemberData(nameof(Rows))]
    public void Original_tree_bare_branch_boundary_keeps_whole_grid_and_full_physical_random(int id)
    {
        using var data = Read();
        var e = data.RootElement[id];
        const int W = 96, H = 180, x = 48, floor = 100;
        var w = new Workspace(W, H);
        w.InitializeVanillaGenerationSolidity();
        Assert.True(w.TrySetLayers(e.GetProperty("surface").GetInt32(), 150));
        var request = new WorldGenerationRequest(new("test:original-tree-bare"), "Direct", 0, W, H) { SeedText = e.GetProperty("remix").GetBoolean() ? "dontdigup" : "1458" };
        w.SetVanillaSeedProfile(WorldSeedResolver1458.Resolve(in request));
        for (int column = x - 1; column <= x + 1; column++)
        {
            ref var cell = ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(column, floor)];
            cell.Flags = WorldTileFlags.Active;
            cell.Type = 2;
        }
        if (e.GetProperty("painted").GetBoolean())
        {
            for (int column = x - 4; column <= x + 4; column++)
            for (int row = floor - 60; row <= floor + 3; row++)
            {
                ref var cell = ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(column, row)];
                cell.TileColor = 9;
                cell.WallColor = 5;
                cell.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall;
            }
            w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, floor)].TileColor = 7;
        }
        var random = new Adapter(e.GetProperty("seed").GetInt32());
        var framing = new GenerationTileFraming1458(w.TileStore, random, w, useWorldGenerationSolidity: true);
        Assert.Equal(e.GetProperty("returned").GetBoolean(), TreeGrower1458.TryGrow(w.TileStore, x, floor, random, e.GetProperty("ignore").GetBoolean(), framing));
        Assert.Equal(e.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(w));
        const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(e.GetProperty("cursor").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random.Value)!);
        Assert.Equal(e.GetProperty("slots").EnumerateArray().Select(v => v.GetInt32()), (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random.Value)!);
        Assert.Equal(e.GetProperty("next").GetInt32(), random.Next());
    }
    private sealed class Adapter(int seed) : IWorldGenerationVanillaRandom
    {
        internal readonly VanillaUnifiedRandom1458 Value = new(seed); public int Next() => Value.Next(); public int Next(int max) => Value.Next(max); public int Next(int min, int max) => Value.Next(min, max); public double NextDouble() => Value.NextDouble(); public void NextBytes(byte[] b) => Value.NextBytes(b);
    }
}
