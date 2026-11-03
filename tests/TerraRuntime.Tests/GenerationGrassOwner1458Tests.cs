using System.Reflection;
using System.Text.Json;
using System.IO.Compression;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class GenerationGrassOwner1458Tests
{
    public static IEnumerable<object[]> OriginalRows() { using var d = Read(); foreach (var r in d.RootElement.GetProperty("rows").EnumerateArray()) yield return new object[] { r.GetProperty("id").GetInt32() }; }
    private static JsonDocument Read() { using var s = typeof(GenerationGrassOwner1458Tests).Assembly.GetManifestResourceStream("GrassOwner1458")!; using var z = new GZipStream(s, CompressionMode.Decompress); return JsonDocument.Parse(z); }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Grass_reads_existing_live_phase_and_matches_original_grid_and_random(int id)
    {
        using var d = Read(); var r = d.RootElement.GetProperty("rows").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == id); int seed = r.GetProperty("seed").GetInt32(), mode = r.GetProperty("mode").GetInt32(); var w = new Workspace(80, 120); w.InitializeVanillaGenerationSolidity(); var random = new FixtureRandom(seed); var framer = new GenerationTileFraming1458(w.TileStore, random, w, useWorldGenerationSolidity: true); var grass = new GenerationGrass1458(w.TileStore, random, CancellationToken.None, 50, (x, y) => framer.SquareTileFrame(x, y), w);
        // Populate the borrowed owner after constructing both collaborators, so constructor snapshots are observable.
        int i = 0; foreach (var value in d.RootElement.GetProperty("phases")[r.GetProperty("phase").GetInt32()].EnumerateArray()) w.SetVanillaGenerationSolid(new TileTypeId(i++), value.GetBoolean());
        for (int x = 39; x <= 41; x++) for (int y = 29; y <= 31; y++) At(x, y) = new WorldTile { Type = 1, Flags = WorldTileFlags.Active }; At(40, 30).Type = 0; At(40, 30).TileColor = 7; At(40, 30).Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock; ref WorldTile n = ref At(41, 30); n.Type = (ushort)r.GetProperty("type").GetInt32(); if (mode == 1) n.Flags &= ~WorldTileFlags.Active; if (mode == 2) n.Flags |= WorldTileFlags.Inactive; if (mode == 3) n.TrySetSourceShape(true, 3); grass.Apply(40, 30, 0, 2);
        Assert.Equal(r.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(w)); Assert.Equal(r.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', r.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32())), random.Physical()); Assert.Equal(r.GetProperty("next").GetInt32(), random.Next());
        ref WorldTile At(int x, int y) => ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y)];
    }
    private sealed class FixtureRandom(int seed) : IWorldGenerationVanillaRandom { private readonly VanillaUnifiedRandom1458 random = new(seed); public string Physical() { const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic; return typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random) + ":" + string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random)!); } public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] b) => random.NextBytes(b); }
}
