using TerraRuntime.World;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: TerraRuntime.TileLocate <world.wld>");
    return 2;
}

string path = Path.GetFullPath(args[0]);
byte[] file = File.ReadAllBytes(path);
WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(file, CreateLimits(), out WorldFileData? world);
if (!diagnostic.IsLoaded || world is null)
{
    Console.Error.WriteLine(
        $"World load failed: result={diagnostic.Result}, stage={diagnostic.Stage}, code={diagnostic.StageResultCode}.");
    return 1;
}

const int margin = 4;
WorldTileStore tiles = world.Tiles;
for (int y = margin; y < tiles.Dimensions.HeightTiles - margin; y++)
{
    for (int x = margin; x < tiles.Dimensions.WidthTiles - margin; x++)
    {
        bool inactiveRing = true;
        for (int offsetY = -1; offsetY <= 1 && inactiveRing; offsetY++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (offsetX == 0 && offsetY == 0)
                    continue;

                if (tiles.Get(x + offsetX, y + offsetY).IsActive)
                {
                    inactiveRing = false;
                    break;
                }
            }
        }

        if (!inactiveRing || !VanillaDirtPlacement.TryPlaceOnEmpty(tiles, x, y))
            continue;

        if (!VanillaDirtPlacement.TryKillIsolatedWithoutDrop(tiles, x, y))
        {
            Console.Error.WriteLine(
                $"Dirt locator invariant failed after placing an isolated canonical Dirt tile at ({x},{y}).");
            return 4;
        }

        Console.WriteLine($"x={x} y={y}");
        return 0;
    }
}

Console.Error.WriteLine("Official world contains no packet17-safe empty tile with an inactive eight-neighbor ring.");
return 3;

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
