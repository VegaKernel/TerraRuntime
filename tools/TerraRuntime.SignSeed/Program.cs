using TerraRuntime.World;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: TerraRuntime.SignSeed <world.wld> <text>");
    return 2;
}

string path = Path.GetFullPath(args[0]);
string text = args[1];
byte[] sourceFile = File.ReadAllBytes(path);
WorldFileLoadLimits limits = CreateLimits();
WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? loadedWorld);
if (!diagnostic.IsLoaded || loadedWorld is null)
{
    Console.Error.WriteLine(
        $"World load failed: result={diagnostic.Result}, stage={diagnostic.Stage}, code={diagnostic.StageResultCode}.");
    return 1;
}

WorldFileData world = loadedWorld;
if (!WorldFilePreservedSections.TryCapture(sourceFile, world.Envelope, out WorldFilePreservedSections? preserved) ||
    preserved is null)
{
    Console.Error.WriteLine("Could not capture preserved world sections.");
    return 3;
}

if (!TryChooseCoordinate(world, out int x, out int y))
{
    Console.Error.WriteLine("Could not choose a unique in-bounds sign coordinate.");
    return 4;
}

WorldSign[] signs = new WorldSign[world.Signs.Length + 1];
Array.Copy(world.Signs, signs, world.Signs.Length);
short slot = checked((short)world.Signs.Length);
signs[^1] = new WorldSign(slot, text, x, y);

using var signStream = new MemoryStream();
WorldFileSignEncodeResult signResult = WorldFileSignEncoder.TryEncode(
    signs,
    world.Header.Dimensions,
    limits.MaxTextBytesPerSign,
    limits.MaxTotalSignTextBytes,
    signStream,
    out _);
if (signResult != WorldFileSignEncodeResult.Encoded)
{
    Console.Error.WriteLine($"Sign encode failed: {signResult}.");
    return 5;
}

WorldTileSaveImage tileImage;
try
{
    tileImage = CaptureImage(world.Tiles);
}
catch (InvalidOperationException error)
{
    Console.Error.WriteLine(error.Message);
    return 6;
}

using var rewritten = new MemoryStream(capacity: sourceFile.Length + checked((int)Math.Min(signStream.Length + 64, int.MaxValue)));
WorldFileTileChestRewriteResult rewriteResult = WorldFileTileChestRewriter.TryRewrite(
    world.Envelope,
    world.Header,
    preserved.Header.Span,
    preserved,
    tileImage,
    world.Chests,
    signStream.ToArray(),
    rewritten,
    out long bytesWritten);
if (rewriteResult != WorldFileTileChestRewriteResult.Rewritten)
{
    Console.Error.WriteLine($"World rewrite failed: {rewriteResult}.");
    return 7;
}

string tempPath = path + ".sign-seed.tmp";
File.WriteAllBytes(tempPath, rewritten.ToArray());
File.Move(tempPath, path, overwrite: true);

WorldFileLoadDiagnostic verifyDiagnostic = WorldFileLoader.TryLoad(
    File.ReadAllBytes(path),
    limits,
    out WorldFileData? verifiedWorld);
if (!verifyDiagnostic.IsLoaded || verifiedWorld is null)
{
    Console.Error.WriteLine(
        $"Seeded world verification failed: result={verifyDiagnostic.Result}, stage={verifyDiagnostic.Stage}, " +
        $"code={verifyDiagnostic.StageResultCode}.");
    return 8;
}

WorldSign? verified = verifiedWorld.Signs.FirstOrDefault(candidate => candidate.SlotId == slot);
if (verified is null || verified.X != x || verified.Y != y || !string.Equals(verified.Text, text, StringComparison.Ordinal))
{
    Console.Error.WriteLine("Seeded sign did not survive a full world reload.");
    return 9;
}

Console.WriteLine(
    $"seeded_sign slot={slot} x={x} y={y} text={text} bytes={bytesWritten} previousSigns={world.Signs.Length}");
return 0;

static bool TryChooseCoordinate(WorldFileData world, out int x, out int y)
{
    var occupied = new HashSet<long>(world.Signs.Select(sign => CoordinateKey(sign.X, sign.Y)));
    int width = world.Header.Dimensions.WidthTiles;
    int height = world.Header.Dimensions.HeightTiles;
    int originX = Math.Clamp(world.RuntimeMetadata.SpawnX, 0, width - 1);
    int originY = Math.Clamp(world.RuntimeMetadata.SpawnY, 0, height - 1);

    const int searchRadius = 64;
    for (int radius = 0; radius <= searchRadius; radius++)
    {
        for (int deltaY = -radius; deltaY <= radius; deltaY++)
        {
            for (int deltaX = -radius; deltaX <= radius; deltaX++)
            {
                if (Math.Max(Math.Abs(deltaX), Math.Abs(deltaY)) != radius)
                    continue;

                int candidateX = originX + deltaX;
                int candidateY = originY + deltaY;
                if ((uint)candidateX >= (uint)width || (uint)candidateY >= (uint)height)
                    continue;
                if (!occupied.Contains(CoordinateKey(candidateX, candidateY)))
                {
                    x = candidateX;
                    y = candidateY;
                    return true;
                }
            }
        }
    }

    x = 0;
    y = 0;
    return false;
}

static long CoordinateKey(int x, int y) => ((long)(uint)x << 32) | (uint)y;

static WorldTileSaveImage CaptureImage(WorldTileStore tiles)
{
    var shadow = new IncrementalWorldTileSaveShadow(tiles.Dimensions);
    for (int index = 0; index < tiles.Dimensions.SectionCount; index++)
    {
        WorldSectionId section = TerrariaSectionGeometry.FromLinearIndex(tiles.Dimensions, index);
        if (!tiles.TryCaptureSectionSnapshot(section, out WorldSectionTileSnapshot? snapshot) || snapshot is null)
            throw new InvalidOperationException($"Could not capture tile section {section} for sign seeding.");
        if (!shadow.TryApply(snapshot))
            throw new InvalidOperationException($"Could not apply tile section {section} to sign seed save image.");
    }

    if (!shadow.TryCaptureImage(out WorldTileSaveImage? image) || image is null)
        throw new InvalidOperationException("Could not capture complete tile save image for sign seeding.");
    return image;
}

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
