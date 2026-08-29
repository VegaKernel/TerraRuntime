using TerraRuntime.World;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: TerraRuntime.ChestLocate <world.wld>");
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

WorldChest? selected = null;
int nonEmptySlot = -1;
for (int chestIndex = 0; chestIndex < world.Chests.Length && selected is null; chestIndex++)
{
    WorldChest chest = world.Chests[chestIndex];
    if (chest.SlotId < 0 || chest.X is < short.MinValue or > short.MaxValue ||
        chest.Y is < short.MinValue or > short.MaxValue || chest.Items.Length > byte.MaxValue + 1)
    {
        continue;
    }

    for (int slot = 0; slot < chest.Items.Length; slot++)
    {
        if (!chest.Items[slot].IsEmpty)
        {
            selected = chest;
            nonEmptySlot = slot;
            break;
        }
    }
}

if (selected is null || nonEmptySlot < 0)
{
    Console.Error.WriteLine("Official world contains no addressable non-empty chest for the live probe.");
    return 3;
}

WorldChestItem item = selected.Items[nonEmptySlot];
Console.WriteLine(
    $"slot={selected.SlotId} x={selected.X} y={selected.Y} slots={selected.Items.Length} " +
    $"itemSlot={nonEmptySlot} itemStack={item.Stack} itemPrefix={item.Prefix} itemNetId={item.ItemType}");
return 0;

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
