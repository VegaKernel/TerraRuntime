using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class DukeFishronOcean1458Tests
{
    // Pinned AI_069: position.Y < 800 / > surface*16; 6400 < position.X < width*16-6400.
    [Theory]
    [InlineData(4200, 500, 799, true)]
    [InlineData(4200, 500, 800, false)]
    [InlineData(4200, 500, 2400, false)]
    [InlineData(4200, 500, 2401, true)]
    [InlineData(4200, 6400, 1000, false)]
    [InlineData(4200, 6401, 1000, true)]
    [InlineData(4200, 60799, 1000, true)]
    [InlineData(4200, 60800, 1000, false)]
    [InlineData(6400, 95999, 1000, true)]
    [InlineData(6400, 96000, 1000, false)]
    [InlineData(8400, 127999, 1000, true)]
    [InlineData(8400, 128000, 1000, false)]
    public void Enrage_uses_player_top_left_and_exact_world_boundaries(int width, float left, float top, bool enraged)
    {
        var stepper = CreateStepper(width, left, top);
        NpcSnapshot npc = Duke(0, 0, 0, left - 400, top - 100);
        Assert.True(stepper.TryStepState(in npc, out var next));
        Assert.Equal(enraged ? 200 : 100, next.Simulation.DamageOverride);
        Assert.Equal(enraged ? 100 : 50, next.Simulation.DefenseOverride);
    }

    [Theory]
    [InlineData(0, false, 22)]
    [InlineData(0, true, 23)]
    [InlineData(5, false, 22)]
    [InlineData(5, true, 27)]
    [InlineData(10, true, 33)]
    public void Enrage_overrides_phase_stats_and_starts_dash_on_tenth_update(int phase, bool expert, float speed)
    {
        var stepper = CreateStepper(4200, 7000, 1000, expert);
        NpcSnapshot npc = Duke(phase, 8, 0);
        Assert.True(stepper.TryStepState(in npc, out var before));
        Assert.Equal(phase, before.Ai.Ai0);
        npc = npc with { Ai = before.Ai, Simulation = before.Simulation };
        Assert.True(stepper.TryStepState(in npc, out var dash));
        Assert.Equal(phase + 1, dash.Ai.Ai0);
        Assert.Equal(0f, dash.Ai.Ai2);
        Assert.Equal(speed, MathF.Sqrt(dash.VelocityX * dash.VelocityX + dash.VelocityY * dash.VelocityY), 4);
        Assert.Equal(expert ? 280 : 200, dash.Simulation.DamageOverride);
        Assert.Equal(100, dash.Simulation.DefenseOverride);
    }

    [Theory]
    [InlineData(0, 10, 3, 50, 1)]
    [InlineData(0, 11, 3, 50, 0)]
    [InlineData(5, 6, 8, 0, 1)]
    [InlineData(5, 7, 8, 0, 0)]
    [InlineData(10, 1, 12, 0, 1)]
    public void Enrage_replaces_bubble_specials_preserving_source_cycle_and_shark_timer(
        int phase, int cycle, int attack, int timer, int nextCycle)
    {
        var stepper = CreateStepper(4200, 7000, 1000, expert: true);
        NpcSnapshot npc = Duke(phase, 9, cycle);
        Assert.True(stepper.TryStepState(in npc, out var next));
        Assert.Equal(attack, next.Ai.Ai0);
        Assert.Equal(timer, next.Ai.Ai2);
        Assert.Equal(nextCycle, next.Ai.Ai3);
    }

    [Theory]
    [InlineData(0, 10, 4)]
    [InlineData(5, 6, 9)]
    public void Health_phase_transition_still_wins_over_enraged_special(int phase, int cycle, int expected)
    {
        var stepper = CreateStepper(4200, 7000, 1000, expert: true);
        NpcSnapshot npc = Duke(phase, 9, cycle);
        npc = npc with { Simulation = npc.Simulation with { Life = 6000 } };
        Assert.True(stepper.TryStepState(in npc, out var next));
        Assert.Equal(expected, next.Ai.Ai0);
        Assert.Equal(0f, next.Ai.Ai2);
    }

    [Theory]
    [InlineData(0, false, 100, 50)]
    [InlineData(5, false, 120, 40)]
    [InlineData(5, true, 201, 40)]
    [InlineData(10, true, 184, 0)]
    public void Returning_to_ocean_restores_phase_stats_and_hover_without_persistent_enrage(
        int phase, bool expert, int damage, int defense)
    {
        var stepper = CreateStepper(4200, 7000, 1000, expert);
        NpcSnapshot npc = Duke(phase, 0, 0);
        Assert.True(stepper.TryStepState(in npc, out var enraged));
        Assert.Equal(expert ? 280 : 200, enraged.Simulation.DamageOverride);
        stepper.SetCandidates([Player(6400, 1000)]);
        npc = npc with { Ai = new NpcAiState(phase, 0, 9, 0), Simulation = enraged.Simulation };
        Assert.True(stepper.TryStepState(in npc, out var normal));
        Assert.Equal(damage, normal.Simulation.DamageOverride);
        Assert.Equal(defense, normal.Simulation.DefenseOverride);
        Assert.Equal(phase, normal.Ai.Ai0);
        Assert.Equal(10f, normal.Ai.Ai2);
    }

    [Theory]
    [InlineData(0, false, false, 100)]
    [InlineData(0, true, false, 140)]
    [InlineData(0, true, true, 210)]
    [InlineData(5, false, false, 120)]
    [InlineData(5, true, false, 201)]
    [InlineData(5, true, true, 302)]
    [InlineData(10, true, false, 184)]
    [InlineData(10, true, true, 277)]
    public void Ordinary_difficulty_defDamage_is_scaled_before_phase_and_enrage(int phase, bool expert, bool master, int damage)
    {
        var stepper = CreateStepper(4200, 6400, 1000);
        stepper.SetWorldConditions(false, false, expertMode: expert, masterMode: master);
        NpcSnapshot npc = Duke(phase, 0, 0);
        Assert.True(stepper.TryStepState(in npc, out var normal));
        Assert.Equal(damage, normal.Simulation.DamageOverride);
        stepper.SetCandidates([Player(6401, 1000)]);
        Assert.True(stepper.TryStepState(in npc, out var enraged));
        Assert.Equal(master ? 420 : expert ? 280 : 200, enraged.Simulation.DamageOverride);
    }

    [Theory]
    [InlineData(3, 6400, 2, 0)]
    [InlineData(3, 6401, 2, 0)]
    [InlineData(8, 6400, 1, 0)]
    [InlineData(8, 6401, 1, 1)]
    public void Only_cthulhunado_bolt_receives_source_enrage_ai2(int phase, float left, int count, int ai2)
    {
        var stepper = CreateStepper(4200, left, 1000);
        NpcSnapshot npc = Duke(phase, 60, 0);
        Assert.True(stepper.TryStepState(in npc, out var next));
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[2];
        Assert.Equal(count, stepper.PlanProjectileSpawns(in npc, in next, intents));
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(VanillaProjectileIds.SharknadoBolt, intents[i].Type);
            Assert.Equal(ai2, intents[i].InitialAi.Ai2);
            Assert.Equal(phase == 8 ? 1f : 0f, intents[i].InitialAi.Ai0);
            Assert.Equal(phase == 8 ? 1f : 0f, intents[i].InitialAi.Ai1);
        }
    }

    [Fact]
    public void Missing_verified_world_bounds_rejects_root_and_projectile_plans()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.SetCandidates([Player(500, 1000)]);
        NpcSnapshot npc = Duke(8, 60, 0);
        Assert.False(stepper.TryStepState(in npc, out _));
        var proposed = new NpcStateUpdate(npc.Type, npc.NetId, npc.PositionX, npc.PositionY,
            0, 0, 0, npc.Ai, npc.Simulation);
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[2];
        Assert.Equal(0, stepper.PlanProjectileSpawns(in npc, in proposed, intents));
    }

    [Theory]
    [InlineData(4200, false)]
    [InlineData(8400, true)]
    public async Task Production_authority_uses_own_world_dimensions_and_server_player_position(int width, bool enraged)
    {
        var tiles = new WorldTileStore(new WorldDimensions(width, 200));
        Assert.True(tiles.TryAttachWorldSurface(150));
        var slots = new PlayerSlotPool(8);
        var identities = new ServerPlayerSlotRegistry(slots);
        var players = new ServerPlayerStateStore(identities, slots.Capacity);
        var id = new ServerPlayerId("test:duke-ocean");
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired, identities.TryAcquire(id, out var acquired));
        using var lease = Assert.IsType<ServerPlayerSlotRegistry.ServerPlayerSlotLease>(acquired);
        Assert.True(players.TrySpawn(id, 61000, 1000, out _));
        var runtime = new ServerRuntimeState(worldTiles: tiles,
            serverPlayers: new ServerPlayerAuthority(players, worldTiles: tiles));
        NpcSnapshot template = Duke(0, 9, 0, 60500, 900);
        var state = new NpcStateUpdate(template.Type, template.NetId, template.PositionX, template.PositionY,
            0, 0, lease.Player.Slot.Value, template.Ai, template.Simulation);
        var completion = new TaskCompletionSource<NpcSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new NpcSpawnRuntimeCommand(7, state, completion));
        NpcSnapshot created = Assert.IsType<NpcSnapshot>(await completion.Task);
        runtime.Tick();
        Assert.True(runtime.TryCaptureNpcSnapshot(created.Handle, out var after));
        Assert.Equal(enraged ? 200 : 100, after.Simulation.DamageOverride);
        Assert.Equal(enraged ? 100 : 50, after.Simulation.DefenseOverride);
        Assert.Equal(enraged ? 1f : 0f, after.Ai.Ai0);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(int width, float left, float top, bool expert = false)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.SetWorldBounds(width, 150);
        stepper.SetWorldConditions(false, false, expertMode: expert);
        stepper.SetCandidates([Player(left, top)]);
        return stepper;
    }

    private static VanillaNpcTargetCandidate Player(float left, float top) =>
        new(0, left + 10, top + 21, 0, true, false, false, false);

    private static NpcSnapshot Duke(int state, int timer, int cycle, float x = 6600, float y = 900) =>
        new(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), 370, 370, x, y, 0, 0, 0,
            new NpcAiState(state, 0, timer, cycle), NpcSimulationState.Initial with
            {
                Life = 60000, LifeMax = 60000, Scale = 1f,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                LocalAi = new NpcAiState(1, 0, 0, 0)
            });
}
