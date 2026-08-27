using System.Buffers.Binary;
using System.Text;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileSignDecoderTests
{
    private const int EnvelopeEnd = 167;
    private const int SignStart = 220;

    [Fact]
    public void Decodes_current_signs_and_keeps_first_duplicate_position()
    {
        byte[] signBytes = CreateSignBytes(writer =>
        {
            writer.Write((short)2);
            writer.Write("first");
            writer.Write(2);
            writer.Write(3);
            writer.Write("duplicate");
            writer.Write(2);
            writer.Write(3);
        });
        byte[] file = CreateCurrentFile(signBytes);

        WorldFileSignDecodeResult result = WorldFileSignDecoder.TryDecode(
            file,
            ParseEnvelope(file),
            CreateHeader(),
            maxTextBytesPerSign: 64,
            maxTotalTextBytes: 128,
            out WorldSign[] signs,
            out int consumed);

        Assert.Equal(WorldFileSignDecodeResult.Decoded, result);
        Assert.Equal(signBytes.Length, consumed);
        WorldSign sign = Assert.Single(signs);
        Assert.Equal(new WorldSign(0, "first", 2, 3), sign);
    }

    [Fact]
    public void Preserves_file_order_slot_after_duplicate_hole()
    {
        byte[] signBytes = CreateSignBytes(writer =>
        {
            writer.Write((short)3);
            writer.Write("first");
            writer.Write(2);
            writer.Write(3);
            writer.Write("duplicate");
            writer.Write(2);
            writer.Write(3);
            writer.Write("later");
            writer.Write(4);
            writer.Write(5);
        });
        byte[] file = CreateCurrentFile(signBytes);

        Assert.Equal(
            WorldFileSignDecodeResult.Decoded,
            WorldFileSignDecoder.TryDecode(
                file,
                ParseEnvelope(file),
                CreateHeader(),
                maxTextBytesPerSign: 64,
                maxTotalTextBytes: 256,
                out WorldSign[] signs,
                out _));

        Assert.Equal(2, signs.Length);
        Assert.Equal((short)0, signs[0].SlotId);
        Assert.Equal((short)2, signs[1].SlotId);
        Assert.Equal("later", signs[1].Text);
    }

    [Fact]
    public void Rejects_text_length_before_allocating_string()
    {
        byte[] signBytes = CreateSignBytes(writer =>
        {
            writer.Write((short)1);
            writer.Write(new string('x', 20));
            writer.Write(1);
            writer.Write(1);
        });
        byte[] file = CreateCurrentFile(signBytes);

        Assert.Equal(
            WorldFileSignDecodeResult.TextBudgetExceeded,
            WorldFileSignDecoder.TryDecode(
                file,
                ParseEnvelope(file),
                CreateHeader(),
                maxTextBytesPerSign: 8,
                maxTotalTextBytes: 100,
                out WorldSign[] signs,
                out _));
        Assert.Empty(signs);
    }

    [Fact]
    public void Enforces_aggregate_text_budget_across_signs()
    {
        byte[] signBytes = CreateSignBytes(writer =>
        {
            writer.Write((short)2);
            writer.Write("12345");
            writer.Write(1);
            writer.Write(1);
            writer.Write("67890");
            writer.Write(2);
            writer.Write(2);
        });
        byte[] file = CreateCurrentFile(signBytes);

        Assert.Equal(
            WorldFileSignDecodeResult.TextBudgetExceeded,
            WorldFileSignDecoder.TryDecode(
                file,
                ParseEnvelope(file),
                CreateHeader(),
                maxTextBytesPerSign: 10,
                maxTotalTextBytes: 9,
                out _,
                out _));
    }

    [Fact]
    public void Rejects_out_of_world_coordinates()
    {
        byte[] signBytes = CreateSignBytes(writer =>
        {
            writer.Write((short)1);
            writer.Write("bad");
            writer.Write(10);
            writer.Write(1);
        });
        byte[] file = CreateCurrentFile(signBytes);

        Assert.Equal(
            WorldFileSignDecodeResult.InvalidSignCoordinates,
            WorldFileSignDecoder.TryDecode(
                file,
                ParseEnvelope(file),
                CreateHeader(),
                maxTextBytesPerSign: 64,
                maxTotalTextBytes: 64,
                out _,
                out _));
    }

    [Fact]
    public void Requires_exact_end_of_sign_section()
    {
        byte[] signBytes = CreateSignBytes(writer => writer.Write((short)0));
        Array.Resize(ref signBytes, signBytes.Length + 1);
        byte[] file = CreateCurrentFile(signBytes);

        Assert.Equal(
            WorldFileSignDecodeResult.SectionLengthMismatch,
            WorldFileSignDecoder.TryDecode(
                file,
                ParseEnvelope(file),
                CreateHeader(),
                maxTextBytesPerSign: 64,
                maxTotalTextBytes: 64,
                out _,
                out int consumed));
        Assert.Equal(sizeof(short), consumed);
    }

    private static WorldFileHeader CreateHeader()
    {
        var dimensions = new WorldDimensions(10, 10);
        return new WorldFileHeader("test", "seed", 1, Guid.Empty, 1, 0, 160, 0, 160, dimensions);
    }

    private static byte[] CreateSignBytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true))
        {
            write(writer);
            writer.Flush();
        }
        return stream.ToArray();
    }

    private static WorldFileEnvelope ParseEnvelope(byte[] file)
    {
        Assert.Equal(
            WorldFileEnvelopeParseResult.Parsed,
            WorldFileEnvelopeParser.TryParse(file, out WorldFileEnvelope? envelope, out int envelopeLength));
        Assert.Equal(EnvelopeEnd, envelopeLength);
        return Assert.IsType<WorldFileEnvelope>(envelope);
    }

    private static byte[] CreateCurrentFile(byte[] signBytes)
    {
        int signEnd = SignStart + signBytes.Length;
        int[] pointers =
        [
            EnvelopeEnd,
            180,
            190,
            SignStart,
            signEnd,
            signEnd + 8,
            signEnd + 16,
            signEnd + 24,
            signEnd + 32,
            signEnd + 40,
            signEnd + 48
        ];
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

        signBytes.CopyTo(file, SignStart);
        return file;
    }
}
