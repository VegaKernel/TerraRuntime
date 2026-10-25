using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using global::Multiplicity.Packets;
using TerraRuntime.Application;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MoonSeasonPersistenceWire1458Tests
{
    private static readonly WorldFileRuntimeMetadataLimits Limits = new(16384, 16 * 1024 * 1024, 255, 762, 255, 16 * 1024 * 1024);

    [Fact]
    public void Temporary_flags_patch_matches_independent_original_headers_across_variable_fields_and_preserves_omitted_values()
    {
        using var reference = Load("MoonSeasonHeader1458");
        var rows = reference.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(64, rows.Length);
        foreach (var row in rows)
        {
            byte[] baseline = Header(row);
            var envelope = Envelope(baseline.Length);
            var (header, metadata) = Parse(baseline);
            foreach (int target in new[] { 0, 1, 2, 3 })
            {
                var expected = rows.Single(candidate => Key(candidate, "variable") == Key(row, "variable") &&
                    Key(candidate, "forever") == Key(row, "forever") && Key(candidate, "shadow") == Key(row, "shadow") &&
                    Key(candidate, "today") == target);
                Assert.Equal(WorldFileSeasonHeaderPatchResult1458.Patched,
                    WorldFileSeasonHeaderPatcher1458.TryPatch(baseline, envelope, header, out var actual,
                        (target & 1) != 0, (target & 2) != 0));
                Assert.Equal(Header(expected), actual);
                Assert.Equal(WorldFileSeasonHeaderPatchResult1458.Patched,
                    WorldFileSeasonHeaderPatcher1458.TryPatch(actual, envelope, header, out var repeated));
                Assert.Equal(actual, repeated);
                var (_, loaded) = Parse(actual);
                Assert.Equal((target & 1) != 0, loaded.ForceHalloweenForToday);
                Assert.Equal((target & 2) != 0, loaded.ForceXMasForToday);
                Assert.Equal(metadata.ForceHalloweenForever, loaded.ForceHalloweenForever);
                Assert.Equal(metadata.ForceXMasForever, loaded.ForceXMasForever);
                Assert.Equal(metadata.ShadowOrbSmashed, loaded.ShadowOrbSmashed);
                Assert.Equal(metadata.TreeTopVariations, loaded.TreeTopVariations);
            }
            Assert.Equal(WorldFileSeasonHeaderPatchResult1458.Patched,
                WorldFileSeasonHeaderPatcher1458.TryPatch(baseline, envelope, header, out var omitted));
            Assert.Equal(baseline, omitted);
            Assert.Equal(WorldFileSeasonHeaderPatchResult1458.Patched,
                WorldFileSeasonHeaderPatcher1458.TryPatch(baseline, envelope, header, out var partial, forceHalloweenForToday: true));
            Assert.Equal(metadata.ForceXMasForToday, Parse(partial).Metadata.ForceXMasForToday);
            Assert.Equal(baseline, Header(row));
        }
        Assert.NotEqual(Parse(Header(rows[0])).Metadata.SeasonalFlagsOffset,
            Parse(Header(rows.First(row => Key(row, "variable") == 1))).Metadata.SeasonalFlagsOffset);
    }

    [Fact]
    public void Invalid_season_header_identity_version_and_tail_never_produce_a_partial_patch()
    {
        using var reference = Load("MoonSeasonHeader1458");
        byte[] original = Header(reference.RootElement.GetProperty("rows")[0]);
        var (header, _) = Parse(original);
        Assert.Equal(WorldFileSeasonHeaderPatchResult1458.UnsupportedVersion,
            WorldFileSeasonHeaderPatcher1458.TryPatch(original, Envelope(original.Length, 325), header, out var output, true, true));
        Assert.Empty(output);
        foreach (int length in new[] { 0, 50, original.Length - 1 })
        {
            Assert.Equal(WorldFileSeasonHeaderPatchResult1458.InvalidHeader,
                WorldFileSeasonHeaderPatcher1458.TryPatch(original.AsSpan(0, length), Envelope(original.Length), header,
                    out output, true, true));
            Assert.Empty(output);
        }
        Assert.Equal(WorldFileSeasonHeaderPatchResult1458.InvalidHeader,
            WorldFileSeasonHeaderPatcher1458.TryPatch(original, Envelope(original.Length), header with { WorldId = -1 },
                out output, true, true));
        Assert.Empty(output);
    }

    [Fact]
    public void Live_today_overlays_match_original_wire_and_never_modify_saved_or_forever_flags()
    {
        using var headers = Load("MoonSeasonHeader1458");
        using var wires = Load("ProgressionWorldWire1458");
        foreach (var row in wires.RootElement.GetProperty("seasons").EnumerateArray())
        {
            int today = Key(row, "today"), forever = Key(row, "forever");
            var persistedRow = headers.RootElement.GetProperty("rows").EnumerateArray().First(value =>
                Key(value, "today") == (today ^ 3) && Key(value, "forever") == forever && Key(value, "shadow") == 0);
            var (header, metadata) = Parse(Header(persistedRow));
            var live = new WorldInfoRuntimeState(0, true, 0, false, false)
            {
                ForceHalloweenForToday = (today & 1) != 0, ForceXMasForToday = (today & 2) != 0
            };
            var actual = WorldInfoPacketMapper.Create(header, metadata, runtime: live);
            var expected = Wire(row.GetProperty("frameHex").GetString()!);
            Assert.Equal(expected.EventInfo6 & 0xc0, actual.EventInfo6 & 0xc0);
            Assert.Equal(expected.EventInfo11 & 6, actual.EventInfo11 & 6);
            Assert.Equal((today & 1) == 0, metadata.ForceHalloweenForToday);
            Assert.Equal((today & 2) == 0, metadata.ForceXMasForToday);
            var retained = WorldInfoPacketMapper.Create(header, metadata);
            Assert.Equal((today ^ 3) << 6, retained.EventInfo6 & 0xc0);
        }
    }

    [Fact]
    public async Task Detached_season_snapshot_survives_later_dawn_full_checkpoint_reload_and_prepared_cache()
    {
        var fixture = new CheckpointFixture();
        var clock = new RuntimeWorldClock(RuntimeWorldClock.NightLength, false, 0, 0, 1,
            forceHalloweenForToday: true, forceXMasForToday: true, forceHalloweenForever: false,
            forceXMasForever: false, seasonDateSource: () => new DateTime(2020, 6, 1));
        var source = new RuntimeWorldCheckpointSnapshotSource(fixture.World.Tiles, new RuntimeChestStore([]), 8, clock);
        source.CaptureTileBootstrap(8); Assert.True(source.TryCapture(out var captured));
        Assert.True(captured!.Clock!.Value.ForceHalloweenForToday);
        Assert.True(captured.Clock.Value.ForceXMasForToday);
        clock.Tick();
        Assert.False(clock.ForceHalloweenForToday); Assert.False(clock.ForceXMasForToday);
        var saved = await fixture.Serialize(captured);
        Assert.True(saved.RuntimeMetadata.ForceHalloweenForToday); Assert.True(saved.RuntimeMetadata.ForceXMasForToday);
        Assert.True(RuntimeWorldPreparedStateCodec.TryDecode(RuntimeWorldPreparedStateCodec.Encode(saved), saved.Tiles, out var cached));
        Assert.True(cached!.RuntimeMetadata.ForceHalloweenForToday); Assert.True(cached.RuntimeMetadata.ForceXMasForToday);
        var restarted = RuntimeWorldClock.FromWorld(cached.RuntimeMetadata, cached.CreativePowers);
        Assert.True(restarted.HasOwnedSeasonalState); Assert.True(restarted.ForceHalloweenForToday); Assert.True(restarted.ForceXMasForToday);
        Assert.True(source.TryCapture(out var after));
        var expired = await fixture.Serialize(after!);
        Assert.False(expired.RuntimeMetadata.ForceHalloweenForToday); Assert.False(expired.RuntimeMetadata.ForceXMasForToday);
    }

    [Fact]
    public async Task Legacy_or_unowned_clock_snapshot_keeps_raw_temporary_seasons()
    {
        var fixture = new CheckpointFixture(today: true);
        var unknown = new RuntimeWorldClock(1234, true, 0, 0, 0);
        Assert.False(unknown.HasOwnedSeasonalState);
        var source = new RuntimeWorldCheckpointSnapshotSource(fixture.World.Tiles, new RuntimeChestStore([]), 8, unknown);
        source.CaptureTileBootstrap(8); Assert.True(source.TryCapture(out var captured));
        Assert.Null(captured!.Clock!.Value.ForceHalloweenForToday); Assert.Null(captured.Clock.Value.ForceXMasForToday);
        var preserved = await fixture.Serialize(captured);
        Assert.True(preserved.RuntimeMetadata.ForceHalloweenForToday); Assert.True(preserved.RuntimeMetadata.ForceXMasForToday);
        var legacy = captured with { Clock = new RuntimeWorldClockSaveState(1234, true, 0, 0, 0, false, 0, 0) };
        preserved = await fixture.Serialize(legacy);
        Assert.True(preserved.RuntimeMetadata.ForceHalloweenForToday); Assert.True(preserved.RuntimeMetadata.ForceXMasForToday);
    }

    private static JsonDocument Load(string name)
    {
        using var stream = typeof(MoonSeasonPersistenceWire1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        var document = JsonDocument.Parse(gzip);
        Assert.Equal("4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034", document.RootElement.GetProperty("source").GetString());
        return document;
    }
    private static int Key(JsonElement row, string key) => row.GetProperty(key).GetInt32();
    private static byte[] Header(JsonElement row)
    {
        byte[] bytes = Convert.FromBase64String(row.GetProperty("headerBase64").GetString()!);
        Assert.Equal(row.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)));
        return bytes;
    }
    private static WorldFileEnvelope Envelope(int length, int version = 326) => new(version, 0, 0, [0, length], 0, []);
    private static (WorldFileHeader Header, WorldFileRuntimeMetadata Metadata) Parse(byte[] bytes)
    {
        var envelope = Envelope(bytes.Length);
        Assert.Equal(WorldFileHeaderParseResult.Parsed, WorldFileHeaderParser.TryParse(bytes, envelope, out var header));
        Assert.Equal(WorldFileRuntimeMetadataParseResult.Parsed,
            WorldFileRuntimeMetadataParser.TryParse(bytes, envelope, header!, Limits, out var metadata, out _));
        return (header!, metadata!);
    }
    private static WorldInfo Wire(string hex) => Assert.IsType<WorldInfo>(TerrariaPacket.DeserializePayload(
        PacketTypes.WorldInfo, Convert.FromHexString(hex).AsSpan(3).ToArray()));

    private sealed class CheckpointFixture
    {
        internal readonly WorldFileData World;
        private readonly WorldFileLoadLimits limits;
        private readonly WorldFilePreservedSections preserved;
        internal CheckpointFixture(bool today = false)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            byte[] bytes = (byte[])typeof(WorldFileLoaderTests).GetMethod("CreateCompleteCurrentWorld", flags)!.Invoke(null, null)!;
            limits = (WorldFileLoadLimits)typeof(WorldFileLoaderTests).GetMethod("CreateLimits", flags)!.Invoke(null, null)!;
            Assert.True(WorldFileLoader.TryLoad(bytes, limits, out var world).IsLoaded);
            if (today)
            {
                int start = world!.Envelope.SectionOffsets[0], end = world.Envelope.SectionOffsets[1];
                Assert.Equal(WorldFileSeasonHeaderPatchResult1458.Patched, WorldFileSeasonHeaderPatcher1458.TryPatch(
                    bytes.AsSpan(start, end - start), world.Envelope, world.Header, out var header, true, true));
                header.CopyTo(bytes, start); Assert.True(WorldFileLoader.TryLoad(bytes, limits, out world).IsLoaded);
            }
            World = world!;
            Assert.True(WorldFilePreservedSections.TryCapture(bytes, World.Envelope, out var captured)); preserved = captured!;
        }
        internal async Task<WorldFileData> Serialize(RuntimeWorldCheckpointSnapshot snapshot)
        {
            using var stream = new MemoryStream();
            var method = typeof(RuntimeWorldCheckpointCoordinator).GetMethod("SerializeAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(null, [World.Envelope, World.Header, preserved, snapshot, stream, CancellationToken.None])!;
            Assert.True(WorldFileLoader.TryLoad(stream.ToArray(), limits, out var saved).IsLoaded);
            return saved!;
        }
    }
}
