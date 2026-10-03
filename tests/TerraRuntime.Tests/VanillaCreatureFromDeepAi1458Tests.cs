using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaCreatureFromDeepAi1458Tests
{
    public static IEnumerable<object[]> OriginalCases()
    {
        using var resource = typeof(VanillaCreatureFromDeepAi1458Tests).Assembly.GetManifestResourceStream("CreatureDeepAi003Linux1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.Clone()];
    }
    [Theory]
    [MemberData(nameof(OriginalCases))]
    public void Whole_original_AI_matches_forms_steering_exit_and_difficulty(JsonElement row)
    {
        float F(string key) => row.GetProperty(key).GetSingle();
        int I(string key) => row.GetProperty(key).GetInt32();
        bool B(string key) => row.GetProperty(key).GetBoolean();
        var random = new SystemVanillaNpcRandom(1458);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.EnableZombieMotion(140d);
        stepper.SetWorldConditions(dayTime: B("day"), slimeRainActive: false, eclipseActive: B("eclipse"));
        stepper.SetProjectileEnvironment(new Visibility(B("visible")));
        int width = B("swimBody") ? 34 : 18, height = B("swimBody") ? 24 : 40;
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1000f + width * .5f + F("dx"),
            1000f + height * .5f + F("dy"), 0, true, false, false, false)]);
        var npc = new NpcSnapshot(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1),
            461, 461, 1000f, 1000f, F("vx"), F("vy"), 0, new NpcAiState(0, 0, 0, F("clock")),
            NpcSimulationState.Initial with { Life = 400, LifeMax = 400, OldPositionX = 999f,
                DirectionX = 1, DirectionY = 1, SpriteDirection = 1, Wet = B("wet"),
                NoGravity = B("swimBody"), HitboxOverride = new NpcHitboxDimensions(width, height),
                CollideX = B("collide"), OldVelocityX = 3f, OldVelocityY = 1f,
                SpawnDifficulty = F("difficulty"), KnockBackResist = .3f, TimeLeft = 750 });
        Assert.True(stepper.TryStepState(in npc, out var next));
        Assert.Equal(F("outX"), next.PositionX); Assert.Equal(F("outY"), next.PositionY);
        Assert.Equal(F("outVx"), next.VelocityX); Assert.Equal(F("outVy"), next.VelocityY);
        Assert.Equal(new NpcHitboxDimensions(I("width"), I("height")), next.Simulation.HitboxOverride);
        Assert.Equal(F("knockback"), next.Simulation.KnockBackResist);
        Assert.Equal(B("noGravity"), next.Simulation.NoGravity);
        Assert.Equal(I("direction"), next.Simulation.DirectionX);
        Assert.Equal(I("directionY"), next.Simulation.DirectionY);
        Assert.Equal(I("sprite"), next.Simulation.SpriteDirection);
        Assert.Equal(I("target"), next.Target); Assert.Equal(I("timeLeft"), next.Simulation.TimeLeft);
        var ai = row.GetProperty("outAi");
        Assert.Equal(new NpcAiState(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle()), next.Ai);
        Assert.Equal(I("nextRandom"), random.NextInt32(0, int.MaxValue));
    }
    [Fact]
    public void World_executor_preserves_swim_clock_body_and_dry_exit_through_outer_physics()
    {
        var npcs = new RuntimeNpcStore();
        var initial = new NpcStateUpdate(461, 461, 1600f, 1600f, 2f, -1f, 0, new NpcAiState(0f, 7f, 8f, 0f),
            NpcSimulationState.Initial with { Wet = true, Life = 400, LifeMax = 400 });
        Assert.True(npcs.TrySpawnVanilla(in initial, out var spawned));
        var tiles = new WorldTileStore(new WorldDimensions(600, 500));
        for (int x = 95; x < 108; x++) for (int y = 95; y < 108; y++)
            tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = new WorldTile { LiquidAmount = 255 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { Eclipse = true, WorldSurface = 140, RockLayer = 200 },
            townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458), naturalSpawnRandom: new RejectSpawnRandom());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        state.Apply(new PlayerSpawnRuntimeCommand(new ConnectionHandle(GameCommandSourceId.FromConnection(9352), session.Handle), session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 100, 100, 0, 0, 0, 0, 0)));
        state.Tick();
        Assert.True(npcs.TryGet(spawned.Handle, out var swimming));
        Assert.Equal(VanillaCreatureFromDeepMotion1458.SwimmingClock, swimming.Ai.Ai3);
        Assert.Equal(7f, swimming.Ai.Ai1); Assert.Equal(8f, swimming.Ai.Ai2);
        Assert.Equal(new NpcHitboxDimensions(34, 24), swimming.Simulation.HitboxOverride);
        Assert.True(swimming.Simulation.NoGravity);
        Assert.Equal(0f, swimming.Simulation.KnockBackResist);
        Assert.Equal(1592f, swimming.Simulation.OldPositionX);
        Assert.Equal(1608f, swimming.Simulation.OldPositionY);
        for (int i = 0; i < tiles.Tiles.Length; i++) tiles.Tiles[i].LiquidAmount = 0;
        state.Tick(); // Outer contact clears the retained wet input after the swimming AI tick.
        state.Tick();
        Assert.True(npcs.TryGet(spawned.Handle, out var dry));
        Assert.Equal(new NpcHitboxDimensions(18, 40), dry.Simulation.HitboxOverride);
        Assert.False(dry.Simulation.Wet);
        Assert.False(dry.Simulation.NoGravity);
        Assert.Equal(.4f, dry.Simulation.KnockBackResist);
        Assert.True(dry.Ai.Ai3 >= 0f);
        Assert.True(dry.Revision.Value > swimming.Revision.Value);
        Assert.Equal(VanillaNpcIds.CreatureFromTheDeep, dry.TypeIdentity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Executor_rejects_original_nan_normalization_without_mutating_owned_state(bool wet)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.EnableZombieMotion(140d);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1017f, 1012f, 0, true, false, false, false)]);
        stepper.SetProjectileEnvironment(new Visibility(true));
        var npcs = new RuntimeNpcStore();
        var initial = new NpcStateUpdate(461, 461, 1000f, 1000f, 0f, 0f, 0,
            new NpcAiState(0, 0, 0, VanillaCreatureFromDeepMotion1458.SwimmingClock),
            NpcSimulationState.Initial with { Life = 400, LifeMax = 400, Wet = wet,
                HitboxOverride = new NpcHitboxDimensions(34, 24) });
        Assert.True(npcs.TrySpawnVanilla(in initial, out var before));
        var result = new RuntimeNpcAiStateExecutor(npcs).Tick(stepper);
        Assert.Equal(0, result.Proposed);
        Assert.Equal(0, result.Applied);
        Assert.True(npcs.TryGet(before.Handle, out var after));
        Assert.Equal(before, after);
        Assert.True(float.IsFinite(after.PositionX) && float.IsFinite(after.PositionY));
        Assert.Equal(0f, after.VelocityX); Assert.Equal(0f, after.VelocityY);
    }
    private sealed class RejectSpawnRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin + 1;
    }
    private sealed class Visibility(bool visible) : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(float a, float b, int c, int d, float e, float f, int g, int h) => visible;
    }
    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
}
