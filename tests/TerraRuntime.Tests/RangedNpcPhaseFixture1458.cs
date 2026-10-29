using System.Globalization;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

// The source corpus observes ONE NPC.UpdateNPC, with its other slots held constant.
// This host uses the real composed App steppers and ownership stores at that boundary;
// TickWholeNpcPhase is separately available and includes later newly born higher slots.
internal sealed class RangedNpcPhaseFixture1458 : IDisposable
{
    internal readonly WorldTileStore Tiles;
    internal readonly RuntimeNpcStore Npcs;
    internal readonly RuntimeProjectileStore Projectiles;
    internal readonly VanillaUnifiedRandom1458 Random;
    internal readonly RuntimeNpcReplicationRegistry Registry = new();
    internal readonly RuntimeProjectileReplicationRegistry ProjectileRegistry = new();
    internal readonly ServerRuntimeState World;
    internal readonly RuntimeWorldClock Clock;
    internal readonly RuntimeNpcBuffStatus1458 Status;
    internal readonly VanillaNpcTargetingAiStepper Targeting;
    internal readonly VanillaNpcWorldMotionAiStepper Motion;
    internal readonly ConnectionHandle Connection;
    internal readonly TerrariaConnectionOutboundQueue Outbound = new(new OutboundQueueOptions(512, 1048576, 16384));
    internal Action<NpcSnapshot>? OnNpcBirth = null;
    internal Action<NpcStateCommitKind, NpcSnapshot>? OnNpcCommit = null;
    internal NpcSnapshot LastBirth;
    internal bool RandomInputImported { get; private set; }
    private bool stepped;
    private readonly PlayerJoinSession session;
    private readonly RuntimeNpcAiStateExecutor executor;
    private readonly RuntimeNpcSpawnCycle1458 spawnCycle;
    private readonly NpcAuthority authority;
    private readonly NpcSnapshot[] peers;
    private readonly VanillaNpcRetainedSlot[] retained;

    internal RangedNpcPhaseFixture1458(JsonElement sourceCase)
    {
        string scene = sourceCase.GetProperty("scene").GetString()!;
        int floor = I(sourceCase, "floor"), mode = I(sourceCase, "mode");
        int seed = I(sourceCase, "caseSeed");
        bool day = sourceCase.GetProperty("day").GetBoolean(), remix = sourceCase.GetProperty("remix").GetBoolean();
        Tiles = new(new(192, 200));
        Assert.True(Tiles.TryAttachWorldSurface(80d));
        for (int x = 0; x < 192; x++)
            for (int y = 0; y < 200; y++)
            {
                if (y == floor || scene.StartsWith("stuck", StringComparison.Ordinal) && x == 66 && y >= floor - 4 && y < floor ||
                    scene is "cenx-blocked" or "los-blocked" && x == 69 && y >= floor - 6 && y < floor ||
                    scene.Contains("upper-wall", StringComparison.Ordinal) && x == 68 && y == floor - 4)
                    Tiles.Set(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
                if ((scene == "water" || scene.StartsWith("rider-create-water", StringComparison.Ordinal) ||
                        scene.StartsWith("rain-wet", StringComparison.Ordinal) || scene == "rain-cycle") && y >= floor - 3 && y < floor)
                    Tiles.Set(x, y, Tiles.Get(x, y) with { LiquidAmount = 255 });
            }
        Random = new(seed);
        Npcs = new(200, new RuntimeNpcStateCommitFanout(Registry, new Observer(this)));
        Projectiles = new(commitSink: ProjectileRegistry);
        Clock = new(0d, day, (VanillaMoonPhase)0, 0d, 0, remixWorld: remix);
        var facts = default(RuntimeTownCommerceWorldFacts1458) with
            { GameMode = (byte)mode, InitialDayTime = day, RemixWorld = remix, WorldSurface = 80d, RockLayer = 120d };
        World = new(playerEvents: new RuntimePlayerEventFanout(Registry, ProjectileRegistry),
            npcs: Npcs, projectiles: Projectiles, worldTiles: Tiles, worldClock: Clock,
            npcReplication: Registry, projectileReplication: ProjectileRegistry,
            townCommerceWorldFacts: facts, expertMode: mode >= 1, masterMode: mode >= 2,
            naturalSpawnRandom: new SystemVanillaNpcRandom(Random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random));
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        session = new(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        Connection = new(GameCommandSourceId.FromConnection(29107), session.Handle);
        Assert.True(Registry.TryRegister(Connection.Source, Outbound));
        Assert.True(ProjectileRegistry.TryRegister(Connection.Source, Outbound));
        World.Apply(new PlayerSpawnRuntimeCommand(Connection, session,
            new(session.Slot, (short)(sourceCase.GetProperty("playerX").GetSingle() / 16f), (short)floor, 0, 0, 0, 0, 0)));
        World.Apply(new PlayerAppearanceRuntimeCommand(Connection, new(session.Slot, 0, 0, 0, 0,
            scene.StartsWith("cenx", StringComparison.Ordinal) ? "cenx" : "Oracle",
            0, 0, 0, default, default, default, default, default, default, default, 0, 0, 0)));
        World.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 100, 100)));
        World.Apply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, 20, 20)));
        World.Apply(new PlayerStealthRuntimeCommand(Connection, 1f));
        MovePlayer(sourceCase.GetProperty("playerX").GetSingle(), floor * 16f - 42f);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var graph = Assert.IsType<ServerRuntimeComposition>(typeof(ServerRuntimeState).GetField("_runtime", flags)!.GetValue(World));
        authority = graph.Npcs;
        Targeting = Assert.IsType<VanillaNpcTargetingAiStepper>(typeof(NpcAuthority).GetField("vanillaTargeting", flags)!.GetValue(graph.Npcs));
        Motion = Assert.IsType<VanillaNpcWorldMotionAiStepper>(typeof(NpcAuthority).GetField("pirateGhostMotion", flags)!.GetValue(graph.Npcs));
        spawnCycle = Assert.IsType<RuntimeNpcSpawnCycle1458>(typeof(NpcAuthority).GetField("sourceSpawnCycle", flags)!.GetValue(graph.Npcs));
        Status = graph.Npcs.NpcBuffStatus;
        executor = new(Npcs, Projectiles);
        peers = new NpcSnapshot[Npcs.Capacity]; retained = new VanillaNpcRetainedSlot[Npcs.Capacity];
        // Initialize real player/raw/world bindings without warming the actor under test.
        TickWholeNpcPhase();
        // The original bootstrap resets its seed immediately before NewNPC, too.
        Random.SetSeed(seed);
        Drain();
    }

    // This is creation only. ImportObserved deliberately remains a separate source-authored input step.
    internal NpcSnapshot SpawnObserved(JsonElement observed, int bottomX, int bottomY,
        byte expectedSlot = 0, ushort spawnTarget = 255)
    {
        var creation = new NpcStateUpdate(I(observed, "type"), (short)I(observed, "netId"),
            0f, 0f, 0f, 0f, spawnTarget, default, NpcSimulationState.Initial with
            {
                // This API accepts the caller's creation state. Supply the same
                // NewNPC lifetime as the existing production spawn-intent owner.
                TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft
            });
        Assert.True(Npcs.TrySpawnVanillaPendingAtBottomCenter(in creation, bottomX, bottomY, out var born));
        Assert.Equal(expectedSlot, born.Handle.Slot);
        LastBirth = born;
        return born;
    }

    internal NpcSnapshot ImportObserved(NpcHandle handle, JsonElement observed)
    {
        var update = ReadObserved(observed);
        Assert.True(Npcs.TryUpdateUnpublished(handle, in update, out var imported));
        return imported;
    }

    internal void ImportStatus(NpcHandle handle, JsonElement observed)
    {
        var types = observed.GetProperty("rawBuffTypes"); var times = observed.GetProperty("rawBuffTimes");
        for (int i = 0; i < types.GetArrayLength(); i++)
            if (types[i].GetInt32() != 0 && times[i].GetInt32() > 0)
                Assert.True(Status.TryApply(handle, new(types[i].GetInt32()), times[i].GetInt32()));
    }

    internal NpcSnapshot Current(NpcHandle handle)
    { Assert.True(Npcs.TryGet(handle, out var current)); return current; }

    internal NpcSnapshot Step(NpcHandle handle)
    {
        stepped = true;
        var before = Current(handle);
        int count = Npcs.CopyActive(peers); Targeting.SetNpcPeers(peers.AsSpan(0, count));
        count = Npcs.CopyRetainedSlots(retained);
        ((INpcAiRetainedSlotSnapshotConsumer)Targeting).SetRetainedNpcSlots(retained.AsSpan(0, count));
        Assert.True(Targeting.TryStepState(in before, out var proposal), $"No real phase for {before.Type}/{before.Ai}.");
        Assert.True(Npcs.TryUpdateUnpublished(handle, in proposal, out var placeholder));
        var completed = Motion.CompleteCommittedState(in before, in placeholder, executor);
        Assert.True(completed.Handle.IsAssigned, $"Real phase refused completion for {before.Type}.");
        // These are the same publication/retirement owners called by RuntimeNpcAiStateExecutor.
        // Observer replacement is allowed after joint adoption and must stop old publication.
        if (Npcs.TryGet(completed.Handle, out var current) && current == completed)
        {
            if (Targeting.RetiresDuringAiAfterCompletion(in before, in completed))
            {
                Assert.True(Npcs.TryRetireAfterAi(in completed));
                return completed;
            }
            bool force = proposal.RequiresImmediateSync || Targeting.RequiresForcedUpdateAfterCompletion(in before, in completed);
            Assert.True(Npcs.TryPublishUpdate(in completed, force, proposal.VanillaNetworkPhase,
                proposal.RequiresLowPrioritySync && completed.Type == before.Type && completed.NetId == before.NetId));
            if (Targeting.DeactivatesAfterCompletion(in before, in completed) &&
                Npcs.TryGet(completed.Handle, out current) && current == completed)
                Assert.True(Npcs.TryDespawnAfterCheckActive(in completed, spawnCycle));
        }
        return completed;
    }

    internal void TickWholeNpcPhase()
    { stepped |= Npcs.ActiveCount != 0; spawnCycle.SuppressNext(); authority.AdvanceWorldTick(); authority.TickSimulation(); }

    // An explicit supplied RNG preimage is permitted only before the first observed phase,
    // and is recorded separately from replaying the source creation/prelude history.
    internal void SetRandomSnapshot(JsonElement input)
    {
        if (stepped || RandomInputImported) throw new InvalidOperationException("RNG input cannot be restored after a phase.");
        uint cursor = input.GetProperty("cursor").GetUInt32();
        int[] slots = input.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        if (cursor > 55 || slots.Length != 56 || slots.Any(x => x < 0)) throw new ArgumentException("Invalid physical RNG input.");
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.SetValue(Random, cursor);
        slots.CopyTo((int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(Random)!, 0);
        RandomInputImported = true;
    }

    internal void AssertBirth(JsonElement sourceBirth)
    {
        var state = sourceBirth.GetProperty("state");
        var expected = state.GetProperty("npc");
        Assert.Equal((I(expected, "type"), I(expected, "netId")), (LastBirth.Type, (int)LastBirth.NetId));
        Assert.Equal((F(expected.GetProperty("position")[0]), F(expected.GetProperty("position")[1])),
            (LastBirth.PositionX, LastBirth.PositionY));
        Assert.Equal(A(expected.GetProperty("ai")), LastBirth.Ai);
        Assert.Equal(A(expected.GetProperty("localAi")), LastBirth.Simulation.LocalAi);
        Assert.Equal((I(expected, "life"), I(expected, "lifeMax"), I(expected, "timeLeft")),
            (LastBirth.Simulation.Life, LastBirth.Simulation.LifeMax, LastBirth.Simulation.TimeLeft));
        Assert.Equal((I(expected, "direction"), I(expected, "directionY"), I(expected, "spriteDirection")),
            (LastBirth.Simulation.DirectionX, LastBirth.Simulation.DirectionY, LastBirth.Simulation.SpriteDirection));
        Assert.Equal((B(expected, "wet"), B(expected, "noGravity"), B(expected, "noTileCollide"), B(expected, "friendly")),
            (LastBirth.Simulation.Wet, LastBirth.Simulation.NoGravity, LastBirth.Simulation.NoTileCollide,
                LastBirth.Simulation.Friendly == true));
        Assert.Equal(I(expected, "immunity"), LastBirth.Simulation.CatchableNpcTempImmunityCounter);
        Assert.Equal((ushort)I(expected, "target"), LastBirth.Target);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(LastBirth.TypeIdentity, LastBirth.NetIdentity, out var definition));
        Assert.True(definition.TryResolveHitbox(LastBirth.Simulation, out var body));
        Assert.Equal((I(expected, "width"), I(expected, "height")), (body.Width, body.Height));
        Assert.Equal(F(expected.GetProperty("scale")), LastBirth.Simulation.Scale);
        Assert.Equal(I(expected, "defense"), LastBirth.Simulation.DefenseOverride ?? definition.Defense);
        Assert.Equal(I(expected, "damage"), LastBirth.Simulation.DamageOverride ?? definition.Damage);
        AssertRandom(state.GetProperty("rng"), Random);
    }

    internal static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(),
            (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!);
        Assert.Equal(expected.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!);
    }
    internal void MovePlayer(float x, float y) => World.Apply(new PlayerMovementRuntimeCommand(Connection,
        new(session.Slot, 0, 0, 0, 0, 0, x, y, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
    internal byte[][] Drain()
    {
        var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetField("_queue", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Outbound)!;
        var frames = new List<byte[]>();
        while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
        return frames.ToArray();
    }
    public void Dispose() => session.Dispose();

    internal static NpcStateUpdate ReadObserved(JsonElement x)
    {
        var simulation = NpcSimulationState.Initial with
        {
            DirectionX = I(x, "direction"), DirectionY = I(x, "directionY"), SpriteDirection = I(x, "spriteDirection"),
            OldPositionX = F(x.GetProperty("oldPosition")[0]), OldPositionY = F(x.GetProperty("oldPosition")[1]),
            OldVelocityX = F(x.GetProperty("oldVelocity")[0]), OldVelocityY = F(x.GetProperty("oldVelocity")[1]),
            CollideX = B(x, "collideX"), CollideY = B(x, "collideY"), Wet = B(x, "wet"),
            NoGravity = B(x, "noGravity"), NoTileCollide = B(x, "noTileCollide"), Scale = F(x.GetProperty("scale")),
            HitboxOverride = new(I(x, "width"), I(x, "height")), Life = I(x, "life"), LifeMax = I(x, "lifeMax"),
            TimeLeft = I(x, "timeLeft"), JustHit = B(x, "justHit"), LocalAi = A(x.GetProperty("localAi")),
            FrameCounter = D(x.GetProperty("frameCounter")), FrameIndex = I(x, "frameIndex"),
            Alpha = I(x, "alpha"), DontTakeDamage = B(x, "dontTakeDamage"), Rotation = F(x.GetProperty("rotation")),
            Friendly = B(x, "friendly"), Chaseable = B(x, "chaseable"), Immortal = B(x, "immortal"),
            HomeTileX = I(x, "homeX"), HomeTileY = I(x, "homeY"), Homeless = B(x, "homeless"),
            CatchableNpcTempImmunityCounter = I(x, "immunity"), ReleaseOwner = (short)I(x, "release"),
            DefenseOverride = I(x, "defense"), DamageOverride = I(x, "damage"), BaseDefense = I(x, "baseDefense"),
            BaseDamage = I(x, "baseDamage"), BaseLifeMax = I(x, "baseLifeMax"),
            MoneyValue = F(x.GetProperty("money")), KnockBackResist = F(x.GetProperty("knockBack")),
            ShimmerTransparency = F(x.GetProperty("shimmer")), TownNpc = B(x, "townNpc"),
            Breath = I(x, "breath"), BreathCounter = I(x, "breathCounter"), WetCount = I(x, "wetCount"),
            LavaWet = B(x, "lavaWet"), HoneyWet = B(x, "honeyWet"), ShimmerWet = B(x, "shimmerWet"),
            TeleportTime = F(x.GetProperty("teleportTime")), TeleportStyle = I(x, "teleportStyle"),
            StairFall = B(x, "stairFall"), SpawnedFromStatue = B(x, "spawnedFromStatue"),
            FriendlyRegenerationCounter = I(x, "friendlyRegen"), LifeRegenCounter = I(x, "lifeRegen")
        };
        return new(I(x, "type"), (short)I(x, "netId"), F(x.GetProperty("position")[0]), F(x.GetProperty("position")[1]),
            F(x.GetProperty("velocity")[0]), F(x.GetProperty("velocity")[1]), (ushort)I(x, "target"), A(x.GetProperty("ai")), simulation);
    }
    private static bool B(JsonElement x, string name) => x.GetProperty(name).GetBoolean();
    private static int I(JsonElement x, string name) => x.GetProperty(name).GetInt32();
    private static float F(JsonElement x) => BitConverter.UInt32BitsToSingle(uint.Parse(x.GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    private static double D(JsonElement x) => BitConverter.UInt64BitsToDouble(ulong.Parse(x.GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    private static NpcAiState A(JsonElement x) => new(F(x[0]), F(x[1]), F(x[2]), F(x[3]));

    private sealed class Observer(RangedNpcPhaseFixture1458 owner) : INpcStateCommitSink, INpcBirthRetentionSink
    {
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => owner.OnNpcCommit?.Invoke(kind, snapshot);
        public void NpcBirthRetained(in NpcSnapshot snapshot) => owner.OnNpcBirth?.Invoke(snapshot);
    }
}
