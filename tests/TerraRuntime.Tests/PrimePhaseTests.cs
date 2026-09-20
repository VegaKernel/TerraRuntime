using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PrimePhaseTests
{
    private static readonly JsonElement[] Spawns = Read("PrimeSpawn1458", "cbd0b18a90dab38e7f201ef0c5f9590a6011c007cdc6772226f08f0cad15e398");
    private static readonly JsonElement[] Phases = Read("PrimePhase1458", "835461e0dffe725b1630230f655c118b73ab1a9040357ad9a6aee8b3e3474680");
    public static TheoryData<int> SpawnCases => new(Enumerable.Range(0, Spawns.Length));
    public static TheoryData<int> PhaseCases => new(Enumerable.Range(0, Phases.Length));

    [Theory]
    [MemberData(nameof(SpawnCases))]
    public void Prime_and_arms_creation_matches_original(int index)
    {
        var row = Spawns[index];
        NpcSpawnContextTests.AssertSpawn(row, row.TryGetProperty("requestedDifficulty", out var value) ? value.GetSingle() : null);
    }

    [Theory]
    [MemberData(nameof(PhaseCases))]
    public void Head_phase_restores_spawn_baselines_and_matches_original_motion(int index)
    {
        var row = Phases[index];
        int mode = row.GetProperty("mode").GetInt32(), count = row.GetProperty("players").GetInt32();
        bool good = row.GetProperty("good").GetBoolean();
        var store = new RuntimeNpcStore();
        store.SetVanillaSpawnContextSource(() => new(mode + 1 + (good ? 1 : 0), count, good));
        var intent = new NpcAiSpawnIntent(VanillaNpcIds.SkeletronPrime, 1000, 1000, 0, 0, 0)
        {
            StartSlot = 10,
            InitialAi = new NpcAiState(1, row.GetProperty("phase").GetInt32(), row.GetProperty("timer").GetInt32(), 0)
        };
        Assert.True(store.TrySpawnIntent(in intent, out var head));
        var oldLiveState = new NpcStateUpdate(head.Type, head.NetId, head.PositionX, head.PositionY, 0, 0, 0, head.Ai,
            head.Simulation with { DamageOverride = 777, DefenseOverride = 888 });
        Assert.True(store.TryUpdate(head.Handle, in oldLiveState, out _));
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        ai.SetWorldConditions(false, false, goodWorld: good, expertMode: mode >= 1 || good, masterMode: mode == 2 || (mode == 1 && good));
        var target = new VanillaNpcTargetCandidate(0, row.GetProperty("targetX").GetSingle(), row.GetProperty("targetY").GetSingle(), 0, true, false, false, false);
        ai.SetCandidates(count == 1 ? [target] : [target, new VanillaNpcTargetCandidate(1, 10, 21, 0, true, false, false, false)]);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(store).Tick(ai).Applied);
        Assert.True(store.TryGet(head.Handle, out var after));
        var expectedAi = row.GetProperty("ai");
        Assert.Equal(new NpcAiState(expectedAi[0].GetSingle(), expectedAi[1].GetSingle(), expectedAi[2].GetSingle(), expectedAi[3].GetSingle()), after.Ai);
        Assert.Equal(row.GetProperty("x").GetSingle(), after.PositionX);
        Assert.Equal(row.GetProperty("y").GetSingle(), after.PositionY);
        Assert.Equal(row.GetProperty("vx").GetSingle(), after.VelocityX);
        Assert.Equal(row.GetProperty("vy").GetSingle(), after.VelocityY);
        Assert.Equal(row.GetProperty("damage").GetInt32(), after.Simulation.DamageOverride);
        Assert.Equal(row.GetProperty("defense").GetInt32(), after.Simulation.DefenseOverride);
        Assert.Equal(row.GetProperty("defDamage").GetInt32(), after.Simulation.BaseDamage);
        Assert.Equal(row.GetProperty("defDefense").GetInt32(), after.Simulation.BaseDefense);
        Assert.Equal(row.GetProperty("lifeMax").GetInt32(), after.Simulation.LifeMax);
        Assert.Equal(row.GetProperty("timeLeft").GetInt32(), after.Simulation.TimeLeft);
        Assert.Equal(row.GetProperty("reflects").GetBoolean(), after.Simulation.ReflectsProjectiles);
    }

    private static JsonElement[] Read(string name, string hash)
    {
        if (OperatingSystem.IsWindows())
        {
            (name, hash) = name switch
            {
                "PrimeSpawn1458" => ("PrimeSpawnWindows1458", "22e71fbde1769e1d897bd44288d0f47f53211d36e97b613da8c5cf677b43acae"),
                "PrimePhase1458" => ("PrimePhaseWindows1458", "ab97024444a0eb5080ced00888ee5d06a2ecbf25c87035618295624c23f4f6cc"),
                _ => (name, hash)
            };
        }
        using var resource = typeof(PrimePhaseTests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }

    [Fact]
    public void Spin_exit_restores_original_baseline_after_world_context_changes()
    {
        var store = new RuntimeNpcStore();
        var context = new VanillaNpcSpawnContext(2, 1, false);
        store.SetVanillaSpawnContextSource(() => context);
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronPrime, 1000, 1000, 0, 0, 0)
            { InitialAi = new NpcAiState(1, 1, 399, 0) }, out var head));
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        ai.SetWorldConditions(false, false, expertMode: true);
        ai.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 1021, 0, true, false, false, false)]);
        var executor = new RuntimeNpcAiStateExecutor(store);
        Assert.Equal(1, executor.Tick(ai).Applied);
        Assert.True(store.TryGet(head.Handle, out var spin));
        Assert.Equal(160, spin.Simulation.DamageOverride);
        Assert.Equal(48, spin.Simulation.DefenseOverride);
        Assert.Equal(0f, spin.Ai.Ai1);
        context = new(4, 8, true);
        Assert.Equal(1, executor.Tick(ai).Applied);
        Assert.True(store.TryGet(head.Handle, out var hover));
        Assert.Equal(80, hover.Simulation.DamageOverride);
        Assert.Equal(24, hover.Simulation.DefenseOverride);
        Assert.Equal(80, hover.Simulation.BaseDamage);
        Assert.Equal(42000, hover.Simulation.LifeMax);
    }

    [Fact]
    public void Hover_to_spin_publishes_the_source_requested_immediate_update()
    {
        var commits = new CommitRecorder();
        var store = new RuntimeNpcStore(commitSink: commits);
        store.SetVanillaSpawnContextSource(() => new VanillaNpcSpawnContext(1, 1, false));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronPrime, 1000, 1000, 0, 0, 0)
        {
            InitialAi = new NpcAiState(1f, 0f, 599f, 0f)
        }, out var head));
        commits.Kinds.Clear();

        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        ai.SetWorldConditions(dayTime: false, slimeRainActive: false);
        ai.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 1021, 0, true, false, false, false)]);

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(store).Tick(ai).Applied);
        Assert.True(store.TryGet(head.Handle, out var after));
        Assert.Equal(new NpcAiState(1f, 1f, 0f, 0f), after.Ai);
        Assert.Equal([NpcStateCommitKind.ForcedUpdate], commits.Kinds);
    }

    [Fact]
    public void Spin_to_hover_keeps_the_source_cadenced_update()
    {
        var commits = new CommitRecorder();
        var store = new RuntimeNpcStore(commitSink: commits);
        store.SetVanillaSpawnContextSource(() => new VanillaNpcSpawnContext(1, 1, false));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronPrime, 1000, 1000, 0, 0, 0)
        {
            InitialAi = new NpcAiState(1f, 1f, 399f, 0f)
        }, out _));
        commits.Kinds.Clear();

        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        ai.SetWorldConditions(dayTime: false, slimeRainActive: false);
        ai.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 1021, 0, true, false, false, false)]);

        Assert.Equal(1, new RuntimeNpcAiStateExecutor(store).Tick(ai).Applied);
        Assert.Equal([NpcStateCommitKind.Update], commits.Kinds);
    }

    [Fact]
    public void TargetClosest_predicate_forces_head_update_unless_colliding()
    {
        var store = new RuntimeNpcStore();
        store.SetVanillaSpawnContextSource(() => new VanillaNpcSpawnContext(1, 1, false));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronPrime, 1000, 1000, 0, 0, 0)
        {
            InitialAi = new NpcAiState(1f, 0f, 0f, 0f)
        }, out var head));
        Assert.True(store.TryGet(head.Handle, out var before));
        before = before with
        {
            Target = 0,
            Ai = new NpcAiState(1f, 0f, 0f, 0f),
            Simulation = before.Simulation with { DirectionX = 1, DirectionY = 1, CollideX = false, CollideY = false }
        };
        var proposed = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY, before.VelocityX,
            before.VelocityY, 1, before.Ai, before.Simulation with { DirectionX = -1, DirectionY = -1 });
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        Assert.True(ai.RequiresForcedUpdate(in before, in proposed));
        before = before with { Simulation = before.Simulation with { CollideX = true } };
        Assert.False(ai.RequiresForcedUpdate(in before, in proposed));
    }

    [Fact]
    public void Initial_targeting_sets_charge_direction_and_rotation_before_velocity_changes()
    {
        var store = new RuntimeNpcStore();
        store.SetVanillaSpawnContextSource(() => new(1, 1, false));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronPrime, 1000, 1000, 0, 0, 0)
        {
            InitialAi = new NpcAiState(0f, 1f, 399f, 0f)
        }, out var head));
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        ai.SetWorldConditions(dayTime: false, slimeRainActive: false);
        ai.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 1021, 0, true, false, false, false)]);

        Assert.Equal(5, new RuntimeNpcAiStateExecutor(store).Tick(ai).Applied);
        Assert.True(store.TryGet(head.Handle, out var after));
        Assert.Equal(1, after.Simulation.DirectionX);
        Assert.Equal(0.3f, after.Simulation.Rotation);
        Assert.Equal(new NpcAiState(1f, 0f, 0f, 0f), after.Ai);
    }

    [Fact]
    public void Unspecified_updates_preserve_baselines_and_type_or_generation_changes_reset_them()
    {
        var store = new RuntimeNpcStore();
        store.SetVanillaSpawnContextSource(() => new(2, 1, false));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronPrime, 1000, 1000, 0, 0, 0), out var head));
        var update = new NpcStateUpdate(head.Type, head.NetId, 1000, 1000, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(store.TryUpdate(head.Handle, in update, out var retained));
        Assert.Equal(80, retained.Simulation.BaseDamage);
        Assert.Equal(24, retained.Simulation.BaseDefense);
        update = update with { Type = 139, NetId = 139, Simulation = retained.Simulation };
        Assert.True(store.TryUpdate(head.Handle, in update, out var changed));
        Assert.Equal(50, changed.Simulation.BaseDamage);
        Assert.Equal(20, changed.Simulation.BaseDefense);
        Assert.True(store.TryDespawn(head.Handle));
        update = update with { Type = 134, NetId = 134, Simulation = NpcSimulationState.Initial };
        Assert.True(store.TrySpawn(head.Handle.Slot, in update, out var fresh));
        Assert.NotEqual(head.Handle.Generation, fresh.Handle.Generation);
        Assert.Equal(70, fresh.Simulation.BaseDamage);
        Assert.Equal(0, fresh.Simulation.BaseDefense);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void World_head_initialization_gives_every_arm_the_same_spawn_context(bool good)
    {
        var slots = new PlayerSlotPool(4);
        var identities = new ServerPlayerSlotRegistry(slots);
        var players = new ServerPlayerAuthority(new ServerPlayerStateStore(identities, slots.Capacity), identities);
        var first = new ServerPlayerId("test:prime-first"); var second = new ServerPlayerId("test:prime-second");
        Assert.True(players.Create(first, 1500, 1000).IsCreated);
        Assert.True(players.Create(second, 1600, 1000).IsCreated);
        var store = new RuntimeNpcStore();
        var runtime = new ServerRuntimeState(npcs: store, serverPlayers: players, expertMode: true,
            worldTiles: new WorldTileStore(new WorldDimensions(400, 400)),
            worldClock: new RuntimeWorldClock(0, false, default, 0, 1, getGoodWorld: good));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.SkeletronPrime, 1000, 1000, 0, 0, 0)
            { StartSlot = 10 }, out _));
        runtime.Tick();
        for (byte slot = 10; slot <= 14; slot++)
        {
            Assert.True(store.TryGetActive(slot, out var npc));
            var row = Spawns.Single(row => !row.TryGetProperty("requestedDifficulty", out _) &&
                row.GetProperty("mode").GetInt32() == 1 && row.GetProperty("good").GetBoolean() == good &&
                !row.GetProperty("hard").GetBoolean() && row.GetProperty("players").GetInt32() == 2 &&
                row.GetProperty("type").GetInt32() == npc.Type);
            Assert.Equal(row.GetProperty("lifeMax").GetInt32(), npc.Simulation.LifeMax);
            Assert.Equal(row.GetProperty("defDamage").GetInt32(), npc.Simulation.BaseDamage);
            Assert.Equal(row.GetProperty("defDefense").GetInt32(), npc.Simulation.BaseDefense);
        }
        Assert.True(players.Despawn(first)); Assert.True(players.Despawn(second));
    }

    private sealed class CommitRecorder : INpcStateCommitSink
    {
        public List<NpcStateCommitKind> Kinds { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Kinds.Add(kind);
    }
}
