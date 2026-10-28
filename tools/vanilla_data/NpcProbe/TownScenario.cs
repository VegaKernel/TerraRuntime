using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.World;

// Reflection is confined to this offline adapter. Both simulation bodies remain
// in their original assemblies; no shipping visibility or AI implementation is added.
internal static class TownScenario
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public static void Run(string[] args)
    {
        string engine = args[0], official = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
        int seed = int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
        string control = args[5];
        if (engine is not ("official" or "runtime") || control is not ("normal" or "omit-step" or "extra-rng") ||
            engine == "official" && control != "normal") throw new ArgumentException("Unsupported role/control");
        if (File.Exists(output)) throw new IOException("Observation already exists");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        using var pin = JsonDocument.Parse(File.ReadAllBytes(args[2]));
        string admitted = pin.RootElement.GetProperty("assemblySha256").GetString()!;
        var dependencies = pin.RootElement.GetProperty("dependenciesSha256").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString()!);
        string directory = Path.GetDirectoryName(official)!;
        void Check()
        {
            if (Hash(official) != admitted) throw new InvalidDataException("Official assembly differs");
            foreach (var item in dependencies)
                if (Hash(Path.Combine(directory, item.Key)) != item.Value) throw new InvalidDataException("Official dependency differs");
        }
        Dictionary<string, string> Closure() => Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(AppContext.BaseDirectory, path).Replace('\\', '/'), Hash);
        Check(); var closure = Closure();
        string F(float value) => BitConverter.SingleToUInt32Bits(value).ToString("x8");
        string D(double value) => BitConverter.DoubleToUInt64Bits(value).ToString("x16");
        string[] V(float x, float y) => [F(x), F(y)];
        string[] A(NpcAiState ai) => [F(ai.Ai0), F(ai.Ai1), F(ai.Ai2), F(ai.Ai3)];
        object Rng(object source)
        {
            Type t = source.GetType();
            return new { algorithm = "terraria1458-unifiedrandom-inext21", owner = "Main.rand",
                inext = Convert.ToInt32(t.GetField("inext", Flags)!.GetValue(source)),
                seedArray = ((int[])(t.GetField("SeedArray", Flags) ?? t.GetField("seedArray", Flags))!.GetValue(source)!).ToArray() };
        }
        var frames = new List<object>();
        object input, provenance;
        var context = new { frames = 496, widthTiles = 100, heightTiles = 80, worldSurface = 20,
            floorY = 30, floorType = 1, netMode = 2, dedServ = true, gameMenu = false, dayTime = true,
            hardMode = false, expert = false, master = false, players = "all256inactive", peers = "oneDryad",
            home = new { x = 40, y = 30, homeless = false },
            carrier = "actual NPC.UpdateNPC versus actual ServerRuntimeState.Tick; no player/clock; no animation initiation" };
        if (engine == "runtime")
        {
            Assembly app = Assembly.Load("TerraRuntime.Application");
            Type T(string name) => app.GetType("TerraRuntime.Application." + name, true)!;
            object Make(string name, params object?[] values) => Activator.CreateInstance(T(name), Flags, null, values, null)!;
            var tiles = new WorldTileStore(new WorldDimensions(100, 80));
            if (!(bool)typeof(WorldTileStore).GetMethod("TryAttachWorldSurface", Flags)!.Invoke(tiles, [20d])!)
                throw new InvalidDataException("Cannot attach explicit surface context");
            for (int x = 0; x < 100; x++) tiles.Set(x, 30, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var persistence = new WorldNpcPersistence([], [new WorldTownNpc(20, "Dryad", 639f, 440f, false, 40, 30, null, false)], []);
            object town = Make("RuntimeTownNpcStateStore", persistence, new WorldTownRoom[] { new(20, 40, 30) }, tiles.Dimensions);
            var npcs = new RuntimeNpcStore();
            if (!(bool)T("RuntimeTownNpcStateStore").GetMethod("TryReserveRuntimeSlots")!.Invoke(town, [npcs])! || !npcs.TryGetActive(0, out var npc))
                throw new InvalidOperationException("Cannot initialize resident");
            var seeded = new NpcStateUpdate(20, 20, 639f, 440f, .5f, 0f, 255, new(24f, 480f, 0f, 0f), npc.Simulation with {
                OldPositionX = 639f, OldPositionY = 440f, OldVelocityX = .5f, OldVelocityY = 0f,
                DirectionX = 1, DirectionY = -1, SpriteDirection = 1, TimeLeft = 750,
                HomeTileX = 40, HomeTileY = 30, Homeless = false, LocalAi = new(0f, 0f, 480f, 0f), FrameCounter = 0d, FrameIndex = 0 });
            if (!npcs.TryUpdate(npc.Handle, in seeded, out npc)) throw new InvalidOperationException("Cannot seed continuation");
            var random = new TownRandom(seed);
            object registry = Make("RuntimeNpcReplicationRegistry");
            var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(1024, 1024 * 1024, 65536));
            var source = GameCommandSourceId.FromConnection(9001);
            T("RuntimeNpcReplicationRegistry").GetMethod("TryRegister")!.Invoke(registry, [source, queue]);
            var handle = new ConnectionHandle(source, new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
            var spawn = new PlayerSpawnCommitRequest(handle.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
            T("RuntimeNpcReplicationRegistry").GetMethod("PlayerSpawned")!.Invoke(registry, [handle, spawn]);
            ConstructorInfo ctor = T("ServerRuntimeState").GetConstructors(Flags).Single();
            object?[] ctorArgs = ctor.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
            foreach (var value in new (string, object)[] { ("npcs", npcs), ("worldTiles", tiles), ("townNpcs", town),
                ("naturalSpawnRandom", random), ("npcReplication", registry) })
                ctorArgs[Array.FindIndex(ctor.GetParameters(), p => p.Name == value.Item1)] = value.Item2;
            object runtime = ctor.Invoke(ctorArgs);
            var ownedQueue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetField("_queue", Flags)!.GetValue(queue)!;
            List<object> Drain()
            {
                var result = new List<object>();
                while (ownedQueue.TryRead(out OutboundFrame packet))
                {
                    byte[] bytes = packet.Bytes.ToArray();
                    if (bytes[2] != 91) continue;
                    if (bytes.Length != 13 || bytes[7] != 0) throw new InvalidDataException("Unknown emitted emote packet");
                    result.Add(new { id = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(3)),
                        slot = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8)),
                        lifetime = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10)), emote = bytes[12] });
                }
                return result;
            }
            object State()
            {
                if (!npcs.TryGetActive(0, out npc)) throw new InvalidDataException("Resident disappeared");
                NpcSimulationState s = npc.Simulation;
                if (s.Rotation is not float rotation || s.KnockBackResist is not float knockback || s.FrameIndex is not int frame ||
                    s.BaseDefense is not int defense || s.BaseDamage is not int damage || s.Breath is not int breath ||
                    s.HomeTileX is not int homeX || s.HomeTileY is not int homeY || s.Homeless is not bool homeless ||
                    s.LifeRegenCounter is not int regen) throw new InvalidDataException("Unknown runtime observed state");
                return new { npc = new { type = npc.Type, netId = npc.NetId, position = V(npc.PositionX, npc.PositionY),
                    oldPosition = V(s.OldPositionX, s.OldPositionY), velocity = V(npc.VelocityX, npc.VelocityY),
                    oldVelocity = V(s.OldVelocityX, s.OldVelocityY), ai = A(npc.Ai), localAi = A(s.LocalAi),
                    direction = s.DirectionX, directionY = s.DirectionY, spriteDirection = s.SpriteDirection,
                    target = npc.Target, life = s.Life, lifeMax = s.LifeMax, timeLeft = s.TimeLeft,
                    scale = F(s.Scale), rotation = F(rotation), knockBackResist = F(knockback),
                    defense = s.DefenseOverride ?? defense, damage = s.DamageOverride ?? damage,
                    frameIndex = frame, frameCounter = D(s.FrameCounter), homeX, homeY, homeless,
                    breath, lifeRegenCounter = regen, friendlyRegenerationCounter = s.FriendlyRegenerationCounter,
                    justHit = s.JustHit, collideX = s.CollideX, collideY = s.CollideY,
                    wet = s.Wet, noGravity = s.NoGravity, noTileCollide = s.NoTileCollide }, rng = Rng(random.Source) };
            }
            Drain(); input = State();
            for (int i = 0; i < 496; i++)
            {
                object before = State();
                if (control != "omit-step") T("ServerRuntimeState").GetMethod("Tick")!.Invoke(runtime, null);
                if (control == "extra-rng") random.Source.Next();
                frames.Add(new { index = i, before, after = State(), emotes = Drain() });
            }
            provenance = new { entry = "TerraRuntime.Application.ServerRuntimeState.Tick/RuntimeTownNpcSchedule1458", control,
                officialAssemblySha256 = admitted, dependenciesSha256 = dependencies,
                probeSha256 = Hash(Assembly.GetExecutingAssembly().Location), runtimeAssemblySha256 = Hash(app.Location) };
        }
        else
        {
            Assembly? game = null;
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path))
                {
                    if (!dependencies.TryGetValue(Path.GetFileName(path), out var expected) || Hash(path) != expected) throw new InvalidDataException("Unadmitted dependency");
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                }
                string? resource = game?.GetManifestResourceNames().SingleOrDefault(r => r.EndsWith("." + name.Name + ".dll", StringComparison.OrdinalIgnoreCase));
                if (resource is null) return null;
                using var stream = game!.GetManifestResourceStream(resource)!;
                return AssemblyLoadContext.Default.LoadFromStream(stream);
            };
            game = AssemblyLoadContext.Default.LoadFromAssemblyPath(official);
            Type T(string name) => game.GetType("Terraria." + name, true)!;
            object Get(Type t, object? o, string name) => t.GetField(name, Flags)!.GetValue(o)!;
            void Set(Type t, object? o, string name, object value) => t.GetField(name, Flags)!.SetValue(o, value);
            Type main = T("Main"), npcType = T("NPC"), tileType = T("Tile"), playerType = T("Player");
            Set(T("Program"), null, "SavePath", Path.Combine(Path.GetDirectoryName(output)!, "town-state"));
            foreach (var p in new (string, object)[] { ("maxTilesX", 100), ("maxTilesY", 80), ("worldSurface", 20d),
                ("bottomWorld", 1280f), ("rightWorld", 1600f), ("dedServ", true), ("netMode", 2), ("gameMenu", false),
                ("dayTime", true), ("hardMode", false), ("raining", false), ("eclipse", false), ("slimeRain", false) }) Set(main, null, p.Item1, p.Item2);
            var tiles = Array.CreateInstance(tileType, 100, 80);
            for (int y = 0; y < 80; y++) for (int x = 0; x < 100; x++)
            {
                object cell = Activator.CreateInstance(tileType)!;
                if (y == 30) { tileType.GetMethod("active", [typeof(bool)])!.Invoke(cell, [true]); Set(tileType, cell, "type", (ushort)1); }
                tiles.SetValue(cell, x, y);
            }
            Set(main, null, "tile", tiles); ((bool[])Get(main, null, "tileSolid"))[1] = true;
            var players = Array.CreateInstance(playerType, 256);
            for (int i = 0; i < 256; i++) { object p = Activator.CreateInstance(playerType)!; Set(playerType, p, "active", false); players.SetValue(p, i); }
            Set(main, null, "player", players);
            var npcs = Array.CreateInstance(npcType, 200);
            for (int i = 0; i < 200; i++) npcs.SetValue(Activator.CreateInstance(npcType), i);
            Set(main, null, "npc", npcs);
            // Dedicated server packet serialization still needs its bounded
            // buffers even with zero connected clients; broadcast remains false.
            Type messageType = T("MessageBuffer");
            var buffers = Array.CreateInstance(messageType, 257);
            for (int i = 0; i < 257; i++) buffers.SetValue(Activator.CreateInstance(messageType), i);
            Set(T("NetMessage"), null, "buffer", buffers);
            Type clientType = T("RemoteClient");
            var clients = Array.CreateInstance(clientType, 256);
            for (int i = 0; i < 256; i++) clients.SetValue(Activator.CreateInstance(clientType), i);
            Set(T("Netplay"), null, "Clients", clients);
            object npc = npcs.GetValue(0)!;
            npcType.GetMethod("SetDefaults", [typeof(int), T("NPCSpawnParams")])!.Invoke(npc, [20, Activator.CreateInstance(T("NPCSpawnParams"))]);
            Type vector = npcType.GetField("position")!.FieldType;
            object Vec(float x, float y) => Activator.CreateInstance(vector, [x, y])!;
            foreach (var p in new (string, object)[] { ("active", true), ("whoAmI", 0), ("position", Vec(639f, 440f)),
                ("oldPosition", Vec(639f, 440f)), ("velocity", Vec(.5f, 0f)), ("oldVelocity", Vec(.5f, 0f)),
                ("ai", new float[] { 24, 480, 0, 0 }), ("localAI", new float[] { 0, 0, 480, 0 }),
                ("direction", 1), ("directionY", -1), ("spriteDirection", 1), ("target", 255), ("timeLeft", 750),
                ("homeTileX", 40), ("homeTileY", 30), ("homeless", false), ("frameCounter", 0d), ("breath", 200) }) Set(npcType, npc, p.Item1, p.Item2);
            object random = Activator.CreateInstance(T("Utilities.UnifiedRandom"), [seed])!; Set(main, null, "rand", random);
            var bubblesType = T("GameContent.UI.EmoteBubble");
            int lastEmote = 0;
            object State()
            {
                int I(string n) => Convert.ToInt32(Get(npcType, npc, n));
                bool B(string n) => (bool)Get(npcType, npc, n);
                string Fl(string n) => F((float)Get(npcType, npc, n));
                string[] Vector(string n) { object v = Get(npcType, npc, n); return V((float)vector.GetField("X")!.GetValue(v)!, (float)vector.GetField("Y")!.GetValue(v)!); }
                object rect = Get(npcType, npc, "frame");
                return new { npc = new { type = I("type"), netId = I("netID"), position = Vector("position"), oldPosition = Vector("oldPosition"),
                    velocity = Vector("velocity"), oldVelocity = Vector("oldVelocity"), ai = ((float[])Get(npcType, npc, "ai")).Select(F).ToArray(),
                    localAi = ((float[])Get(npcType, npc, "localAI")).Select(F).ToArray(), direction = I("direction"), directionY = I("directionY"),
                    spriteDirection = I("spriteDirection"), target = I("target"), life = I("life"), lifeMax = I("lifeMax"), timeLeft = I("timeLeft"),
                    scale = Fl("scale"), rotation = Fl("rotation"), knockBackResist = Fl("knockBackResist"), defense = I("defense"), damage = I("damage"),
                    frameIndex = (int)rect.GetType().GetField("Y")!.GetValue(rect)!, frameCounter = D((double)Get(npcType, npc, "frameCounter")),
                    homeX = I("homeTileX"), homeY = I("homeTileY"), homeless = B("homeless"), breath = I("breath"), lifeRegenCounter = I("lifeRegenCount"),
                    friendlyRegenerationCounter = I("friendlyRegen"),
                    justHit = B("justHit"), collideX = B("collideX"), collideY = B("collideY"), wet = B("wet"), noGravity = B("noGravity"), noTileCollide = B("noTileCollide") }, rng = Rng(random) };
            }
            input = State();
            for (int i = 0; i < 496; i++)
            {
                object before = State();
                npcType.GetMethod("UpdateNPC")!.Invoke(npc, [0]);
                var emotes = new List<object>();
                var bubbles = (IDictionary)Get(bubblesType, null, "byID");
                int next = (int)Get(bubblesType, null, "NextID");
                while (lastEmote < next)
                {
                    object b = bubbles[lastEmote]!;
                    emotes.Add(new { id = lastEmote++, slot = 0, lifetime = (int)Get(bubblesType, b, "lifeTimeStart"), emote = (int)Get(bubblesType, b, "emote") });
                }
                frames.Add(new { index = i, before, after = State(), emotes });
            }
            provenance = new { entry = "Terraria.NPC.UpdateNPC/AI_007_TownEntities/FindFrame", control,
                officialAssemblySha256 = admitted, dependenciesSha256 = dependencies, probeSha256 = Hash(Assembly.GetExecutingAssembly().Location) };
        }
        Check(); var afterClosure = Closure();
        if (closure.Count != afterClosure.Count || closure.Any(p => !afterClosure.TryGetValue(p.Key, out var h) || h != p.Value)) throw new InvalidDataException("DLL closure changed");
        using var outputStream = new FileStream(output, FileMode.CreateNew);
        JsonSerializer.Serialize(outputStream, new { schemaVersion = 1, kind = "npc-behavior-observation", engine,
            scenario = "ai007-dryad-stardew-v1", seed, context, input, frames, provenance, executedDlls = closure });
        Console.WriteLine($"Captured {engine} AI7 Dryad496 frames seed={seed} control={control}");
    }
    sealed class TownRandom(int seed) : IVanillaNpcRandom
    {
        public VanillaUnifiedRandom1458 Source { get; } = new(seed);
        public int NextInt32(int min, int max) => Source.Next(min, max);
        public double NextDouble() => Source.NextDouble();
    }
}
