using TerraRuntime.World;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: TerraRuntime.WorldPatchVerify <source.wld> <patched.wld>");
    return 2;
}

string sourcePath = Path.GetFullPath(args[0]);
string patchedPath = Path.GetFullPath(args[1]);
byte[] sourceFile = File.ReadAllBytes(sourcePath);
WorldFileLoadLimits limits = CreateLimits();
WorldFileLoadDiagnostic sourceDiagnostic = WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld);
if (!sourceDiagnostic.IsLoaded || sourceWorld is null)
{
    Console.Error.WriteLine(
        $"Source world load failed: result={sourceDiagnostic.Result}, stage={sourceDiagnostic.Stage}, code={sourceDiagnostic.StageResultCode}.");
    return 3;
}

for (int index = 0; index < sourceWorld.Chests.Length; index++)
{
    if (sourceWorld.Chests[index].SlotId != index)
    {
        Console.Error.WriteLine(
            $"Source world chest slots are not canonical: index={index}, slot={sourceWorld.Chests[index].SlotId}.");
        return 4;
    }
}

var synchronizer = new WorldTileSaveShadowSynchronizer(
    sourceWorld.Tiles,
    dirtyBatchCapacity: Math.Max(1, sourceWorld.Header.Dimensions.SectionCount));
while (!synchronizer.IsBootstrapped)
{
    int captured = synchronizer.CaptureBootstrap(synchronizer.RemainingBootstrapSections);
    if (captured == 0)
    {
        Console.Error.WriteLine("Could not complete detached tile save-image bootstrap.");
        return 5;
    }
}
if (!synchronizer.TryCaptureImage(out WorldTileSaveImage? tileImage) || tileImage is null)
{
    Console.Error.WriteLine("Detached tile save image was unavailable after bootstrap.");
    return 6;
}

WorldChest[] patchedChests = (WorldChest[])sourceWorld.Chests.Clone();
int selectedChest = -1;
int selectedItemSlot = -1;
int originalStack = 0;
int patchedStack = 0;
for (int chestIndex = 0; chestIndex < patchedChests.Length && selectedChest < 0; chestIndex++)
{
    WorldChest chest = patchedChests[chestIndex];
    for (int itemSlot = 0; itemSlot < chest.Items.Length; itemSlot++)
    {
        WorldChestItem item = chest.Items[itemSlot];
        if (item.IsEmpty)
            continue;

        WorldChestItem[] items = (WorldChestItem[])chest.Items.Clone();
        originalStack = item.Stack;
        patchedStack = item.Stack < short.MaxValue ? item.Stack + 1 : item.Stack - 1;
        items[itemSlot] = new WorldChestItem(patchedStack, item.ItemType, item.Prefix);
        patchedChests[chestIndex] = new WorldChest(chest.SlotId, chest.X, chest.Y, chest.Name, items);
        selectedChest = chestIndex;
        selectedItemSlot = itemSlot;
        break;
    }
}

if (selectedChest < 0)
{
    Console.Error.WriteLine("Source world contains no non-empty chest item to mutate for persistence verification.");
    return 7;
}

Directory.CreateDirectory(Path.GetDirectoryName(patchedPath)!);
if (File.Exists(patchedPath))
    File.Delete(patchedPath);

long writtenBytes;
WorldFileTileChestPatchWriteResult patchResult;
using (var destination = new FileStream(
    patchedPath,
    FileMode.CreateNew,
    FileAccess.ReadWrite,
    FileShare.None,
    bufferSize: 64 * 1024,
    FileOptions.SequentialScan))
{
    patchResult = WorldFileTileChestPatchWriter.TryWrite(
        sourceFile,
        tileImage,
        patchedChests,
        destination,
        out writtenBytes);
}

if (patchResult != WorldFileTileChestPatchWriteResult.Written)
{
    File.Delete(patchedPath);
    Console.Error.WriteLine($"World patch write failed: result={patchResult}.");
    return 8;
}

byte[] patchedFile = File.ReadAllBytes(patchedPath);
WorldFileLoadDiagnostic patchedDiagnostic = WorldFileLoader.TryLoad(patchedFile, limits, out WorldFileData? patchedWorld);
if (!patchedDiagnostic.IsLoaded || patchedWorld is null)
{
    Console.Error.WriteLine(
        $"Patched world reload failed: result={patchedDiagnostic.Result}, stage={patchedDiagnostic.Stage}, code={patchedDiagnostic.StageResultCode}.");
    return 9;
}

WorldChest expectedChest = patchedChests[selectedChest];
WorldChest? reloadedChest = patchedWorld.Chests.FirstOrDefault(chest => chest.SlotId == expectedChest.SlotId);
if (reloadedChest is null ||
    reloadedChest.X != expectedChest.X ||
    reloadedChest.Y != expectedChest.Y ||
    selectedItemSlot >= reloadedChest.Items.Length)
{
    Console.Error.WriteLine("Patched world reload did not preserve the selected chest identity.");
    return 10;
}

WorldChestItem reloadedItem = reloadedChest.Items[selectedItemSlot];
WorldChestItem expectedItem = expectedChest.Items[selectedItemSlot];
if (reloadedItem != expectedItem)
{
    Console.Error.WriteLine(
        $"Patched chest item mismatch: expected={expectedItem}, actual={reloadedItem}.");
    return 11;
}

ReadOnlySpan<byte> sourceTail = sourceFile.AsSpan(sourceWorld.Envelope.SectionOffsets[3]);
ReadOnlySpan<byte> patchedTail = patchedFile.AsSpan(patchedWorld.Envelope.SectionOffsets[3]);
if (!sourceTail.SequenceEqual(patchedTail))
{
    Console.Error.WriteLine("World patch changed a persistence section after chests.");
    return 12;
}

Console.WriteLine(
    $"world_patch_ok bytes={writtenBytes} chest={expectedChest.SlotId} x={expectedChest.X} y={expectedChest.Y} " +
    $"itemSlot={selectedItemSlot} originalStack={originalStack} patchedStack={patchedStack} " +
    $"itemType={expectedItem.ItemType} prefix={expectedItem.Prefix} preservedTailBytes={sourceTail.Length}");
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
