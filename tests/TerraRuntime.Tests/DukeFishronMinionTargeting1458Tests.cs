using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

/// <summary>Independent predicates/order from TerrariaServer 1.4.5.8 NPC.AI, aiStyle 70/71.</summary>
public sealed class DukeFishronMinionTargeting1458Tests
{
    [Theory]
    [InlineData(372)]
    [InlineData(373)]
    public void Charge_reselects_the_closest_living_player_after_emergence(int type)
    {
        var stepper = Stepper();
        stepper.SetCandidates([Player(0, 800, 120), Player(1, 50, 120)]);
        NpcSnapshot shark = Npc(type, new NpcAiState(0, 89, 0, 3));
        Assert.True(stepper.TryStepState(in shark, out var next));
        Assert.Equal((ushort)1, next.Target);
        Assert.True(next.VelocityX < 0);
        Assert.Equal(-1, next.Simulation.DirectionX);
        Assert.Equal(-1, next.Simulation.SpriteDirection);
        Assert.Equal(16f, MathF.Sqrt(next.VelocityX * next.VelocityX + next.VelocityY * next.VelocityY), 5);
        Assert.True(stepper.RequiresForcedUpdate(in shark, in next));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Emergence_retains_inactive_and_ghost_targets(bool active, bool ghost)
    {
        var stepper = Stepper();
        stepper.SetCandidates([Player(0, 800, 120) with { Active = active, Ghost = ghost }, Player(1, 50, 120)]);
        NpcSnapshot shark = Npc(372, new NpcAiState(0, 20, 0, 3));
        Assert.True(stepper.TryStepState(in shark, out var next));
        Assert.Equal((ushort)0, next.Target);
        Assert.Equal(21f, next.Ai.Ai1);
        Assert.Equal(3f, next.VelocityY);
        Assert.False(stepper.RequiresForcedUpdate(in shark, in next));
    }

    [Fact]
    public void Sharkron_without_living_players_finishes_emergence_and_charge_lifetime()
    {
        var stepper = Stepper();
        stepper.SetCandidates([Player(0, 800, 120) with { Dead = true }]);
        NpcSnapshot shark = Npc(372, new NpcAiState(0, 89, 0, 3));
        Assert.True(stepper.TryStepState(in shark, out var charge));
        Assert.Equal(1f, charge.Ai.Ai0);
        Assert.Equal((ushort)0, charge.Target);
        Assert.True(charge.VelocityX > 0);
        shark = shark with { Ai = charge.Ai with { Ai1 = 59 }, Simulation = charge.Simulation };
        Assert.True(stepper.TryStepState(in shark, out var falling));
        Assert.Equal(60f, falling.Ai.Ai1);
        Assert.False(falling.Simulation.NoGravity);
        Assert.False(falling.Simulation.DontTakeDamage);
    }

    [Fact]
    public void Bubble_retains_assigned_dead_target_for_homing_instead_of_switching_players()
    {
        var stepper = Stepper();
        stepper.SetCandidates([Player(0, 800, 118) with { Dead = true }, Player(1, 20, 118)]);
        NpcSnapshot bubble = Npc(371, new NpcAiState(0, 20, 0, 1));
        Assert.True(stepper.TryStepState(in bubble, out var next));
        Assert.Equal((ushort)0, next.Target);
        Assert.True(next.VelocityX > 0);
        Assert.False(stepper.RequiresForcedUpdate(in bubble, in next));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Bubble_contact_requests_sync_but_the_lifetime_threshold_does_not(bool contact, bool forced)
    {
        var stepper = Stepper();
        stepper.SetCandidates([Player(0, contact ? 100 : 800, 120)]);
        NpcSnapshot bubble = Npc(371, new NpcAiState(0, contact ? 20 : 149, 0, 1));
        Assert.True(stepper.TryStepState(in bubble, out var next));
        Assert.Equal(1f, next.Ai.Ai0);
        Assert.Equal(3f, next.Ai.Ai1);
        Assert.Equal(new NpcHitboxDimensions(100, 100), next.Simulation.HitboxOverride);
        Assert.Equal(forced, stepper.RequiresForcedUpdate(in bubble, in next));
    }

    [Fact]
    public void Bubble_unassigned_bootstrap_still_consumes_six_draws_without_living_players()
    {
        var random = new CountingRandom();
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), random: random);
        stepper.SetCandidates([Player(0, 800, 118) with { Dead = true }]);
        NpcSnapshot bubble = Npc(371, default) with { Target = 255 };
        Assert.True(stepper.TryStepState(in bubble, out var next));
        Assert.Equal((ushort)0, next.Target);
        Assert.Equal(.8f, next.Ai.Ai3);
        Assert.Equal(6, random.Draws);
        Assert.True(stepper.RequiresForcedUpdate(in bubble, in next));
    }

    private static VanillaNpcTargetingAiStepper Stepper() =>
        new(new VanillaDemonEyeAiStepper(), random: new CountingRandom());

    private static VanillaNpcTargetCandidate Player(byte slot, float x, float y) =>
        new(slot, x, y, 0, true, false, false, false);

    private static NpcSnapshot Npc(int type, NpcAiState ai) => new(
        new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), type, (short)type,
        100, 100, 0, 0, 0, ai,
        NpcSimulationState.Initial with { Life = 100, LifeMax = 100, TimeLeft = 750, Scale = 1 });

    private sealed class CountingRandom : IVanillaNpcRandom
    {
        public int Draws { get; private set; }
        public int NextInt32(int inclusiveMin, int exclusiveMax) { Draws++; return inclusiveMin; }
    }
}
