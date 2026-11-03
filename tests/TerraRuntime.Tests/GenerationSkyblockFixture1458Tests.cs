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

public sealed class GenerationSkyblockFixture1458Tests
{
    private static JsonDocument Read()
    {
        using var stream = typeof(GenerationSkyblockFixture1458Tests).Assembly.GetManifestResourceStream("GenerationSkyblockFixture1458")!;
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
    public void Skyblock_floor_furniture_and_switches_match_actual_original_grid_return_and_physical_random(int id)
    {
        using var data = Read(); JsonElement row = data.RootElement[id];
        int seed = row.GetProperty("seed").GetInt32(), mode = row.GetProperty("mode").GetInt32();
        ushort type = row.GetProperty("type").GetUInt16();
        var workspace = new Workspace(96, 128);
        for (int x = 40; x <= 56; x++) { At(x, 80).Flags |= WorldTileFlags.Active; At(x, 80).Type = 1; }
        if (mode == 1) At(48, 80).TrySetSourceShape(true, 0);
        if (mode == 2) At(48, 80).TrySetSourceShape(false, 1);
        if (mode == 3) At(48, 80).TrySetSourceShape(false, 3);
        if (mode == 4) At(48, 80).Flags |= WorldTileFlags.Inactive;
        if (mode == 5) for (int x = 40; x <= 56; x++) At(x, 80).Type = 19;
        if (mode == 6) for (int x = 40; x <= 56; x++) At(x, 80).Type = 14;
        if (mode == 7) At(48, 80).Flags &= ~WorldTileFlags.Active;
        if (mode == 8) { At(48, 79).Flags |= WorldTileFlags.Active; At(48, 79).Type = 178; }
        if (mode == 9) { At(48, 79).Flags |= WorldTileFlags.Active; At(48, 79).Type = 3; }
        if (mode == 10)
        {
            ref WorldTile tile = ref At(48, 79); tile.Type = 178; tile.TileColor = 7; tile.WallColor = 6; tile.Wall = 4;
            tile.FrameX = 90; tile.FrameY = 36; tile.TrySetSourceShape(true, 3); tile.LiquidAmount = 128; tile.LiquidKind = WorldLiquidKind.Honey;
            tile.Flags = WorldTileFlags.WireBlue | WorldTileFlags.Actuator | WorldTileFlags.Inactive | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall;
        }
        if (mode == 11) { At(49, 78).Flags |= WorldTileFlags.Active; At(49, 78).Type = 1; }
        if (mode == 12) for (int x = 46; x <= 51; x++) for (int y = 75; y <= 81; y++)
        {
            ref WorldTile tile = ref At(x, y); tile.TileColor = 7; tile.WallColor = 8; tile.TrySetSourceShape(true, 3);
            tile.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.WireGreen;
        }
        var random = new RandomAdapter(seed);
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace);
        bool result = framing.TryPlaceSkyblockFixture(48, 79, type, type == 139 ? 87 : 0);
        int switches = mode == 13 ? 1 : mode == 14 ? 2 : 0;
        if (type != 220 && At(48, 79).Type == type) for (int count = 0; count < switches; count++) framing.SwitchSkyblockFixture(48, 79);
        Assert.Equal(row.GetProperty("returned").GetBoolean(), result);
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
        public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
