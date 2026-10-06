using System.IO.Compression;
using System.Text.Json;
using Multiplicity.Packets;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class InvasionWorldWire1458Tests
{
    [Fact]
    public void Completion_event_codec_matches_actual_source_UpdateInvasion_packets()
    {
        using var source = Load("InvasionCompletionEvent1458");
        foreach (var row in source.RootElement.GetProperty("rows").EnumerateArray())
        {
            var after = row.GetProperty("after");
            short eventId = after.GetProperty("downedGoblin").GetBoolean() ? (short)10 :
                after.GetProperty("downedPirate").GetBoolean() ? (short)11 : (short)13;
            byte[] expected = Convert.FromHexString(row.GetProperty("frames")[0].GetString()!);
            Assert.Equal(expected, TerrariaProgressionEventCodec1458.Encode(eventId));
        }
    }

    [Fact]
    public void Live_identity_and_completion_bits_match_actual_source_packet7()
    {
        using var source = Load();
        var header = new WorldFileHeader("wire-invasion", "", 0, Guid.Empty, 4242, 0, 1600, 0, 1280, new(100, 80));
        // Compare the invasion fields against original wire bytes, not a runtime round-trip.
        foreach (var row in source.RootElement.GetProperty("rows").EnumerateArray())
        {
            foreach (var frame in row.GetProperty("frames").EnumerateArray())
            {
                byte[] bytes = Convert.FromHexString(frame.GetString()!);
                if (bytes[2] != 7) continue;
                var after = row.GetProperty("after");
                ulong mask = 0;
                if (after.GetProperty("downedGoblin").GetBoolean()) mask |= 1UL << (int)VanillaWorldProgressionId.GoblinArmy;
                if (after.GetProperty("downedPirate").GetBoolean()) mask |= 1UL << (int)VanillaWorldProgressionId.PirateInvasion;
                if (after.GetProperty("downedMartian").GetBoolean()) mask |= 1UL << (int)VanillaWorldProgressionId.MartianMadness;
                var live = new WorldInfoRuntimeState(1234, true, 2, false, false)
                {
                    InvasionType = after.GetProperty("type").GetSByte(),
                    ProgressionMutations = new(mask)
                };
                // Deliberately stale persisted identity detects missing live override.
                var persisted = new WorldFileRuntimeMetadata { InvasionType = 2 };
                var actual = WorldInfoPacketMapper.Create(header, persisted, runtime: live);
                var expected = Assert.IsType<WorldInfo>(TerrariaPacket.DeserializePayload(PacketTypes.WorldInfo, bytes.AsSpan(3).ToArray()));
                Assert.Equal(expected.InvasionType, actual.InvasionType);
                Assert.Equal(expected.EventInfo3 & 0x40, actual.EventInfo3 & 0x40);
                Assert.Equal(expected.EventInfo5 & 0x05, actual.EventInfo5 & 0x05);
            }
        }
        var loaded = new WorldFileRuntimeMetadata { InvasionType = 3, DownedGoblins = true, DownedPirates = true, DownedMartians = true };
        var unchanged = WorldInfoPacketMapper.Create(header, loaded);
        Assert.Equal(3, unchanged.InvasionType);
        Assert.Equal(0x40, unchanged.EventInfo3 & 0x40);
        Assert.Equal(0x05, unchanged.EventInfo5 & 0x05);
    }

    [Fact]
    public void Progress_codec_matches_actual_SyncAnInvasion_and_packet61_including_signed_residue()
    {
        using var source = Load();
        int compared = 0;
        foreach (var row in source.RootElement.GetProperty("rows").EnumerateArray())
        {
            bool request = row.GetProperty("phase").GetString() == "actual MessageBuffer.GetData61";
            var after = row.GetProperty("after");
            foreach (var captured in row.GetProperty("frames").EnumerateArray())
            {
                byte[] bytes = Convert.FromHexString(captured.GetString()!);
                if (bytes[2] != 78) continue;
                int start = after.GetProperty("sizeStart").GetInt32();
                var state = new TerrariaInvasionProgressState(
                    request ? 0 : start - after.GetProperty("size").GetInt32(),
                    request ? 1 : Math.Max(start, 1),
                    checked((sbyte)(after.GetProperty("type").GetInt32() + 3)), 0);
                Assert.True(TerrariaInvasionProgressCodec.TryEncode(in state, out var actual));
                Assert.Equal(bytes, actual);
                compared++;
            }
        }
        Assert.Equal(10, compared);
    }

    private static JsonDocument Load(string name = "InvasionWorldWire1458")
    {
        using var resource = typeof(InvasionWorldWire1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
