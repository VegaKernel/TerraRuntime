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

public sealed class GenerationEarlyRunnerSpecial1458Tests
{
    private static JsonDocument Read()
    {
        using var stream = typeof(GenerationEarlyRunnerSpecial1458Tests).Assembly.GetManifestResourceStream("GenerationEarlyRunnerSpecial1458")!;
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
    public void Shared_early_runner_matches_actual_original_special_profile_grid_and_physical_random(int id)
    {
        using var data = Read();
        JsonElement row = data.RootElement[id];
        int seed = row.GetProperty("seed").GetInt32(), mode = row.GetProperty("mode").GetInt32();
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
                if (mode is 2 or 4 or 8) { tile.LiquidAmount = 255; tile.LiquidKind = (WorldLiquidKind)((x + y) % 4); }
                if (mode == 6) tile.Type = 53;
                if (mode == 7) tile.TrySetSourceShape(x % 2 == 0, (byte)(y % 5));
                if (mode == 8) { tile.Flags &= ~WorldTileFlags.Active; tile.Type = 0; }
                if (mode == 9 && x >= 60 && x <= 68 && y >= 290 && y <= 310) { tile.Type = 15; tile.Flags |= WorldTileFlags.Active; }
            }
        var state = new EarlyState1458 { Profile = WorldSeedResolver1458.Resolve(in request), MainWorldSurface = 160, MainRockLayer = 270, WaterLine = 230, LavaLine = 400, MudWall = mode == 11 };
        var pass = new EarlyPass1458(EarlyStage1458.SandPatches, state);
        var random = new RandomAdapter(seed);
        Type gridType = typeof(EarlyPass1458).GetNestedType("RuntimeGrid", BindingFlags.NonPublic)!;
        object grid = Activator.CreateInstance(gridType, workspace)!;
        object wrappedRandom = Activator.CreateInstance(typeof(EarlyPass1458).GetNestedType("VanillaRandom", BindingFlags.NonPublic)!, random)!;
        int type = mode switch { 0 => 40, 1 => 53, 2 => 59, 3 => -1, 4 => -2, 5 => 63, 6 => 59, 7 => 40, 8 => 53, 9 => 0, 10 => 68, _ => 59 };
        typeof(EarlyPass1458).GetMethod("RunTileRunner", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pass,
            [grid, wrappedRandom, 64, mode == 4 ? 450 : 300, mode == 1 ? 25d : 14d, mode == 1 ? 35 : 18, type, mode == 1 || mode == 8, mode == 10 ? 1.25d : 0d, mode == 10 ? -.5d : 0d, mode == 7]);
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
