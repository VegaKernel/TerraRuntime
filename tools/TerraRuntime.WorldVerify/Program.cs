using Multiplicity.Packets;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: TerraRuntime.WorldVerify <world.wld>");
    return 2;
}

string path = Path.GetFullPath(args[0]);
byte[] file = File.ReadAllBytes(path);
var limits = new WorldFileLoadLimits(
    MaxTileCount: 6_000_000,
    MaxItemsPerChest: 100,
    MaxTotalChestItems: 1_000_000,
    MaxTextBytesPerSign: 64 * 1024,
    MaxTotalSignTextBytes: 64L * 1024 * 1024,
    Npcs: new WorldFileNpcDecodeOptions(
        MaxShimmeredTownNpcIndices: 1_024,
        MaxShimmerIndexExclusive: 1_024,
        MaxTownNpcs: 1_024,
        MaxPersistentNpcs: 4_096,
        MaxNameBytesPerTownNpc: 4 * 1024,
        MaxTotalNameBytes: 4L * 1024 * 1024),
    MaxTileEntities: 100_000,
    MaxPressurePlates: 1_000_000,
    MaxTownRooms: VanillaWorldFormat326.NpcTypeCount,
    Bestiary: new WorldFileBestiaryLimits(
        MaxKillEntries: 100_000,
        MaxSightEntries: 100_000,
        MaxChatEntries: 100_000,
        MaxPersistentIdBytes: 4 * 1024,
        MaxTotalPersistentIdBytes: 64L * 1024 * 1024),
    RuntimeMetadata: new WorldFileRuntimeMetadataLimits(
        MaxStringBytes: 64 * 1024,
        MaxTotalStringBytes: 64L * 1024 * 1024,
        MaxAnglerNames: 4_096,
        MaxBannerEntries: 8_192,
        MaxPartyNpcEntries: 4_096,
        MaxManifestBytes: 4 * 1024 * 1024));

WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(file, limits, out WorldFileData? world);
if (!diagnostic.IsLoaded || world is null)
{
    Console.Error.WriteLine(
        $"World verification failed: result={diagnostic.Result}, stage={diagnostic.Stage}, code={diagnostic.StageResultCode}.");
    return 1;
}

WorldInfo worldInfo = WorldInfoPacketMapper.Create(world);
using var payloadStream = new MemoryStream();
worldInfo.ToStream(payloadStream, includeHeader: false);
byte[] payload = payloadStream.ToArray();
WorldInfo parsedWorldInfo;
try
{
    parsedWorldInfo = (WorldInfo)TerrariaPacket.DeserializePayload(PacketTypes.WorldInfo, payload);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"WorldInfo verification failed during protocol 326 round-trip: {exception}");
    return 3;
}

if (parsedWorldInfo.WorldName != world.Header.Name ||
    parsedWorldInfo.WorldId != world.Header.WorldId ||
    parsedWorldInfo.WorldGeneratorVersion != world.Header.WorldGeneratorVersion ||
    parsedWorldInfo.MaxTilesX != world.Header.Dimensions.WidthTiles ||
    parsedWorldInfo.MaxTilesY != world.Header.Dimensions.HeightTiles ||
    !parsedWorldInfo.WorldUniqueId.AsSpan().SequenceEqual(world.Header.UniqueId.ToByteArray()) ||
    parsedWorldInfo.DungeonX != world.RuntimeMetadata.DungeonX ||
    parsedWorldInfo.DungeonY != world.RuntimeMetadata.DungeonY ||
    parsedWorldInfo.ExtraSpawnPoints.Length != world.RuntimeMetadata.ExtraSpawnPoints.Length ||
    parsedWorldInfo.TreeTopVariations.Length != 13 ||
    parsedWorldInfo.TrailingDataMemory.Length != 0)
{
    Console.Error.WriteLine(
        "WorldInfo verification failed: loaded .wld state did not survive the protocol 326 packet-7 round-trip.");
    return 4;
}

int sectionWidth = Math.Min(WorldSectionGeometry.SectionWidthTiles, world.Header.Dimensions.WidthTiles);
int sectionHeight = Math.Min(WorldSectionGeometry.SectionHeightTiles, world.Header.Dimensions.HeightTiles);
WorldSectionPayloadEncodeResult sectionResult = WorldSectionPayloadEncoder.TryEncodeTileOnly(
    world,
    xStart: 0,
    yStart: 0,
    sectionWidth,
    sectionHeight,
    out byte[] sectionPayload);
if (sectionResult != WorldSectionPayloadEncodeResult.Encoded || sectionPayload.Length <= 18)
{
    Console.Error.WriteLine(
        $"World section verification failed: result={sectionResult}, payload={sectionPayload.Length} bytes.");
    return 5;
}

Console.WriteLine(
    $"Verified {Path.GetFileName(path)}: version={world.Envelope.FormatVersion}, " +
    $"name={world.Header.Name}, size={world.Header.Dimensions.WidthTiles}x{world.Header.Dimensions.HeightTiles}, " +
    $"time={world.RuntimeMetadata.Time}, gameMode={world.RuntimeMetadata.GameMode}, " +
    $"tiles={world.Tiles.Count}, chests={world.Chests.Length}, signs={world.Signs.Length}, " +
    $"townNpcs={world.Npcs.TownNpcs.Length}, persistentNpcs={world.Npcs.PersistentNpcs.Length}, " +
    $"tileEntities={world.TileEntities.Length}, pressurePlates={world.PressurePlates.Length}, " +
    $"townRooms={world.TownRooms.Length}, bestiaryKills={world.Bestiary.Kills.Length}, " +
    $"worldInfoPayload={payload.Length} bytes, sectionPayload={sectionPayload.Length} bytes.");
return 0;
