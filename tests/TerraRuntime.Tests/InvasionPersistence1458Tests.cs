using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class InvasionPersistence1458Tests
{
    private static readonly WorldFileRuntimeMetadataLimits Limits = new(16384, 16 * 1024 * 1024, 255, 762, 255, 16 * 1024 * 1024);

    [Fact]
    public void Actual_source_headers_retain_all_five_fields_without_fake_reconstruction()
    {
        using var source = LoadReference();
        Assert.Equal(0, source.RootElement.GetProperty("fullOriginalLoadClaims").GetInt32());
        foreach (var row in source.RootElement.GetProperty("saves").EnumerateArray())
        {
            byte[] bytes = Convert.FromBase64String(row.GetProperty("headerBase64").GetString()!);
            Assert.Equal(row.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)));
            var metadata = Parse(bytes);
            var expected = row.GetProperty("before");
            Assert.Equal(expected.GetProperty("delay").GetInt32(), metadata.InvasionDelay);
            Assert.Equal(expected.GetProperty("size").GetInt32(), metadata.InvasionSize);
            Assert.Equal(expected.GetProperty("type").GetSByte(), metadata.InvasionType);
            Assert.Equal(expected.GetProperty("x").GetDouble(), metadata.InvasionX);
            Assert.Equal(expected.GetProperty("sizeStart").GetInt32(), metadata.InvasionSizeStart);
        }
    }

    [Fact]
    public void Patched_header_matches_actual_source_pair_except_source_save_timestamp()
    {
        byte[] baseline = ReadHeader("fresh");
        var envelope = Envelope(baseline);
        Assert.Equal(WorldFileHeaderParseResult.Parsed, WorldFileHeaderParser.TryParse(baseline, envelope, out var header));
        foreach (string profile in new[] { "fresh", "goblin", "pirate", "martian-stored-start", "inactive-residue" })
        {
            byte[] target = ReadHeader(profile);
            var metadata = Parse(target);
            var state = State(metadata);
            Assert.Equal(WorldFileInvasionHeaderPatchResult1458.Patched,
                WorldFileInvasionHeaderPatcher1458.TryPatch(baseline, envelope, header!, in state, out var patched));
            CopyLastPlayed(baseline, target);
            Assert.Equal(target, patched);
        }
    }

    [Fact]
    public void Source_progression_pairs_match_verified_completion_bytes()
    {
        byte[] baseline = ReadHeader("none", "progressions");
        Assert.Equal(WorldFileHeaderParseResult.Parsed, WorldFileHeaderParser.TryParse(baseline, Envelope(baseline), out var header));
        foreach (var profile in new[]
                 {
                     ("goblin", VanillaWorldProgressionId.GoblinArmy),
                     ("pirate", VanillaWorldProgressionId.PirateInvasion),
                     ("martian", VanillaWorldProgressionId.MartianMadness)
                 })
        {
            byte[] target = ReadHeader(profile.Item1, "progressions");
            var mutation = new RuntimeWorldProgressionMutationSnapshot(1UL << (int)profile.Item2);
            Assert.Equal(WorldFileProgressionHeaderPatchResult.Patched,
                WorldFileProgressionHeaderPatcher.TryPatch(baseline, header!, in mutation, out var patched));
            CopyLastPlayed(baseline, target);
            Assert.Equal(target, patched);
        }
    }

    [Fact]
    public void Invalid_header_version_state_and_identity_never_return_partial_patch()
    {
        byte[] bytes = ReadHeader("fresh");
        var envelope = Envelope(bytes);
        Assert.Equal(WorldFileHeaderParseResult.Parsed, WorldFileHeaderParser.TryParse(bytes, envelope, out var header));
        var state = new WorldInvasionSaveState1458(-1, -2, 0, -1.5, -3);
        Assert.Equal(WorldFileInvasionHeaderPatchResult1458.Patched,
            WorldFileInvasionHeaderPatcher1458.TryPatch(bytes, envelope, header!, in state, out var accepted));
        Assert.Equal(state, State(Parse(accepted)));
        Assert.Equal(WorldFileInvasionHeaderPatchResult1458.UnsupportedVersion,
            WorldFileInvasionHeaderPatcher1458.TryPatch(bytes, Envelope(bytes, 325), header!, in state, out var rejected));
        Assert.Empty(rejected);
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var badState = state with { X = invalid };
            Assert.Equal(WorldFileInvasionHeaderPatchResult1458.InvalidState,
                WorldFileInvasionHeaderPatcher1458.TryPatch(bytes, envelope, header!, in badState, out rejected));
            Assert.Empty(rejected);
        }
        foreach (int length in new[] { 0, 20, bytes.Length - 1 })
        {
            Assert.Equal(WorldFileInvasionHeaderPatchResult1458.InvalidHeader,
                WorldFileInvasionHeaderPatcher1458.TryPatch(bytes.AsSpan(0, length), envelope, header!, in state, out rejected));
            Assert.Empty(rejected);
        }
        Assert.Equal(WorldFileInvasionHeaderPatchResult1458.InvalidHeader,
            WorldFileInvasionHeaderPatcher1458.TryPatch(bytes, envelope, header! with { WorldId = 123 }, in state, out rejected));
        Assert.Empty(rejected);
    }

    [Fact]
    public void Prepared_payload_retains_source_values_and_declares_new_disposable_cache_layout()
    {
        Assert.Equal(5, RuntimeWorldSnapshotCache.CurrentLayoutVersion);
        foreach (string profile in new[] { "fresh", "goblin", "pirate", "martian-stored-start", "inactive-residue" })
        {
            byte[] bytes = ReadHeader(profile);
            var envelope = Envelope(bytes);
            Assert.Equal(WorldFileHeaderParseResult.Parsed, WorldFileHeaderParser.TryParse(bytes, envelope, out var header));
            var tiles = new WorldTileStore(header!.Dimensions);
            var metadata = Parse(bytes);
            var world = new WorldFileData(envelope, header, metadata, tiles, [], [], new([], [], []), [], [], [],
                new([], [], []), new(false, 0, false, false, 0, false));
            byte[] payload = RuntimeWorldPreparedStateCodec.Encode(world);
            Assert.True(RuntimeWorldPreparedStateCodec.TryDecode(payload, tiles, out var decoded));
            Assert.Equal(State(metadata), State(decoded!.RuntimeMetadata));
        }
    }

    [Fact]
    public void Complete_world_fixture_header_patch_preserves_every_other_world_byte()
    {
        // Complement the independent official header pairs with the existing complete loader fixture.
        // This structural preservation check is not an original full-world loading claim.
        byte[] canonical = (byte[])typeof(WorldFileLoaderTests).GetMethod("CreateCompleteCurrentWorld",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        Assert.Equal(WorldFileEnvelopeParseResult.Parsed, WorldFileEnvelopeParser.TryParse(canonical, out var envelope, out _));
        Assert.Equal(WorldFileHeaderParseResult.Parsed, WorldFileHeaderParser.TryParse(canonical, envelope!, out var header));
        int start = envelope!.SectionOffsets[0], end = envelope.SectionOffsets[1];
        var state = new WorldInvasionSaveState1458(7, 19, 4, 43.125, 333);
        Assert.Equal(WorldFileInvasionHeaderPatchResult1458.Patched,
            WorldFileInvasionHeaderPatcher1458.TryPatch(canonical.AsSpan(start, end - start), envelope, header!, in state, out var patched));
        byte[] copy = (byte[])canonical.Clone();
        patched.CopyTo(copy, start);
        Assert.Equal(WorldFileRuntimeMetadataParseResult.Parsed,
            WorldFileRuntimeMetadataParser.TryParse(copy, envelope, header!, Limits, out var metadata, out _));
        Assert.Equal(state, State(metadata!));
        Assert.Equal(canonical.AsSpan(0, start).ToArray(), copy.AsSpan(0, start).ToArray());
        Assert.Equal(canonical.AsSpan(end).ToArray(), copy.AsSpan(end).ToArray());
        var original = canonical.AsSpan(start, end - start);
        for (int index = 0; index < patched.Length; index++)
            if (index < metadata!.InvasionFieldsOffset || index >= metadata.InvasionFieldsOffset + 20)
                if (index < metadata.InvasionSizeStartOffset || index >= metadata.InvasionSizeStartOffset + 4)
                    Assert.Equal(original[index], patched[index]);
    }

    private static WorldFileEnvelope Envelope(byte[] bytes, int version = 326) => new(version, 0, 0, [0, bytes.Length], 0, []);
    private static JsonDocument LoadReference()
    {
        using var stream = typeof(InvasionPersistence1458Tests).Assembly.GetManifestResourceStream("InvasionPersistence1458");
        using var gzip = new GZipStream(Assert.IsAssignableFrom<Stream>(stream), CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
    private static byte[] ReadHeader(string profile, string kind = "saves")
    {
        using var document = LoadReference();
        var row = document.RootElement.GetProperty(kind).EnumerateArray()
            .Single(row => row.GetProperty("profile").GetString() == profile);
        return Convert.FromBase64String(row.GetProperty("headerBase64").GetString()!);
    }
    private static WorldFileRuntimeMetadata Parse(byte[] bytes)
    {
        var envelope = Envelope(bytes);
        Assert.Equal(WorldFileHeaderParseResult.Parsed, WorldFileHeaderParser.TryParse(bytes, envelope, out var header));
        Assert.Equal(WorldFileRuntimeMetadataParseResult.Parsed,
            WorldFileRuntimeMetadataParser.TryParse(bytes, envelope, header!, Limits, out var metadata, out _));
        return metadata!;
    }
    private static WorldInvasionSaveState1458 State(WorldFileRuntimeMetadata metadata) => new(
        metadata.InvasionDelay, metadata.InvasionSize, metadata.InvasionType, metadata.InvasionX, metadata.InvasionSizeStart);
    private static void CopyLastPlayed(byte[] baseline, byte[] target)
    {
        using var stream = new MemoryStream(baseline);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadString();
        _ = reader.ReadString();
        // Header identity: generator8/guid16/id4/bounds16/dimensions8; flags: mode4/seeds9/creation8.
        stream.Position += 8 + 16 + 4 + 16 + 8 + 4 + 9 + 8;
        baseline.AsSpan((int)stream.Position, 8).CopyTo(target.AsSpan((int)stream.Position, 8));
    }
}
