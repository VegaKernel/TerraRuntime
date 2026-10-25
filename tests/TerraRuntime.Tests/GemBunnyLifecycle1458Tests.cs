using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GemBunnyLifecycle1458Tests
{
    [Fact]
    public void Whole_original_updates_run_existing_store_executor_physics_and_rng_until_state_completion()
    {
        using var json = Read("GemBunnyRuntimeLinux1458");
        int updates = 0;
        foreach (var group in json.RootElement.EnumerateArray().Where(row => row.GetProperty("outer").GetBoolean())
                     .GroupBy(row => (Type: row.GetProperty("type").GetInt32(), Scene: row.GetProperty("scene").GetString()!)))
        {
            var f = new Fixture(group.Key.Type, group.Key.Scene, 1458, group.First().GetProperty("before"));
            foreach (var row in group)
            {
                f.Tick();
                var expected = row.GetProperty("after");
                Assert.True(f.Npcs.TryGet(f.Birth.Handle, out var actual), $"{group.Key} step{row.GetProperty("step")}");
                Same(expected, actual);
                Assert.Equal(expected.GetProperty("next").GetInt32(), f.Random.Clone().Next());
                updates++;
            }
        }
        Assert.Equal(1008, updates);
    }

    [Fact]
    public void Source_contact_regeneration_lava_and_lower_world_deaths_use_existing_transaction_owners()
    {
        using var json = Read("GemBunnyInteractionsLinux1458");
        int updates = 0, deaths = 0;
        foreach (var group in json.RootElement.EnumerateArray().Where(row => row.GetProperty("scene").GetString() != "catch")
                     .GroupBy(row => (Type: row.GetProperty("type").GetInt32(), Scene: row.GetProperty("scene").GetString()!, Seed: row.GetProperty("seed").GetInt32())))
        {
            var f = new Fixture(group.Key.Type, group.Key.Scene, group.Key.Seed, group.First().GetProperty("before"));
            foreach (var row in group)
            {
                f.Tick();
                var expected = row.GetProperty("after");
                bool alive = expected.GetProperty("active").GetBoolean();
                Assert.Equal(alive, f.Npcs.TryGet(f.Birth.Handle, out var actual));
                if (alive)
                {
                    Same(expected, actual);
                    Assert.Equal(row.GetProperty("immunity").GetInt32(), actual.Simulation.HostileContactImmunity);
                    Assert.Equal(row.GetProperty("friendlyRegen").GetInt32(), actual.Simulation.FriendlyRegenerationCounter);
                }
                else
                {
                    deaths++;
                    Assert.Equal(NpcStateCommitKind.Despawn, f.Commits[^1]);
                    Assert.Equal(0, f.Items.ActiveCount);
                }
                Assert.Equal(expected.GetProperty("next").GetInt32(), f.Random.Clone().Next());
                updates++;
            }
        }
        Assert.Equal(294, updates);
        Assert.Equal(105, deaths);
    }

    [Fact]
    public void Exact_gem_bunny_catch_after_real_updates_uses_authenticated_ingress_and_source_item_rng()
    {
        using var json = Read("GemBunnyInteractionsLinux1458");
        foreach (var group in json.RootElement.EnumerateArray().Where(row => row.GetProperty("scene").GetString() == "catch")
                     .GroupBy(row => (Type: row.GetProperty("type").GetInt32(), Seed: row.GetProperty("seed").GetInt32())))
        {
            var f = new Fixture(group.Key.Type, "catch", group.Key.Seed, group.First().GetProperty("before"));
            f.Tick();
            var state = new ServerRuntimeState(npcs: f.Npcs, worldItems: f.Items,
                naturalSpawnRandom: new SystemVanillaNpcRandom(f.Random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(f.Random));
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(733), session.Handle);
            state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 40, 100, 0, 0, 0, 0, 0)));
            state.Apply(new PlayerMovementRuntimeCommand(connection,
                new(session.Slot, 0, 0, 0, 0, 0, 660f, 1558f, false, 0, 0, false, 0,
                    false, 0, 0, 0, 0, false, 0, 0)));
            state.Apply(new ClientNpcCatchRuntimeCommand(connection, new TerrariaNpcCatchState(f.Birth.Handle.Slot)));
            Assert.False(f.Npcs.TryGet(f.Birth.Handle, out _));
            Assert.Equal(1, f.Items.ActiveCount);
            Assert.True(f.Items.TryGetActive(0, out var item));
            Assert.Equal((short)(4838 + group.Key.Type - 646), item.ItemNetId);
            Assert.Equal((byte)0, item.OwnerPlayerId);
            Assert.Equal(100, item.TimeToKeepReservation);
            Assert.Equal(group.Last().GetProperty("after").GetProperty("next").GetInt32(), f.Random.Clone().Next());
        }
    }

    internal static JsonDocument Read(string name)
    {
        if (OperatingSystem.IsWindows())
            name = name.Replace("GemBunnyRuntimeLinux", "GemBunnyWireRuntimeWindows", StringComparison.Ordinal)
                .Replace("GemBunnyInteractionsLinux", "GemBunnyWireInteractionsWindows", StringComparison.Ordinal)
                .Replace("GemBunnyStinkyLinux", "GemBunnyStinkyWindows", StringComparison.Ordinal)
                .Replace("GemBunnyTerrainLinux", "GemBunnyTerrainWindows", StringComparison.Ordinal)
                .Replace("GemBunnyFreshLinux", "GemBunnyFreshWindows", StringComparison.Ordinal)
                .Replace("GemBunnyImmunityLinux", "GemBunnyImmunityWindows", StringComparison.Ordinal)
                .Replace("GemBunnyWetStatusLinux", "GemBunnyWetStatusWindows", StringComparison.Ordinal)
                .Replace("GemBunnyStatueCatchLinux", "GemBunnyStatueCatchWindows", StringComparison.Ordinal)
                .Replace("GemBunnyGraceMovementLinux", "GemBunnyGraceMovementWindows", StringComparison.Ordinal)
                .Replace("GemBunnyWireRuntimeLinux", "GemBunnyWireRuntimeWindows", StringComparison.Ordinal);
        using var stream = typeof(GemBunnyLifecycle1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    internal static void Same(JsonElement expected, NpcSnapshot actual)
    {
        Assert.Equal(expected.GetProperty("x").GetSingle(), actual.PositionX);
        Assert.Equal(expected.GetProperty("y").GetSingle(), actual.PositionY);
        Assert.Equal(expected.GetProperty("vx").GetSingle(), actual.VelocityX);
        Assert.Equal(expected.GetProperty("vy").GetSingle(), actual.VelocityY);
        Assert.Equal(expected.GetProperty("direction").GetInt32(), actual.Simulation.DirectionX);
        Assert.Equal(expected.GetProperty("sprite").GetInt32(), actual.Simulation.SpriteDirection);
        Assert.Equal(expected.GetProperty("homeX").GetInt32(), actual.Simulation.HomeTileX);
        Assert.Equal(expected.GetProperty("homeY").GetInt32(), actual.Simulation.HomeTileY);
        Assert.Equal(Ai(expected.GetProperty("ai")), actual.Ai);
        Assert.Equal(Ai(expected.GetProperty("local")), actual.Simulation.LocalAi);
        Assert.Equal(expected.GetProperty("life").GetInt32(), actual.Simulation.Life);
        Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), actual.Simulation.TimeLeft);
        Assert.Equal(expected.GetProperty("collideX").GetBoolean(), actual.Simulation.CollideX);
        Assert.Equal(expected.GetProperty("collideY").GetBoolean(), actual.Simulation.CollideY);
        Assert.Equal(expected.GetProperty("frameCounter").GetDouble(), actual.Simulation.FrameCounter);
        if (expected.TryGetProperty("friendly", out var friendly)) Assert.Equal(friendly.GetBoolean(), actual.Simulation.Friendly);
        if (expected.TryGetProperty("counter", out var counter)) Assert.Equal(counter.GetInt32(), actual.Simulation.CatchableNpcTempImmunityCounter);
        if (expected.TryGetProperty("releaseOwner", out var owner)) Assert.Equal(owner.GetInt16(), actual.Simulation.ReleaseOwner);
        if (expected.TryGetProperty("statue", out var statue)) Assert.Equal(statue.GetBoolean(), actual.Simulation.SpawnedFromStatue);
    }

    private static NpcAiState Ai(JsonElement row) => new(row[0].GetSingle(), row[1].GetSingle(), row[2].GetSingle(), row[3].GetSingle());

    internal sealed class Fixture : IRuntimePlayerSlotSnapshotLookup, INpcStateCommitSink,
        INpcAiStateStepper, INpcAiStatePostCommitEffect, INpcAiForcedUpdateIntentPlanner
    {
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly NpcSnapshot Birth;
        internal readonly List<NpcStateCommitKind> Commits = [];
        internal Action? DuringPlan = null, BeforeCompletion = null;
        internal Action<NpcSnapshot>? OnCommit = null;
        private readonly RuntimeNpcReplicationRegistry? replication;
        internal readonly WorldTileStore tiles;
        private readonly VanillaGemBunnyAccepted1458 ai = new();
        private readonly RuntimeGemBunnyLifecycle1458 lifecycle;
        private readonly VanillaNpcBehaviorContext context = new();
        internal readonly RuntimeNpcBuffStatus1458 status;
        private readonly PlayerStateSnapshot player;
        private readonly bool dayTime;
        private readonly bool playerPresent;

        internal Fixture(int type, string scene, int seed, JsonElement initial, RuntimeNpcReplicationRegistry? replication = null)
        {
            this.replication = replication;
            playerPresent = scene != "noPlayer";
            tiles = GemBunnyRuntime1458Tests.Scene(scene == "graceWet" ? "wet" : scene);
            if (scene.StartsWith("repeated", StringComparison.Ordinal) || scene == "waterPoo")
                for (int x = 0; x < 100; x++)
                    tiles.Set(x, 100, new WorldTile { Type = 666, Flags = WorldTileFlags.Active });
            if (scene is "lava" or "waterStinky" or "lavaStinky" or "waterPoo" or "waterContact")
                for (int x = 38; x <= 44; x++) for (int y = 97; y < 100; y++)
                    tiles.Set(x, y, new WorldTile { LiquidAmount = byte.MaxValue,
                        LiquidKind = scene is "lava" or "lavaStinky" ? WorldLiquidKind.Lava : WorldLiquidKind.Water });
            Npcs = new(commitSink: this); Random = new(seed);
            var adapter = new SystemVanillaNpcRandom(Random); Npcs.SetVanillaSpawnRandomSource(adapter);
            player = default(PlayerStateSnapshot) with { Player = new(new(0), new(1)), Revision = new(1),
                PositionX = scene == "far" ? 6000f : 660f, PositionY = 1558f, IsDead = scene == "deadPlayer",
                HasHealth = true, Life = 100, MaxLife = 100, DerivedLifeMax = 100,
                HasMana = true, Mana = 20, MaxMana = 20, Zones = new(0, 0, 0, 0, 0, 0) };
            var raw = new RuntimeNpcRawPlayerSlots1458(this);
            if (playerPresent) Assert.True(raw.TryAttach(player.Player));
            context.SetRawPlayerSlots(raw); context.SetPlayerSnapshotLookup(this);
            context.SetWorldBounds(100, 80, 120, 200); dayTime = scene is not ("night" or "homeNight");
            context.SetWorldConditions(dayTime, false);
            Assert.True(raw.TryCapture(0, out var target)); context.SetCandidates(playerPresent ? [target.Facts.Candidate] : []);
            var simulation = NpcSimulationState.Initial with {
                Life = initial.GetProperty("life").GetInt32(), LifeMax = 5, TimeLeft = initial.GetProperty("timeLeft").GetInt32(),
                // Source constructor directionY=1 remains visible when the lower-world strike precedes AI.
                DirectionX = initial.GetProperty("direction").GetInt32(), DirectionY = 1, SpriteDirection = -1,
                HomeTileX = initial.GetProperty("homeX").GetInt32(), HomeTileY = initial.GetProperty("homeY").GetInt32(), Homeless = false,
                CatchableNpcTempImmunityCounter = initial.TryGetProperty("counter", out var initialCounter) ? initialCounter.GetInt32() : null,
                ReleaseOwner = initial.TryGetProperty("releaseOwner", out var initialOwner) ? initialOwner.GetInt16() : null,
                Friendly = initial.TryGetProperty("friendly", out var initialFriendly) ? initialFriendly.GetBoolean() : null,
                SpawnedFromStatue = initial.TryGetProperty("statue", out var initialStatue) ? initialStatue.GetBoolean() : scene == "catchStatue",
                Wet = initial.GetProperty("wet").GetBoolean(), TownNpc = false,
                OldPositionX = initial.GetProperty("oldX").GetSingle(), OldPositionY = initial.GetProperty("oldY").GetSingle(),
                OldVelocityX = initial.GetProperty("oldVx").GetSingle(), OldVelocityY = initial.GetProperty("oldVy").GetSingle(),
                LocalAi = Ai(initial.GetProperty("local")), HostileContactImmunity = scene == "contactImmune" ? 2 : 0,
                FriendlyRegenerationCounter = scene == "regen" ? 180 : 0 };
            Assert.True(Npcs.TrySpawn(0, new(type, (short)type, initial.GetProperty("x").GetSingle(), initial.GetProperty("y").GetSingle(),
                initial.GetProperty("vx").GetSingle(), initial.GetProperty("vy").GetSingle(),
                (ushort)initial.GetProperty("target").GetInt32(), Ai(initial.GetProperty("ai")), simulation), out Birth));
            if (scene is "danger" or "dangerBlocked" or "graceDanger" or "waterContact" || scene.StartsWith("contact", StringComparison.Ordinal))
            {
                float x = scene.StartsWith("contact", StringComparison.Ordinal) || scene == "waterContact" ? scene == "contactLeft" ? 646f : 630f : 580f;
                Assert.True(Npcs.TrySpawn(1, new(3, 3, x, 1560f, 0, 0, 0, default,
                    NpcSimulationState.Initial with { Life = 45, LifeMax = 45, DamageOverride = scene == "contactSurvive" ? 2 : 10 }), out _));
            }
            status = new(Npcs); status.BeginWorldTick();
            replication?.BindNpcBuffStatus(status);
            if (scene is "stinkyExpire" or "stinky" or "repeatedLow" or "repeatedHigh" or
                "lowerWorldStinky" or "lowerWorldStinkyExpire" or "waterStinky" or "lavaStinky" or "waterContact")
                Assert.True(status.TryApply(Birth.Handle, scene is "stinkyExpire" or "lowerWorldStinkyExpire" ? 1 :
                    scene == "repeatedLow" ? 11 : scene == "repeatedHigh" ? 12 : 2));
            var world = new RuntimeNpcGlobalLootWorldFacts1458(100, 200, 1f, false, false, false, false, 120d, 80d, false, false);
            var combat = new RuntimeNpcNetworkCombatPipeline(Npcs, Items, this, new PlayerAuthority(null, tiles),
                static () => 0, replication, new(Items), null, new RuntimeWorldClock(0d, dayTime, 0, 0, 1), new(), false, false,
                worldTiles: tiles, lootRandom: Random, globalLootWorld: world, seasonalItemContext: static () => new(false, false, false),
                mechanicalLootBaseline: new(0, false, false, false, false), npcSpecificLowTiles: static () => false,
                onlyShimmerOceanWorlds: false);
            lifecycle = new(tiles, 80d, Npcs, combat, replication: replication, status: status);
            ai.Configure(Npcs, new VanillaGemBunnyEnvironment1458(tiles, Npcs, status,
                _ => new(dayTime, false, false, false, false),
                () => { DuringPlan?.Invoke(); return playerPresent ? [new(0,
                    new(player.PositionX, player.PositionY, 20f, 42f), player.IsDead, false)] : []; }, replication), adapter);
            Commits.Clear();
        }

        internal void Tick()
        {
            status.BeginWorldTick();
            replication?.AdvanceAuthoritativeTick();
            if (Npcs.TryGet(Birth.Handle, out var current) && current.PositionY > 2580f)
            { Assert.True(lifecycle.TryCommitLowerWorldDeath(in current, Random)); return; }
            new RuntimeNpcAiStateExecutor(Npcs).Tick(this);
        }
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot value) { value = player; return playerPresent && slot.Value == 0; }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc)
        { Commits.Add(kind); replication?.NpcStateCommitted(kind, in npc); OnCommit?.Invoke(npc); }
        public void NpcVanillaAiStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc, bool lowPriority = false)
        { Commits.Add(kind); replication?.NpcVanillaAiStateCommitted(kind, in npc, lowPriority); OnCommit?.Invoke(npc); }
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return VanillaGemBunnyAi1458.IsSupported(npc.Type) &&
                VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition) &&
                ai.TryStep(in npc, in definition, context, out next);
        }
        public bool DefersStatePublication(in NpcSnapshot before, in NpcStateUpdate proposed) => ai.HasPlan(in before);
        public NpcSnapshot CompleteCommittedState(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations)
        { BeforeCompletion?.Invoke(); return lifecycle.Complete(in before, in committed, ai, dayTime, false, false); }
        public bool RequiresForcedUpdate(in NpcSnapshot before, in NpcStateUpdate proposed) => false;
        public bool RequiresForcedUpdateAfterCompletion(in NpcSnapshot before, in NpcSnapshot finalized) => ai.RequiresForcedUpdate(in before, in finalized);
        public bool DeactivatesAfterCompletion(in NpcSnapshot before, in NpcSnapshot after) => ai.Deactivates(in before, in after);
        public void ApplyCommittedEffect(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) { }
    }
}
