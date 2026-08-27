using System.Buffers.Binary;
using System.Text;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileFooterValidatorTests
{
    private const int EnvelopeEnd = 167;
    private const int FooterStart = 300;

    [Fact]
    public void Validates_current_footer_against_header_identity()
    {
        byte[] file = CreateFile("footer-world", 77);
        WorldFileEnvelope envelope = ParseEnvelope(file);
        WorldFileHeader header = CreateHeader("footer-world", 77);

        Assert.Equal(
            WorldFileFooterValidationResult.Valid,
            WorldFileFooterValidator.Validate(file, envelope, header, out int consumed));
        Assert.Equal(file.Length - FooterStart, consumed);
    }

    [Fact]
    public void Rejects_identity_mismatch_and_trailing_bytes()
    {
        byte[] wrongId = CreateFile("footer-world", 77);
        Assert.Equal(
            WorldFileFooterValidationResult.WorldIdMismatch,
            WorldFileFooterValidator.Validate(wrongId, ParseEnvelope(wrongId), CreateHeader("footer-world", 78), out _));

        byte[] trailing = CreateFile("footer-world", 77, trailingByte: 0xAA);
        Assert.Equal(
            WorldFileFooterValidationResult.TrailingBytes,
            WorldFileFooterValidator.Validate(trailing, ParseEnvelope(trailing), CreateHeader("footer-world", 77), out _));
    }

    private static WorldFileHeader CreateHeader(string name, int worldId)
    {
        var dimensions = new WorldDimensions(100, 100);
        return new WorldFileHeader(name, "seed", 1, Guid.Empty, worldId, 0, 1600, 0, 1600, dimensions);
    }

    private static WorldFileEnvelope ParseEnvelope(byte[] file)
    {
        Assert.Equal(
            WorldFileEnvelopeParseResult.Parsed,
            WorldFileEnvelopeParser.TryParse(file, out WorldFileEnvelope? envelope, out int length));
        Assert.Equal(EnvelopeEnd, length);
        return Assert.IsType<WorldFileEnvelope>(envelope);
    }

    private static byte[] CreateFile(string name, int worldId, byte? trailingByte = null)
    {
        byte[] footer;
        using (var stream = new MemoryStream())
        {
            using var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            writer.Write(true);
            writer.Write(name);
            writer.Write(worldId);
            writer.Flush();
            footer = stream.ToArray();
        }

        int[] pointers =
        [
            EnvelopeEnd,
            180,
            190,
            200,
            210,
            220,
            230,
            240,
            250,
            260,
            FooterStart
        ];
        int trailing = trailingByte.HasValue ? 1 : 0;
        var file = new byte[FooterStart + footer.Length + trailing];
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

        footer.CopyTo(file, FooterStart);
        if (trailingByte.HasValue)
            file[^1] = trailingByte.Value;
        return file;
    }
}
