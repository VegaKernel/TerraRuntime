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

public sealed class GenerationSunflowerPlacement1458Tests
{
    private static JsonDocument Read() { using var stream = typeof(GenerationSunflowerPlacement1458Tests).Assembly.GetManifestResourceStream("GenerationSunflowerPlacement1458")!; using var gzip = new GZipStream(stream, CompressionMode.Decompress); return JsonDocument.Parse(gzip); }
    public static IEnumerable<object[]> OriginalRows() { using var d = Read(); for (int i = 0; i < d.RootElement.GetArrayLength(); i++) yield return new object[] { i }; }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Sunflower_matches_original_raw_and_PlaceTile_full_grid_return_and_physical_random(int id) { using var data = Read(); var row = data.RootElement[id]; int mode = row.GetProperty("mode").GetInt32(); var w = Create(mode); var random = new RandomAdapter(row.GetProperty("seed").GetInt32()); var framing = new GenerationTileFraming1458(w.TileStore, random, w, useWorldGenerationSolidity: true); double surface = mode == 29 ? 79 : mode == 30 ? 80.5 : mode == 31 ? 81 : 120; if (row.GetProperty("stage").GetString() == "raw") framing.PlaceSunflower(40, 80, surface); else Assert.Equal(row.GetProperty("returned").GetBoolean(), framing.TryPlaceSunflower(40, 80, surface)); Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(w)); Assert.Equal(row.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', row.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32())), random.State()); Assert.Equal(row.GetProperty("next").GetInt32(), random.Next()); Assert.Equal(row.GetProperty("checking").GetBoolean(), w.TileStore.LiquidUpdates.IsCheckingLiquid(40, 80)); }
    private static Workspace Create(int mode)
    {
        const int W = 120, H = 160; var w = new Workspace(W, H); w.InitializeVanillaGenerationSolidity();
        for (int x = 40; x <= 41; x++) At(x, 81) = new() { Flags = WorldTileFlags.Active, Type = (ushort)(mode == 2 ? 109 : mode == 3 ? 60 : mode == 4 ? 477 : mode == 5 ? 492 : mode == 6 ? 633 : mode == 7 ? 1 : 2) }; if (mode == 1) At(41, 81).Type = 109;
        if (mode == 8) At(40, 81).TrySetSourceShape(true, 0); if (mode == 9) At(41, 81).TrySetSourceShape(false, 1); if (mode == 10) At(41, 81).TrySetSourceShape(false, 3); if (mode == 11) At(40, 81).Flags |= WorldTileFlags.Inactive; if (mode == 12) At(40, 80).Flags |= WorldTileFlags.Inactive;
        if (mode == 13) { ref var anchor = ref At(40, 80); anchor.Type = 54; anchor.FrameX = 36; anchor.FrameY = 72; anchor.TrySetSourceShape(true, 3); anchor.TileColor = 7; anchor.Flags |= WorldTileFlags.WireBlue | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock; }
        if (mode == 14 || mode == 33) { ref var anchor = ref At(40, 80); anchor.Flags = WorldTileFlags.Active; anchor.Type = 1; anchor.FrameX = 36; anchor.FrameY = 72; anchor.TrySetSourceShape(true, 3); if (mode == 33) anchor.Flags |= WorldTileFlags.Inactive; }
        if (mode == 15 || mode == 16) At(40, 80) = new() { Flags = WorldTileFlags.Active, Type = (ushort)(mode == 15 ? 178 : 488) };
        if (mode == 17) At(40, 80).Wall = 2; if (mode == 18) At(41, 77).Wall = 1;
        if (mode >= 19 && mode <= 22) { At(40, 80).LiquidAmount = 128; At(40, 80).LiquidKind = (WorldLiquidKind)(mode - 19); }
        if (mode == 23) At(40, 77).LiquidAmount = 128; if (mode == 24) { At(41, 80).LiquidAmount = 128; At(41, 80).LiquidKind = WorldLiquidKind.Lava; }
        if (mode == 25) w.TileStore.LiquidUpdates.TryEnqueue(40, 80);
        if (mode == 26 || mode == 38) for (int x = 40; x <= 41; x++) for (int y = 77; y <= 81; y++) { ref var cell = ref At(x, y); cell.TileColor = 7; cell.WallColor = 5; cell.Flags |= WorldTileFlags.WireRed | WorldTileFlags.WireBlue | WorldTileFlags.WireGreen | WorldTileFlags.WireYellow | WorldTileFlags.Actuator | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall; if (mode == 26 && y <= 80) cell.TrySetSourceShape(true, 3); }
        if (mode == 27) At(41, 77) = new() { Flags = WorldTileFlags.Active, Type = 1 }; if (mode == 28) for (int x = 40; x <= 41; x++) for (int y = 77; y <= 80; y++) At(x, y) = new() { Flags = WorldTileFlags.Active, Type = 27, FrameX = (short)((x - 40) * 18), FrameY = (short)((y - 77) * 18) };
        if (mode == 32) At(40, 80).LiquidKind = WorldLiquidKind.Lava; if (mode == 34) At(40, 81).Flags |= WorldTileFlags.Actuator; if (mode == 35) At(41, 77).Flags |= WorldTileFlags.Inactive; if (mode == 36) At(41, 81).TrySetSourceShape(true, 1); if (mode == 37) At(39, 80) = new() { Flags = WorldTileFlags.Active, Type = 178 }; if (mode == 39) At(41, 81).TrySetSourceShape(true, 0);
        return w; ref WorldTile At(int x, int y) => ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y)];
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom { private readonly VanillaUnifiedRandom1458 random = new(seed); public string State() { const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic; return typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random) + ":" + string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random)!); } public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] b) => random.NextBytes(b); }
}
