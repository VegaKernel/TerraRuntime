using System.Reflection;
using System.Text.Json;
using System.IO.Compression;
using TerraRuntime.Core;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class GenerationSpawnAndWalls1458Tests
{
    private static JsonDocument Read() { using var stream = typeof(GenerationSpawnAndWalls1458Tests).Assembly.GetManifestResourceStream("GenerationSpawnAndWalls1458")!; using var gzip = new GZipStream(stream, CompressionMode.Decompress); return JsonDocument.Parse(gzip); }
    public static IEnumerable<object[]> OriginalRows() { using var data = Read(); foreach (string stage in new[] { "spawn", "walls", "grasswall" }) { int i = 0; foreach (var row in data.RootElement.GetProperty(stage).EnumerateArray()) yield return new object[] { stage, i++ }; } }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Spawn_and_wall_owners_match_actual_original_grid_return_spawn_and_full_random(string stage, int id)
    {
        using var data = Read(); var row = data.RootElement.GetProperty(stage)[id]; var random = new RandomAdapter(stage == "spawn" ? row.GetProperty("seed").GetInt32() : stage == "grasswall" ? row.GetProperty("seed").GetInt32() : 1458); Workspace w;
        if (stage == "spawn")
        {
            int seed = row.GetProperty("seed").GetInt32(), mode = row.GetProperty("mode").GetInt32(); w = Spawn(mode, seed); new GenerationSpawnPoint1458(w, random, mode == 21 ? 39.5 : 60, Xunit.TestContext.Current.CancellationToken).Apply(); Assert.True(w.TryGetSpawn(out var point)); Assert.Equal(row.GetProperty("spawnX").GetInt32(), point.X); Assert.Equal(row.GetProperty("spawnY").GetInt32(), point.Y); Assert.Equal(row.GetProperty("solid379").GetBoolean(), w.IsVanillaGenerationSolid(new(379)));
        }
        else if (stage == "grasswall")
        {
            w = new(120, 100); w.InitializeVanillaGenerationSolidity(); int i = 0; foreach (var v in data.RootElement.GetProperty("phase78").EnumerateArray()) w.SetVanillaGenerationSolid(new(i++), v.GetBoolean()); int type = row.GetProperty("type").GetInt32(), shape = row.GetProperty("shape").GetInt32(); w.SetVanillaGenerationSolid(new(type), row.GetProperty("solid").GetBoolean());
            for (int x = 56; x <= 64; x++) for (int y = 36; y <= 44; y++) At(x, y).Wall = 2;
            for (int x = 59; x <= 61; x++) for (int y = 39; y <= 41; y++) { At(x, y).Flags = WorldTileFlags.Active; At(x, y).Type = 1; }
            At(60, 40).Type = 2; At(61, 41).Flags &= ~WorldTileFlags.Active; ref var exposure = ref At(59, 39); exposure.Wall = 0; exposure.Type = (ushort)type; if (shape == 1) exposure.TrySetSourceShape(true, 0); if (shape == 2) exposure.TrySetSourceShape(true, 3); if (shape == 3) exposure.Flags |= WorldTileFlags.Inactive;
            new GrassWallPass1458(w.TileStore, random, 60, Xunit.TestContext.Current.CancellationToken, w).Apply();
            ref WorldTile At(int x, int y) => ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y)];
        }
        else
        {
            int type = row.GetProperty("type").GetInt32(), shape = row.GetProperty("shape").GetInt32(); w = new(80, 80); w.InitializeVanillaGenerationSolidity(); w.SetVanillaGenerationSolid(new(type), row.GetProperty("solid").GetBoolean());
            for (int x = 36; x <= 44; x++) for (int y = 36; y <= 44; y++) { ref var t = ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y)]; t.Wall = 2; if (x == 40) { t.Flags = WorldTileFlags.Active; t.Type = (ushort)type; if (shape == 1) t.TrySetSourceShape(true, 0); if (shape == 2) t.TrySetSourceShape(false, 1); if (shape == 3) t.TrySetSourceShape(true, 3); if (shape == 4) t.Flags |= WorldTileFlags.Inactive; if (shape == 5) t.Flags &= ~WorldTileFlags.Active; } }
            if (row.GetProperty("stage").GetString() == "count") Assert.Equal(row.GetProperty("returned").GetInt32(), new GenerationDirtWallCount1458(w.TileStore, w).Count(40, 40, 3500)); else new GenerationWallSpread1458(w.TileStore, w).Wall2(40, 40, 63);
        }
        Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(w)); Assert.Equal(row.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', row.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32())), random.State()); Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
    }
    private static Workspace Spawn(int mode, int seed)
    {
        const int W = 200, H = 120; var w = new Workspace(W, H); w.InitializeVanillaGenerationSolidity(); w.TrySetSpawn(120, 45); int first = 100 + new VanillaUnifiedRandom1458(seed).Next(-5, 6), floor = mode == 20 ? 3 : mode == 21 ? 39 : 40;
        for (int x = 0; x < W; x++) { ref var t = ref At(x, floor); t.Flags = WorldTileFlags.Active; t.Type = (ushort)(mode == 1 ? 2 : mode == 2 ? 21 : mode == 3 ? 19 : 1); if (mode == 4) t.Flags |= WorldTileFlags.Inactive; if (mode == 18) t.TrySetSourceShape(true, 3); }
        if (mode == 5) for (int x = 0; x < W; x++) At(x, 20).Type = 1;
        if (mode == 6) for (int x = 0; x < W; x++) At(x, 25) = new() { Type = 379, Flags = WorldTileFlags.Active };
        if (mode == 7) { At(first, floor).Flags &= ~WorldTileFlags.Active; At(first, 80) = new() { Type = 1, Flags = WorldTileFlags.Active }; }
        if (mode >= 8 && mode <= 11) At(first, floor - (mode - 7)).LiquidAmount = 128;
        if (mode == 12 || mode == 13) At(first + (mode == 12 ? 10 : 11), floor - 1) = new() { Type = 379, Flags = WorldTileFlags.Active };
        if (mode == 14) At(first, floor - 1).Type = 379;
        if (mode == 15) for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) { ref var t = ref At(x, y); t.TileColor = 7; t.WallColor = 5; t.Flags |= WorldTileFlags.WireRed | WorldTileFlags.WireBlue | WorldTileFlags.WireGreen | WorldTileFlags.WireYellow | WorldTileFlags.Actuator | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall; t.FrameX = -18; t.FrameY = 36; }
        if (mode == 16 || mode == 17) { At(first, floor).Flags &= ~WorldTileFlags.Active; if (mode == 16) w.TrySetSpawn(50, 40); }
        if (mode == 19) for (int x = 0; x < W; x++) At(x, 20) = new() { Type = 4, Flags = WorldTileFlags.Active };
        if (mode == 21) { At(first, 39).Flags &= ~WorldTileFlags.Active; At(first, 40) = new() { Type = 1, Flags = WorldTileFlags.Active }; }
        return w; ref WorldTile At(int x, int y) => ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y)];
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom { private readonly VanillaUnifiedRandom1458 random = new(seed); public string State() { const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic; return typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random) + ":" + string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random)!); } public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] b) => random.NextBytes(b); }
}
