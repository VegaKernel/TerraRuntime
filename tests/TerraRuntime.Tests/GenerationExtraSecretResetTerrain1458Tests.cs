using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class GenerationExtraSecretResetTerrain1458Tests
{
    private static readonly JsonDocument Golden = ReadGolden();
    public static IEnumerable<object[]> CanonicalCases => Keys("actual");
    public static IEnumerable<object[]> BindingCases => Keys("bindings");
    public static IEnumerable<object[]> FreshCases => Keys("fresh");

    [Fact]
    public void Fresh_rain_requires_its_retained_bootstrap_instead_of_fabricating_weather()
    {
        JsonElement row = Row("actual", "Rain-4200");
        Workspace workspace = RunPrefix(row, checkOriginal: true);
        Assert.True(workspace.TryGetLayers(out var layers));
        var header = new WorldFileHeader("MissingWeather", "bring a towel", 0, Guid.Empty,
            workspace.VanillaBootstrapState!.WorldId, 0, workspace.WidthTiles * 16, 0, workspace.HeightTiles * 16,
            workspace.TileStore.Dimensions);
        var generation = new RuntimeWorldGenerationMetadataSnapshot(new(0, 0), new(0, 0), layers, workspace.VanillaSeedProfile);
        using var destination = new MemoryStream();
        var result = WorldFileFreshRuntimeMetadata326Encoder.TryEncode(header, new(generation, 0, false, 0, 0),
            destination, out long written);
        Assert.Equal(WorldFileFreshRuntimeMetadata326EncodeResult.InvalidMetadata, result);
        Assert.Equal(0, written);
        Assert.Equal(0, destination.Length);
    }
    public static IEnumerable<object[]> UnsupportedCases => Golden.RootElement.GetProperty("actual").EnumerateArray()
        .Where(x => x.GetProperty("width").GetInt32() == 4200)
        .Select(x => new object[] { x.GetProperty("key").GetString()! });

    [Theory, MemberData(nameof(FreshCases))]
    public void Actual_public_SaveNewWorld_weather_and_live_metadata_survive_prepared_state(string key)
    {
        JsonElement row = Row("actual", key);
        Workspace workspace = RunPrefix(row, checkOriginal: true);
        Assert.True(workspace.TryGetLayers(out var layers));
        var header = new WorldFileHeader("SpecialPrefix", row.GetProperty("seedText").GetString()!, 0, Guid.Empty,
            workspace.VanillaBootstrapState!.WorldId, 0, workspace.WidthTiles * 16, 0, workspace.HeightTiles * 16,
            workspace.TileStore.Dimensions);
        var generation = new RuntimeWorldGenerationMetadataSnapshot(new(0, 0), new(0, 0), layers, workspace.VanillaSeedProfile)
        { VanillaBootstrapState = workspace.VanillaBootstrapState };
        var composed = WorldFileFreshComposer326.TryCompose(header, generation, workspace.TileStore, 0, false, 0, 0,
            out _, out var world);
        Assert.True(composed.Succeeded, composed.ToString());
        Assert.NotNull(world);
        JsonElement expected = Row("fresh", key).GetProperty("expected");
        AssertSubset(expected, world.RuntimeMetadata);
        byte[] payload = RuntimeWorldPreparedStateCodec.Encode(world);
        Assert.True(RuntimeWorldPreparedStateCodec.TryDecode(payload, workspace.TileStore, out var prepared));
        Assert.NotNull(prepared);
        AssertSubset(expected, prepared.RuntimeMetadata);
    }

    [Theory, MemberData(nameof(BindingCases))]
    public void Real_secret_text_binding_matches_original_CheckInput(string key)
    {
        JsonElement row = Row("bindings", key);
        var actual = WorldSeedResolver1458.ResolveSecret(row.GetProperty("text").GetString()!);
        Assert.Equal(row.GetProperty("flags").GetUInt64(), (ulong)actual);
    }

    [Theory, MemberData(nameof(CanonicalCases))]
    public void Actual_Reset_Terrain_matches_all_source_fields_grid_and_random(string key)
    {
        JsonElement row = Row("actual", key);
        Assert.Equal(row.GetProperty("numericSeed").GetInt32(), Request(row.GetProperty("seedText").GetString()!, row.GetProperty("width").GetInt32()).ResolveVanillaSeed1458());
        Workspace workspace = RunPrefix(row, checkOriginal: true);
        Assert.Equal(row.GetProperty("secretFlags").GetUInt64(), (ulong)workspace.VanillaSeedProfile.Secret);
        var live = new Dictionary<string, bool>
        {
            ["vampireSeed"] = workspace.VanillaSeedProfile.Has(VanillaSecretWorldSeed1458.WhatAHorribleNightToHaveACurse),
            ["infectedSeed"] = workspace.VanillaSeedProfile.Has(VanillaSecretWorldSeed1458.PurifyThis),
            ["teamBasedSpawnsSeed"] = workspace.VanillaSeedProfile.Has(VanillaSecretWorldSeed1458.RoyaleWithCheese),
            ["dualDungeonsSeed"] = workspace.VanillaSeedProfile.Has(VanillaSecretWorldSeed1458.DoubleDaringDangers),
            ["forceHalloweenForever"] = workspace.VanillaSeedProfile.Has(VanillaSecretWorldSeed1458.HocusPocus),
            ["forceXMasForever"] = workspace.VanillaSeedProfile.Has(VanillaSecretWorldSeed1458.JingleAllTheWay),
            ["moreLightningSeed"] = workspace.VanillaSeedProfile.Has(VanillaSecretWorldSeed1458.ElectricBoogaloo),
            ["noLightningSeed"] = workspace.VanillaSeedProfile.Has(VanillaSecretWorldSeed1458.CalmBeforeTheStorm)
        };
        AssertSubset(row.GetProperty("liveSeedFlags"), live);
    }

    [Theory, MemberData(nameof(UnsupportedCases))]
    public void Uncancelled_new_extra_profile_refuses_unqualified_later_generation(string key)
    {
        JsonElement row = Row("actual", key);
        var workspace = new Workspace(4200, 1200);
        var result = RuntimeWorldGenerationExecutor.Execute(new SourceBackedFinal1458(), Request(row.GetProperty("seedText").GetString()!, 4200), workspace, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(WorldGenerationExecutionStatus.PassFailed, result.Status);
        Assert.Equal("terraria:1.4.5.8/Biomes", result.PassId.Value);
        Assert.IsType<NotSupportedException>(result.Error);
        Assert.Contains("Reset and Terrain only", result.Error!.Message);
    }

    private static Workspace RunPrefix(JsonElement row, bool checkOriginal)
    {
        int width = row.GetProperty("width").GetInt32();
        var workspace = new Workspace(width, Height(width));
        var provider = new SourceBackedFinal1458();
        var request = Request(row.GetProperty("seedText").GetString()!, width);
        using var cancel = new CancellationTokenSource();
        var capture = new Capture(workspace, row, cancel, checkOriginal);
        var result = RuntimeWorldGenerationExecutor.Execute(new Provider(provider, capture), request, workspace, null, cancel.Token);
        Assert.True(result.Status == WorldGenerationExecutionStatus.Cancelled, $"{result.Status}: {result.Error}");
        Assert.Equal(SourceBackedProvider1458.TerrainPassId, result.PassId);
        Assert.Equal(1, capture.Resets); Assert.Equal(1, capture.Terrains);
        return workspace;
    }

    private sealed class Capture(Workspace workspace, JsonElement row, CancellationTokenSource cancel, bool check)
    {
        public int Resets, Terrains;
        public void After(WorldGenerationPassId id, IWorldGenerationContext context)
        {
            if (id == SourceBackedProvider1458.ResetPassId)
            {
                Resets++;
                Assert.NotNull(workspace.VanillaBootstrapState);
                if (!check) return;
                AssertSubset(row.GetProperty("bootstrap"), workspace.VanillaBootstrapState!);
                AssertRng(row.GetProperty("resetRng"), context.VanillaRandom!, false, null);
                int type = 0;
                foreach (JsonElement value in row.GetProperty("solidity").EnumerateArray())
                    Assert.Equal(value.GetBoolean(), workspace.IsVanillaGenerationSolid(new(type++)));
            }
            if (id != SourceBackedProvider1458.TerrainPassId) return;
            Terrains++;
            if (check)
            {
                Assert.Equal(row.GetProperty("grid").GetString(), Grid(workspace));
                AssertSubset(row.GetProperty("terrain"), Terrain(workspace));
                AssertRng(row.GetProperty("terrainRng"), context.VanillaRandom!, true, row.GetProperty("report").GetInt32());
            }
            cancel.Cancel();
        }
    }

    private sealed class Provider(IWorldGenerationProvider inner, Capture capture) : IWorldGenerationProvider
    {
        public WorldGeneratorId Id => inner.Id;
        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder) => inner.BuildPlan(request, new Builder(builder, capture));
    }
    private sealed class Builder(IWorldGenerationPlanBuilder inner, Capture capture) : IWorldGenerationPlanBuilder
    {
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) => inner.Add(descriptor, new Pass(descriptor, pass, capture));
    }
    private sealed class Pass(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass inner, Capture capture) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context) { inner.Execute(context); capture.After(descriptor.Id, context); }
    }
    private static void AssertRng(JsonElement expected, IWorldGenerationVanillaRandom adapter, bool report, int? expectedNext)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var rng = ((VanillaUnifiedRandom1458)adapter.GetType().GetField("random", flags)!.GetValue(adapter)!).Clone();
        if (report) Assert.Equal(expectedNext, rng.Next()); // Detached native RunPass report, never fed into executor.
        Assert.Equal(expected.GetProperty("cursor").GetInt32(), Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(rng), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(expected.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()), (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(rng)!);
    }
    private static void AssertSubset(JsonElement expected, object value)
    {
        JsonElement actual = JsonSerializer.SerializeToElement(value, value.GetType());
        foreach (JsonProperty property in expected.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Null) continue;
            Assert.True(actual.TryGetProperty(property.Name, out JsonElement observed) && JsonElement.DeepEquals(property.Value, observed),
                $"{property.Name}: original={property.Value}, actual={observed}");
        }
    }
    private static object Terrain(Workspace workspace)
    {
        var t = workspace.VanillaTerrainState!.Value; var l = workspace.VanillaLiquidLines!.Value;
        return new
        {
            t.WorldSurface,
            t.RockLayer,
            t.CurrentWorldSurface,
            t.CurrentRockLayer,
            t.WorldSurfaceLow,
            t.WorldSurfaceHigh,
            t.RockLayerLow,
            t.RockLayerHigh,
            l.WaterLine,
            l.LavaLine,
            t.RemixMushroomLayerLow,
            t.RemixMushroomLayerHigh,
            t.RemixSurfaceLayerLow,
            t.RemixSurfaceLayerHigh
        };
    }
    private static string Grid(Workspace workspace)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] column = new byte[workspace.HeightTiles * 17];
        WorldTileFlags[] flags = [WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen, WorldTileFlags.WireYellow,
            WorldTileFlags.Actuator, WorldTileFlags.Inactive, WorldTileFlags.InvisibleBlock, WorldTileFlags.FullbrightBlock, WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall];
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile t = workspace.TileStore.Get(x, y); Span<byte> b = column.AsSpan(y * 17, 17);
                b[0] = (byte)(t.IsActive ? 1 : 0); BinaryPrimitives.WriteUInt16LittleEndian(b[1..], t.Type); BinaryPrimitives.WriteUInt16LittleEndian(b[3..], t.Wall);
                BinaryPrimitives.WriteInt16LittleEndian(b[5..], t.FrameX); BinaryPrimitives.WriteInt16LittleEndian(b[7..], t.FrameY);
                b[9] = t.LiquidAmount; b[10] = (byte)t.LiquidKind; b[11] = (byte)(t.IsHalfBrick ? 1 : 0); b[12] = t.SourceSlope; b[13] = t.TileColor; b[14] = t.WallColor;
                ushort packed = 0; for (int i = 0; i < flags.Length; i++) if ((t.Flags & flags[i]) != 0) packed |= (ushort)(1 << i);
                BinaryPrimitives.WriteUInt16LittleEndian(b[15..], packed);
            }
            hash.AppendData(column);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static int Height(int width) => width == 4200 ? 1200 : width == 6400 ? 1800 : 2400;
    private static WorldGenerationRequest Request(string text, int width) => new(Provider1458.GeneratorId, "SpecialPrefix", 0, width, Height(width)) { SeedText = text };
    private static IEnumerable<object[]> Keys(string group) => Golden.RootElement.GetProperty(group).EnumerateArray().Select(x => new object[] { x.GetProperty("key").GetString()! });
    private static JsonElement Row(string group, string key) => Golden.RootElement.GetProperty(group).EnumerateArray().Single(x => x.GetProperty("key").GetString() == key);
    private static JsonDocument ReadGolden()
    {
        using var stream = typeof(GenerationExtraSecretResetTerrain1458Tests).Assembly.GetManifestResourceStream("GenerationExtraSecretResetTerrain1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
