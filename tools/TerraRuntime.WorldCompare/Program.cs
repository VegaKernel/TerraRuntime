using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;

if (args.Length < 2)
{
    Console.Error.WriteLine(
        "Usage: TerraRuntime.WorldCompare <reference.wld> <candidate.wld> [--json <report.json>] [--enforce]");
    return 2;
}

string referencePath = Path.GetFullPath(args[0]);
string candidatePath = Path.GetFullPath(args[1]);
string? jsonPath = null;
bool enforce = false;
for (int i = 2; i < args.Length; i++)
{
    if (args[i] == "--enforce")
    {
        if (enforce)
        {
            Console.Error.WriteLine("Duplicate --enforce option.");
            return 2;
        }

        enforce = true;
        continue;
    }

    if (args[i] == "--json")
    {
        if (jsonPath is not null)
        {
            Console.Error.WriteLine("Duplicate --json option.");
            return 2;
        }

        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Option --json requires a report path.");
            return 2;
        }

        jsonPath = Path.GetFullPath(args[++i]);
        continue;
    }

    Console.Error.WriteLine($"Unknown or incomplete argument '{args[i]}'.");
    return 2;
}

WorldFileLoadLimits limits = CreateLimits();
if (!TryLoad(referencePath, limits, out WorldFileData? reference))
    return 3;
if (!TryLoad(candidatePath, limits, out WorldFileData? candidate))
    return 4;

WorldStructuralFingerprint referenceFingerprint = CaptureFingerprint(reference!);
WorldStructuralFingerprint candidateFingerprint = CaptureFingerprint(candidate!);
WorldStructuralComparison comparison = Compare(referenceFingerprint, candidateFingerprint);
var report = new WorldReferenceComparisonReport(
    ReferencePath: Path.GetFileName(referencePath),
    CandidatePath: Path.GetFileName(candidatePath),
    Reference: referenceFingerprint,
    Candidate: candidateFingerprint,
    Comparison: comparison);

string json = JsonSerializer.Serialize(
    report,
    new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    });

if (jsonPath is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
    File.WriteAllText(jsonPath, json + Environment.NewLine);
}

Console.WriteLine(
    $"reference_world_compare size={candidateFingerprint.Width}x{candidateFingerprint.Height} " +
    $"tile_l1={comparison.ActiveTileHistogramL1:F6} wall_l1={comparison.WallHistogramL1:F6} " +
    $"active_ratio={comparison.ActiveTileRatio:F6} liquid_ratio={comparison.TotalLiquidRatio:F6} " +
    $"silhouette_nmae={comparison.TerrainSilhouette.NormalizedMeanAbsoluteError:F6} " +
    $"silhouette_p95={comparison.TerrainSilhouette.NormalizedPercentile95AbsoluteError:F6} " +
    $"silhouette_corr={comparison.TerrainSilhouette.Correlation:F6} " +
    $"spawn_delta=({comparison.SpawnDeltaX},{comparison.SpawnDeltaY}) " +
    $"dungeon_delta=({comparison.DungeonDeltaX},{comparison.DungeonDeltaY}) " +
    $"surface_delta={comparison.WorldSurfaceDelta} rock_delta={comparison.RockLayerDelta} " +
    $"chests={referenceFingerprint.ChestCount}->{candidateFingerprint.ChestCount} " +
    $"town_npcs={referenceFingerprint.TownNpcCount}->{candidateFingerprint.TownNpcCount}.");
Console.WriteLine(
    $"reference_sha256={referenceFingerprint.StructuralSha256} candidate_sha256={candidateFingerprint.StructuralSha256}");

if (!enforce)
    return 0;

List<string> violations = EvaluateBudgets(referenceFingerprint, candidateFingerprint, comparison);
if (violations.Count == 0)
{
    Console.WriteLine("Reference-world structural budgets passed.");
    return 0;
}

Console.Error.WriteLine("Reference-world structural budgets failed:");
foreach (string violation in violations)
    Console.Error.WriteLine($"- {violation}");
return 5;

static bool TryLoad(string path, WorldFileLoadLimits limits, out WorldFileData? world)
{
    world = null;
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"World file not found: '{path}'.");
        return false;
    }

    byte[] file = File.ReadAllBytes(path);
    WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(file, limits, out world);
    if (diagnostic.IsLoaded && world is not null)
        return true;

    Console.Error.WriteLine(
        $"World load failed for '{path}': result={diagnostic.Result}, stage={diagnostic.Stage}, code={diagnostic.StageResultCode}.");
    world = null;
    return false;
}

static WorldStructuralFingerprint CaptureFingerprint(WorldFileData world)
{
    WorldTileStore tiles = world.Tiles;
    int width = tiles.Dimensions.WidthTiles;
    int height = tiles.Dimensions.HeightTiles;
    var tileHistogram = new Dictionary<int, long>();
    var wallHistogram = new Dictionary<int, long>();
    var liquidHistogram = new Dictionary<string, long>(StringComparer.Ordinal);
    long activeTiles = 0;
    long nonZeroWalls = 0;
    long totalLiquid = 0;

    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    Span<byte> encoded = stackalloc byte[14];

    for (int x = 0; x < width; x++)
    {
        for (int y = 0; y < height; y++)
        {
            WorldTile tile = tiles.Get(x, y);
            if (tile.IsActive)
            {
                activeTiles++;
                Increment(tileHistogram, tile.Type, 1);
            }
            if (tile.Wall != 0)
            {
                nonZeroWalls++;
                Increment(wallHistogram, tile.Wall, 1);
            }
            if (tile.LiquidAmount != 0)
            {
                totalLiquid += tile.LiquidAmount;
                string key = tile.LiquidKind.ToString();
                liquidHistogram.TryGetValue(key, out long current);
                liquidHistogram[key] = current + tile.LiquidAmount;
            }

            BinaryPrimitives.WriteUInt16LittleEndian(encoded[0..2], tile.Type);
            BinaryPrimitives.WriteUInt16LittleEndian(encoded[2..4], tile.Wall);
            BinaryPrimitives.WriteInt16LittleEndian(encoded[4..6], tile.FrameX);
            BinaryPrimitives.WriteInt16LittleEndian(encoded[6..8], tile.FrameY);
            BinaryPrimitives.WriteUInt16LittleEndian(encoded[8..10], (ushort)tile.Flags);
            encoded[10] = tile.LiquidAmount;
            encoded[11] = tile.Shape;
            encoded[12] = (byte)tile.LiquidKind;
            encoded[13] = tile.TileColor;
            hash.AppendData(encoded);
        }
    }

    WorldFileRuntimeMetadata metadata = world.RuntimeMetadata;
    return new WorldStructuralFingerprint(
        FormatVersion: world.Envelope.FormatVersion,
        Width: width,
        Height: height,
        SpawnX: metadata.SpawnX,
        SpawnY: metadata.SpawnY,
        DungeonX: metadata.DungeonX,
        DungeonY: metadata.DungeonY,
        WorldSurface: metadata.WorldSurface,
        RockLayer: metadata.RockLayer,
        Crimson: metadata.Crimson,
        ActiveTiles: activeTiles,
        NonZeroWalls: nonZeroWalls,
        TotalLiquid: totalLiquid,
        TileHistogram: tileHistogram,
        WallHistogram: wallHistogram,
        LiquidHistogram: liquidHistogram,
        ChestCount: world.Chests.Length,
        SignCount: world.Signs.Length,
        TownNpcCount: world.Npcs.TownNpcs.Length,
        PersistentNpcCount: world.Npcs.PersistentNpcs.Length,
        TileEntityCount: world.TileEntities.Length,
        TerrainSilhouette: VanillaTerrainSilhouetteAnalyzer1458.Capture(tiles),
        StructuralSha256: Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
}

static WorldStructuralComparison Compare(
    WorldStructuralFingerprint reference,
    WorldStructuralFingerprint candidate)
{
    return new WorldStructuralComparison(
        ActiveTileHistogramL1: HistogramDistance(reference.TileHistogram, candidate.TileHistogram),
        WallHistogramL1: HistogramDistance(reference.WallHistogram, candidate.WallHistogram),
        ActiveTileRatio: Ratio(candidate.ActiveTiles, reference.ActiveTiles),
        NonZeroWallRatio: Ratio(candidate.NonZeroWalls, reference.NonZeroWalls),
        TotalLiquidRatio: Ratio(candidate.TotalLiquid, reference.TotalLiquid),
        SpawnDeltaX: candidate.SpawnX - reference.SpawnX,
        SpawnDeltaY: candidate.SpawnY - reference.SpawnY,
        DungeonDeltaX: candidate.DungeonX - reference.DungeonX,
        DungeonDeltaY: candidate.DungeonY - reference.DungeonY,
        WorldSurfaceDelta: candidate.WorldSurface - reference.WorldSurface,
        RockLayerDelta: candidate.RockLayer - reference.RockLayer,
        TerrainSilhouette: VanillaTerrainSilhouetteAnalyzer1458.Compare(
            reference.TerrainSilhouette,
            candidate.TerrainSilhouette),
        SameDungeonSide: Math.Sign(candidate.DungeonX - candidate.Width / 2) ==
                         Math.Sign(reference.DungeonX - reference.Width / 2));
}

static List<string> EvaluateBudgets(
    WorldStructuralFingerprint reference,
    WorldStructuralFingerprint candidate,
    WorldStructuralComparison comparison)
{
    var violations = new List<string>();
    if (reference.FormatVersion != 326 || candidate.FormatVersion != 326)
        violations.Add($"Expected Terraria 1.4.5.8 world format 326, got {reference.FormatVersion}/{candidate.FormatVersion}.");
    if (reference.Width != candidate.Width || reference.Height != candidate.Height)
        violations.Add($"World dimensions differ: {reference.Width}x{reference.Height} vs {candidate.Width}x{candidate.Height}.");
    if (!comparison.SameDungeonSide)
        violations.Add("Dungeon was generated on the opposite side of the world for the same seed.");
    if (comparison.ActiveTileRatio is < 0.55 or > 1.45)
        violations.Add($"Active-tile ratio {comparison.ActiveTileRatio:F4} is outside [0.55, 1.45].");
    if (comparison.NonZeroWallRatio is < 0.20 or > 3.00)
        violations.Add($"Wall-cell ratio {comparison.NonZeroWallRatio:F4} is outside [0.20, 3.00].");
    if (comparison.TotalLiquidRatio is < 0.15 or > 4.00)
        violations.Add($"Liquid-volume ratio {comparison.TotalLiquidRatio:F4} is outside [0.15, 4.00].");
    if (comparison.ActiveTileHistogramL1 > 1.35)
        violations.Add($"Active tile histogram L1 {comparison.ActiveTileHistogramL1:F4} exceeds 1.35.");
    if (comparison.WallHistogramL1 > 1.80)
        violations.Add($"Wall histogram L1 {comparison.WallHistogramL1:F4} exceeds 1.80.");
    if (Math.Abs(comparison.WorldSurfaceDelta) > candidate.Height * 0.15)
        violations.Add($"World-surface delta {comparison.WorldSurfaceDelta} exceeds 15% of world height.");
    if (Math.Abs(comparison.RockLayerDelta) > candidate.Height * 0.15)
        violations.Add($"Rock-layer delta {comparison.RockLayerDelta} exceeds 15% of world height.");
    if (Math.Abs(comparison.SpawnDeltaX) > candidate.Width * 0.20)
        violations.Add($"Spawn X delta {comparison.SpawnDeltaX} exceeds 20% of world width.");
    if (Math.Abs(comparison.DungeonDeltaX) > candidate.Width * 0.20)
        violations.Add($"Dungeon X delta {comparison.DungeonDeltaX} exceeds 20% of world width.");

    RequireMaterial(candidate, 53, "sand", violations);
    RequireMaterial(candidate, 59, "mud", violations);
    RequireMaterial(candidate, 60, "jungle grass", violations);
    RequireMaterial(candidate, 147, "snow", violations);
    RequireAnyMaterial(candidate, [41, 43, 44], "dungeon brick", violations);
    RequireMaterial(candidate, 226, "Lihzahrd brick", violations);
    RequireMaterial(candidate, 21, "container tiles", violations);
    if (candidate.ChestCount <= 0)
        violations.Add("Candidate world has no persisted chests.");
    if (candidate.TownNpcCount <= 0)
        violations.Add("Candidate world has no starting town NPC.");

    return violations;
}

static void RequireMaterial(
    WorldStructuralFingerprint candidate,
    int type,
    string name,
    List<string> violations)
{
    if (!candidate.TileHistogram.TryGetValue(type, out long count) || count <= 0)
        violations.Add($"Candidate world is missing {name} tile type {type}.");
}

static void RequireAnyMaterial(
    WorldStructuralFingerprint candidate,
    int[] types,
    string name,
    List<string> violations)
{
    foreach (int type in types)
    {
        if (candidate.TileHistogram.TryGetValue(type, out long count) && count > 0)
            return;
    }
    violations.Add($"Candidate world is missing {name} tile family [{string.Join(',', types)}].");
}

static double HistogramDistance(Dictionary<int, long> left, Dictionary<int, long> right)
{
    long leftTotal = left.Values.Sum();
    long rightTotal = right.Values.Sum();
    long denominator = Math.Max(leftTotal, rightTotal);
    if (denominator == 0)
        return 0d;

    var keys = new HashSet<int>(left.Keys);
    keys.UnionWith(right.Keys);
    double delta = 0d;
    foreach (int key in keys)
    {
        left.TryGetValue(key, out long a);
        right.TryGetValue(key, out long b);
        delta += Math.Abs((double)a - b);
    }
    return delta / denominator;
}

static double Ratio(long numerator, long denominator)
{
    if (denominator == 0)
        return numerator == 0 ? 1d : double.PositiveInfinity;
    return numerator / (double)denominator;
}

static void Increment(Dictionary<int, long> histogram, int key, long amount)
{
    histogram.TryGetValue(key, out long current);
    histogram[key] = current + amount;
}

static WorldFileLoadLimits CreateLimits() =>
    new(
        MaxTileCount: 8_400L * 2_400,
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

internal sealed record WorldStructuralFingerprint(
    int FormatVersion,
    int Width,
    int Height,
    int SpawnX,
    int SpawnY,
    int DungeonX,
    int DungeonY,
    int WorldSurface,
    int RockLayer,
    bool Crimson,
    long ActiveTiles,
    long NonZeroWalls,
    long TotalLiquid,
    Dictionary<int, long> TileHistogram,
    Dictionary<int, long> WallHistogram,
    Dictionary<string, long> LiquidHistogram,
    int ChestCount,
    int SignCount,
    int TownNpcCount,
    int PersistentNpcCount,
    int TileEntityCount,
    VanillaTerrainSilhouette1458 TerrainSilhouette,
    string StructuralSha256);

internal sealed record WorldStructuralComparison(
    double ActiveTileHistogramL1,
    double WallHistogramL1,
    double ActiveTileRatio,
    double NonZeroWallRatio,
    double TotalLiquidRatio,
    int SpawnDeltaX,
    int SpawnDeltaY,
    int DungeonDeltaX,
    int DungeonDeltaY,
    int WorldSurfaceDelta,
    int RockLayerDelta,
    VanillaTerrainSilhouetteComparison1458 TerrainSilhouette,
    bool SameDungeonSide);

internal sealed record WorldReferenceComparisonReport(
    string ReferencePath,
    string CandidatePath,
    WorldStructuralFingerprint Reference,
    WorldStructuralFingerprint Candidate,
    WorldStructuralComparison Comparison);
