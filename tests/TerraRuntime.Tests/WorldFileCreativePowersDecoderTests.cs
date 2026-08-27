using System.Buffers.Binary;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileCreativePowersDecoderTests
{
    private const int EnvelopeEnd = 167;
    private const int CreativeStart = 280;

    [Fact]
    public void Decodes_all_current_world_persistent_powers()
    {
        byte[] section = CreateSection(writer =>
        {
            WriteBoolPower(writer, 0, true);
            WriteFloatPower(writer, 8, 0.25f);
            WriteBoolPower(writer, 9, false);
            WriteBoolPower(writer, 10, true);
            WriteFloatPower(writer, 12, 0.75f);
            WriteBoolPower(writer, 13, true);
            writer.Write(false);
        });
        byte[] file = CreateCurrentFile(section);

        Assert.Equal(
            WorldFileCreativePowersDecodeResult.Decoded,
            WorldFileCreativePowersDecoder.TryDecode(file, ParseEnvelope(file), out WorldCreativePowersData? powers, out int consumed));
        Assert.Equal(section.Length, consumed);
        Assert.Equal(new WorldCreativePowersData(true, 0.25f, false, true, 0.75f, true), Assert.IsType<WorldCreativePowersData>(powers));
    }

    [Fact]
    public void Rejects_unknown_missing_and_invalid_slider_payloads()
    {
        byte[] unknown = CreateSection(writer =>
        {
            writer.Write(true);
            writer.Write((ushort)15);
            writer.Write(false);
        });
        byte[] unknownFile = CreateCurrentFile(unknown);
        Assert.Equal(
            WorldFileCreativePowersDecodeResult.UnknownPowerId,
            WorldFileCreativePowersDecoder.TryDecode(unknownFile, ParseEnvelope(unknownFile), out _, out _));

        byte[] missing = CreateSection(writer => writer.Write(false));
        byte[] missingFile = CreateCurrentFile(missing);
        Assert.Equal(
            WorldFileCreativePowersDecodeResult.MissingPower,
            WorldFileCreativePowersDecoder.TryDecode(missingFile, ParseEnvelope(missingFile), out _, out _));

        byte[] badSlider = CreateSection(writer =>
        {
            WriteBoolPower(writer, 0, false);
            WriteFloatPower(writer, 8, float.NaN);
        });
        byte[] badSliderFile = CreateCurrentFile(badSlider);
        Assert.Equal(
            WorldFileCreativePowersDecodeResult.InvalidSliderValue,
            WorldFileCreativePowersDecoder.TryDecode(badSliderFile, ParseEnvelope(badSliderFile), out _, out _));
    }

    private static void WriteBoolPower(BinaryWriter writer, ushort id, bool value)
    {
        writer.Write(true);
        writer.Write(id);
        writer.Write(value);
    }

    private static void WriteFloatPower(BinaryWriter writer, ushort id, float value)
    {
        writer.Write(true);
        writer.Write(id);
        writer.Write(value);
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

    private static byte[] CreateCurrentFile(byte[] creativeSection)
    {
        int creativeEnd = CreativeStart + creativeSection.Length;
        int[] pointers = [EnvelopeEnd, 180, 190, 200, 210, 220, 230, 240, 250, CreativeStart, creativeEnd];
        var file = new byte[creativeEnd + 1];
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
        creativeSection.CopyTo(file, CreativeStart);
        return file;
    }
}
