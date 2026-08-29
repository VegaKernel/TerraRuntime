using TerraRuntime.World;

if (args.Length != 1 && args.Length != 7)
{
    Console.Error.WriteLine(
        "Usage: TerraRuntime.ChestLocate <world.wld> [--assert <chest-id> <item-slot> <stack> <prefix> <item-net-id>]");
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

if (args.Length == 7)
{
    if (!string.Equals(args[1], "--assert", StringComparison.Ordinal) ||
        !short.TryParse(args[2], out short chestId) ||
        !byte.TryParse(args[3], out byte itemSlot) ||
        !short.TryParse(args[4], out short stack) ||
        !byte.TryParse(args[5], out byte prefix) ||
        !short.TryParse(args[6], out short itemNetId))
    {
        Console.Error.WriteLine("Invalid --assert arguments.");
        return 2;
    }

    WorldChest? chest = world.Chests.FirstOrDefault(candidate => candidate.SlotId == chestId);
    if (chest is null)
    {
        Console.Error.WriteLine($"Persisted chest assertion failed: chest {chestId} was not found.");
        return 4;
    }
    if (itemSlot >= chest.Items.Length)
    {
        Console.Error.WriteLine(
            $"Persisted chest assertion failed: chest {chestId} has {chest.Items.Length} slots, requested {itemSlot}.");
        return 5;
    }

    WorldChestItem item = chest.Items[itemSlot];
    if (item.Stack != stack || item.Prefix != prefix || item.ItemType != itemNetId)
    {
        Console.Error.WriteLine(
            $"Persisted chest assertion failed: chest={chestId} itemSlot={itemSlot} " +
            $"expected=({stack},{prefix},{itemNetId}) actual=({item.Stack},{item.Prefix},{item.ItemType}).");
        return 6;
    }

    Console.WriteLine(
        $"persisted_chest_item_ok chest={chestId} itemSlot={itemSlot} " +
        $"itemStack={item.Stack} itemPrefix={item.Prefix} itemNetId={item.ItemType}");
    return 0;
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
