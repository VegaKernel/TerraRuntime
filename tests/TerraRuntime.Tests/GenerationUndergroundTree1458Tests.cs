using System.Reflection;
using System.Security.Cryptography;
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

public sealed class GenerationUndergroundTree1458Tests
{
    public static IEnumerable<object[]> OriginalRows() { using var doc = Read(); foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray()) yield return new object[] { row.GetProperty("id").GetInt32() }; }
    private static JsonDocument Read() { using var s = typeof(GenerationUndergroundTree1458Tests).Assembly.GetManifestResourceStream("UndergroundTree1458")!; using var z = new GZipStream(s, CompressionMode.Decompress); return JsonDocument.Parse(z); }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Underground_growth_matches_original_full_grid_rng_and_next(int id)
    {
        // Direct original GrowUndergroundTree with explicit imported native68 exit phase. The separate macro
        // fixture exercises uninterrupted actual generation; this bounded fixture imports only its phase context.
        using var doc = Read(); var row = doc.RootElement.GetProperty("rows").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == id);
        int seed = row.GetProperty("seed").GetInt32(), mode = row.GetProperty("mode").GetInt32(); const int W = 64, H = 80, X = 32, Y = 55;
        var workspace = new Workspace(W, H); workspace.InitializeVanillaGenerationSolidity(); int type = 0;
        foreach (var v in doc.RootElement.GetProperty("phase").EnumerateArray()) workspace.SetVanillaGenerationSolid(new TileTypeId(type++), v.GetBoolean()); workspace.SetVanillaGenerationSolid(new TileTypeId(379), true);
        var store = workspace.TileStore; for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) store.Set(x, y, new WorldTile { Wall = 64 }); for (int x = 0; x < W; x++) At(x, Y) = new WorldTile { Type = 60, Wall = 64, Flags = WorldTileFlags.Active };
        ref WorldTile ground = ref At(X, Y);
        if (mode is 1 or 3) At(X - 1, Y).Type = 1; if (mode is 2 or 3) At(X + 1, Y).Type = 1;
        if (mode is 4 or 5 or 6 or 16 or 17 or 18) { ref WorldTile t = ref At(X, Y - 3); t.Flags |= WorldTileFlags.Active; t.Type = (ushort)(mode switch { 4 => 3, 5 => 595, 6 => 1, 16 => 637, 17 => 232, _ => 20 }); }
        if (mode == 7) ground.TrySetSourceShape(true, 0); if (mode == 8) ground.TrySetSourceShape(false, 1); if (mode == 9) ground.Flags |= WorldTileFlags.Inactive;
        if (mode == 10) { At(X, Y - 2).LiquidAmount = 128; At(X, Y - 2).LiquidKind = WorldLiquidKind.Water; }
        if (mode == 11) { ref WorldTile t = ref At(X, Y - 2); t.TileColor = 7; t.WallColor = 5; t.TrySetSourceShape(true, 3); t.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall | WorldTileFlags.WireRed | WorldTileFlags.WireBlue | WorldTileFlags.WireGreen | WorldTileFlags.WireYellow; }
        if (mode == 12) ground.Type = 2; if (mode == 13) ground.Flags &= ~WorldTileFlags.Active; if (mode == 14) At(X - 1, Y).Type = 147; if (mode == 15) At(X + 1, Y).Type = 161;
        if (mode == 19) { ground.TileColor = 13; ground.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock; }
        if (mode == 20) { At(X, Y - 2).LiquidAmount = 128; At(X, Y - 2).LiquidKind = WorldLiquidKind.Lava; }
        if (mode == 21) ground.TrySetSourceShape(true, 3);
        if (mode == 22) { At(X - 1, Y).Flags |= WorldTileFlags.Inactive; At(X + 1, Y).Type = 1; }
        if (mode == 23) { At(X - 1, Y).TrySetSourceShape(true, 0); At(X + 1, Y).Type = 1; }
        if (mode >= 24) { int tx = mode == 25 ? X + 4 : X + 2; int ty = mode == 26 ? Y + 2 : Y - 2; Decorate(tx, ty); if (mode == 27) ground.Type = 2; if (mode == 26) Decorate(tx, Y + 3); }
        void Decorate(int x, int y) { ref WorldTile t = ref At(x, y); t.TileColor = 7; t.Wall = 0; t.WallColor = 5; t.TrySetSourceShape(true, 3); t.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall; }
        var random = new FixtureRandom(seed); var framing = new GenerationTileFraming1458(store, random, workspace, true); var walls = new GenerationWallFraming1458(store, random);
        TreeGrower1458.TryGrowUnderground(store, X, Y, random, framing, walls);
        Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(row.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', row.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32())), random.Physical());
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
        ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
    }
    private sealed class FixtureRandom(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public string Physical() { const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic; return typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random) + ":" + string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random)!); }
        public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
