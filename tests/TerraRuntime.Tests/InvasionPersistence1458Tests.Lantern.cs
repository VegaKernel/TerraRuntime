using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class InvasionPersistence1458Tests
{
    [Fact]
    public void Actual_save_header_pending_flag_is_parsed_and_patched_without_changing_other_bytes()
    {
        using var source = LoadLanternReference();
        Assert.Empty(source.RootElement.GetProperty("errors").EnumerateArray());
        var rows = source.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        byte[] baseline = Convert.FromBase64String(rows[0].GetProperty("headerBase64").GetString()!);
        foreach (var row in rows)
        {
            bool pending = row.GetProperty("pending").GetBoolean();
            byte[] expected = Convert.FromBase64String(row.GetProperty("headerBase64").GetString()!);
            Assert.Equal(row.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(expected)));
            Assert.Equal(pending, Parse(expected).LanternNightNextNight);
            var envelope = Envelope(baseline);
            Assert.Equal(WorldFileHeaderParseResult.Parsed, WorldFileHeaderParser.TryParse(baseline, envelope, out var header));
            var state = State(Parse(baseline)) with { LanternNightNextNight = pending };
            Assert.Equal(WorldFileInvasionHeaderPatchResult1458.Patched,
                WorldFileInvasionHeaderPatcher1458.TryPatch(baseline, envelope, header!, in state, out var actual));
            CopyLastPlayed(baseline, expected);
            Assert.Equal(expected, actual);
            // Old five-field callers must retain the source's existing pending flag.
            var unspecified = State(Parse(expected));
            Assert.Equal(WorldFileInvasionHeaderPatchResult1458.Patched,
                WorldFileInvasionHeaderPatcher1458.TryPatch(expected, envelope, header!, in unspecified, out var preserved));
            Assert.Equal(expected, preserved);
        }
    }

    [Fact]
    public void Prepared_metadata_and_live_owner_retain_pending_flag_but_unknown_frost_keeps_raw_state()
    {
        using var source = LoadLanternReference();
        foreach (var row in source.RootElement.GetProperty("rows").EnumerateArray())
        {
            byte[] bytes = Convert.FromBase64String(row.GetProperty("headerBase64").GetString()!);
            var metadata = Parse(bytes);
            var envelope = Envelope(bytes);
            Assert.Equal(WorldFileHeaderParseResult.Parsed, WorldFileHeaderParser.TryParse(bytes, envelope, out var header));
            var tiles = new WorldTileStore(header!.Dimensions);
            var world = new WorldFileData(envelope, header, metadata, tiles, [], [], new([], [], []), [], [], [],
                new([], [], []), new(false, 0, false, false, 0, false));
            Assert.True(RuntimeWorldPreparedStateCodec.TryDecode(RuntimeWorldPreparedStateCodec.Encode(world), tiles, out var decoded));
            Assert.Equal(metadata.LanternNightNextNight, decoded!.RuntimeMetadata.LanternNightNextNight);
            var owner = RuntimeWorldInvasion1458.FromMetadata(decoded.RuntimeMetadata, 100);
            Assert.True(owner.TryCapture(out var capture));
            Assert.Equal(metadata.LanternNightNextNight, capture.State.LanternNextNight);
            Assert.Equal(metadata.LanternNightNextNight, owner.CaptureSaveState()!.Value.LanternNightNextNight);
            if (metadata.LanternNightNextNight)
            {
                var clearing = new InvasionTransition1458(capture.State with { LanternNextNight = false }, default);
                Assert.False(owner.CanAdopt(in capture, in clearing));
                Assert.False(owner.TryAdopt(in capture, in clearing, out _));
                Assert.True(owner.IsCurrent(in capture));
            }
            var frost = RuntimeWorldInvasion1458.FromMetadata(new WorldFileRuntimeMetadata
            {
                InvasionType = 2,
                LanternNightNextNight = metadata.LanternNightNextNight
            }, 100);
            Assert.False(frost.TryCapture(out _));
            Assert.Null(frost.CaptureSaveState());
        }
    }

    private static JsonDocument LoadLanternReference()
    {
        using var stream = typeof(InvasionPersistence1458Tests).Assembly.GetManifestResourceStream("LanternPendingPersistence1458");
        using var gzip = new GZipStream(Assert.IsAssignableFrom<Stream>(stream), CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
