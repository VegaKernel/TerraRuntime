using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

// Offline adapter only: invokes both existing AI bodies, never reproduces AI logic.
if (args.Length is not (5 or 6))
    throw new ArgumentException("NpcProbe <official|runtime> <official-exe> <reference.json> <new-output.json> <seed> [normal|omit-step|extra-rng]");
string engine = args[0], official = Path.GetFullPath(args[1]), outputPath = Path.GetFullPath(args[3]);
int seed = int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
string control = args.Length == 6 ? args[5] : "normal";
if (engine is not ("official" or "runtime") || control is not ("normal" or "omit-step" or "extra-rng") ||
    engine == "official" && control != "normal") throw new ArgumentException("Unsupported engine/control");
if (File.Exists(outputPath)) throw new IOException("Output already exists");
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
using var pin = JsonDocument.Parse(File.ReadAllBytes(args[2]));
string admitted = pin.RootElement.GetProperty("assemblySha256").GetString()!;
var dependencies = pin.RootElement.GetProperty("dependenciesSha256").EnumerateObject()
    .ToDictionary(item => item.Name, item => item.Value.GetString()!);
string directory = Path.GetDirectoryName(official)!;
void CheckReference()
{
    if (Hash(official) != admitted) throw new InvalidDataException("Official assembly differs");
    foreach (var item in dependencies)
        if (Hash(Path.Combine(directory, item.Key)) != item.Value) throw new InvalidDataException("Official dependency differs: " + item.Key);
}
CheckReference();
Dictionary<string, string> Closure() => Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll", SearchOption.AllDirectories)
    .ToDictionary(path => Path.GetRelativePath(AppContext.BaseDirectory, path).Replace('\\', '/'), Hash);
var closure = Closure();
const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
string Bits(float value) => BitConverter.SingleToUInt32Bits(value).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
string[] Vector(float x, float y) => [Bits(x), Bits(y)];
string[] Ai(NpcAiState ai) => [Bits(ai.Ai0), Bits(ai.Ai1), Bits(ai.Ai2), Bits(ai.Ai3)];
object Rng(object random)
{
    Type type = random.GetType();
    var array = (int[])(type.GetField("SeedArray", flags) ?? type.GetField("seedArray", flags))!.GetValue(random)!;
    return new { algorithm = "terraria1458-unifiedrandom-inext21", owner = "Main.rand",
        inext = Convert.ToInt32(type.GetField("inext", flags)!.GetValue(random)), seedArray = array.ToArray() };
}
bool Hit(int frame) => frame is 2 or 5 or 9 or 13;
object context = new
{
    frames = 16, widthTiles = 256, heightTiles = 128, worldSurface = 100,
    netMode = 2, dedServ = true, gameMenu = false, dayTime = false, hardMode = false, gameMode = 0,
    expert = false, master = false, goodWorld = false, remix = false, eclipse = false, invasionType = 0,
    tiles = "all-empty", player = new { slot = 7, position = Vector(400f, 100f), width = 20, height = 42,
        active = true, dead = false, ghost = false, aggro = 0, frozen = false },
    carrier = "AI+caller justHit retirement; fixed position/oldPosition; retained velocity/AI/localAI; justHit pulses before each call",
    justHitFrames = new[] { 2, 5, 9, 13 }
};
var frames = new List<object>();
var rawAfterAiJustHit = new List<bool>();
const string retirement = "NPC.Update tail justHit=false after AI; runtime retires in admitted proposal";
object input, provenance;
if (engine == "runtime")
{
    var random = new NpcRandom(seed);
    var store = new RuntimeNpcStore(2);
    var update = new NpcStateUpdate(243, 243, 200f, 300f, .3f, 1f, 7,
        new NpcAiState(0f, 0f, 58f, 0f), NpcSimulationState.Initial with
        {
            OldPositionX = 199f, OldPositionY = 300f, OldVelocityX = .3f, OldVelocityY = 1f,
            DirectionX = 1, DirectionY = -1, SpriteDirection = 1, Life = 2000, LifeMax = 4000,
            Scale = 1f, TimeLeft = 750, KnockBackResist = .05f, Rotation = 0f
        });
    if (!store.TrySpawn(1, in update, out NpcSnapshot npc)) throw new InvalidOperationException("Cannot initialize runtime NPC");
    var stepper = new VanillaNpcTargetingAiStepper(new RefuseFallback(), random: random);
    stepper.EnableZombieMotion(100d);
    stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
    stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 410f, 121f, 0, true, false, false, false)
        { HitboxWidth = 20, HitboxHeight = 42 }]);
    var executor = new RuntimeNpcAiStateExecutor(store);
    object State(NpcSnapshot value)
    {
        NpcSimulationState s = value.Simulation;
        if (s.Rotation is not float rotation || s.KnockBackResist is not float knockBackResist)
            throw new InvalidDataException("Observed runtime rotation/knockback provenance became unknown");
        if (!VanillaNpcDefinitionCatalog.TryGet(value.TypeIdentity, value.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(s, out var body))
            throw new InvalidDataException("Observed runtime type/body became unknown");
        return new { npc = new { type = value.Type, netId = value.NetId, aiStyle = definition.AiStyle.Value, width = body.Width, height = body.Height,
            position = Vector(value.PositionX, value.PositionY), oldPosition = Vector(s.OldPositionX, s.OldPositionY),
            velocity = Vector(value.VelocityX, value.VelocityY), oldVelocity = Vector(s.OldVelocityX, s.OldVelocityY),
            ai = Ai(value.Ai), localAi = Ai(s.LocalAi), direction = s.DirectionX, directionY = s.DirectionY,
            spriteDirection = s.SpriteDirection, target = value.Target, life = s.Life, lifeMax = s.LifeMax,
            timeLeft = s.TimeLeft, scale = Bits(s.Scale), rotation = Bits(rotation),
            knockBackResist = Bits(knockBackResist), alpha = s.Alpha,
            active = true, justHit = s.JustHit, collideX = s.CollideX, collideY = s.CollideY,
            wet = s.Wet, noGravity = s.NoGravity, noTileCollide = s.NoTileCollide }, rng = Rng(random.Source) };
    }
    input = State(npc);
    for (int frame = 0; frame < 16; frame++)
    {
        update = new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY,
            npc.Target, npc.Ai, npc.Simulation with { JustHit = Hit(frame) });
        if (!store.TryUpdate(npc.Handle, in update, out npc)) throw new InvalidOperationException("Cannot apply frame context");
        object before = State(npc);
        if (control != "omit-step")
        {
            var result = executor.Tick(stepper);
            if (result.Applied != 1 || !store.TryGet(npc.Handle, out npc))
                throw new InvalidOperationException("NPC AI did not apply exactly one state transition");
        }
        if (control == "extra-rng") random.Source.Next();
        rawAfterAiJustHit.Add(npc.Simulation.JustHit);
        frames.Add(new { index = frame, justHit = Hit(frame), before, after = State(npc) });
    }
    Type body = typeof(VanillaNpcTargetingAiStepper);
    provenance = new { entry = "TerraRuntime.Core.Npcs.RuntimeNpcAiStateExecutor.Tick/VanillaNpcTargetingAiStepper",
        control, rawAfterAiJustHit, carrierRetirement = retirement,
        officialAssemblySha256 = admitted, dependenciesSha256 = dependencies,
        runtimeAssemblySha256 = Hash(body.Assembly.Location), probeSha256 = Hash(Assembly.GetExecutingAssembly().Location),
        runtimeRng = "Current VanillaUnifiedRandom1458.cs linked into offline adapter; existing production AI called",
        methodIlSha256 = Convert.ToHexStringLower(SHA256.HashData(body.GetMethod("TryStepState")!.GetMethodBody()!.GetILAsByteArray()!)) };
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
    game = AssemblyLoadContext.Default.LoadFromAssemblyPath(official);
    Type T(string name) => game.GetType(name, true)!;
    object Get(Type type, object? owner, string name) => type.GetField(name, flags)!.GetValue(owner)!;
    void Set(Type type, object? owner, string name, object value) => type.GetField(name, flags)!.SetValue(owner, value);
    Set(T("Terraria.Program"), null, "SavePath", Path.Combine(Path.GetDirectoryName(outputPath)!, "npc-probe-state"));
    Type main = T("Terraria.Main"), npcType = T("Terraria.NPC"), playerType = T("Terraria.Player");
    if ((string)Get(main, null, "versionNumber") != "v1.4.5.8") throw new InvalidDataException("Unexpected official version");
    foreach (var pair in new (string, object)[] { ("maxTilesX", 256), ("maxTilesY", 128), ("worldSurface", 100d),
        ("dedServ", true), ("netMode", 2), ("gameMenu", false), ("dayTime", false), ("hardMode", false),
        ("GameMode", 0), ("eclipse", false), ("invasionType", 0), ("getGoodWorld", false), ("remixWorld", false) })
    {
        if (pair.Item1 == "GameMode") main.GetProperty("GameMode", flags)!.SetValue(null, pair.Item2);
        else Set(main, null, pair.Item1, pair.Item2);
    }
    Type vector = npcType.GetField("position")!.FieldType;
    object V(float x, float y) => Activator.CreateInstance(vector, [x, y])!;
    object npc = Activator.CreateInstance(npcType)!;
    foreach (var pair in new (string, object)[] { ("type", 243), ("netID", 243), ("aiStyle", 3), ("width", 30), ("height", 114),
        ("position", V(200f, 300f)), ("oldPosition", V(199f, 300f)), ("velocity", V(.3f, 1f)), ("oldVelocity", V(.3f, 1f)),
        ("ai", new float[] { 0f, 0f, 58f, 0f }), ("localAI", new float[4]), ("active", true), ("target", 7),
        ("direction", 1), ("directionY", -1), ("spriteDirection", 1), ("life", 2000), ("lifeMax", 4000),
        ("timeLeft", 750), ("scale", 1f), ("rotation", 0f), ("knockBackResist", .05f), ("alpha", 0),
        ("justHit", false), ("collideX", false), ("collideY", false), ("wet", false), ("noGravity", false), ("noTileCollide", false) })
        Set(npcType, npc, pair.Item1, pair.Item2);
    var players = Array.CreateInstance(playerType, 256);
    for (int i = 0; i < 256; i++)
    {
        object player = Activator.CreateInstance(playerType)!;
        Set(playerType, player, "active", i == 7);
        Set(playerType, player, "dead", false);
        Set(playerType, player, "ghost", false);
        Set(playerType, player, "position", i == 7 ? V(400f, 100f) : V(0f, 0f));
        Set(playerType, player, "width", 20); Set(playerType, player, "height", 42);
        players.SetValue(player, i);
    }
    Set(main, null, "player", players);
    Type tileType = T("Terraria.Tile");
    var tiles = Array.CreateInstance(tileType, 256, 128);
    for (int y = 0; y < 128; y++) for (int x = 0; x < 256; x++) tiles.SetValue(Activator.CreateInstance(tileType), x, y);
    Set(main, null, "tile", tiles);
    var npcs = Array.CreateInstance(npcType, 200);
    for (int i = 0; i < 200; i++) npcs.SetValue(i == 1 ? npc : Activator.CreateInstance(npcType), i);
    Set(main, null, "npc", npcs);
    Set(npcType, npc, "whoAmI", 1);
    object random = Activator.CreateInstance(T("Terraria.Utilities.UnifiedRandom"), [seed])!;
    Set(main, null, "rand", random);
    object State()
    {
        string[] Vec(string name)
        {
            object v = Get(npcType, npc, name);
            return Vector((float)vector.GetField("X")!.GetValue(v)!, (float)vector.GetField("Y")!.GetValue(v)!);
        }
        int I(string name) => Convert.ToInt32(Get(npcType, npc, name));
        bool B(string name) => (bool)Get(npcType, npc, name);
        string F(string name) => Bits((float)Get(npcType, npc, name));
        return new { npc = new { type = I("type"), netId = I("netID"), aiStyle = I("aiStyle"), width = I("width"), height = I("height"),
            position = Vec("position"), oldPosition = Vec("oldPosition"), velocity = Vec("velocity"), oldVelocity = Vec("oldVelocity"),
            ai = ((float[])Get(npcType, npc, "ai")).Select(Bits).ToArray(), localAi = ((float[])Get(npcType, npc, "localAI")).Select(Bits).ToArray(),
            direction = I("direction"), directionY = I("directionY"), spriteDirection = I("spriteDirection"), target = I("target"),
            life = I("life"), lifeMax = I("lifeMax"), timeLeft = I("timeLeft"), scale = F("scale"), rotation = F("rotation"),
            knockBackResist = F("knockBackResist"), alpha = I("alpha"), active = B("active"), justHit = B("justHit"),
            collideX = B("collideX"), collideY = B("collideY"), wet = B("wet"), noGravity = B("noGravity"), noTileCollide = B("noTileCollide") }, rng = Rng(random) };
    }
    input = State();
    MethodInfo method = npcType.GetMethod("AI_003_Fighters", flags)!;
    for (int frame = 0; frame < 16; frame++)
    {
        Set(npcType, npc, "justHit", Hit(frame));
        object before = State();
        method.Invoke(npc, null);
        rawAfterAiJustHit.Add((bool)Get(npcType, npc, "justHit"));
        // The production proposal retires the pulse. Official NPC.Update retires
        // it after AI/FindFrame/CheckActive; apply only this named external
        // carrier operation, preserving raw direct-AI flags in provenance.
        Set(npcType, npc, "justHit", false);
        frames.Add(new { index = frame, justHit = Hit(frame), before, after = State() });
    }
    provenance = new { entry = "Terraria.NPC.AI_003_Fighters", control, rawAfterAiJustHit, carrierRetirement = retirement,
        officialAssemblySha256 = admitted, dependenciesSha256 = dependencies,
        probeSha256 = Hash(Assembly.GetExecutingAssembly().Location),
        methodIlSha256 = Convert.ToHexStringLower(SHA256.HashData(method.GetMethodBody()!.GetILAsByteArray()!)) };
}
CheckReference();
var after = Closure();
if (closure.Count != after.Count || closure.Any(item => !after.TryGetValue(item.Key, out string? actual) || actual != item.Value))
    throw new InvalidDataException("Probe/runtime DLL closure changed");
var observation = new { schemaVersion = 1, kind = "npc-behavior-observation", engine,
    scenario = "ai003-ice-golem-airborne-v1", seed, context, input, frames, provenance, executedDlls = closure };
using (var stream = new FileStream(outputPath, FileMode.CreateNew)) JsonSerializer.Serialize(stream, observation);
Console.WriteLine($"Captured {engine} NPC AI003 seed={seed} frames=16 control={control}");

sealed class RefuseFallback : INpcAiStateStepper
{
    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
}
sealed class NpcRandom(int seed) : IVanillaNpcRandom
{
    public VanillaUnifiedRandom1458 Source { get; } = new(seed);
    public int NextInt32(int inclusiveMin, int exclusiveMax) => Source.Next(inclusiveMin, exclusiveMax);
    public double NextDouble() => Source.NextDouble();
}
