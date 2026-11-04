using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationManifestHash1458Tests
{
    private static readonly JsonDocument Original = Read();
    public static IEnumerable<object[]> TileCases => Enumerable.Range(0,
        Original.RootElement.GetProperty("cases").GetArrayLength()).Select(index => new object[] { index });
    public static IEnumerable<object[]> WorldCases => Original.RootElement.GetProperty("worlds").EnumerateArray()
        .Select(row => new object[] { row.GetProperty("seed").GetInt32(), row.GetProperty("hash").GetUInt32() });

    [Theory, MemberData(nameof(TileCases))]
    public void Snapshot_hash_matches_actual_original_all_tile_types_and_headers(int index)
    {
        JsonElement row = Original.RootElement.GetProperty("cases")[index];
        var tile = new WorldTile
        {
            Type = row.GetProperty("type").GetUInt16(), Wall = row.GetProperty("wall").GetUInt16(),
            FrameX = row.GetProperty("frameX").GetInt16(), FrameY = row.GetProperty("frameY").GetInt16(),
            TileColor = row.GetProperty("color").GetByte(), WallColor = row.GetProperty("wallColor").GetByte(),
            LiquidAmount = row.GetProperty("liquid").GetByte(), LiquidKind = (WorldLiquidKind)row.GetProperty("liquidType").GetByte(),
            Flags = row.GetProperty("active").GetInt32() != 0 ? WorldTileFlags.Active : WorldTileFlags.None
        };
        WorldTileFlags[] flags = [WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen,
            WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive, WorldTileFlags.InvisibleBlock,
            WorldTileFlags.FullbrightBlock, WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall];
        int bits = row.GetProperty("flags").GetInt32();
        for (int bit = 0; bit < flags.Length; bit++) if ((bits & (1 << bit)) != 0) tile.Flags |= flags[bit];
        Assert.True(tile.TrySetSourceShape(row.GetProperty("half").GetBoolean(), row.GetProperty("slope").GetByte()));
        Assert.Equal(row.GetProperty("hash").GetUInt32(), VanillaWorldGenerationHash1458.TileHash(in tile));
    }

    [Theory, MemberData(nameof(WorldCases))]
    public void Full_generated_world_hash_matches_actual_original_manifest(int seed, uint expected)
    {
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "Manifest hash", (ulong)seed, 4200, 1200)
        { SeedText = seed.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var result = pipeline.CreateCandidate(in request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Generation.Execution?.Error?.ToString() ?? result.Finalization?.Validation?.Detail);
        Assert.Equal(expected, VanillaWorldGenerationHash1458.Calculate(result.Candidate!.TileStore));
        AssertManifest(seed, result.Metadata);
    }

    private static void AssertManifest(int seed, RuntimeWorldGenerationMetadataSnapshot metadata)
    {
        using var resource = typeof(GenerationManifestHash1458Tests).Assembly.GetManifestResourceStream("GenerationManifest1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var original = JsonDocument.Parse(gzip);
        JsonElement expected = original.RootElement.GetProperty("worlds").EnumerateArray()
            .Single(row => row.GetProperty("seed").GetInt32() == seed).GetProperty("manifest");
        Assert.NotNull(metadata.VanillaManifest);
        string serialized = metadata.VanillaManifest.Serialize();
        using var report = JsonDocument.Parse(serialized);
        JsonElement actual = report.RootElement;
        foreach (string key in new[] { "Version", "GitSHA", "FinalHash" })
            Assert.Equal(expected.GetProperty(key).GetRawText(), actual.GetProperty(key).GetRawText());
        var sourceRows = expected.GetProperty("GenPassResults").EnumerateArray().ToArray();
        var runtimeRows = actual.GetProperty("GenPassResults").EnumerateArray().ToArray();
        Assert.Equal(106, runtimeRows.Length);
        Assert.Equal(sourceRows.Length, runtimeRows.Length);
        for (int i = 0; i < sourceRows.Length; i++)
        {
            foreach (string key in new[] { "Name", "RandNext", "Hash", "Skipped" })
                Assert.Equal(sourceRows[i].GetProperty(key).ToString(), runtimeRows[i].GetProperty(key).ToString());
            Assert.InRange(runtimeRows[i].GetProperty("DurationMs").GetInt32(), 0, int.MaxValue);
        }
        var header = VanillaFreshWorldHeader326.Create("Manifest hash", seed.ToString(), 4200, 1200,
            Guid.Parse("7c67e028-9af0-430f-a616-d6cf5c001458"), worldId: 1458);
        var state = new WorldFileFreshRuntimeMetadata326(metadata, 0, false, 0, 0);
        using var encoded = new MemoryStream();
        Assert.Equal(WorldFileFreshRuntimeMetadata326EncodeResult.Encoded,
            WorldFileFreshRuntimeMetadata326Encoder.TryEncode(header, in state, encoded, out _));
        byte[] bytes = encoded.ToArray(), payload = System.Text.Encoding.UTF8.GetBytes(serialized);
        Assert.True(bytes.Length >= payload.Length);
        Assert.True(bytes.AsSpan(bytes.Length - payload.Length).SequenceEqual(payload));
    }

    private static JsonDocument Read()
    {
        using var stream = typeof(GenerationManifestHash1458Tests).Assembly.GetManifestResourceStream("GenerationManifestHash1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
