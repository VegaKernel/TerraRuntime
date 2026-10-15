using System.Text;
using System.Buffers.Binary;
using TerraRuntime.World;
namespace TerraRuntime.Tests.DurableWorldChestCustody;

internal static class WorldFixture
{
private const int EnvelopeEnd=167;
    internal static WorldFileLoadLimits CreateLimits() =>
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
                MaxTotalPersistentIdBytes: 512 * 1024),
            RuntimeMetadata: new WorldFileRuntimeMetadataLimits(
                MaxStringBytes: 4_096,
                MaxTotalStringBytes: 64 * 1024,
                MaxAnglerNames: 256,
                MaxBannerEntries: 1_024,
                MaxPartyNpcEntries: 256,
                MaxManifestBytes: 16 * 1024));

    internal static byte[] CreateCompleteCurrentWorld(int width = 2, int height = 3)
    {
        const int headerStart = EnvelopeEnd;
        byte[] header = CreateHeader("full-loader", 77, width, height);
        int tileStart = headerStart + header.Length;
        using var tileStream = new MemoryStream();
        var tileStore = new WorldTileStore(new WorldDimensions(width, height));
        if (WorldFileTileEncoder.TryEncode(tileStore, VanillaWorldFormat326.TileTypeCount,
                new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3], tileStream, out _) != WorldFileTileEncodeResult.Encoded)
            throw new InvalidOperationException("fixture tile encoding");
        byte[] tiles = tileStream.ToArray();
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
        if (EnvelopeEnd != offset) throw new InvalidOperationException("fixture envelope position");
    }

    private static byte[] CreateHeader(string name, int worldId, int width, int height)
    {
        return CreateSection(writer =>
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

            writer.Write(0); // GameMode
            WriteFalseBooleans(writer, 9); // world seed flags through skyblock
            writer.Write(0L); // creation time
            writer.Write(0L); // last played
            writer.Write((byte)0); // moon type
            WriteInt32Zeros(writer, 3); // tree X
            WriteInt32Zeros(writer, 4); // tree styles
            WriteInt32Zeros(writer, 3); // cave background X
            WriteInt32Zeros(writer, 4); // cave background styles
            WriteInt32Zeros(writer, 3); // ice/jungle/hell background styles
            writer.Write(1); // spawn X
            writer.Write(1); // spawn Y
            writer.Write(1d); // world surface
            writer.Write(2d); // rock layer
            writer.Write(13500d); // time
            writer.Write(true); // day time
            writer.Write(0); // moon phase
            writer.Write(false); // blood moon
            writer.Write(false); // eclipse
            writer.Write(1); // dungeon X
            writer.Write(2); // dungeon Y

            WriteFalseBooleans(writer, 21); // crimson through spawnMeteor
            writer.Write((byte)0); // shadow orb count
            writer.Write(0); // altar count
            writer.Write(false); // hard mode
            writer.Write(false); // after party of doom
            writer.Write(0); // invasion delay
            writer.Write(0); // invasion size
            writer.Write(0); // invasion type
            writer.Write(0d); // invasion X
            writer.Write(0d); // slime rain time
            writer.Write((byte)0); // sundial cooldown
            writer.Write(false); // raining
            writer.Write(0); // rain time
            writer.Write(0f); // max rain
            WriteInt32Zeros(writer, 3); // cobalt/mythril/adamantite
            WriteByteZeros(writer, 8); // tree/corruption/jungle/snow/hallow/crimson/desert/ocean backgrounds
            writer.Write(0); // cloud background active
            writer.Write((short)0); // cloud count
            writer.Write(0f); // wind
            writer.Write(0); // angler names
            writer.Write(false); // saved angler
            writer.Write(0); // angler quest
            WriteFalseBooleans(writer, 3); // stylist/tax collector/golfer
            writer.Write(0); // invasion size start
            writer.Write(0); // cultist delay
            writer.Write((short)0); // banner kill count length
            writer.Write((short)0); // banner claim count length

            WriteFalseBooleans(writer, 19); // fast dawn through lunar apocalypse
            writer.Write(false); // party manual
            writer.Write(false); // party genuine
            writer.Write(0); // party cooldown
            writer.Write(0); // celebrating NPC count
            writer.Write(false); // sandstorm happening
            writer.Write(0); // sandstorm time left
            writer.Write(0f); // sandstorm severity
            writer.Write(0f); // sandstorm intended severity
            writer.Write(false); // saved bartender
            WriteFalseBooleans(writer, 3); // DD2 downed tiers
            WriteByteZeros(writer, 5); // mushroom/underworld/treeBG2/treeBG3/treeBG4
            writer.Write(false); // combat book
            writer.Write(0); // lantern cooldown
            WriteFalseBooleans(writer, 3); // lantern genuine/manual/next
            writer.Write(13); // tree top variation count
            WriteInt32Zeros(writer, 13);
            WriteFalseBooleans(writer, 2); // force Halloween/XMas today
            WriteInt32Zeros(writer, 4); // copper/iron/silver/gold
            WriteFalseBooleans(writer, 25); // pets, progression unlocks and fast-forward dusk
            writer.Write((byte)0); // moondial cooldown
            WriteFalseBooleans(writer, 4); // force seasons forever, vampire, infected
            writer.Write(0); // meteor shower count
            writer.Write(0); // coin rain
            writer.Write(false); // team-based spawns seed
            writer.Write((byte)0); // extra spawn count
            WriteFalseBooleans(writer, 3); // dual dungeons, more lightning, no lightning
            writer.Write("{}"); // world manifest
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
