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

public sealed class GenerationDyePlacement1458Tests
{
    private static JsonDocument Read() { using var stream = typeof(GenerationDyePlacement1458Tests).Assembly.GetManifestResourceStream("GenerationDyePlacement1458")!; using var gzip = new GZipStream(stream, CompressionMode.Decompress); return JsonDocument.Parse(gzip); }
    public static IEnumerable<object[]> OriginalRows() { using var d = Read(); for (int i = 0; i < d.RootElement.GetArrayLength(); i++) yield return new object[] { i }; }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Dye_placement_matches_original_raw_and_PlaceTile_return_entire_grid_and_full_random(int id) { using var d = Read(); var row = d.RootElement[id]; int style = row.GetProperty("style").GetInt32(), mode = row.GetProperty("mode").GetInt32(); var w = Create(style, mode); var random = new RandomAdapter(row.GetProperty("seed").GetInt32()); var framing = new GenerationTileFraming1458(w.TileStore, random, w, useWorldGenerationSolidity: true); if (row.GetProperty("stage").GetString() == "raw") framing.PlaceDyePlant(60, 80, style); else Assert.Equal(row.GetProperty("returned").GetBoolean(), framing.TryPlaceDyePlant(60, 80, style)); Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(w)); Assert.Equal(row.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', row.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32())), random.State()); Assert.Equal(row.GetProperty("next").GetInt32(), random.Next()); }
    private static Workspace Create(int style, int mode)
    {
        var w = new Workspace(120, 180); w.InitializeVanillaGenerationSolidity(); w.SetVanillaGenerationSolid(new(225), mode != 16); w.SetVanillaGenerationSolid(new(659), mode == 21); int supportY = style == 7 ? 79 : 81; ref var support = ref At(60, supportY); support.Flags = WorldTileFlags.Active; support.Type = (ushort)(style == 6 ? 80 : 1); if (style == 6) At(60, 82) = new() { Flags = WorldTileFlags.Active, Type = 53 };
        if (mode == 1) { ref var cell = ref At(60, 80); cell.Type = 54; cell.FrameX = 36; cell.FrameY = 72; cell.TrySetSourceShape(true, 3); cell.TileColor = 9; cell.Flags |= WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.WireYellow; }
        if (mode == 2) support.TrySetSourceShape(true, 0); if (mode == 3) support.TrySetSourceShape(false, 3); if (mode == 4) support.Flags |= WorldTileFlags.Inactive;
        if (mode == 5 || mode == 6) At(60, style == 7 ? 81 : 79) = new() { Flags = WorldTileFlags.Active, Type = (ushort)(mode == 5 ? 1 : 3) };
        if (mode >= 7 && mode <= 10) { At(60, 80).LiquidAmount = 255; At(60, 80).LiquidKind = (WorldLiquidKind)(mode - 7); }
        if (mode == 11) At(60, 80).Wall = 2; if (mode == 12 || mode == 13) At(60, 80) = new() { Flags = WorldTileFlags.Active, Type = (ushort)(mode == 12 ? 178 : 488) };
        if (mode == 14) support.Type = 19; if (mode == 15) support.TrySetSourceShape(true, 3); if (mode == 16 || mode == 17) support.Type = 225; if (mode == 18 || mode == 21) support.Type = 659; if (mode == 19) support.Type = 0; if (mode == 20) At(60, 80).Flags |= WorldTileFlags.Inactive;
        return w; ref WorldTile At(int x, int y) => ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y)];
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom { private readonly VanillaUnifiedRandom1458 random = new(seed); public string State() { const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic; return typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random) + ":" + string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random)!); } public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] b) => random.NextBytes(b); }
}
