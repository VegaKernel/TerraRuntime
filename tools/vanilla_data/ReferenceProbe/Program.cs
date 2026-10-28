using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

// Developer tool only. Reflection invokes the pinned game in a separate process;
// no official assembly, loader or generated code enters the production graph.
if (args.Length != 4)
    throw new ArgumentException("ReferenceProbe <TerrariaServer.exe> <expected-sha256> <new-output.json> <reference.json>");
string assemblyPath = Path.GetFullPath(args[0]);
string outputPath = Path.GetFullPath(args[2]);
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
if (!string.Equals(Hash(assemblyPath), args[1], StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("Official assembly hash differs from the admitted reference.");
if (File.Exists(outputPath)) throw new IOException("Capture output already exists.");
string directory = Path.GetDirectoryName(assemblyPath)!;
using var reference = JsonDocument.Parse(File.ReadAllBytes(args[3]));
var dependencies = reference.RootElement.GetProperty("dependenciesSha256").EnumerateObject()
    .ToDictionary(property => property.Name, property => property.Value.GetString()!);
void CheckDependencies()
{
    foreach (var dependency in dependencies)
        if (Hash(Path.Combine(directory, dependency.Key)) != dependency.Value)
            throw new InvalidDataException("Official dependency hash changed: " + dependency.Key);
}
CheckDependencies();
Assembly? game = null;
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string adjacent = Path.Combine(directory, name.Name + ".dll");
    if (File.Exists(adjacent))
    {
        if (!dependencies.TryGetValue(Path.GetFileName(adjacent), out string? expected) || Hash(adjacent) != expected)
            throw new InvalidDataException("Unadmitted official dependency: " + adjacent);
        return AssemblyLoadContext.Default.LoadFromAssemblyPath(adjacent);
    }
    string? resource = game?.GetManifestResourceNames().SingleOrDefault(
        value => value.EndsWith("." + name.Name + ".dll", StringComparison.OrdinalIgnoreCase));
    if (resource is null) return null;
    using Stream stream = game!.GetManifestResourceStream(resource)!;
    return AssemblyLoadContext.Default.LoadFromStream(stream);
};
game = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
Type Type(string name) => game.GetType(name, throwOnError: true)!;
object Read(Type type, object? owner, string name) =>
    (type.GetField(name, flags) ?? throw new MissingFieldException(type.FullName, name)).GetValue(owner)!;
void Set(Type type, object? owner, string name, object value) =>
    (type.GetField(name, flags) ?? throw new MissingFieldException(type.FullName, name)).SetValue(owner, value);
object New(Type type) => Activator.CreateInstance(type)!;
var main = Type("Terraria.Main");
// Main's initializer derives paths from Program.SavePath even in a headless probe.
Set(Type("Terraria.Program"), null, "SavePath", Path.Combine(Path.GetDirectoryName(outputPath)!, "probe-state"));
string version = (string)Read(main, null, "versionNumber");
if (version != "v1.4.5.8") throw new InvalidDataException("Unexpected game version: " + version);
Set(main, null, "dedServ", true);
Set(main, null, "netMode", 2);
Set(main, null, "gameMenu", true);
Set(main, null, "getGoodWorld", false);
Set(main, null, "hardMode", false);
Set(main, null, "remixWorld", false);
Set(main, null, "ActiveWorldFileData", New(Type("Terraria.IO.WorldFileData")));
main.GetProperty("GameMode", flags)!.SetValue(null, 0);
var playerType = Type("Terraria.Player");
var npcType = Type("Terraria.NPC");
var randomType = Type("Terraria.Utilities.UnifiedRandom");
Array ConstructArray(Type type, int count)
{
    var result = Array.CreateInstance(type, count);
    for (int index = 0; index < count; index++) result.SetValue(New(type), index);
    return result;
}
var players = ConstructArray(playerType, 256);
Set(main, null, "player", players);
Set(main, null, "npc", ConstructArray(npcType, 201));
var player = players.GetValue(0)!;
Set(playerType, player, "active", true);
Set(playerType, player, "statLife", 400);
Set(playerType, player, "statLifeMax2", 400);
Set(playerType, player, "statMana", 20);
Set(playerType, player, "statManaMax2", 20);
void ResetRandom() => Set(main, null, "rand", Activator.CreateInstance(randomType, [1458])!);
int npcCount = Convert.ToInt32(Read(Type("Terraria.ID.NPCID"), null, "Count"));
int negativeCount = -Convert.ToInt32(Read(Type("Terraria.ID.NPCID"), null, "NegativeIDCount")) - 1;
var spawnType = Type("Terraria.NPCSpawnParams");
var npcDefaults = npcType.GetMethod("SetDefaults", [typeof(int), spawnType])!;
string[] npcFields = ["type", "netID", "aiStyle", "width", "height", "lifeMax", "damage", "defense",
    "scale", "knockBackResist", "alpha", "noGravity", "noTileCollide", "dontTakeDamage", "hide"];
string[] npcNames = ["type", "netId", "aiStyle", "width", "height", "lifeMax", "damage", "defense",
    "scale", "knockBackResist", "alpha", "noGravity", "noTileCollide", "dontTakeDamage", "hidden"];
var npcs = new List<Dictionary<string, object>>();
foreach (int requested in Enumerable.Range(1, npcCount - 1).Concat(Enumerable.Range(1, negativeCount).Select(value => -value)).Order())
{
    ResetRandom();
    object npc = New(npcType);
    npcDefaults.Invoke(npc, [requested, New(spawnType)]);
    var row = new Dictionary<string, object> { ["requested"] = requested };
    for (int index = 0; index < npcFields.Length; index++) row[npcNames[index]] = Read(npcType, npc, npcFields[index]);
    npcs.Add(row);
}
var itemType = Type("Terraria.Item");
int itemCount = Convert.ToInt32(Read(Type("Terraria.ID.ItemID"), null, "Count"));
var itemDefaults = itemType.GetMethod("SetDefaults", [typeof(int), Type("Terraria.GameContent.Items.ItemVariant")])!;
var items = new List<Dictionary<string, object>>();
foreach (int id in Enumerable.Range(1, itemCount - 1))
{
    ResetRandom();
    object item = New(itemType);
    itemDefaults.Invoke(item, [id, null]);
    items.Add(new()
    {
        ["id"] = id, ["createTile"] = Read(itemType, item, "createTile"),
        ["createWall"] = Read(itemType, item, "createWall"), ["placeStyle"] = Read(itemType, item, "placeStyle"),
        ["consumable"] = Read(itemType, item, "consumable")
    });
}
if (!string.Equals(Hash(assemblyPath), args[1], StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("Official assembly changed during capture.");
CheckDependencies();
var capture = new
{
    schemaVersion = 1, gameVersion = "1.4.5.8", assemblySha256 = args[1].ToLowerInvariant(),
    dependenciesSha256 = dependencies,
    profile = new { name = "classic-constructor-coreclr", seed = 1458, gameMenu = true,
        netMode = 2, hardMode = false, getGoodWorld = false, remixWorld = false,
        hostOs = RuntimeInformation.OSDescription, framework = RuntimeInformation.FrameworkDescription,
        scope = "Fresh instances, default spawn parameters, no content-sample initialization/world/update/AI. Assembly edition is not host OS." },
    bounds = new { npcPositiveCount = npcCount - 1, npcNegativeCount = negativeCount, itemCount },
    datasets = new Dictionary<string, object> { ["npc-defaults"] = npcs, ["item-placement"] = items }
};
using (var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write))
    JsonSerializer.Serialize(stream, capture);
Console.WriteLine($"Captured {npcs.Count} NPC identities and {items.Count} item identities.");
