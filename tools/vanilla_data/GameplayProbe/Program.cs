using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

// SDK-only boundary experiment: invoke the original method, or current compiled
// helpers. No ItemCheck/UpdateEquips, packet admission or projectile birth is implied.
if (args.Length == 7 && args[6] == "owned-bow")
{
    OwnedAmmoProbe.Run(args);
    return;
}
if (args.Length == 7 && args[6] == "pick-ammo") args = args[..6];
if (args.Length is not (5 or 6))
    throw new ArgumentException("GameplayProbe <official|runtime> <official-exe> <reference.json> <new-output.json> <seed> [normal|omit-step|extra-rng]");
string engine = args[0], assemblyPath = Path.GetFullPath(args[1]), outputPath = Path.GetFullPath(args[3]);
int seed = int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
string control = args.Length == 6 ? args[5] : "normal";
if (engine is not ("official" or "runtime") || control is not ("normal" or "omit-step" or "extra-rng") || engine == "official" && control != "normal")
    throw new ArgumentException("Unsupported engine/control");
if (File.Exists(outputPath)) throw new IOException("Output exists; use a new path");
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
using var pin = JsonDocument.Parse(File.ReadAllBytes(args[2]));
string admitted = pin.RootElement.GetProperty("assemblySha256").GetString()!;
string directory = Path.GetDirectoryName(assemblyPath)!;
var dependencies = pin.RootElement.GetProperty("dependenciesSha256").EnumerateObject().ToDictionary(v => v.Name, v => v.Value.GetString()!);
void CheckReference()
{
    if (Hash(assemblyPath) != admitted) throw new InvalidDataException("Official assembly changed");
    foreach (var item in dependencies)
        if (Hash(Path.Combine(directory, item.Key)) != item.Value) throw new InvalidDataException("Official dependency changed: " + item.Key);
}
CheckReference();
var closure = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll", SearchOption.AllDirectories)
    .ToDictionary(p => Path.GetRelativePath(AppContext.BaseDirectory, p).Replace('\\', '/'), Hash);
const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
const int steps = 24;
const string scenario = "pick-ammo-random-conservation-v1";
const string officialEntry = "Terraria.Player.PickAmmo";
const string runtimeEntry = "VanillaAmmoCycling1458.TrySelect+TryAdvanceSelectedAmmo/VanillaProjectileWeaponCombatCatalog.PrepareAmmoConservation/VanillaOrdinaryBowLaunch1458.TryResolve";
object Rng(object random)
{
    Type type = random.GetType();
    var array = (int[])(type.GetField("SeedArray", flags) ?? type.GetField("seedArray", flags))!.GetValue(random)!;
    return new { algorithm = "terraria1458-unifiedrandom-inext21", ownerAlias = new[] { "Main.rand" },
        inext = (uint)type.GetField("inext", flags)!.GetValue(random)!, seedArray = array.ToArray() };
}
object Launch(int projectile, int damage, float speed, float knockBack) =>
    new { projectile, damage, speedBits = BitConverter.SingleToInt32Bits(speed), knockBackBits = BitConverter.SingleToInt32Bits(knockBack) };
object input;
var frames = new List<object>();
string? runtimeHash = null;
if (engine == "runtime")
{
    var random = new VanillaUnifiedRandom1458(seed);
    var inventory = Enumerable.Range(0, 58).Select(_ => new Slot(0, 0, 0, false)).ToArray();
    inventory[0] = new(39, 1, 0, false);
    foreach (var (slot, type, stock) in new[] { (1, 40, 1), (50, 40, 3), (54, 41, 2), (55, 47, 20) })
    {
        if (!VanillaProjectileWeaponCombatCatalog.TryGetArrowAmmo(new ItemTypeId(type), out var ammo)) throw new InvalidDataException("Missing ammo facts");
        inventory[slot] = new(type, stock, 40, ammo.Consumable);
    }
    var cycling = new AmmoCyclingState1458(0, 0);
    var combat = VanillaPlayerCombatSnapshot.Baseline with { MagicQuiver = true, ArrowDamageAdditiveStack = 0.1f, AmmoCost75 = true };
    if (!VanillaProjectileWeaponCombatCatalog.TryGetWeapon(new ItemTypeId(39), out var weapon)) throw new InvalidDataException("Missing weapon");
    object State() => new { inventory = inventory.ToArray(), cyclingOffset = cycling.Offset, cyclingCooldown = cycling.Cooldown, rng = Rng(random) };
    input = State();
    for (int step = 0; step < steps; step++)
    {
        object before = State();
        bool omitted = control == "omit-step" && step == 7;
        var slots = inventory.Select(item => new AmmoCyclingSlot1458(item.ammoFamily, item.stack)).ToArray();
        if (!VanillaAmmoCycling1458.TrySelect(39, 40, AmmoCyclingMode1458.Random, in cycling, slots, out int selected) || selected < 0)
            throw new InvalidDataException("Scenario lost selected ammo");
        var item = inventory[selected];
        if (!VanillaProjectileWeaponCombatCatalog.TryGetArrowAmmo(new ItemTypeId(item.type), out var ammo) ||
            !VanillaOrdinaryBowLaunch1458.TryResolve(new ItemTypeId(39), new PrefixId(0), new ItemTypeId(item.type), in combat, out var launch))
            throw new InvalidDataException("Unrepresented launch");
        bool conserved = true;
        if (!omitted)
        {
            if (!VanillaAmmoCycling1458.TryAdvanceSelectedAmmo(weapon.AnimationTicks, false, AmmoCyclingMode1458.Random, in cycling, random.Next, out cycling))
                throw new InvalidDataException("Unsupported selector transition");
            conserved = VanillaProjectileWeaponCombatCatalog.PrepareAmmoConservation(in weapon, in ammo, in combat, random.Next, ammoBox: true, ammoPotion: true);
            // Caller-owned detached stock projection only. Production admission,
            // reservation and debit journaling are outside this helper boundary.
            if (!conserved && item.consumable)
                inventory[selected] = item.stack == 1 ? new(0, 0, 0, false) : item with { stack = item.stack - 1 };
        }
        if (control == "extra-rng" && step == 7) random.Next();
        frames.Add(new { step, before, selectedSlot = selected, usedAmmoItemId = item.type, canShoot = true, conserved,
            launch = Launch(launch.ProjectileType.Value, launch.Damage, launch.Speed, launch.KnockBack), state = State() });
    }
    runtimeHash = Hash(typeof(VanillaAmmoCycling1458).Assembly.Location);
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
        string? resource = game?.GetManifestResourceNames().SingleOrDefault(v => v.EndsWith("." + name.Name + ".dll", StringComparison.OrdinalIgnoreCase));
        if (resource is null) return null;
        using var stream = game!.GetManifestResourceStream(resource)!;
        return AssemblyLoadContext.Default.LoadFromStream(stream);
    };
    game = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    Type Type(string name) => game.GetType(name, true)!;
    object Read(Type type, object? owner, string name) => type.GetField(name, flags)!.GetValue(owner)!;
    void Set(Type type, object? owner, string name, object value) => type.GetField(name, flags)!.SetValue(owner, value);
    Set(Type("Terraria.Program"), null, "SavePath", Path.Combine(Path.GetDirectoryName(outputPath)!, "probe-state"));
    Type main = Type("Terraria.Main"), playerType = Type("Terraria.Player"), itemType = Type("Terraria.Item");
    if ((string)Read(main, null, "versionNumber") != "v1.4.5.8") throw new InvalidDataException("Unexpected version");
    Set(main, null, "dedServ", true); Set(main, null, "netMode", 2); Set(main, null, "gameMenu", true); Set(main, null, "remixWorld", false);
    Set(main, null, "ActiveWorldFileData", Activator.CreateInstance(Type("Terraria.IO.WorldFileData"))!);
    main.GetProperty("GameMode", flags)!.SetValue(null, 0);
    object player = Activator.CreateInstance(playerType)!;
    var inventory = (Array)Read(playerType, player, "inventory");
    var defaults = itemType.GetMethod("SetDefaults", [typeof(int), Type("Terraria.GameContent.Items.ItemVariant")])!;
    foreach (var (slot, type, stock) in new[] { (0, 39, 1), (1, 40, 1), (50, 40, 3), (54, 41, 2), (55, 47, 20) })
    {
        object item = inventory.GetValue(slot)!;
        defaults.Invoke(item, [type, null]); Set(itemType, item, "stack", stock);
    }
    foreach (string name in new[] { "magicQuiver", "ammoBox", "ammoPotion", "ammoCost75" }) Set(playerType, player, name, true);
    foreach (string name in new[] { "rangedDamage", "rangedMultDamage", "arrowDamage" }) Set(playerType, player, name, 1f);
    Set(playerType, player, "arrowDamageAdditiveStack", 0.1f);
    Set(playerType, player, "ammoCyclingMode", Enum.Parse(Type("Terraria.GameContent.Items.PlayerAmmoCyclingMode"), "Random"));
    Set(playerType, player, "ammoCyclingOffset", 0); Set(playerType, player, "ammoCyclingCooldown", 0);
    if ((int)playerType.GetProperty("selectedItem", flags)!.GetValue(player)! != 0) throw new InvalidDataException("Expected constructor selected slot zero");
    object weapon = inventory.GetValue(0)!;
    object random = Activator.CreateInstance(Type("Terraria.Utilities.UnifiedRandom"), [seed])!;
    Set(main, null, "rand", random);
    Slot Project(int index)
    {
        object item = inventory.GetValue(index)!;
        return new((int)Read(itemType, item, "type"), (int)Read(itemType, item, "stack"), (int)Read(itemType, item, "ammo"), (bool)Read(itemType, item, "consumable"));
    }
    object State() => new { inventory = Enumerable.Range(0, 58).Select(Project).ToArray(),
        cyclingOffset = (int)Read(playerType, player, "ammoCyclingOffset"), cyclingCooldown = (int)Read(playerType, player, "ammoCyclingCooldown"), rng = Rng(random) };
    input = State();
    var select = playerType.GetMethod("PickAmmo_PickAmmoItem", flags)!;
    var pick = playerType.GetMethod("PickAmmo", flags)!;
    for (int step = 0; step < steps; step++)
    {
        object before = State();
        // Selector is a read-only query; assert it did not draw before the real invocation.
        string beforeSelect = JsonSerializer.Serialize(Rng(random));
        object selected = select.Invoke(player, [weapon]) ?? throw new InvalidDataException("No source ammo");
        if (beforeSelect != JsonSerializer.Serialize(Rng(random))) throw new InvalidDataException("Selector unexpectedly consumes RNG");
        int slot = Enumerable.Range(0, 58).Single(index => ReferenceEquals(selected, inventory.GetValue(index)));
        int stock = (int)Read(itemType, selected, "stack");
        int damage = (int)playerType.GetMethod("GetWeaponDamage", [itemType])!.Invoke(player, [weapon])!;
        object?[] call = [weapon, Read(itemType, weapon, "shoot"), Read(itemType, weapon, "shootSpeed"), false, damage, Read(itemType, weapon, "knockBack"), 0, false];
        pick.Invoke(player, call);
        bool conserved = stock == (int)Read(itemType, selected, "stack");
        frames.Add(new { step, before, selectedSlot = slot, usedAmmoItemId = (int)call[6]!, canShoot = (bool)call[3]!, conserved,
            launch = Launch((int)call[1]!, (int)call[4]!, (float)call[2]!, (float)call[5]!), state = State() });
    }
    if (Project(58) != new Slot(0, 0, 0, false)) throw new InvalidDataException("Unprojected mouse slot changed");
}
CheckReference();
var after = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll", SearchOption.AllDirectories)
    .ToDictionary(p => Path.GetRelativePath(AppContext.BaseDirectory, p).Replace('\\', '/'), Hash);
if (closure.Count != after.Count || closure.Any(v => !after.TryGetValue(v.Key, out string? hash) || hash != v.Value)) throw new InvalidDataException("Executed DLL closure changed");
var provenance = new Dictionary<string, object> { ["entry"] = engine == "official" ? officialEntry : runtimeEntry,
    ["control"] = control, ["probeSha256"] = Hash(Assembly.GetExecutingAssembly().Location),
    ["officialAssemblySha256"] = admitted, ["dependenciesSha256"] = dependencies };
if (runtimeHash is not null) provenance["runtimeAssemblySha256"] = runtimeHash;
var observation = new { schemaVersion = 1, kind = "gameplay-behavior-observation", engine, scenario, seed,
    context = new { weaponType = 39, prefix = 0, steps, cyclingMode = "Random", magicQuiver = true, ammoBox = true, ammoPotion = true, ammoCost75 = true,
        effects = "explicit-preprojected-not-UpdateEquips", inventoryProjection = "58-ordinary-slots:type,stack,ammoFamily,consumable",
        debit = "runtime-detached-caller-projection-not-authoritative-admission", floatEncoding = "ieee754-binary32-signed-bits" },
    input, frames, provenance, executedDlls = closure };
using var file = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
JsonSerializer.Serialize(file, observation, new JsonSerializerOptions { WriteIndented = true });

internal sealed record Slot(int type, int stack, int ammoFamily, bool consumable);
