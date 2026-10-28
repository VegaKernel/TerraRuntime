using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;

// Developer-only adapter to internal existing owners, without a shipping friend
// assembly or a second stock balance. All mutations go through real commands.
internal static class OwnedAmmoProbe
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static object Get(object owner, string name) => Get(owner.GetType(), owner, name);
    private static object Get(Type type, object? owner, string name) => type.GetField(name, Flags)?.GetValue(owner) ??
        type.GetProperty(name, Flags)?.GetValue(owner) ?? throw new MissingMemberException(type.FullName, name);
    private static void Set(Type type, object? owner, string name, object value)
    {
        if (type.GetField(name, Flags) is { } field) field.SetValue(owner, value);
        else if (type.GetProperty(name, Flags) is { } property) property.SetValue(owner, value);
        else throw new MissingMemberException(type.FullName, name);
    }
    private static object New(Type type, Dictionary<string, object?> values)
    {
        values = new(values, StringComparer.OrdinalIgnoreCase);
        var constructor = type.GetConstructors(Flags).Where(c => c.GetParameters().All(p => values.ContainsKey(p.Name!) || p.HasDefaultValue))
            .OrderByDescending(c => c.GetParameters().Length).First();
        return constructor.Invoke(constructor.GetParameters().Select(p => values.TryGetValue(p.Name!, out object? value) ? value : p.DefaultValue).ToArray());
    }
    private static object Rng(object random)
    {
        Type type = random.GetType();
        return new { algorithm = "terraria1458-unifiedrandom-inext21", ownerAlias = new[] { "Main.rand" }, inext = Get(type, random, "inext"), seedArray = ((int[])Get(type, random,
            type.GetField("SeedArray", Flags) is null ? "seedArray" : "SeedArray")).ToArray() };
    }
    internal static void Run(string[] args)
    {
        string output = Path.GetFullPath(args[3]);
        if (File.Exists(output)) throw new IOException("Output exists");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        int seed = int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
        string engine = args[0], control = args[5];
        if (engine is not ("official" or "runtime") || control is not ("normal" or "omit-step" or "extra-rng") || engine == "official" && control != "normal") throw new ArgumentException("Unknown engine/control");
        string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        using var pin = JsonDocument.Parse(File.ReadAllBytes(args[2]));
        string officialHash = pin.RootElement.GetProperty("assemblySha256").GetString()!;
        string officialPath = Path.GetFullPath(args[1]);
        var dependencies = pin.RootElement.GetProperty("dependenciesSha256").EnumerateObject().ToDictionary(v => v.Name, v => v.Value.GetString()!);
        void CheckOfficial()
        {
            if (Hash(officialPath) != officialHash) throw new InvalidDataException("Official assembly changed");
            foreach (var dependency in dependencies)
                if (Hash(Path.Combine(Path.GetDirectoryName(officialPath)!, dependency.Key)) != dependency.Value) throw new InvalidDataException("Official dependency changed");
        }
        CheckOfficial();
        Dictionary<string, string> Dlls() => Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(AppContext.BaseDirectory, p).Replace('\\', '/'), Hash);
        var dlls = Dlls();
        var frames = new List<object>();
        object? input = null;
        string? runtimeHash = null;
        if (engine == "runtime") Runtime(); else Official();
        CheckOfficial();
        var afterDlls = Dlls();
        if (dlls.Count != afterDlls.Count || dlls.Any(v => !afterDlls.TryGetValue(v.Key, out string? digest) || digest != v.Value)) throw new InvalidDataException("Executed DLL closure changed");
        var provenance = new Dictionary<string, object> { ["entry"] = engine == "official" ? "Terraria.Player.Update" : "TerraRuntime.Application.ServerRuntimeState.Tick/PlayerAuthority.TickRemotePlayerPhase/RuntimePlayerInventoryStore",
            ["control"] = control, ["probeSha256"] = Hash(Assembly.GetExecutingAssembly().Location), ["officialAssemblySha256"] = officialHash, ["dependenciesSha256"] = dependencies };
        if (runtimeHash is not null) provenance["runtimeAssemblySha256"] = runtimeHash;
        using var stream = new FileStream(output, FileMode.CreateNew);
        JsonSerializer.Serialize(stream, new { schemaVersion = 1, kind = "gameplay-behavior-observation", engine, seed, scenario = "owned-bow-player-update-v1",
            context = new { steps = 65, weaponType = 39, ammoType = 40, ammoSlot = 54, initialAmmo = 4, widthTiles = 400, heightTiles = 300, worldSurface = 80,
                netMode = 2, dedServ = true, gameMenu = true, myPlayer = 0, fallStart = 51,
                sourceCaller = "original-headless-local-Player.Update-not-UpdateWorld_Players-or-GUI", runtimeCaller = "real-joined-LocalOwnerProjection-command-then-State.Tick",
                runtimeStock = "trusted-reserved-world-drop-then-real-owned-contact-pickup",
                controls = "held:1,2,32,33;released:all-other-frames", terrain = "empty-no-cracked-or-thin-ice", positionInput = new[] { 736f, 822f },
                projection = "58x(type,stack,prefix,favoriteFlag);item-clocks;manaHeatBits;physical-Main.rand",
                publication = "original-shoot-versus-runtime-paid-receipt-before-delayed-packet27" },
            input = input ?? throw new InvalidDataException("Missing observed input"), frames, provenance, executedDlls = dlls }, new JsonSerializerOptions { WriteIndented = true });

        void Runtime()
        {
            Assembly app = Assembly.Load("TerraRuntime.Application");
            runtimeHash = Hash(app.Location);
            Type A(string name) => app.GetType("TerraRuntime.Application." + name, true)!;
            object Make(string name, Dictionary<string, object?> values) => New(A(name), values);
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            object world = Activator.CreateInstance(A("RuntimePlayerUpdateWorld1458"), [400, 300, 80d, false, false])!;
            foreach (string field in new[] { "GetGoodWorld", "InfectedSeed", "NotTheBeesWorld", "NoTrapsWorld", "TenthAnniversaryWorld", "DontStarveWorld", "DrunkWorld", "JourneyMode", "OnlyShimmerOceanWorlds" })
                Set(world.GetType(), world, field, false);
            Set(world.GetType(), world, "HallowBackground", 0);
            object clock = Make("RuntimeWorldClock", new() { ["time"] = 1000d, ["dayTime"] = true,
                ["moonPhase"] = Activator.CreateInstance(A("RuntimeWorldClock").GetConstructors().Single().GetParameters()[2].ParameterType), ["slimeRainTime"] = 0d, ["dayRate"] = 0 });
            object healthWorld = Activator.CreateInstance(A("RuntimeTownCommerceWorldFacts1458"))!;
            Set(healthWorld.GetType(), healthWorld, "WorldSurface", 80d);
            var groundItems = new RuntimeWorldItemStore();
            object state = Make("ServerRuntimeState", new() { ["worldTiles"] = tiles, ["worldClock"] = clock, ["worldItems"] = groundItems,
                ["sourcePlayerWorld"] = world, ["playerUpdateRandomSeed"] = Activator.CreateInstance(A("PlayerUpdateRandomSeed1458"), [seed]),
                ["townSpawnWorldFacts"] = Activator.CreateInstance(typeof(TerraRuntime.Gameplay.Npcs.VanillaTownSpawnWorldFacts1458)),
                ["townCommerceWorldFacts"] = healthWorld });
            var apply = state.GetType().GetMethod("Apply", Flags)!;
            void Apply(string command, Dictionary<string, object?> values) => apply.Invoke(state, [Make(command, values)]);
            object permission = Make("RuntimeToolBuildingPermission1458", new() { ["tiles"] = tiles });
            permission.GetType().GetMethod("TrySetSourceEvent", Flags)!.Invoke(permission, [false, null]);
            state.GetType().GetMethod("BindToolBuildingPermission", Flags)!.Invoke(state, [permission]);
            var pool = new PlayerSlotPool(1);
            if (!pool.TryAcquireConnection(out var lease)) throw new InvalidOperationException("No player lease");
            using var session = new PlayerJoinSession(lease!);
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9917), session.Handle);
            for (short slot = 0; slot < 59; slot++)
                Apply("PlayerEquipmentRuntimeCommand", new() { ["Connection"] = connection,
                    ["Request"] = new PlayerEquipmentCommitRequest(session.Slot, slot, slot == 0 ? (short)1 : (short)0, 0, slot == 0 ? (short)39 : (short)0, 0) });
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Apply("PlayerSpawnRuntimeCommand", new() { ["Connection"] = connection, ["Session"] = session,
                ["Request"] = new PlayerSpawnCommitRequest(session.Slot, 50, 52, 0, 0, 0, 0, 0) });
            Apply("PlayerHealthRuntimeCommand", new() { ["Connection"] = connection, ["Request"] = new PlayerHealthCommitRequest(session.Slot, 100, 100) });
            Apply("PlayerManaRuntimeCommand", new() { ["Connection"] = connection, ["Request"] = new PlayerManaCommitRequest(session.Slot, 0, 20) });
            Apply("PlayerInventorySyncCompleteRuntimeCommand", new() { ["Connection"] = connection });
            object players = Get(Get(state, "_runtime"), "Players");
            object member = ((IEnumerable)Get(players, "Members")).Cast<object>().Single();
            object inventory = Get(players, "inventory");
            object initialMovement = Make("PlayerMovementRuntimeCommand", new() { ["Connection"] = connection,
                ["Request"] = new PlayerMovementCommitRequest(session.Slot, 0, 16, 0, 64, 0, 736f, 822f, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0) });
            Set(initialMovement.GetType(), initialMovement, "ItemUseCaller", Enum.Parse(A("RuntimeItemUseCaller1458"), "LocalOwnerProjection"));
            apply.Invoke(state, [initialMovement]);
            // Establish funding through the actual trusted ground-item producer
            // and contact pickup owner. A raw imported packet5 is not funding.
            if (!groundItems.TryReserveDropSlot(out var reservation) ||
                !groundItems.TryCommitReservedDrop(reservation, new WorldItemDropStateUpdate(736f, 822f, 0f, 0f, 4, 0, default, 40, false, 0, 0), out var drop) ||
                !groundItems.TryApplyOwner(drop.Handle.Slot, new WorldItemOwnerStateUpdate(session.Slot.Value, 0, byte.MaxValue, 0, 736f, 822f), out _))
                throw new InvalidDataException("Trusted ammo source preparation failed");
            object pickup = Get(Get(state, "_runtime"), "WorldItems");
            pickup.GetType().GetMethod("TickOwnedHumanPickups", Flags)!.Invoke(pickup, null);
            if (groundItems.ActiveCount != 0) throw new InvalidDataException("Real owned contact pickup did not consume the funded drop");
            object Snapshot()
            {
                object phase = Get(member, "ItemPhase"), tools = Get(phase, "ToolUse"), clocks = Get(tools, "Clocks"), selected = Get(clocks, "Selected");
                var items = (Array)Get(inventory, "items");
                return new { inventory = Enumerable.Range(0, 58).Select(i => new[] { Convert.ToInt32(Get(Get(items.GetValue(i)!, "ItemType"), "Value")), Convert.ToInt32(Get(items.GetValue(i)!, "Stack")),
                        Convert.ToInt32(Get(Get(items.GetValue(i)!, "Prefix"), "Value")), Convert.ToInt32(Get(items.GetValue(i)!, "ItemFlags")) }).ToArray(),
                    animation = Get(selected, "Animation"), animationMax = Get(selected, "AnimationMax"), itemTime = Get(selected, "ItemTime"), itemTimeMax = Get(selected, "ItemTimeMax"),
                    release = Get(selected, "ReleaseUseItem"), reuse = Get(clocks, "ReuseDelay"), manaHeatBits = BitConverter.SingleToInt32Bits((float)Get(phase, "ManaHeat")), rng = Rng(Get(players, "playerUpdateRandom")) };
            }
            object Proof()
            {
                if ((ConnectionHandle)Get(member, "Connection") != connection) throw new InvalidDataException("Player generation changed");
                object phase = Get(member, "ItemPhase"), tools = Get(phase, "ToolUse");
                object? history = tools.GetType().GetProperty("WeaponShootHistory", Flags)!.GetValue(tools);
                int paid = history is null ? 0 : ((Array)Get(history, "Uses")).Length;
                object?[] quantity = [connection, 54, (short)0];
                if (!(bool)inventory.GetType().GetMethod("TryGetFundedQuantity", Flags)!.Invoke(inventory, quantity)!) throw new InvalidDataException("Missing funded owner");
                return new { kind = "owned-authority", connectionAssigned = connection.IsAssigned, knownPhase = true,
                    knownClocks = (bool)Get(tools, "LocalClocksKnown"), fundedAmmo = (short)quantity[2]!, paidUses = paid };
            }
            for (int step = 0; step < 65; step++)
            {
                bool held = step is >= 1 and < 3 or >= 32 and < 34;
                object movement = Make("PlayerMovementRuntimeCommand", new() { ["Connection"] = connection,
                    ["Request"] = new PlayerMovementCommitRequest(session.Slot, held ? (byte)32 : (byte)0, 16, 0, 64, 0, 736f, 822f, false, 0f, 0f, false, 0, false, 0, 0, 0, 0, false, 0, 0) });
                Set(movement.GetType(), movement, "ItemUseCaller", Enum.Parse(A("RuntimeItemUseCaller1458"), "LocalOwnerProjection"));
                apply.Invoke(state, [movement]);
                object before = Snapshot();
                if (step == 0) input = before;
                if (!(control == "omit-step" && step == 32)) state.GetType().GetMethod("Tick", Flags)!.Invoke(state, null);
                if (control == "extra-rng" && step == 32) Get(players, "playerUpdateRandom").GetType().GetMethod("Next", Type.EmptyTypes)!.Invoke(Get(players, "playerUpdateRandom"), null);
                frames.Add(new { step, held, before, after = Snapshot(), proof = Proof() });
            }
        }
        void Official()
        {
            string path = Path.GetFullPath(args[1]), directory = Path.GetDirectoryName(path)!;
            Assembly? game = null;
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                string adjacent = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(adjacent))
                {
                    if (!dependencies.TryGetValue(Path.GetFileName(adjacent), out string? expected) || Hash(adjacent) != expected) throw new InvalidDataException("Unadmitted official dependency");
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(adjacent);
                }
                string? resource = game?.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith("." + name.Name + ".dll", StringComparison.OrdinalIgnoreCase));
                if (resource is null) return null;
                using var stream = game!.GetManifestResourceStream(resource)!;
                return AssemblyLoadContext.Default.LoadFromStream(stream);
            };
            game = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            Type G(string name) => game.GetType(name, true)!;
            Set(G("Terraria.Program"), null, "SavePath", Path.Combine(Path.GetDirectoryName(output)!, "probe-state"));
            Type main = G("Terraria.Main"), playerType = G("Terraria.Player"), itemType = G("Terraria.Item");
            foreach (var (name, value) in new (string, object)[] { ("dedServ", true), ("netMode", 2), ("gameMenu", true), ("myPlayer", 0), ("maxTilesX", 400), ("maxTilesY", 300), ("worldSurface", 80d), ("remixWorld", false),
                ("leftWorld", 0f), ("rightWorld", 6400f), ("topWorld", 0f), ("bottomWorld", 4800f), ("rockLayer", 120d), ("dayTime", true), ("time", 1000d) }) Set(main, null, name, value);
            Set(main, null, "ActiveWorldFileData", Activator.CreateInstance(G("Terraria.IO.WorldFileData"))!);
            if ((string)Get(main, null, "versionNumber") != "v1.4.5.8") throw new InvalidDataException("Unexpected version");
            Set(main, null, "GameMode", 0);
            object view = Activator.CreateInstance(G("Terraria.Graphics.SpriteViewMatrix"), [null])!;
            Set(view.GetType(), view, "_needsRebuild", false);
            Set(view.GetType(), view, "_overrideSystemViewport", true);
            Set(main, null, "GameViewMatrix", view);
            Set(main, null, "instance", System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(main));
            main.GetMethod("Initialize_TileAndNPCData1", Flags)!.Invoke(null, null);
            Set(main, null, "rand", Activator.CreateInstance(G("Terraria.Utilities.UnifiedRandom"), [0])!);
            main.GetMethod("Initialize_TileAndNPCData2", Flags)!.Invoke(null, null);
            G("Terraria.GameContent.Creative.CreativePowerManager").GetMethod("Initialize")!.Invoke(null, null);
            G("Terraria.DataStructures.ArmorSetBonuses").GetMethod("Initialize")!.Invoke(null, null);
            G("Terraria.DataStructures.ArmorSetBonuses").GetMethod("BuildLookup")!.Invoke(null, null);
            object triggers = Get(G("Terraria.GameInput.PlayerInput"), null, "Triggers");
            triggers.GetType().GetMethod("Initialize")!.Invoke(triggers, null);
            Set(G("Terraria.GameInput.PlayerInput"), null, "_currentProfile", Activator.CreateInstance(G("Terraria.GameInput.PlayerInputProfile"), ["offline-owned-profile"])!);
            foreach (string name in new[] { "sittingManager", "sleepingManager" }) Set(main, null, name, Activator.CreateInstance(main.GetField(name, Flags)!.FieldType)!);
            foreach (var (owner, name, type, count) in new[] { (main, "player", playerType, 256), (main, "npc", G("Terraria.NPC"), 201), (main, "projectile", G("Terraria.Projectile"), 1001),
                (main, "dust", G("Terraria.Dust"), 6001), (main, "item", G("Terraria.WorldItem"), 401),
                (G("Terraria.NetMessage"), "buffer", G("Terraria.MessageBuffer"), 257), (G("Terraria.Netplay"), "Clients", G("Terraria.RemoteClient"), 256) })
            {
                var values = Array.CreateInstance(type, count);
                for (int i = 0; i < count; i++)
                {
                    object value = Activator.CreateInstance(type)!;
                    if (type.GetField("whoAmI", Flags) is { } id) id.SetValue(value, i);
                    values.SetValue(value, i);
                }
                Set(owner, null, name, values);
            }
            var tileType = G("Terraria.Tile"); var tiles = Array.CreateInstance(tileType, 400, 300);
            for (int x = 0; x < 400; x++) for (int y = 0; y < 300; y++) tiles.SetValue(Activator.CreateInstance(tileType), x, y);
            Set(main, null, "tile", tiles);
            object player = ((Array)Get(main, null, "player")).GetValue(0)!;
            Set(playerType, player, "active", true); Set(playerType, player, "whoAmI", 0);
            Set(playerType, player, "statLife", 100); Set(playerType, player, "statLifeMax2", 100);
            Set(playerType, player, "statMana", 0); Set(playerType, player, "statManaMax2", 20);
            Set(playerType, player, "position", Activator.CreateInstance(G("Terraria.Entity").GetField("position")!.FieldType, [736f, 822f])!);
            Set(playerType, player, "fallStart", 51);
            var inventory = (Array)Get(player, "inventory");
            var defaults = itemType.GetMethod("SetDefaults", [typeof(int), G("Terraria.GameContent.Items.ItemVariant")])!;
            foreach (var (slot, type, stock) in new[] { (0, 39, 1), (54, 40, 4) }) { defaults.Invoke(inventory.GetValue(slot), [type, null]); Set(itemType, inventory.GetValue(slot), "stack", stock); }
            object random = Activator.CreateInstance(G("Terraria.Utilities.UnifiedRandom"), [seed])!; Set(main, null, "rand", random);
            object Snapshot() => new { inventory = Enumerable.Range(0, 58).Select(i => new[] { (int)Get(inventory.GetValue(i)!, "type"), (int)Get(inventory.GetValue(i)!, "stack"),
                    (int)(byte)Get(inventory.GetValue(i)!, "prefix"), (bool)Get(inventory.GetValue(i)!, "favorited") ? 1 : 0 }).ToArray(),
                animation = Get(player, "itemAnimation"), animationMax = Get(player, "itemAnimationMax"), itemTime = Get(player, "itemTime"), itemTimeMax = Get(player, "itemTimeMax"),
                release = Get(player, "releaseUseItem"), reuse = Get(player, "reuseDelay"), manaHeatBits = BitConverter.SingleToInt32Bits((float)Get(player, "manaHeat")), rng = Rng(random) };
            for (int step = 0; step < 65; step++)
            {
                bool held = step is >= 1 and < 3 or >= 32 and < 34;
                Set(playerType, player, "controlUseItem", held);
                Set(Get(triggers, "Current").GetType(), Get(triggers, "Current"), "MouseLeft", held);
                Set(playerType, player, "position", Activator.CreateInstance(G("Terraria.Entity").GetField("position")!.FieldType, [736f, 822f])!);
                object before = Snapshot();
                if (step == 0) input = before;
                playerType.GetMethod("Update", Flags)!.Invoke(player, [0]);
                if ((bool)Get(player, "dead") || !(bool)Get(player, "active")) throw new InvalidDataException("Original player lifecycle left the admitted profile");
                int projectiles = ((Array)Get(main, null, "projectile")).Cast<object>().Count(p => (bool)Get(p, "active"));
                frames.Add(new { step, held, before, after = Snapshot(), proof = new { kind = "original-player", active = true, dead = false, life = (int)Get(player, "statLife"), projectileCount = projectiles } });
            }
        }
    }
}
