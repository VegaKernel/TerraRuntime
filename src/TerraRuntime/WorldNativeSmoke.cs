using System.Buffers.Binary;
using System.Text;
using TerraRuntime.World;

namespace TerraRuntime;

internal static class WorldNativeSmoke
{
    private const int EnvelopeEnd = 167;

    public static int Run()
    {
        var dimensions = new WorldDimensions(widthTiles: 421, heightTiles: 301);
        var dirty = new DirtySectionTracker(dimensions);
        if (!dirty.MarkTileDirty(420, 300))
        {
            Console.Error.WriteLine("World smoke failed while marking an edge section dirty.");
            return 18;
        }

        Span<WorldSectionId> drained = stackalloc WorldSectionId[1];
        if (dirty.Drain(drained) != 1 || drained[0] != new WorldSectionId(2, 2) || dirty.DirtyCount != 0)
        {
            Console.Error.WriteLine("World smoke failed while draining the dirty-section tracker.");
            return 19;
        }

        byte[] file = CreateCompleteCurrentWorld();
        WorldFileLoadLimits limits = CreateLimits();
        WorldFileLoadDiagnostic load = WorldFileLoader.TryLoad(file, limits, out WorldFileData? world);
        if (!load.IsLoaded ||
            world is null ||
            world.Envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion ||
            world.Envelope.Compatibility != WorldFormatCompatibility.Verified ||
            world.Header.Name != "native-smoke" ||
            world.Header.Dimensions.WidthTiles != 2 ||
            world.Header.Dimensions.HeightTiles != 3 ||
            world.RuntimeMetadata.Time != 13500 ||
            !world.RuntimeMetadata.DayTime ||
            world.RuntimeMetadata.TreeTopVariations.Length != 13 ||
            world.Tiles.Count != 6 ||
            world.Chests.Length != 0 ||
            world.Signs.Length != 0 ||
            world.Npcs.TownNpcs.Length != 0 ||
            world.Npcs.PersistentNpcs.Length != 0 ||
            world.TileEntities.Length != 0 ||
            world.PressurePlates.Length != 0 ||
            world.TownRooms.Length != 0 ||
            world.Bestiary.Kills.Length != 0 ||
            !world.CreativePowers.FreezeTime ||
            world.CreativePowers.TimeRateSlider != 0.25f ||
            world.CreativePowers.DifficultySlider != 0.75f)
        {
            Console.Error.WriteLine(
                $"World smoke failed during complete current .wld load: result={load.Result}, " +
                $"stage={load.Stage}, code={load.StageResultCode}.");
            return 20;
        }

        WorldFileLoadLimits tooSmall = limits with { MaxTileCount = 5 };
        WorldFileLoadDiagnostic rejectedLoad = WorldFileLoader.TryLoad(file, tooSmall, out WorldFileData? rejected);
        if (rejectedLoad.Result != WorldFileLoadResult.InvalidTiles ||
            rejectedLoad.Stage != WorldFileLoadStage.Tiles ||
            rejected is not null)
        {
            Console.Error.WriteLine("World smoke failed while enforcing transactional pre-allocation tile budget.");
            return 21;
        }

        Console.WriteLine(
            $"World smoke passed: sections={dimensions.SectionCount}, dirtySection={drained[0]}, " +
            $"wldVersion={world.Envelope.FormatVersion}, world={world.Header.Name}, tiles={world.Tiles.Count}, " +
            "full current header metadata, all persistence sections and footer decoded transactionally.");
        return 0;
    }

    private static WorldFileLoadLimits CreateLimits() =>
        new(
            MaxTileCount: 6,
            MaxItemsPerChest: 40,
            MaxTotalChestItems: 100,
            MaxTextBytesPerSign: 256,
            MaxTotalSignTextBytes: 1_024,
            Npcs: new WorldFileNpcDecodeOptions(
                MaxShimmeredTownNpcIndices: 32,
                MaxShimmerIndexExclusive: 256,
                MaxTownNpcs: 32,
                MaxPersistentNpcs: 32,
                MaxNameBytesPerTownNpc: 128,
                MaxTotalNameBytes: 1_024),
            MaxTileEntities: 32,
            MaxPressurePlates: 32,
            MaxTownRooms: 32,
            Bestiary: new WorldFileBestiaryLimits(
                MaxKillEntries: 32,
                MaxSightEntries: 32,
                MaxChatEntries: 32,
                MaxPersistentIdBytes: 128,
                MaxTotalPersistentIdBytes: 4_096),
            RuntimeMetadata: new WorldFileRuntimeMetadataLimits(
                MaxStringBytes: 256,
                MaxTotalStringBytes: 4_096,
                MaxAnglerNames: 32,
                MaxBannerEntries: 512,
                MaxPartyNpcEntries: 32,
                MaxManifestBytes: 1_024));

    private static byte[] CreateCompleteCurrentWorld()
    {
        const int headerStart = EnvelopeEnd;
        byte[] header = CreateHeader();
        int tileStart = headerStart + header.Length;
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
            writer.Write("native-smoke");
            writer.Write(7);
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
        header.CopyTo(file, headerStart);
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
        if (offset != EnvelopeEnd)
            throw new InvalidOperationException("Current .wld smoke envelope size drifted from verified 1.4.5.8 layout.");
    }

    private static byte[] CreateHeader()
    {
        return CreateSection(static writer =>
        {
            writer.Write("native-smoke");
            writer.Write("326");
            writer.Write(1UL);
            writer.Write(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff").ToByteArray());
            writer.Write(7);
            writer.Write(0);
            writer.Write(32);
            writer.Write(0);
            writer.Write(48);
            writer.Write(3);
            writer.Write(2);

            writer.Write(0);
            WriteFalseBooleans(writer, 9);
            writer.Write(0L);
            writer.Write(0L);
            writer.Write((byte)0);
            WriteInt32Zeros(writer, 3);
            WriteInt32Zeros(writer, 4);
            WriteInt32Zeros(writer, 3);
            WriteInt32Zeros(writer, 4);
            WriteInt32Zeros(writer, 3);
            writer.Write(1);
            writer.Write(1);
            writer.Write(1d);
            writer.Write(2d);
            writer.Write(13500d);
            writer.Write(true);
            writer.Write(0);
            writer.Write(false);
            writer.Write(false);
            writer.Write(1);
            writer.Write(2);
            WriteFalseBooleans(writer, 21);
            writer.Write((byte)0);
            writer.Write(0);
            writer.Write(false);
            writer.Write(false);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0d);
            writer.Write(0d);
            writer.Write((byte)0);
            writer.Write(false);
            writer.Write(0);
            writer.Write(0f);
            WriteInt32Zeros(writer, 3);
            WriteByteZeros(writer, 8);
            writer.Write(0);
            writer.Write((short)0);
            writer.Write(0f);
            writer.Write(0);
            writer.Write(false);
            writer.Write(0);
            WriteFalseBooleans(writer, 3);
            writer.Write(0);
            writer.Write(0);
            writer.Write((short)0);
            writer.Write((short)0);
            WriteFalseBooleans(writer, 19);
            writer.Write(false);
            writer.Write(false);
            writer.Write(0);
            writer.Write(0);
            writer.Write(false);
            writer.Write(0);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(false);
            WriteFalseBooleans(writer, 3);
            WriteByteZeros(writer, 5);
            writer.Write(false);
            writer.Write(0);
            WriteFalseBooleans(writer, 3);
            writer.Write(13);
            WriteInt32Zeros(writer, 13);
            WriteFalseBooleans(writer, 2);
            WriteInt32Zeros(writer, 4);
            WriteFalseBooleans(writer, 25);
            writer.Write((byte)0);
            WriteFalseBooleans(writer, 4);
            writer.Write(0);
            writer.Write(0);
            writer.Write(false);
            writer.Write((byte)0);
            WriteFalseBooleans(writer, 3);
            writer.Write("{}");
        });
    }

    private static void WriteFalseBooleans(BinaryWriter writer, int count)
    {
        for (int i = 0; i < count; i++)
            writer.Write(false);
    }

    private static void WriteInt32Zeros(BinaryWriter writer, int count)
    {
        for (int i = 0; i < count; i++)
            writer.Write(0);
    }

    private static void WriteByteZeros(BinaryWriter writer, int count)
    {
        for (int i = 0; i < count; i++)
            writer.Write((byte)0);
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
