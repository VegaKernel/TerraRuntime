using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 2 || !OperatingSystem.IsWindows())
    throw new ArgumentException("Usage on Windows CoreCLR: DeltaProbe <official Linux TerrariaServer.exe> <new output.json>");

string original = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
if (File.Exists(output)) throw new IOException("Capture output already exists.");
string originalDirectory = Path.GetDirectoryName(original)!;
string fna = Path.Combine(originalDirectory, "FNA.dll");
string assemblySha256 = Digest(original);
string version = assemblySha256 switch
{
    "4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034" => "1.4.5.8",
    "DFBE424CC369DF146534536EA1C1CC6D1F37573AC37A5F4BA74948C0D6CBB211" => "1.4.6.1",
    _ => throw new InvalidDataException("Unrecognized original Linux-edition server digest.")
};
const string fnaSha256 = "46D201F59CE6C3827414B2C2C5141C218ACABDA605B02E564D73CA2EFB2A3171";
if (Digest(fna) != fnaSha256 || Path.GetDirectoryName(output)!.Equals(originalDirectory, StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("Expected unchanged FNA and an output outside the original server directory.");

Assembly game = AssemblyLoadContext.Default.LoadFromAssemblyPath(original);
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    if (name.Name == "FNA") return context.LoadFromAssemblyPath(fna);
    var resource = game.GetManifestResourceNames().SingleOrDefault(value =>
        value.EndsWith($".{name.Name}.dll", StringComparison.OrdinalIgnoreCase));
    if (resource is null) return null;
    using Stream stream = game.GetManifestResourceStream(resource)!;
    return context.LoadFromStream(stream);
};
Type main = game.GetType("Terraria.Main", true)!;
Type wiring = game.GetType("Terraria.Wiring", true)!;
Type tileType = game.GetType("Terraria.Tile", true)!;
Type projectileType = game.GetType("Terraria.Projectile", true)!;
Type randomType = game.GetType("Terraria.Utilities.UnifiedRandom", true)!;
Type vectorType = AssemblyLoadContext.Default.LoadFromAssemblyPath(fna).GetType("Microsoft.Xna.Framework.Vector2", true)!;
const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
FieldInfo Field(Type type, string name) => type.GetField(name, flags) ?? throw new MissingFieldException(type.FullName, name);
MethodInfo Method(Type type, string name, params Type[] parameterTypes) =>
    type.GetMethod(name, flags, null, parameterTypes, null) ?? throw new MissingMethodException(type.FullName, name);
object Read(Type type, object? target, string name) => Field(type, name).GetValue(target)!;
void Write(Type type, object? target, string name, object value) => Field(type, name).SetValue(target, value);

Write(game.GetType("Terraria.Program", true)!, null, "SavePath", Path.Combine(Path.GetDirectoryName(output)!, "save"));
Write(main, null, "dedServ", true);
Write(main, null, "netMode", 0);
Write(main, null, "myPlayer", 0);
((Array)Read(main, null, "player")).SetValue(Activator.CreateInstance(game.GetType("Terraria.Player", true)!), 0);
const int worldSide = 80;
const int origin = 30;
Array tiles = Array.CreateInstance(tileType, worldSide, worldSide);
for (int x = 0; x < worldSide; x++)
    for (int y = 0; y < worldSide; y++)
        tiles.SetValue(Activator.CreateInstance(tileType), x, y);
Write(main, null, "maxTilesX", worldSide);
Write(main, null, "maxTilesY", worldSide);
Write(main, null, "tile", tiles);
Method(wiring, "Initialize").Invoke(null, null);
MethodInfo clear = Method(wiring, "ClearAll");
MethodInfo hitSwitch = Method(wiring, "HitSwitch", typeof(int), typeof(int));
FieldInfo frameX = Field(tileType, "frameX");
MethodInfo setActive = Method(tileType, "active", typeof(bool));
MethodInfo[] wireSetters = [.. new[] { "wire", "wire2", "wire3", "wire4" }
    .Select(name => Method(tileType, name, typeof(bool)))];
MethodInfo nextRandom = Method(randomType, "Next");

void Put(int x, int type, int mask, int frameY = 0)
{
    object tile = tiles.GetValue(x, origin)!;
    Write(tileType, tile, "type", (ushort)type);
    Write(tileType, tile, "frameX", (short)0);
    Write(tileType, tile, "frameY", (short)frameY);
    setActive.Invoke(tile, [true]);
    for (int color = 0; color < wireSetters.Length; color++)
        wireSetters[color].Invoke(tile, [(mask & (1 << color)) != 0]);
}

var colors = new List<object>();
for (int source = 1; source <= 15; source++)
{
    for (int target = 0; target <= 15; target++)
    {
        clear.Invoke(null, null);
        Put(origin, 136, source);
        Put(origin + 1, 4, target);
        hitSwitch.Invoke(null, [origin, origin]);
        short observed = (short)frameX.GetValue(tiles.GetValue(origin + 1, origin))!;
        bool expectToggle = (System.Numerics.BitOperations.PopCount((uint)(source & target)) & 1) != 0;
        if (observed != (expectToggle ? 66 : 0)) throw new InvalidDataException($"Unexpected torch result for {source}/{target}: {observed}.");
        colors.Add(new { source, target, frameX = observed });
    }
}

Array projectiles = (Array)Read(main, null, "projectile");
for (int index = 0; index < projectiles.Length; index++)
    projectiles.SetValue(Activator.CreateInstance(projectileType), index);
var traps = new List<object>();
foreach (int style in new[] { 3, -7 })
{
    foreach (int distance in new[] { 49, 50, 99, 100, 199, 200, 299, 300, 399, 400, 499, 500, 699, 700, 899, 900, 1199, 1200 })
    {
        foreach (int count in new[] { 3, 4, 13, 14, 19, 20, 24, 25, 33, 34, 39, 40, 49, 50, 66, 67, 99, 100, 199, 200 })
        {
            clear.Invoke(null, null);
            object random = Activator.CreateInstance(randomType, [1458])!;
            Write(main, null, "rand", random);
            Put(origin, 136, 1);
            Put(origin + 1, 137, 1, style * 18);
            for (int index = 0; index < projectiles.Length; index++)
            {
                object projectile = projectiles.GetValue(index)!;
                Write(projectileType, projectile, "active", index < count);
                if (index >= count) continue;
                Write(projectileType, projectile, "type", style == 3 ? 0 : 185);
                Write(projectileType, projectile, "width", 0);
                Write(projectileType, projectile, "height", 0);
                Write(projectileType, projectile, "position", Activator.CreateInstance(vectorType,
                    [(float)((origin + 1) * 16 + 8 + distance), (float)(origin * 18 + 8)])!);
            }
            hitSwitch.Invoke(null, [origin, origin]);
            var births = new List<object>();
            for (int index = count; index < projectiles.Length; index++)
            {
                object projectile = projectiles.GetValue(index)!;
                if (!(bool)Read(projectileType, projectile, "active")) continue;
                births.Add(new
                {
                    slot = index,
                    type = (int)Read(projectileType, projectile, "type"),
                    damage = (int)Read(projectileType, projectile, "damage"),
                    position = Vector(Read(projectileType, projectile, "position")),
                    velocity = Vector(Read(projectileType, projectile, "velocity"))
                });
            }
            int cooldown = ((int[])Read(wiring, null, "_mechTime"))[0];
            if (cooldown != 300 || (count == 4 && distance is 49 or 50 &&
                births.Count != (style == -7 ? 1 : 0) + (distance == 50 ? 1 : 0)))
                throw new InvalidDataException($"Trap cutoff control failed for {style}/{distance}/{count}.");
            traps.Add(new { style, distance, count, cooldown, births,
                rngNext = (int)nextRandom.Invoke(random, null)! });
        }
    }
}

if (Digest(original) != assemblySha256 || Digest(fna) != fnaSha256)
    throw new InvalidDataException("Reference inputs changed during capture.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using var stream = new FileStream(output, FileMode.CreateNew);
JsonSerializer.Serialize(stream, new { version, assemblySha256, fnaSha256, observations = new { colors, traps } });
Console.WriteLine($"{version}: {colors.Count} color scenes, {traps.Count} distance/count scenes.");

float[] Vector(object value) => [(float)Read(vectorType, value, "X"), (float)Read(vectorType, value, "Y")];
static string Digest(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}
