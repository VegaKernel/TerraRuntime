using System.Reflection;
using System.Text.Json;
using System.IO.Compression;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using TerraRuntime.Contracts.Gameplay;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class GenerationMidRunnerSpecial1458Tests
{
    private static JsonDocument Read()
    {
        using var stream = typeof(GenerationMidRunnerSpecial1458Tests).Assembly.GetManifestResourceStream("GenerationMidRunnerSpecial1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
    public static IEnumerable<object[]> OriginalRows()
    {
        using var data = Read();
        for (int i = 0; i < data.RootElement.GetArrayLength(); i++) yield return new object[] { i };
    }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Existing_mid_runner_matches_actual_original_special_profiles_grid_and_physical_random(int id)
    {
        using var data = Read();
        JsonElement row = data.RootElement[id];
        int seed = row.GetProperty("seed").GetInt32(), mode = row.GetProperty("mode").GetInt32(), context = row.GetProperty("context").GetInt32();
        string text = row.GetProperty("profile").GetString()! switch
        {
            "Drunk" => "05162020",
            "NotTheBees" => "not the bees",
            "ForTheWorthy" => "for the worthy",
            "Anniversary" => "celebrationmk10",
            "DontStarve" => "the constant",
            "NoTraps" => "no traps",
            "Remix" => "dontdigup",
            "Everything" => "getfixedboi",
            _ => "1458"
        };
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "OriginalEarlyRunner", 0, 128, 512) { SeedText = text };
        var workspace = new Workspace(128, 512);
        for (int x = 0; x < 128; x++)
            for (int y = 0; y < 512; y++)
            {
                ref WorldTile tile = ref At(x, y);
                tile.Type = (ushort)(y >= 170 ? 1 : 0);
                tile.FrameX = (short)((x + y) % 3 == 0 ? -1 : 18); tile.FrameY = (short)((x + y) % 2 == 0 ? -1 : 36);
                tile.Wall = (ushort)((x + y) % 4); tile.TileColor = (byte)(x % 31); tile.WallColor = (byte)(y % 31);
                tile.Flags = (y >= 170 ? WorldTileFlags.Active : 0) |
                    (x % 2 == 0 ? WorldTileFlags.WireRed : 0) | (y % 2 == 0 ? WorldTileFlags.WireBlue : 0) |
                    (x % 3 == 0 ? WorldTileFlags.WireGreen : 0) | (y % 3 == 0 ? WorldTileFlags.WireYellow : 0) |
                    (x % 5 == 0 ? WorldTileFlags.Actuator : 0) | (y % 5 == 0 ? WorldTileFlags.Inactive : 0) |
                    (x % 7 == 0 ? WorldTileFlags.InvisibleBlock : 0) | (y % 7 == 0 ? WorldTileFlags.FullbrightBlock : 0) |
                    (x % 11 == 0 ? WorldTileFlags.InvisibleWall : 0) | (y % 11 == 0 ? WorldTileFlags.FullbrightWall : 0);
                if (context == 1) { tile.LiquidAmount = 123; tile.LiquidKind = (WorldLiquidKind)((x + y) % 4); }
                if (context == 2) { tile.Type = 225; tile.TrySetSourceShape(x % 2 == 0, (byte)(y % 5)); }
            }
        var profile = WorldSeedResolver1458.Resolve(in request);
        var random = new RandomAdapter(seed);
        var runner = new SmallTerrainRunner1458(workspace.TileStore, random, 160, new(230, 400), TestContext.Current.CancellationToken, rockLayer: 270, seedProfile: profile);
        int type = mode switch { 0 or 1 => 59, 2 => 51, 3 => 123, 4 => 7, 5 => 168, 6 => 6, 7 => 169, 8 => 22, 9 => 204, _ => 63 + mode - 10 };
        double strength = mode == 0 ? 4 : mode == 1 ? 14 : mode == 2 ? 8 : mode >= 10 ? 4 : 5;
        int steps = mode == 0 ? 18 : mode == 1 ? 18 : mode == 2 ? 3 : mode >= 10 ? 5 : 6;
        runner.Run(64, 300, strength, steps, type, mode == 2, mode == 2 ? -1 : 0, mode == 2 ? -1 : mode == 1 ? 2 : 0, mode == 1, mode == 0 ? 53 : -1, mode != 2);
        Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(row.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', row.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32())), random.State());
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
        ref WorldTile At(int x, int y) => ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, y)];
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public string State()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            return typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random) + ":" + string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!);
        }
        public int Next() => random.Next(); public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
