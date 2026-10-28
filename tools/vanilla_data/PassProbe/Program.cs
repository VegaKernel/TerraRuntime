using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

// An offline isolated-pass probe. It calls the original registered pass and the
// current runtime body; it does not copy either algorithm or generate a world.
if (args.Length is not (5 or 6))
    throw new ArgumentException("PassProbe <official|runtime> <official-exe> <reference.json> <new-output.json> <seed> [normal|omit-pass|extra-rng]");
string engine = args[0];
string assemblyPath = Path.GetFullPath(args[1]);
string outputPath = Path.GetFullPath(args[3]);
int seed = int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
string control = args.Length == 6 ? args[5] : "normal";
if (engine is not ("official" or "runtime") || control is not ("normal" or "omit-pass" or "extra-rng") ||
    engine == "official" && control != "normal") throw new ArgumentException("Unsupported engine/control");
if (File.Exists(outputPath)) throw new IOException("Output exists; use a new path");
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
using var pin = JsonDocument.Parse(File.ReadAllBytes(args[2]));
string admitted = pin.RootElement.GetProperty("assemblySha256").GetString()!;
if (Hash(assemblyPath) != admitted) throw new InvalidDataException("Official assembly hash differs");
string directory = Path.GetDirectoryName(assemblyPath)!;
var dependencies = pin.RootElement.GetProperty("dependenciesSha256").EnumerateObject()
    .ToDictionary(item => item.Name, item => item.Value.GetString()!);
void CheckReference()
{
    if (Hash(assemblyPath) != admitted) throw new InvalidDataException("Official assembly changed");
    foreach (var item in dependencies)
        if (Hash(Path.Combine(directory, item.Key)) != item.Value) throw new InvalidDataException("Official dependency changed: " + item.Key);
}
CheckReference();
var closure = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll", SearchOption.AllDirectories)
    .ToDictionary(path => Path.GetRelativePath(AppContext.BaseDirectory, path).Replace('\\', '/'), Hash);
const int width = 160, height = 112;
const double surface = 70;
const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
WorldTile[] Fixture()
{
    var cells = new WorldTile[width * height];
    // Deliberately exercises green-wall staining, not a naturally generated
    // predecessor or the pass's dirt-pocket/SpreadGrass branches.
    for (int y = 18; y < 53; y++)
    for (int x = 64; x < 96; x++)
    {
        int code = x * 17 + y * 23;
        var cell = new WorldTile
        {
            Type = 1, Wall = (ushort)(code % 3 == 0 ? 65 : 63),
            FrameX = (short)(code % 5 * 18), FrameY = (short)(code % 7 * 18),
            Flags = (WorldTileFlags)(code % 0x800), LiquidAmount = (byte)(code % 256),
            LiquidKind = (WorldLiquidKind)(code % 4), TileColor = (byte)(code % 31),
            WallColor = (byte)((code / 7) % 31)
        };
        cell.TrySetSourceShape(code % 7 == 0, (byte)(code % 5));
        cells[y * width + x] = cell;
    }
    return cells;
}
byte[] Pack(Func<int, int, WorldTile> at)
{
    var bytes = new byte[width * height * 16];
    Span<WorldTile> physical = stackalloc WorldTile[1];
    for (int y = 0; y < height; y++)
    for (int x = 0; x < width; x++)
    {
        var value = at(x, y);
        Span<byte> row = bytes.AsSpan((y * width + x) * 16, 16);
        BinaryPrimitives.WriteUInt16LittleEndian(row, value.Type);
        BinaryPrimitives.WriteUInt16LittleEndian(row[2..], value.Wall);
        BinaryPrimitives.WriteInt16LittleEndian(row[4..], value.FrameX);
        BinaryPrimitives.WriteInt16LittleEndian(row[6..], value.FrameY);
        BinaryPrimitives.WriteUInt16LittleEndian(row[8..], (ushort)value.Flags);
        row[10] = value.LiquidAmount; row[11] = value.TileColor; row[12] = value.WallColor;
        physical[0] = value;
        row[13] = value.Shape; row[14] = (byte)value.LiquidKind;
        row[15] = MemoryMarshal.AsBytes(physical)[15]; // Observe, rather than hide, a violated reserved-byte invariant.
    }
    return bytes;
}
object Rng(object random)
{
    Type type = random.GetType();
    var array = (int[])(type.GetField("SeedArray", flags) ?? type.GetField("seedArray", flags))!.GetValue(random)!;
    return new { algorithm = "terraria1458-unifiedrandom-inext21", ownerAlias = new[] { "Main.rand", "WorldGen.genRand" },
        inext = (uint)type.GetField("inext", flags)!.GetValue(random)!, seedArray = array.ToArray() };
}
object Snapshot(Func<int, int, WorldTile> at, object random)
{
    byte[] bytes = Pack(at);
    return new { cellsBase64 = Convert.ToBase64String(bytes), cellsSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), rng = Rng(random) };
}
var initial = Fixture();
object input, output, provenance;
if (engine == "runtime")
{
    var store = new WorldTileStore(new WorldDimensions(width, height));
    for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) store.Set(x, y, initial[y * width + x]);
    var random = new RuntimeRandom(seed);
    input = Snapshot(store.Get, random.Source);
    Type passType = typeof(SourceBackedSurfaceFinish1458).Assembly.GetType("TerraRuntime.WorldGeneration.Vanilla.GrassWallPass1458", true)!;
    object body = Activator.CreateInstance(passType, flags, null, [store, random, surface, CancellationToken.None], null)!;
    if (control != "omit-pass") passType.GetMethod("Apply")!.Invoke(body, null);
    if (control == "extra-rng") random.Next();
    output = Snapshot(store.Get, random.Source);
    provenance = new { entry = passType.FullName + ".Apply", control,
        runtimeAssemblySha256 = Hash(passType.Assembly.Location), probeSha256 = Hash(Assembly.GetExecutingAssembly().Location),
        runtimeRng = "Current src/TerraRuntime.Core/VanillaUnifiedRandom1458.cs linked into this offline probe",
        methodIlSha256 = Convert.ToHexStringLower(SHA256.HashData(passType.GetMethod("Apply")!.GetMethodBody()!.GetILAsByteArray()!)) };
}
else
{
    Assembly? game = null;
    AssemblyLoadContext.Default.Resolving += (_, name) =>
    {
        string adjacent = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(adjacent))
        {
            if (!dependencies.TryGetValue(Path.GetFileName(adjacent), out string? expected) || Hash(adjacent) != expected)
                throw new InvalidDataException("Unadmitted dependency: " + adjacent);
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(adjacent);
        }
        string? resource = game?.GetManifestResourceNames().SingleOrDefault(value => value.EndsWith("." + name.Name + ".dll", StringComparison.OrdinalIgnoreCase));
        if (resource is null) return null;
        using var stream = game!.GetManifestResourceStream(resource)!;
        return AssemblyLoadContext.Default.LoadFromStream(stream);
    };
    game = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    Type Type(string name) => game.GetType(name, true)!;
    object Read(Type type, object? owner, string name) => type.GetField(name, flags)!.GetValue(owner)!;
    void Set(Type type, object? owner, string name, object value) => type.GetField(name, flags)!.SetValue(owner, value);
    Set(Type("Terraria.Program"), null, "SavePath", Path.Combine(Path.GetDirectoryName(outputPath)!, "probe-state"));
    var main = Type("Terraria.Main");
    if ((string)Read(main, null, "versionNumber") != "v1.4.5.8") throw new InvalidDataException("Unexpected game version");
    foreach (var setting in new (string, object)[] { ("maxTilesX", width), ("maxTilesY", height), ("worldSurface", surface),
        ("rockLayer", 95d), ("dedServ", true), ("netMode", 2), ("gameMenu", false), ("hardMode", false) })
        Set(main, null, setting.Item1, setting.Item2);
    Type worldGen = Type("Terraria.WorldGen");
    worldGen.GetNestedType("SecretSeed", flags)!.GetMethod("InitializeSecretSeeds", flags)!.Invoke(null, null);
    foreach (string setting in new[] { "skyblockWorldGen", "getGoodWorldGen", "dontStarveWorldGen", "remixWorldGen" })
        Set(worldGen, null, setting, false);
    foreach (string setting in new[] { "skyblockWorld", "getGoodWorld", "dontStarveWorld", "remixWorld" })
        Set(main, null, setting, false);
    foreach (string name in new[] { "generatingWorld", "isGeneratingOrLoadingWorld" }) Set(worldGen, null, name, true);
    Type randomType = Type("Terraria.Utilities.UnifiedRandom");
    object random = Activator.CreateInstance(randomType, [seed])!;
    Set(main, null, "rand", random);
    Type tileType = Type("Terraria.Tile");
    var tiles = Array.CreateInstance(tileType, width, height);
    string[] booleanNames = ["active", "wire", "wire2", "wire3", "wire4", "actuator", "inActive", "invisibleBlock", "invisibleWall", "fullbrightBlock", "fullbrightWall"];
    WorldTileFlags[] booleanFlags = [WorldTileFlags.Active, WorldTileFlags.WireRed, WorldTileFlags.WireBlue,
        WorldTileFlags.WireGreen, WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive,
        WorldTileFlags.InvisibleBlock, WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightBlock, WorldTileFlags.FullbrightWall];
    var getters = booleanNames.Select(name => tileType.GetMethod(name, System.Type.EmptyTypes)!).ToArray();
    var setters = booleanNames.Select(name => tileType.GetMethod(name, [typeof(bool)])!).ToArray();
    MethodInfo Method(string name, params Type[] types) => tileType.GetMethod(name, types)!;
    var fields = new[] { "type", "wall", "frameX", "frameY", "liquid" }.Select(name => tileType.GetField(name)!).ToArray();
    for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
    {
        object tile = Activator.CreateInstance(tileType)!;
        var value = initial[y * width + x];
        object[] fieldValues = [value.Type, value.Wall, value.FrameX, value.FrameY, value.LiquidAmount];
        for (int i = 0; i < fields.Length; i++) fields[i].SetValue(tile, fieldValues[i]);
        for (int i = 0; i < setters.Length; i++) setters[i].Invoke(tile, [(value.Flags & booleanFlags[i]) != 0]);
        Method("color", typeof(byte)).Invoke(tile, [value.TileColor]);
        Method("wallColor", typeof(byte)).Invoke(tile, [value.WallColor]);
        Method("liquidType", typeof(int)).Invoke(tile, [(int)value.LiquidKind]);
        Method("slope", typeof(byte)).Invoke(tile, [value.SourceSlope]);
        Method("halfBrick", typeof(bool)).Invoke(tile, [value.IsHalfBrick]);
        tiles.SetValue(tile, x, y);
    }
    Set(main, null, "tile", tiles);
    ((bool[])Read(main, null, "tileSolid"))[1] = true;
    WorldTile At(int x, int y)
    {
        object tile = tiles.GetValue(x, y)!;
        var result = new WorldTile { Type = (ushort)fields[0].GetValue(tile)!, Wall = (ushort)fields[1].GetValue(tile)!,
            FrameX = (short)fields[2].GetValue(tile)!, FrameY = (short)fields[3].GetValue(tile)!, LiquidAmount = (byte)fields[4].GetValue(tile)!,
            TileColor = (byte)Method("color").Invoke(tile, null)!, WallColor = (byte)Method("wallColor").Invoke(tile, null)!,
            LiquidKind = (WorldLiquidKind)Convert.ToByte(Method("liquidType").Invoke(tile, null)) };
        for (int i = 0; i < getters.Length; i++) if ((bool)getters[i].Invoke(tile, null)!) result.Flags |= booleanFlags[i];
        result.TrySetSourceShape((bool)Method("halfBrick").Invoke(tile, null)!, (byte)Method("slope").Invoke(tile, null)!);
        return result;
    }
    Type generatorType = Type("Terraria.WorldBuilding.WorldGenerator");
    object generator = Activator.CreateInstance(generatorType, [seed, null, null, null])!;
    Set(worldGen, null, "_generator", generator);
    worldGen.GetMethod("AddPasses", flags)!.Invoke(null, null);
    var passes = (System.Collections.IEnumerable)Read(generatorType, generator, "_passes");
    string passName = (string)Read(Type("Terraria.ID.GenPassNameID"), null, "SurfaceDirtWallsToGrassWalls");
    Type genPass = Type("Terraria.WorldBuilding.GenPass");
    object selected = passes.Cast<object>().Single(pass => (string)Read(genPass, pass, "Name") == passName);
    // Reset immediately before the isolated delegate body, matching RunPass's
    // entry seed, while deliberately excluding its manifest-tail Next().
    random = Activator.CreateInstance(randomType, [seed])!; Set(main, null, "rand", random);
    input = Snapshot(At, random);
    object progress = Activator.CreateInstance(Type("Terraria.WorldBuilding.GenerationProgress"))!;
    genPass.GetMethod("Apply")!.Invoke(selected, [progress, null]);
    output = Snapshot(At, random);
    var methodDelegate = (Delegate)Read(selected.GetType(), selected, "_method");
    provenance = new { entry = "registered-GenPass.Apply", registeredName = passName, actualMethod = methodDelegate.Method.Name,
        methodIlSha256 = Convert.ToHexStringLower(SHA256.HashData(methodDelegate.Method.GetMethodBody()!.GetILAsByteArray()!)),
        officialAssemblySha256 = admitted, dependenciesSha256 = dependencies,
        probeSha256 = Hash(Assembly.GetExecutingAssembly().Location), control };
}
CheckReference();
var afterClosure = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll", SearchOption.AllDirectories)
    .ToDictionary(path => Path.GetRelativePath(AppContext.BaseDirectory, path).Replace('\\', '/'), Hash);
if (closure.Count != afterClosure.Count || closure.Any(item => !afterClosure.TryGetValue(item.Key, out string? actual) || actual != item.Value))
    throw new InvalidDataException("Probe/runtime DLL closure changed during capture");
var observation = new
{
    schemaVersion = 1, kind = "worldgen-pass-observation", engine, pass = "SurfaceDirtWallsToGrassWalls", scenario = "grass-wall-stain-v1",
    context = new { width, height, worldSurface = surface, seed, netMode = 2, dedServ = true, gameMenu = false,
        skyblock = false, getGoodWorld = false, dontStarve = false, remix = false, predecessorState = "explicit-synthetic-not-natural-prefix" },
    projection = new { order = "y-major/x-fast", fields = new[] { "type", "wall", "frameX", "frameY", "flags", "liquidAmount", "tileColor", "wallColor", "shape", "liquidKind", "reserved" },
        recordBytes = 16, byteOrder = "little-endian" }, input, output, provenance, executedDlls = closure,
    scope = "Isolated complete pass body on explicit green-wall stain input. No natural-prefix, dirt-pocket branch, raw Tile wall-frame metadata, manifest tail, Native or full-world parity claim.",
    host = new { os = RuntimeInformation.OSDescription, framework = RuntimeInformation.FrameworkDescription }
};
using (var stream = new FileStream(outputPath, FileMode.CreateNew)) JsonSerializer.Serialize(stream, observation);
Console.WriteLine($"Captured {engine} SurfaceDirtWallsToGrassWalls seed={seed} cells={width * height} control={control}");

sealed class RuntimeRandom(int seed) : IWorldGenerationVanillaRandom
{
    public VanillaUnifiedRandom1458 Source { get; } = new(seed);
    public int Next() => Source.Next();
    public int Next(int maxValue) => Source.Next(maxValue);
    public int Next(int minValue, int maxValue) => Source.Next(minValue, maxValue);
    public double NextDouble() => Source.NextDouble();
    public void NextBytes(byte[] buffer) => Source.NextBytes(buffer);
}
