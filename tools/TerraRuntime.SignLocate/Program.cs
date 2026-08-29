using System.Text;
using TerraRuntime.World;

if (args.Length != 1 && args.Length != 6)
{
    Console.Error.WriteLine(
        "Usage: TerraRuntime.SignLocate <world.wld> [--assert <sign-id> <x> <y> <text>]");
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

if (args.Length == 6)
{
    if (!string.Equals(args[1], "--assert", StringComparison.Ordinal) ||
        !short.TryParse(args[2], out short signId) ||
        !int.TryParse(args[3], out int x) ||
        !int.TryParse(args[4], out int y))
    {
        Console.Error.WriteLine("Invalid --assert arguments.");
        return 2;
    }

    string expectedText = args[5];
    WorldSign? sign = world.Signs.FirstOrDefault(candidate => candidate.SlotId == signId);
    if (sign is null)
    {
        Console.Error.WriteLine($"Persisted sign assertion failed: sign {signId} was not found.");
        return 4;
    }

    if (sign.X != x || sign.Y != y || !string.Equals(sign.Text, expectedText, StringComparison.Ordinal))
    {
        Console.Error.WriteLine(
            $"Persisted sign assertion failed: sign={signId} expected=({x},{y},{expectedText}) " +
            $"actual=({sign.X},{sign.Y},{sign.Text}).");
        return 5;
    }

    Console.WriteLine(
        $"persisted_sign_ok slot={sign.SlotId} x={sign.X} y={sign.Y} textBytes={Encoding.UTF8.GetByteCount(sign.Text)}");
    return 0;
}

WorldSign? selected = world.Signs.FirstOrDefault(candidate =>
    candidate.SlotId is >= 0 and <= short.MaxValue &&
    candidate.X is >= short.MinValue and <= short.MaxValue &&
    candidate.Y is >= short.MinValue and <= short.MaxValue);
if (selected is null)
{
    Console.Error.WriteLine("World contains no addressable sign for the live sign probe.");
    return 3;
}

Console.WriteLine(
    $"slot={selected.SlotId} x={selected.X} y={selected.Y} textBytes={Encoding.UTF8.GetByteCount(selected.Text)}");
return 0;

static WorldFileLoadLimits CreateLimits() =>
    new(
        MaxTileCount: 32_000_000,
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
