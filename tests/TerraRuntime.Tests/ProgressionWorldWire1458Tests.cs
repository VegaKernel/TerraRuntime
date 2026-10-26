using System.IO.Compression;
using System.Text.Json;
using global::Multiplicity.Packets;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ProgressionWorldWire1458Tests
{
    [Fact]
    public void Every_permanent_milestone_overlays_the_source_packet7_bit_without_reordering_or_mutating_saved_state()
    {
        using var wires = Load("ProgressionWorldWire1458");
        using var headers = Load("MoonSeasonHeader1458");
        var raw = headers.RootElement.GetProperty("rows").EnumerateArray().First(row =>
            row.GetProperty("today").GetInt32() == 0 && row.GetProperty("forever").GetInt32() == 0 && row.GetProperty("shadow").GetInt32() == 0);
        var (header, none) = Parse(Convert.FromBase64String(raw.GetProperty("headerBase64").GetString()!));
        var (_, all) = Parse(Convert.FromBase64String(wires.RootElement.GetProperty("allProgressionHeader").GetString()!));
        byte[] originalNone = Flags(Wire(wires.RootElement.GetProperty("baseline").GetString()!));
        byte[] originalAll = Flags(Wire(wires.RootElement.GetProperty("allProgressionFrame").GetString()!));
        Assert.Equal(originalNone, Flags(WorldInfoPacketMapper.Create(header, none)));
        Assert.Equal(originalAll, Flags(WorldInfoPacketMapper.Create(header, all)));
        var rows = wires.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(VanillaWorldProgressionState.MilestoneCount, rows.Length);
        var covered = new HashSet<VanillaWorldProgressionId>();
        foreach (var row in rows)
        {
            var milestone = (VanillaWorldProgressionId)row.GetProperty("milestone").GetInt32();
            Assert.True(covered.Add(milestone));
            var before = none.Progression;
            Assert.False(before.IsComplete(milestone));
            byte[] expected = Flags(Wire(row.GetProperty("frameHex").GetString()!));
            int offset = Assert.Single(Enumerable.Range(0, expected.Length), i => expected[i] != originalNone[i]);
            int delta = expected[offset] ^ originalNone[offset];
            Assert.True(delta > 0 && (delta & (delta - 1)) == 0); // Independent original toggles exactly one bit.
            var snapshot = new RuntimeWorldProgressionMutationSnapshot(1UL << (int)milestone);
            var runtime = new WorldInfoRuntimeState(0, true, 0, false, false) { ProgressionMutations = snapshot };
            byte[] actual = Flags(WorldInfoPacketMapper.Create(header, none, runtime: runtime));
            Assert.Equal(expected, actual);
            Assert.Equal(actual, Flags(WorldInfoPacketMapper.Create(header, none, runtime: runtime)));
            Assert.Equal(before, none.Progression);
            Assert.False(none.Progression.IsComplete(milestone));
            Assert.True(all.Progression.IsComplete(milestone));
            Assert.Equal(originalAll, Flags(WorldInfoPacketMapper.Create(header, all, runtime: runtime)));
        }
        Assert.Equal(Enum.GetValues<VanillaWorldProgressionId>().Length, covered.Count);
    }

    [Fact]
    public void Existing_blue_yellow_and_truffle_unlock_journals_reach_their_original_wire_flags()
    {
        using var wires = Load("ProgressionWorldWire1458");
        using var headers = Load("MoonSeasonHeader1458");
        var raw = headers.RootElement.GetProperty("rows").EnumerateArray().First(row =>
            row.GetProperty("today").GetInt32() == 0 && row.GetProperty("forever").GetInt32() == 0 && row.GetProperty("shadow").GetInt32() == 0);
        var (header, metadata) = Parse(Convert.FromBase64String(raw.GetProperty("headerBase64").GetString()!));
        foreach (var row in wires.RootElement.GetProperty("unlocks").EnumerateArray())
        {
            var journal = new RuntimeWorldProgressionMutations();
            bool accepted = row.GetProperty("field").GetString() switch
            {
                "unlockedSlimeBlueSpawn" => journal.MarkSlimeBlueSpawnUnlocked(),
                "unlockedTruffleSpawn" => journal.MarkTruffleSpawnUnlocked(),
                "unlockedSlimeYellowSpawn" => journal.MarkSlimeYellowSpawnUnlocked(),
                _ => throw new InvalidDataException()
            };
            Assert.True(accepted);
            var live = new WorldInfoRuntimeState(0, true, 0, false, false) { ProgressionMutations = journal.CaptureSnapshot() };
            Assert.Equal(Flags(Wire(row.GetProperty("frameHex").GetString()!)), Flags(WorldInfoPacketMapper.Create(header, metadata, runtime: live)));
        }
        Assert.False(metadata.UnlockedSlimeBlueSpawn); Assert.False(metadata.UnlockedSlimeYellowSpawn); Assert.False(metadata.UnlockedTruffleSpawn);
    }

    private static JsonDocument Load(string name)
    {
        using var stream = typeof(ProgressionWorldWire1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        var document = JsonDocument.Parse(gzip);
        Assert.Equal("4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034", document.RootElement.GetProperty("source").GetString());
        return document;
    }
    private static (WorldFileHeader Header, WorldFileRuntimeMetadata Metadata) Parse(byte[] bytes)
    {
        var envelope = new WorldFileEnvelope(326, 0, 0, [0, bytes.Length], 0, []);
        var limits = new WorldFileRuntimeMetadataLimits(16384, 16 * 1024 * 1024, 255, 762, 255, 16 * 1024 * 1024);
        Assert.Equal(WorldFileHeaderParseResult.Parsed, WorldFileHeaderParser.TryParse(bytes, envelope, out var header));
        Assert.Equal(WorldFileRuntimeMetadataParseResult.Parsed,
            WorldFileRuntimeMetadataParser.TryParse(bytes, envelope, header!, limits, out var metadata, out _));
        return (header!, metadata!);
    }
    private static WorldInfo Wire(string hex) => Assert.IsType<WorldInfo>(TerrariaPacket.DeserializePayload(
        PacketTypes.WorldInfo, Convert.FromHexString(hex).AsSpan(3).ToArray()));
    private static byte[] Flags(WorldInfo info) => [info.EventInfo, info.EventInfo2, info.EventInfo3, info.EventInfo4,
        info.EventInfo5, info.EventInfo6, info.EventInfo7, info.EventInfo8, info.EventInfo9, info.EventInfo10, info.EventInfo11];
}
