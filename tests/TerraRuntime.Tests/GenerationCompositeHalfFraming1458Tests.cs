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

public sealed class GenerationCompositeHalfFraming1458Tests
{
    private static JsonDocument Read() { using var stream = typeof(GenerationCompositeHalfFraming1458Tests).Assembly.GetManifestResourceStream("GenerationCompositeHalfFraming1458")!; using var gzip = new GZipStream(stream, CompressionMode.Decompress); return JsonDocument.Parse(gzip); }
    public static IEnumerable<object[]> OriginalRows() { using var d = Read(); foreach (string family in new[] { "objects", "platforms" }) for (int i = 0; i < d.RootElement.GetProperty(family).GetArrayLength(); i++) yield return new object[] { family, i }; }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Composite_half_brick_framing_matches_original_entire_grid_and_full_random(string family, int id)
    {
        using var data = Read(); var row = data.RootElement.GetProperty(family)[id]; var w = new Workspace(120, 160); w.InitializeVanillaGenerationSolidity();
        if (family == "objects")
        {
            ushort type = row.GetProperty("type").GetUInt16(), floorType = row.GetProperty("floorType").GetUInt16(); for (int x = 59; x <= 60; x++) { At(x, 81) = new() { Flags = WorldTileFlags.Active, Type = floorType }; for (int y = 79; y <= 80; y++) At(x, y) = new() { Flags = WorldTileFlags.Active, Type = type, FrameX = (short)((x - 59) * 18), FrameY = (short)((y - 79) * 18) }; }
            At(59, 81).TrySetSourceShape(row.GetProperty("half").GetBoolean(), row.GetProperty("slope").GetByte());
        }
        else
        {
            int context = row.GetProperty("context").GetInt32(), centerShape = row.GetProperty("centerShape").GetInt32(), sideShape = row.GetProperty("sideShape").GetInt32(); At(60, 80) = new() { Flags = WorldTileFlags.Active, Type = 19 }; At(60, 80).TrySetSourceShape(centerShape >= 5, (byte)(centerShape % 5)); int sideX = context == 0 || context == 2 ? 59 : 61; At(sideX, 80) = new() { Flags = WorldTileFlags.Active, Type = 19 }; At(sideX, 80).TrySetSourceShape(sideShape >= 5, (byte)(sideShape % 5)); if (context >= 2) At(120 - sideX, 80) = new() { Flags = WorldTileFlags.Active, Type = 19 };
        }
        var random = new RandomAdapter(row.GetProperty("seed").GetInt32()); var framing = new GenerationTileFraming1458(w.TileStore, random, w, useWorldGenerationSolidity: true);
        if (family == "objects") typeof(GenerationTileFraming1458).GetMethod("CheckOrdinaryTwoByTwo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(framing, [60, 80, row.GetProperty("type").GetUInt16()]); else framing.TileFrame(60, 80);
        Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(w)); Assert.Equal(row.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', row.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32())), random.State()); Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
        ref WorldTile At(int x, int y) => ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y)];
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom { private readonly VanillaUnifiedRandom1458 random = new(seed); public string State() { const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic; return typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random) + ":" + string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random)!); } public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] b) => random.NextBytes(b); }
}
