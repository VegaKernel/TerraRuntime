using System.Buffers.Binary;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileBestiaryDecoderTests
{
    private const int EnvelopeEnd = 167;
    private const int BestiaryStart = 260;

    [Fact]
    public void Decodes_current_bestiary_trackers_and_normalizes_set_semantics()
    {
        byte[] section = CreateSection(writer =>
        {
            writer.Write(2);
            writer.Write("Zombie");
            writer.Write(12);
            writer.Write("Zombie");
            writer.Write(13);

            writer.Write(2);
            writer.Write("Bunny");
            writer.Write("Bunny");

            writer.Write(1);
            writer.Write("Guide");
        });
        byte[] file = CreateCurrentFile(section);
        var limits = new WorldFileBestiaryLimits(8, 8, 8, 64, 256);

        Assert.Equal(
            WorldFileBestiaryDecodeResult.Decoded,
            WorldFileBestiaryDecoder.TryDecode(file, ParseEnvelope(file), limits, out WorldBestiaryData? data, out int consumed));
        Assert.Equal(section.Length, consumed);
        WorldBestiaryData bestiary = Assert.IsType<WorldBestiaryData>(data);
        Assert.Equal(new WorldBestiaryKill("Zombie", 13), Assert.Single(bestiary.Kills));
        Assert.Equal("Bunny", Assert.Single(bestiary.Sightings));
        Assert.Equal("Guide", Assert.Single(bestiary.Chats));
    }

    [Fact]
    public void Rejects_entry_and_string_budgets_before_unbounded_growth()
    {
        byte[] tooMany = CreateSection(writer => writer.Write(2));
        byte[] tooManyFile = CreateCurrentFile(tooMany);
        var oneEntry = new WorldFileBestiaryLimits(1, 1, 1, 64, 256);
        Assert.Equal(
            WorldFileBestiaryDecodeResult.EntryBudgetExceeded,
            WorldFileBestiaryDecoder.TryDecode(tooManyFile, ParseEnvelope(tooManyFile), oneEntry, out _, out _));

        byte[] longId = CreateSection(writer =>
        {
            writer.Write(1);
            writer.Write("12345");
            writer.Write(1);
            writer.Write(0);
            writer.Write(0);
        });
        byte[] longIdFile = CreateCurrentFile(longId);
        var shortStrings = new WorldFileBestiaryLimits(2, 2, 2, 4, 64);
        Assert.Equal(
            WorldFileBestiaryDecodeResult.StringTooLarge,
            WorldFileBestiaryDecoder.TryDecode(longIdFile, ParseEnvelope(longIdFile), shortStrings, out _, out _));
    }

    [Fact]
    public void Rejects_kill_counts_outside_vanilla_cap()
    {
        byte[] section = CreateSection(writer =>
        {
            writer.Write(1);
            writer.Write("Zombie");
            writer.Write(1_000_000_000);
            writer.Write(0);
            writer.Write(0);
        });
        byte[] file = CreateCurrentFile(section);

        Assert.Equal(
            WorldFileBestiaryDecodeResult.InvalidKillCount,
            WorldFileBestiaryDecoder.TryDecode(
                file,
                ParseEnvelope(file),
                new WorldFileBestiaryLimits(2, 2, 2, 64, 256),
                out _,
                out _));
    }

    private static byte[] CreateSection(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            write(writer);
        return stream.ToArray();
    }

    private static WorldFileEnvelope ParseEnvelope(byte[] file)
    {
        Assert.Equal(WorldFileEnvelopeParseResult.Parsed, WorldFileEnvelopeParser.TryParse(file, out WorldFileEnvelope? envelope, out _));
        return Assert.IsType<WorldFileEnvelope>(envelope);
    }

    private static byte[] CreateCurrentFile(byte[] bestiarySection)
    {
        int bestiaryEnd = BestiaryStart + bestiarySection.Length;
        int[] pointers = [EnvelopeEnd, 180, 190, 200, 210, 220, 230, 240, BestiaryStart, bestiaryEnd, bestiaryEnd + 10];
        var file = new byte[pointers[^1] + 1];
        int offset = 0;
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(offset), WorldFileFormatPolicy.CurrentVersion);
        offset += sizeof(int);
        "relogic"u8.CopyTo(file.AsSpan(offset));
        offset += 7;
        file[offset++] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(offset), 1);
        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(offset), 0);
        offset += sizeof(ulong);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(offset), VanillaWorldFormat326.SectionCount);
        offset += sizeof(short);
        foreach (int pointer in pointers)
        {
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(offset), pointer);
            offset += sizeof(int);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(offset), VanillaWorldFormat326.TileTypeCount);
        offset += sizeof(ushort);
        offset += (VanillaWorldFormat326.TileTypeCount + 7) >> 3;
        Assert.Equal(EnvelopeEnd, offset);
        bestiarySection.CopyTo(file, BestiaryStart);
        return file;
    }
}
