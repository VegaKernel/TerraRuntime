using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

bool seedDirt = args.Length == 2 && args[1] == "--seed-dirt";
if (args.Length != 1 && !seedDirt)
{
    Console.Error.WriteLine("Usage: TerraRuntime.TileLocate <world.wld> [--seed-dirt]");
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

// Player.BordersMovement keeps ordinary players at least 640 pixels from the world edges.
// Keep the candidate and the probe player's 20x42 body strictly inside that band.
const int margin = 45;
WorldTileStore tiles = world.Tiles;
var mutations = new VanillaWorldTileMutationService(tiles);
for (int y = margin; y < tiles.Dimensions.HeightTiles - margin; y++)
{
    for (int x = margin; x < tiles.Dimensions.WidthTiles - margin; x++)
    {
        bool inactiveRing = true;
        for (int offsetY = seedDirt ? -3 : -1; offsetY <= (seedDirt ? 3 : 1) && inactiveRing; offsetY++)
        {
            for (int offsetX = seedDirt ? -3 : -1; offsetX <= (seedDirt ? 3 : 1); offsetX++)
            {
                if (offsetX == 0 && offsetY == 0)
                    continue;

                WorldTile neighbor = tiles.Get(x + offsetX, y + offsetY);
                if (neighbor.IsActive || seedDirt && neighbor.LiquidAmount != 0)
                {
                    inactiveRing = false;
                    break;
                }
            }
        }

        if (!inactiveRing || !VanillaDirtRules1458.CanPlaceOnEmpty(tiles, x, y) ||
            seedDirt && !VanillaDirtRules1458.CanPlaceOnEmpty(tiles, x, y + 1))
            continue;

        if (!mutations.Apply(new WorldTileMutationRequest(WorldTileMutationKind.PlaceTile, x, y, VanillaTileIds.Dirt)).Applied ||
            !mutations.Apply(new WorldTileMutationRequest(WorldTileMutationKind.KillTile, x, y)).Applied)
        {
            Console.Error.WriteLine(
                $"Dirt locator invariant failed after placing an isolated canonical Dirt tile at ({x},{y}).");
            return 4;
        }

        if (seedDirt)
        {
            if (Directory.Exists(path + ".custody"))
                throw new InvalidOperationException("Seed only an unmanaged CI fixture before explicit custody import.");
            if (!mutations.Apply(new WorldTileMutationRequest(WorldTileMutationKind.PlaceTile, x, y, VanillaTileIds.Dirt)).Applied ||
                !WorldFilePreservedSections.TryCapture(file, world.Envelope, out WorldFilePreservedSections? preserved))
                throw new InvalidOperationException("Could not prepare the CI Dirt fixture.");
            // Local ItemCheck requires a neighboring anchor when replacing
            // the mined Dirt. Keep the player's body outside both seeded cells.
            WorldTile floor = tiles.Get(x, y + 1);
            floor.Type = checked((ushort)VanillaTileIds.Stone.Value);
            floor.Flags |= WorldTileFlags.Active;
            tiles.Set(x, y + 1, in floor);
            var shadow = new WorldTileSaveShadowSynchronizer(tiles, 1);
            while (!shadow.IsBootstrapped) shadow.CaptureBootstrap(1);
            if (!shadow.TryCaptureImage(out WorldTileSaveImage? image))
                throw new InvalidOperationException("Could not capture the CI Dirt fixture.");
            using var rewritten = new MemoryStream();
            if (WorldFileTileChestRewriter.TryRewrite(world.Envelope, world.Header, preserved!, image!, world.Chests,
                    rewritten, out _) != WorldFileTileChestRewriteResult.Rewritten)
                throw new InvalidOperationException("Could not rewrite the CI Dirt fixture.");
            string temporary = path + ".dirt-seed.tmp";
            File.WriteAllBytes(temporary, rewritten.ToArray());
            File.Move(temporary, path, overwrite: true);
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
