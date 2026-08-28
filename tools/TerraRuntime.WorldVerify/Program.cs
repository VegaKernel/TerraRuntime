using System.Diagnostics;
using System.IO.Compression;
using Multiplicity.Packets;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

WorldFileLoadLimits limits = CreateLimits();

if (args.Length == 2 && args[0] == "--cache-bench")
    return RunCacheBenchmark(args[1], limits);

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: TerraRuntime.WorldVerify <world.wld> | --cache-bench <world.wld>");
    return 2;
}

string path = Path.GetFullPath(args[0]);
byte[] file = File.ReadAllBytes(path);
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

int sectionWidth = Math.Min(TerrariaSectionGeometry.WidthTiles, world.Header.Dimensions.WidthTiles);
int sectionHeight = Math.Min(TerrariaSectionGeometry.HeightTiles, world.Header.Dimensions.HeightTiles);
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

WorldSectionPacketEncodeResult packetResult = WorldSectionPacketEncoder.TryEncodeTileOnly(
    world,
    xStart: 0,
    yStart: 0,
    sectionWidth,
    sectionHeight,
    out byte[] sectionFrame);
if (packetResult != WorldSectionPacketEncodeResult.Encoded ||
    !MultiplicityPacketInspector.TryReadHeader(sectionFrame, out PacketHeaderInfo sectionHeader) ||
    sectionHeader.MessageId != (byte)PacketTypes.TileSendSection ||
    sectionHeader.PacketLength != sectionFrame.Length)
{
    Console.Error.WriteLine(
        $"World section packet verification failed: result={packetResult}, frame={sectionFrame.Length} bytes.");
    return 6;
}

byte[] inflated;
try
{
    using var compressedStream = new MemoryStream(sectionFrame, 3, sectionFrame.Length - 3, writable: false);
    using var deflate = new DeflateStream(compressedStream, CompressionMode.Decompress, leaveOpen: false);
    using var inflatedStream = new MemoryStream(sectionPayload.Length);
    deflate.CopyTo(inflatedStream);
    inflated = inflatedStream.ToArray();
}
catch (InvalidDataException exception)
{
    Console.Error.WriteLine($"World section packet is not a valid raw DEFLATE stream: {exception.Message}");
    return 7;
}

if (!inflated.AsSpan().SequenceEqual(sectionPayload))
{
    Console.Error.WriteLine("World section packet failed raw-DEFLATE round-trip.");
    return 8;
}

Console.WriteLine(
    $"Verified {Path.GetFileName(path)}: version={world.Envelope.FormatVersion}, " +
    $"name={world.Header.Name}, size={world.Header.Dimensions.WidthTiles}x{world.Header.Dimensions.HeightTiles}, " +
    $"time={world.RuntimeMetadata.Time}, gameMode={world.RuntimeMetadata.GameMode}, " +
    $"tiles={world.Tiles.Count}, chests={world.Chests.Length}, signs={world.Signs.Length}, " +
    $"townNpcs={world.Npcs.TownNpcs.Length}, persistentNpcs={world.Npcs.PersistentNpcs.Length}, " +
    $"tileEntities={world.TileEntities.Length}, pressurePlates={world.PressurePlates.Length}, " +
    $"townRooms={world.TownRooms.Length}, bestiaryKills={world.Bestiary.Kills.Length}, " +
    $"worldInfoPayload={payload.Length} bytes, sectionPayload={sectionPayload.Length} bytes, " +
    $"sectionFrame={sectionFrame.Length} bytes.");
return 0;

static int RunCacheBenchmark(string worldPath, WorldFileLoadLimits limits)
{
    string path = Path.GetFullPath(worldPath);
    string cachePath = RuntimeWorldSnapshotCache.GetCachePath(path);
    if (!RuntimeWorldSnapshotCache.TryCaptureSourceStamp(path, out RuntimeWorldSourceStamp sourceStamp))
    {
        Console.Error.WriteLine($"Could not stat source world '{path}'.");
        return 20;
    }

    if (!File.Exists(cachePath))
    {
        Console.Error.WriteLine($"Runtime world cache not found: '{cachePath}'.");
        return 21;
    }

    int[] parallelisms = [1, 2, 4, 8];
    foreach (int parallelism in parallelisms)
    {
        RuntimeWorldSnapshotLoadDiagnostic warmup = RuntimeWorldSnapshotCache.TryLoad(
            cachePath,
            sourceStamp,
            limits,
            new RuntimeWorldCacheReadOptions(parallelism),
            out WorldFileData? warmWorld);
        if (!warmup.IsLoaded || warmWorld is null)
        {
            Console.Error.WriteLine(
                $"Cache benchmark warmup failed: parallel={parallelism}, result={warmup.Result}, code={warmup.DetailCode}.");
            return 22;
        }
    }

    const int rounds = 5;
    var samples = parallelisms.ToDictionary(static value => value, static _ => new List<double>(rounds));
    for (int round = 0; round < rounds; round++)
    {
        for (int offset = 0; offset < parallelisms.Length; offset++)
        {
            int parallelism = parallelisms[(round + offset) % parallelisms.Length];
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long start = Stopwatch.GetTimestamp();
            RuntimeWorldSnapshotLoadDiagnostic diagnostic = RuntimeWorldSnapshotCache.TryLoad(
                cachePath,
                sourceStamp,
                limits,
                new RuntimeWorldCacheReadOptions(parallelism),
                out WorldFileData? loaded);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
            if (!diagnostic.IsLoaded || loaded is null)
            {
                Console.Error.WriteLine(
                    $"Cache benchmark failed: parallel={parallelism}, result={diagnostic.Result}, code={diagnostic.DetailCode}.");
                return 23;
            }

            samples[parallelism].Add(elapsed.TotalMilliseconds);
        }
    }

    Console.WriteLine(
        $"cache_parallel_bench cache={Path.GetFileName(cachePath)} bytes={new FileInfo(cachePath).Length} rounds={rounds}");
    foreach (int parallelism in parallelisms)
    {
        List<double> values = samples[parallelism];
        values.Sort();
        double median = values[values.Count / 2];
        Console.WriteLine(
            $"cache_parallel_result parallel={parallelism} median_ms={median:F3} min_ms={values[0]:F3} max_ms={values[^1]:F3}");
    }

    return 0;
}

static WorldFileLoadLimits CreateLimits() =>
    new(
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
