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

public sealed class GenerationSpecialResetTerrain1458Tests
{
    private static readonly JsonDocument Golden = ReadGolden();
    public static IEnumerable<object[]> TranslationCases => Keys("translation");
    public static IEnumerable<object[]> CanonicalCases => Keys("actual");
    public static IEnumerable<object[]> ConfiguredCases => Keys("direct");
    public static IEnumerable<object[]> MetadataCases => Keys("metadata");
    public static IEnumerable<object[]> OngoingCases => Keys("ongoing");
    public static IEnumerable<object[]> UnsupportedCases => Golden.RootElement.GetProperty("actual").EnumerateArray()
        .Where(x => x.GetProperty("width").GetInt32() == 4200 && x.GetProperty("profile").GetString() != "Normal")
        .Select(x => new object[] { x.GetProperty("key").GetString()! });

    [Theory, MemberData(nameof(TranslationCases))]
    public void Raw_seed_translation_matches_actual_WorldFileData_SetSeed(string key)
    {
        // Raw SetSeed, not UI copied-seed option parsing. Control characters exercise only this pure static function.
        JsonElement row = Row("translation", key);
        Assert.Equal(row.GetProperty("seed").GetInt32(), WorldGenerationRequest.ResolveVanillaSeed1458(row.GetProperty("text").GetString()!));
    }

    [Theory, MemberData(nameof(CanonicalCases))]
    public void Canonical_text_executes_actual_Reset_and_Terrain_against_original(string key)
    {
        JsonElement row = Row("actual", key);
        Assert.Equal(row.GetProperty("numericSeed").GetInt32(), Request(row.GetProperty("seedText").GetString()!, row.GetProperty("width").GetInt32()).ResolveVanillaSeed1458());
        RunPrefix(row, checkOriginal: true);
    }

    [Theory, MemberData(nameof(ConfiguredCases))]
    public void Configured_numeric_option_algorithm_matches_original_without_claiming_text_translation(string key)
    {
        // This separate seam supplies the numeric stream and classic option independently, as the native
        // WorldGenerationOptions caller does. Canonical text translation is exercised by the test above.
        JsonElement row = Row("direct", key);
        int width = row.GetProperty("width").GetInt32(), seed = row.GetProperty("seed").GetInt32();
        var workspace = new Workspace(width, Height(width));
        var state = new ParityState1458();
        var request = Request(row.GetProperty("seedText").GetString()!, width);
        var reset = new Context(workspace, new RandomAdapter(seed), request);
        new BootstrapPass1458(state).Execute(reset);
        AssertSubset(row.GetProperty("bootstrap"), workspace.VanillaBootstrapState!);
        AssertRng(row.GetProperty("resetRng"), reset.VanillaRandom, false, null);
        var terrain = new Context(workspace, new RandomAdapter(seed), request);
        new TerrainPass1458(new RefuseFallback(), state).Execute(terrain);
        Assert.Equal(row.GetProperty("grid").GetString(), Grid(workspace));
        AssertSubset(row.GetProperty("terrain"), Terrain(workspace));
        AssertRng(row.GetProperty("terrainRng"), terrain.VanillaRandom, true, row.GetProperty("report").GetInt32());
    }

    [Theory, MemberData(nameof(MetadataCases))]
    public void Actual_fresh_save_resets_transient_clock_and_party_cooldown_and_preserves_owned_metadata(string key)
    {
        JsonElement row = Row("metadata", key);
        string text = row.GetProperty("seedText").GetString()!;
        Workspace workspace = RunPrefix(Row("actual", ActualKey(text)), checkOriginal: true);
        if (row.GetProperty("night").GetBoolean())
            workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(Request(text, 4200).ResolveVanillaSeed1458()),
                4200, false, workspace.VanillaSeedProfile.Special, dayTime: false, time: 12345d));
        AssertSubset(row.GetProperty("bootstrap"), workspace.VanillaBootstrapState!); // Raw evil is unobservable here.
        Assert.True(workspace.TryGetLayers(out WorldGenerationLayers layers));
        var header = new WorldFileHeader("SpecialPrefix", text, 0, Guid.Empty, workspace.VanillaBootstrapState!.WorldId,
            0, 4200 * 16, 0, 1200 * 16, workspace.TileStore.Dimensions);
        var metadata = new RuntimeWorldGenerationMetadataSnapshot(new(0, 0), new(0, 0), layers, workspace.VanillaSeedProfile)
        { VanillaBootstrapState = workspace.VanillaBootstrapState };
        var result = WorldFileFreshComposer326.TryCompose(header, metadata, workspace.TileStore, 0, false, 0, 0, out _, out var world);
        Assert.True(result.Succeeded, result.ToString());
        AssertSubset(row.GetProperty("metadata"), world!.RuntimeMetadata);
        AssertPreparedRoundTrip(world);
    }

    [Theory, MemberData(nameof(OngoingCases))]
    public void Actual_ongoing_party_nonzero_cooldown_and_doom_flag_survive_prepared_metadata(string key)
    {
        JsonElement row = Row("ongoing", key);
        byte[] file = Convert.FromBase64String(row.GetProperty("world").GetString()!);
        var loaded = WorldFileLoader.TryLoad(file, CreateLimits(), out var world);
        Assert.True(loaded.IsLoaded, loaded.ToString());
        JsonElement expected = row.GetProperty("expected");
        Assert.Equal(expected.GetProperty("PartyDaysOnCooldown").GetInt32(), world!.RuntimeMetadata.PartyDaysOnCooldown);
        Assert.Equal(expected.GetProperty("GenuineParty").GetBoolean(), world.RuntimeMetadata.PartyGenuine);
        Assert.Equal(expected.GetProperty("AfterPartyOfDoom").GetBoolean(), world.RuntimeMetadata.AfterPartyOfDoom);
        AssertPreparedRoundTrip(world);
    }

    [Fact]
    public void Disposable_layout6_is_refused_before_decoding_new_metadata_fields()
    {
        JsonElement old = Golden.RootElement.GetProperty("cacheLayout6");
        string path = Path.Combine(Path.GetTempPath(), "terraruntime-cache6-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(path, Convert.FromBase64String(old.GetProperty("header").GetString()!));
            var result = RuntimeWorldSnapshotCache.TryLoad(path,
                new(old.GetProperty("sourceLength").GetInt64(), old.GetProperty("sourceTicks").GetInt64()), CreateLimits(), out _);
            Assert.Equal(RuntimeWorldSnapshotLoadResult.LayoutVersionMismatch, result.Result);
            Assert.Equal(6, result.DetailCode);
        }
        finally { File.Delete(path); }
    }

    [Theory, MemberData(nameof(UnsupportedCases))]
    public void Uncancelled_basic_special_profile_refuses_generation_after_its_qualified_prefix_through_gems(string key)
    {
        JsonElement row = Row("actual", key);
        var workspace = new Workspace(4200, 1200);
        var provider = new SourceBackedFinal1458();
        var request = Request(row.GetProperty("seedText").GetString()!, 4200);
        AttachSourceOwners(workspace, in request);
        var result = RuntimeWorldGenerationExecutor.Execute(provider, request, workspace, cancellationToken: Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(WorldGenerationExecutionStatus.PassFailed, result.Status);
        Assert.Equal("terraria:1.4.5.8/GravitatingSand", result.PassId.Value);
        Assert.IsType<NotSupportedException>(result.Error);
        Assert.Contains("through Gems", result.Error!.Message);
    }

    private static void AttachSourceOwners(Workspace workspace, in WorldGenerationRequest request)
    {
        var npcs = new TerraRuntime.Core.Npcs.RuntimeNpcStore();
        var projectiles = new TerraRuntime.Core.Projectiles.RuntimeProjectileStore();
        var profile = WorldSeedResolver1458.Resolve(in request);
        bool good = profile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy) || profile.Has(VanillaSpecialWorldSeed1458.Zenith);
        float difficulty = good ? 2 : 1;
        var npcContext = new TerraRuntime.Gameplay.Npcs.VanillaNpcSpawnContext(difficulty, 0, good)
        {
            WorldDifficulty = difficulty,
            TenthAnniversaryWorld = profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10) || profile.Has(VanillaSpecialWorldSeed1458.Zenith),
            RemixWorld = profile.Has(VanillaSpecialWorldSeed1458.Remix) || profile.Has(VanillaSpecialWorldSeed1458.Zenith)
        };
        npcs.SetVanillaSpawnContextSource(() => npcContext);
        npcs.SetVanillaSpawnWetCollisionSource((x, y, width, height) =>
            VanillaWorldCollision.GetLiquidContacts(workspace.TileStore, x, y, width, height).Wet);
        workspace.AttachEntityEffects(new TerraRuntime.Core.Worlds.Generation.WorldGenerationEntityEffects1458(
            npcs, projectiles, sourceServer: true), sourceMyPlayer: 0, sourceNpcDifficulty: difficulty);
        Assert.True(workspace.TileStore.TryAttachTileEntities([], 1000));
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
    private sealed class Context(Workspace workspace, RandomAdapter random, WorldGenerationRequest request) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportProgress(double fraction, string? message = null) { }
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next(); public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
    private sealed class RefuseFallback : IWorldGenerationPass { public void Execute(IWorldGenerationContext context) => throw new InvalidOperationException("Unexpected unqualified Terrain fallback."); }

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
    private static void AssertPreparedRoundTrip(WorldFileData original)
    {
        byte[] payload = RuntimeWorldPreparedStateCodec.Encode(original);
        Assert.True(RuntimeWorldPreparedStateCodec.TryDecode(payload, original.Tiles, out var restored));
        foreach (PropertyInfo property in typeof(WorldFileRuntimeMetadata).GetProperties())
        {
            if (property.Name.EndsWith("Offset") || property.Name.EndsWith("Length")) continue;
            Assert.Equal(JsonSerializer.Serialize(property.GetValue(original.RuntimeMetadata), property.PropertyType),
                JsonSerializer.Serialize(property.GetValue(restored!.RuntimeMetadata), property.PropertyType));
        }
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
    private static string ActualKey(string text) => Golden.RootElement.GetProperty("actual").EnumerateArray().Single(x => x.GetProperty("seedText").GetString() == text && x.GetProperty("width").GetInt32() == 4200).GetProperty("key").GetString()!;
    private static JsonDocument ReadGolden() { using var stream = typeof(GenerationSpecialResetTerrain1458Tests).Assembly.GetManifestResourceStream("GenerationSpecialResetTerrain1458")!; using var gzip = new GZipStream(stream, CompressionMode.Decompress); return JsonDocument.Parse(gzip); }
    internal static WorldFileLoadLimits CreateLimits() =>
        new(
            MaxTileCount: 6_000_000,
            MaxItemsPerChest: 40,
            MaxTotalChestItems: 320_000,
            MaxTextBytesPerSign: 4_096,
            MaxTotalSignTextBytes: 64 * 1024,
            Npcs: new WorldFileNpcDecodeOptions(
                MaxShimmeredTownNpcIndices: 256,
                MaxShimmerIndexExclusive: 256,
                MaxTownNpcs: 256,
                MaxPersistentNpcs: 256,
                MaxNameBytesPerTownNpc: 256,
                MaxTotalNameBytes: 64 * 1024),
            MaxTileEntities: 1_024,
            MaxPressurePlates: 16_384,
            MaxTownRooms: VanillaWorldFormat326.NpcTypeCount,
            Bestiary: new WorldFileBestiaryLimits(
                MaxKillEntries: 2_048,
                MaxSightEntries: 2_048,
                MaxChatEntries: 2_048,
                MaxPersistentIdBytes: 256,
                MaxTotalPersistentIdBytes: 512 * 1024),
            RuntimeMetadata: new WorldFileRuntimeMetadataLimits(
                MaxStringBytes: 4_096,
                MaxTotalStringBytes: 64 * 1024,
                MaxAnglerNames: 256,
                MaxBannerEntries: 1_024,
                MaxPartyNpcEntries: 256,
                MaxManifestBytes: 16 * 1024));

}
