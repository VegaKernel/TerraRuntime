using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.World;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaPsychoAi1458Tests
{
    public static IEnumerable<object[]> OriginalCases()
    {
        using var resource = typeof(VanillaPsychoAi1458Tests).Assembly.GetManifestResourceStream("PsychoAi003Linux1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.Clone()];
    }
    [Theory]
    [MemberData(nameof(OriginalCases))]
    public void Complete_AI_state_matches_original_dedicated_server(JsonElement row)
    {
        float F(string key) => row.GetProperty(key).GetSingle();
        int I(string key) => row.GetProperty(key).GetInt32();
        var random = new SystemVanillaNpcRandom(1458);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.EnableZombieMotion(140d);
        stepper.SetWorldConditions(dayTime: row.GetProperty("day").GetBoolean(), slimeRainActive: false, eclipseActive: row.GetProperty("eclipse").GetBoolean());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1009f + F("dx"), 1020f, 0, true, false, false, false)]);
        var npc = new NpcSnapshot(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1),
            466, 466, 1000f, 1000f, F("vx"), F("vy"), 0, new NpcAiState(0, 0, F("clock"), 0),
            NpcSimulationState.Initial with { Life = 550, LifeMax = 550, OldPositionX = 999f,
                DirectionX = 1, DirectionY = 1, SpriteDirection = 1, Alpha = I("alpha"),
                JustHit = row.GetProperty("hit").GetBoolean(), TimeLeft = 750 });
        Assert.True(stepper.TryStepState(in npc, out var next));
        Assert.Equal(F("outVx"), next.VelocityX);
        Assert.Equal(F("outVy"), next.VelocityY);
        Assert.Equal(I("outAlpha"), next.Simulation.Alpha);
        Assert.Equal(I("direction"), next.Simulation.DirectionX);
        Assert.Equal(I("directionY"), next.Simulation.DirectionY);
        Assert.Equal(I("sprite"), next.Simulation.SpriteDirection);
        Assert.Equal(I("target"), next.Target);
        Assert.Equal(I("timeLeft"), next.Simulation.TimeLeft);
        var ai = row.GetProperty("outAi");
        Assert.Equal(new NpcAiState(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle()), next.Ai);
        Assert.Equal(I("nextRandom"), random.NextInt32(0, int.MaxValue));
    }
    [Fact]
    public void Production_world_tick_commits_ambush_reveal_and_pursuit_without_fallback()
    {
        var npcs = new RuntimeNpcStore();
        var initial = new NpcStateUpdate(466, 466, 1581f, 1558f, 0f, 0f, 0, default,
            NpcSimulationState.Initial with { Life = 550, LifeMax = 550 });
        Assert.True(npcs.TrySpawnVanilla(in initial, out var handle));
        var tiles = new WorldTileStore(new WorldDimensions(600, 500));
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, true, default, 0, 0),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { Eclipse = true, WorldSurface = 140, RockLayer = 200 },
            townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458), naturalSpawnRandom: new RejectSpawnRandom());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9351), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 100, 100, 0, 0, 0, 0, 0)));
        state.Tick();
        Assert.True(npcs.TryGet(handle.Handle, out var first));
        Assert.Equal(-16f, first.Ai.Ai2);
        Assert.Equal(200, first.Simulation.Alpha);
        for (int i = 0; i < 16; i++) state.Tick();
        Assert.True(npcs.TryGet(handle.Handle, out var launched));
        Assert.Equal(1f, launched.Ai.Ai2);
        Assert.Equal(8, launched.Simulation.Alpha);
        Assert.Equal(2f, MathF.Abs(launched.VelocityX));
        Assert.True(launched.Revision.Value > first.Revision.Value);
        state.Tick();
        Assert.True(npcs.TryGet(handle.Handle, out var pursuing));
        Assert.Equal(0, pursuing.Simulation.Alpha);
        Assert.Equal(1f, pursuing.Ai.Ai2);
        Assert.Equal(VanillaNpcIds.Psycho, pursuing.TypeIdentity);
    }
    private sealed class RejectSpawnRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin + 1;
    }
    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }
}
