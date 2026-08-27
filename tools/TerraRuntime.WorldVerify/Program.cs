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
        MaxTotalPersistentIdBytes: 64L * 1024 * 1024));

WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(file, limits, out WorldFileData? world);
if (!diagnostic.IsLoaded || world is null)
{
    Console.Error.WriteLine(
        $"World verification failed: result={diagnostic.Result}, stage={diagnostic.Stage}, code={diagnostic.StageResultCode}.");
    return 1;
}

Console.WriteLine(
    $"Verified {Path.GetFileName(path)}: version={world.Envelope.FormatVersion}, " +
    $"name={world.Header.Name}, size={world.Header.Dimensions.WidthTiles}x{world.Header.Dimensions.HeightTiles}, " +
    $"tiles={world.Tiles.Count}, chests={world.Chests.Length}, signs={world.Signs.Length}, " +
    $"townNpcs={world.Npcs.TownNpcs.Length}, persistentNpcs={world.Npcs.PersistentNpcs.Length}, " +
    $"tileEntities={world.TileEntities.Length}, pressurePlates={world.PressurePlates.Length}, " +
    $"townRooms={world.TownRooms.Length}, bestiaryKills={world.Bestiary.Kills.Length}.");
return 0;
