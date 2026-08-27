using System.Buffers.Binary;
using System.Text;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileLoaderTests
{
    private const int EnvelopeEnd = 167;

    [Fact]
    public void Loads_all_current_sections_before_publishing_world()
    {
        byte[] file = CreateCompleteCurrentWorld();
        WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(file, CreateLimits(), out WorldFileData? world);

        Assert.True(diagnostic.IsLoaded);
        Assert.Equal(WorldFileLoadStage.Complete, diagnostic.Stage);
        WorldFileData loaded = Assert.IsType<WorldFileData>(world);
        Assert.Equal(WorldFileFormatPolicy.CurrentVersion, loaded.Envelope.FormatVersion);
        Assert.Equal("full-loader", loaded.Header.Name);
        Assert.Equal(6, loaded.Tiles.Count);
        Assert.Empty(loaded.Chests);
        Assert.Empty(loaded.Signs);
        Assert.Empty(loaded.Npcs.TownNpcs);
        Assert.Empty(loaded.Npcs.PersistentNpcs);
        Assert.Empty(loaded.TileEntities);
        Assert.Empty(loaded.PressurePlates);
        Assert.Empty(loaded.TownRooms);
        Assert.Empty(loaded.Bestiary.Kills);
        Assert.True(loaded.CreativePowers.FreezeTime);
        Assert.Equal(0.25f, loaded.CreativePowers.TimeRateSlider);
        Assert.Equal(0.75f, loaded.CreativePowers.DifficultySlider);
    }

    [Fact]
    public void Does_not_publish_partial_world_when_footer_fails_after_all_sections()
    {
        byte[] file = CreateCompleteCurrentWorld();
        file[^1] ^= 0x01;

        WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(file, CreateLimits(), out WorldFileData? world);

        Assert.Equal(WorldFileLoadResult.InvalidFooter, diagnostic.Result);
        Assert.Equal(WorldFileLoadStage.Footer, diagnostic.Stage);
        Assert.Null(world);
    }

    [Fact]
    public void Reports_late_section_failure_without_publishing_core_state()
    {
        byte[] file = CreateCompleteCurrentWorld();
        WorldFileEnvelopeParser.TryParse(file, out WorldFileEnvelope? envelope, out _);
        WorldFileEnvelope parsed = Assert.IsType<WorldFileEnvelope>(envelope);
        int creativeStart = parsed.SectionOffsets[9];
        file[creativeStart] = 0;

        WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(file, CreateLimits(), out WorldFileData? world);

        Assert.Equal(WorldFileLoadResult.InvalidCreativePowers, diagnostic.Result);
        Assert.Equal(WorldFileLoadStage.CreativePowers, diagnostic.Stage);
        Assert.Null(world);
    }

    private static WorldFileLoadLimits CreateLimits() =>
        new(
            MaxTileCount: 6,
            MaxItemsPerChest: 40,
            MaxTotalChestItems: 320_000,
            MaxTextBytesPerSign: 4_096,
            MaxTotalSignTextBytes: 64 * 1024,
            Npcs: new WorldFileNpcDecodeOptions(
                MaxShimmeredTownNpcIndices: 256,
                MaxShimmerIndexExclusive: 256,
                MaxTownNpcs: 256,
                MaxPersistentNpcs: 256,
                MaxNameBytesPerTownNpc: 256,
                MaxTotalNameBytes: 64 * 1024),
            MaxTileEntities: 1_024,
            MaxPressurePlates: 16_384,
            MaxTownRooms: VanillaWorldFormat326.NpcTypeCount,
            Bestiary: new WorldFileBestiaryLimits(
                MaxKillEntries: 2_048,
                MaxSightEntries: 2_048,
                MaxChatEntries: 2_048,
                MaxPersistentIdBytes: 256,
                MaxTotalPersistentIdBytes: 512 * 1024));

    private static byte[] CreateCompleteCurrentWorld()
    {
        const int headerStart = EnvelopeEnd;
        const int tileStart = 240;
        byte[] tiles = [0x40, 0x02, 0x40, 0x02];
        byte[] chests = CreateSection(static writer => writer.Write((short)0));
        byte[] signs = CreateSection(static writer => writer.Write((short)0));
        byte[] npcs = CreateSection(static writer =>
        {
            writer.Write(0);
            writer.Write(false);
            writer.Write(false);
        });
        byte[] tileEntities = CreateSection(static writer => writer.Write(0));
        byte[] pressurePlates = CreateSection(static writer => writer.Write(0));
        byte[] townRooms = CreateSection(static writer => writer.Write(0));
        byte[] bestiary = CreateSection(static writer =>
        {
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
        });
        byte[] creative = CreateSection(static writer =>
        {
            WriteBoolPower(writer, 0, true);
            WriteFloatPower(writer, 8, 0.25f);
            WriteBoolPower(writer, 9, false);
            WriteBoolPower(writer, 10, true);
            WriteFloatPower(writer, 12, 0.75f);
            WriteBoolPower(writer, 13, true);
            writer.Write(false);
        });
        byte[] footer = CreateSection(static writer =>
        {
            writer.Write(true);
            writer.Write("full-loader");
            writer.Write(77);
        });

        int[] pointers = new int[VanillaWorldFormat326.SectionCount];
        pointers[0] = headerStart;
        pointers[1] = tileStart;
        pointers[2] = pointers[1] + tiles.Length;
        pointers[3] = pointers[2] + chests.Length;
        pointers[4] = pointers[3] + signs.Length;
        pointers[5] = pointers[4] + npcs.Length;
        pointers[6] = pointers[5] + tileEntities.Length;
        pointers[7] = pointers[6] + pressurePlates.Length;
        pointers[8] = pointers[7] + townRooms.Length;
        pointers[9] = pointers[8] + bestiary.Length;
        pointers[10] = pointers[9] + creative.Length;

        var file = new byte[pointers[10] + footer.Length];
        WriteEnvelope(file, pointers);
        WriteHeader(file.AsSpan(headerStart, tileStart - headerStart), "full-loader", 77, width: 2, height: 3);

        tiles.CopyTo(file, pointers[1]);
        chests.CopyTo(file, pointers[2]);
        signs.CopyTo(file, pointers[3]);
        npcs.CopyTo(file, pointers[4]);
        tileEntities.CopyTo(file, pointers[5]);
        pressurePlates.CopyTo(file, pointers[6]);
        townRooms.CopyTo(file, pointers[7]);
        bestiary.CopyTo(file, pointers[8]);
        creative.CopyTo(file, pointers[9]);
        footer.CopyTo(file, pointers[10]);
        return file;
    }

    private static byte[] CreateSection(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true))
            write(writer);
        return stream.ToArray();
    }

    private static void WriteEnvelope(byte[] file, int[] pointers)
    {
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
    }

    private static void WriteHeader(Span<byte> destination, string name, int worldId, int width, int height)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true))
        {
            writer.Write(name);
            writer.Write("326");
            writer.Write(1UL);
            writer.Write(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff").ToByteArray());
            writer.Write(worldId);
            writer.Write(0);
            writer.Write(width * 16);
            writer.Write(0);
            writer.Write(height * 16);
            writer.Write(height);
            writer.Write(width);
        }

        byte[] header = stream.ToArray();
        Assert.True(header.Length <= destination.Length);
        header.CopyTo(destination);
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
}
