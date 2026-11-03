using System.Reflection;
using System.Text;
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

public sealed class GenerationGemCaveProperMain1458Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Gem_cave_matches_actual_original_Main_tables_grid_and_full_physical_random(bool open)
    {
        using var packed = typeof(GenerationGemCaveProperMain1458Tests).Assembly.GetManifestResourceStream("GemCaveProperMain1458")!;
        using var unpacked = new GZipStream(packed, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(unpacked);
        var expected = doc.RootElement.EnumerateArray().Single(x => x.GetProperty("open").GetBoolean() == open);
        var w = new Workspace(600, 500);
        for (int x = 0; x < 600; x++) for (int y = 0; y < 500; y++) w.TileStore.Set(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        if (open) for (int x = 296; x < 304; x++) for (int y = 246; y < 254; y++) w.TileStore.Set(x, y, new WorldTile());
        var rng = new RandomAdapter(1458); LateStructurePass1458.ApplyGemCaveForTesting(w, rng, 300, 250); string physical = rng.PhysicalState(); int next = rng.Next();
        var text = new StringBuilder(); for (int x = 0; x < 600; x++) for (int y = 0; y < 500; y++) { var t = w.TileStore.Get(x, y); text.Append(t.IsActive ? 1 : 0).Append(',').Append(t.Type).Append(',').Append(t.Wall).Append(',').Append(t.FrameX).Append(',').Append(t.FrameY).Append(',').Append(t.LiquidAmount).Append(',').Append((byte)t.LiquidKind).Append(',').Append(t.IsHalfBrick ? 1 : 0).Append(',').Append(t.SourceSlope).Append(',').Append(t.TileColor).Append(',').Append(t.WallColor); foreach (var flag in new[] { WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen, WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive, WorldTileFlags.InvisibleBlock, WorldTileFlags.FullbrightBlock, WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall }) text.Append(',').Append((t.Flags & flag) != 0 ? 1 : 0); text.Append(';'); }
        Assert.Equal(expected.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', expected.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32())), physical);
        Assert.Equal(expected.GetProperty("next").GetInt32(), next);
        Assert.Equal(expected.GetProperty("grid").GetString(), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))));
    }
    sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public string PhysicalState() => typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random) + ":" +
            string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!);
        public int Next() => random.Next();
        public int Next(int maximum) => random.Next(maximum);
        public int Next(int minimum, int maximum) => random.Next(minimum, maximum);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
