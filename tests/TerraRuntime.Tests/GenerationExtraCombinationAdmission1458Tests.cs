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

public sealed class GenerationExtraCombinationAdmission1458Tests
{
    private static readonly JsonDocument Golden = ReadGolden();
    public static IEnumerable<object[]> RejectedCases => Keys("rejected");
    public static IEnumerable<object[]> OrdinaryCases => Keys("actual");
    public static IEnumerable<object[]> UnknownExtraBits => Enumerable.Range(37, 27).Select(x => new object[] { x });
    public static IEnumerable<object[]> UnknownSpecialBits => Enumerable.Range(9, 7).Select(x => new object[] { x });

    [Theory, MemberData(nameof(UnknownExtraBits))]
    public void Unknown_extra_enum_bits_have_no_qualified_prefix(int bit) =>
        Assert.False(new VanillaWorldSeedProfile1458(0, (VanillaSecretWorldSeed1458)(1UL << bit)).SupportsSourceBackedResetAndTerrain);

    [Theory, MemberData(nameof(UnknownSpecialBits))]
    public void Unknown_special_enum_bits_have_no_qualified_prefix(int bit) =>
        Assert.False(new VanillaWorldSeedProfile1458((VanillaSpecialWorldSeed1458)(1 << bit), 0).SupportsSourceBackedResetAndTerrain);

    [Theory, MemberData(nameof(OrdinaryCases))]
    public void Source_unknown_text_remains_an_ordinary_seed_with_exact_original_prefix(string key)
    {
        var workspace = RunPrefix(Row("actual", key), true);
        Assert.True(workspace.VanillaSeedProfile.IsDefault);
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void Unqualified_real_combination_refuses_at_Reset_before_state_or_grid_changes(string key)
    {
        JsonElement row = Row("rejected", key);
        var workspace = new Workspace(4200, 1200);
        string empty = Grid(workspace);
        var request = Request(row.GetProperty("seedText").GetString()!, 4200);
        Assert.False(WorldSeedResolver1458.Resolve(in request).SupportsSourceBackedResetAndTerrain);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var observer = new AdmissionObserver(cancellation);
        var result = RuntimeWorldGenerationExecutor.Execute(new AdmissionProvider(new SourceBackedFinal1458(), observer),
            request, workspace, null, cancellation.Token);
        Assert.Equal(WorldGenerationExecutionStatus.PassFailed, result.Status);
        Assert.Equal(SourceBackedProvider1458.ResetPassId, result.PassId);
        Assert.IsType<NotSupportedException>(result.Error);
        Assert.False(cancellation.IsCancellationRequested);
        Assert.Equal(new[] { SourceBackedProvider1458.ResetPassId }, observer.Entered);
        Assert.Equal(observer.BeforeRandom, observer.AfterRandom);
        Assert.Null(workspace.VanillaBootstrapState);
        Assert.Null(workspace.VanillaTerrainState);
        Assert.Equal(empty, Grid(workspace));
    }

    private sealed class AdmissionObserver(CancellationTokenSource cancellation)
    {
        public List<WorldGenerationPassId> Entered { get; } = [];
        public string? BeforeRandom, AfterRandom;
        public void Execute(WorldGenerationPassId id, IWorldGenerationPass pass, IWorldGenerationContext context)
        {
            Entered.Add(id);
            BeforeRandom ??= PhysicalRandom(context.VanillaRandom!);
            try { pass.Execute(context); }
            finally { AfterRandom = PhysicalRandom(context.VanillaRandom!); }
            // Bounds the unchanged-test old-guard causal after its fallback Terrain. The accepted path fails at Reset.
            if (id == SourceBackedProvider1458.TerrainPassId) cancellation.Cancel();
        }
        private static string PhysicalRandom(IWorldGenerationVanillaRandom adapter)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var random = (VanillaUnifiedRandom1458)adapter.GetType().GetField("random", flags)!.GetValue(adapter)!;
            return JsonSerializer.Serialize(new
            {
                cursor = typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random),
                slots = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!
            });
        }
    }
    private sealed class AdmissionProvider(IWorldGenerationProvider inner, AdmissionObserver observer) : IWorldGenerationProvider
    {
        public WorldGeneratorId Id => inner.Id;
        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder) =>
            inner.BuildPlan(in request, new AdmissionBuilder(builder, observer));
    }
    private sealed class AdmissionBuilder(IWorldGenerationPlanBuilder builder, AdmissionObserver observer) : IWorldGenerationPlanBuilder
    {
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            builder.Add(descriptor, new AdmissionPass(descriptor.Id, pass, observer));
    }
    private sealed class AdmissionPass(WorldGenerationPassId id, IWorldGenerationPass pass, AdmissionObserver observer) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context) => observer.Execute(id, pass, context);
    }

    private static Workspace RunPrefix(JsonElement row, bool checkOriginal)
    {
        int width = row.GetProperty("width").GetInt32();
        var workspace = new Workspace(width, Height(width));
        var provider = new SourceBackedFinal1458();
        var request = Request(row.GetProperty("seedText").GetString()!, width);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
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
        using var stream = typeof(GenerationExtraCombinationAdmission1458Tests).Assembly.GetManifestResourceStream("GenerationExtraCombinationAdmission1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
