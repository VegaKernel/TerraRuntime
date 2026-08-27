using System.Buffers.Binary;
using System.Text;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileNpcDecoderTests
{
    private const int EnvelopeEnd = 167;
    private const int NpcStart = 220;

    private static readonly WorldFileNpcDecodeOptions DefaultOptions = new(
        MaxShimmeredTownNpcIndices: 16,
        MaxShimmerIndexExclusive: 128,
        MaxTownNpcs: 16,
        MaxPersistentNpcs: 16,
        MaxNameBytesPerTownNpc: 64,
        MaxTotalNameBytes: 256);

    [Fact]
    public void Decodes_current_shimmer_town_and_persistent_npc_sequences()
    {
        byte[] npcBytes = CreateNpcBytes(writer =>
        {
            writer.Write(2);
            writer.Write(3);
            writer.Write(7);

            writer.Write(true);
            writer.Write(17);
            writer.Write("Merchant");
            writer.Write(12.5f);
            writer.Write(33.25f);
            writer.Write(false);
            writer.Write(10);
            writer.Write(20);
            writer.Write((byte)1);
            writer.Write(4);
            writer.Write(true);
            writer.Write(false);

            writer.Write(true);
            writer.Write(488);
            writer.Write(99.5f);
            writer.Write(101.25f);
            writer.Write(false);
        });
        byte[] file = CreateCurrentFile(npcBytes);

        WorldFileNpcDecodeResult result = WorldFileNpcDecoder.TryDecode(
            file,
            ParseEnvelope(file),
            DefaultOptions,
            out WorldNpcPersistence? persistence,
            out int consumed);

        Assert.Equal(WorldFileNpcDecodeResult.Decoded, result);
        Assert.Equal(npcBytes.Length, consumed);
        Assert.NotNull(persistence);
        Assert.Equal(new[] { 3, 7 }, persistence.ShimmeredTownNpcIndices);

        WorldTownNpc town = Assert.Single(persistence.TownNpcs);
        Assert.Equal(17, town.NetId);
        Assert.Equal("Merchant", town.GivenName);
        Assert.Equal(12.5f, town.X);
        Assert.Equal(33.25f, town.Y);
        Assert.False(town.Homeless);
        Assert.Equal(10, town.HomeTileX);
        Assert.Equal(20, town.HomeTileY);
        Assert.Equal(4, town.TownNpcVariationIndex);
        Assert.True(town.HomelessDespawn);

        Assert.Equal(new WorldPersistentNpc(488, 99.5f, 101.25f), Assert.Single(persistence.PersistentNpcs));
    }

    [Fact]
    public void Accepts_current_town_entry_without_variation_when_flag_is_clear()
    {
        byte[] npcBytes = CreateNpcBytes(writer =>
        {
            writer.Write(0);
            writer.Write(true);
            writer.Write(18);
            writer.Write("Nurse");
            writer.Write(1f);
            writer.Write(2f);
            writer.Write(true);
            writer.Write(-1);
            writer.Write(-1);
            writer.Write((byte)0);
            writer.Write(false);
            writer.Write(false);
            writer.Write(false);
        });
        byte[] file = CreateCurrentFile(npcBytes);

        Assert.Equal(
            WorldFileNpcDecodeResult.Decoded,
            WorldFileNpcDecoder.TryDecode(file, ParseEnvelope(file), DefaultOptions, out WorldNpcPersistence? persistence, out _));
        Assert.Null(Assert.Single(Assert.IsType<WorldNpcPersistence>(persistence).TownNpcs).TownNpcVariationIndex);
    }

    [Fact]
    public void Bounds_unterminated_town_sequence_by_caller_budget()
    {
        byte[] npcBytes = CreateNpcBytes(writer =>
        {
            writer.Write(0);
            writer.Write(true);
        });
        byte[] file = CreateCurrentFile(npcBytes);
        WorldFileNpcDecodeOptions options = DefaultOptions with { MaxTownNpcs = 0 };

        Assert.Equal(
            WorldFileNpcDecodeResult.TownNpcBudgetExceeded,
            WorldFileNpcDecoder.TryDecode(file, ParseEnvelope(file), options, out WorldNpcPersistence? persistence, out _));
        Assert.Null(persistence);
    }

    [Fact]
    public void Rejects_shimmer_index_outside_configured_state_table()
    {
        byte[] npcBytes = CreateNpcBytes(writer =>
        {
            writer.Write(1);
            writer.Write(128);
        });
        byte[] file = CreateCurrentFile(npcBytes);

        Assert.Equal(
            WorldFileNpcDecodeResult.InvalidShimmerIndex,
            WorldFileNpcDecoder.TryDecode(file, ParseEnvelope(file), DefaultOptions, out _, out _));
    }

    [Fact]
    public void Rejects_non_finite_npc_positions()
    {
        byte[] npcBytes = CreateNpcBytes(writer =>
        {
            writer.Write(0);
            writer.Write(false);
            writer.Write(true);
            writer.Write(1);
            writer.Write(float.NaN);
            writer.Write(10f);
        });
        byte[] file = CreateCurrentFile(npcBytes);

        Assert.Equal(
            WorldFileNpcDecodeResult.NonFinitePosition,
            WorldFileNpcDecoder.TryDecode(file, ParseEnvelope(file), DefaultOptions, out _, out _));
    }

    [Fact]
    public void Rejects_name_before_exceeding_string_allocation_budget()
    {
        byte[] npcBytes = CreateNpcBytes(writer =>
        {
            writer.Write(0);
            writer.Write(true);
            writer.Write(17);
            writer.Write(new string('x', 20));
        });
        byte[] file = CreateCurrentFile(npcBytes);
        WorldFileNpcDecodeOptions options = DefaultOptions with { MaxNameBytesPerTownNpc = 8 };

        Assert.Equal(
            WorldFileNpcDecodeResult.NameBudgetExceeded,
            WorldFileNpcDecoder.TryDecode(file, ParseEnvelope(file), options, out _, out _));
    }

    [Fact]
    public void Requires_exact_end_of_npc_section()
    {
        byte[] npcBytes = CreateNpcBytes(writer =>
        {
            writer.Write(0);
            writer.Write(false);
            writer.Write(false);
            writer.Write((byte)0x55);
        });
        byte[] file = CreateCurrentFile(npcBytes);

        Assert.Equal(
            WorldFileNpcDecodeResult.SectionLengthMismatch,
            WorldFileNpcDecoder.TryDecode(file, ParseEnvelope(file), DefaultOptions, out _, out int consumed));
        Assert.Equal(sizeof(int) + 2, consumed);
    }

    private static byte[] CreateNpcBytes(Action<BinaryWriter> write)
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

    private static byte[] CreateCurrentFile(byte[] npcBytes)
    {
        int npcEnd = NpcStart + npcBytes.Length;
        int[] pointers =
        [
            EnvelopeEnd,
            180,
            190,
            200,
            NpcStart,
            npcEnd,
            npcEnd + 8,
            npcEnd + 16,
            npcEnd + 24,
            npcEnd + 32,
            npcEnd + 40
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

        npcBytes.CopyTo(file, NpcStart);
        return file;
    }
}
