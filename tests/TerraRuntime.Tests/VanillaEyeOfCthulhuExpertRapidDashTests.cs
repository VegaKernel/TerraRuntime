using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaEyeOfCthulhuExpertRapidDashTests
{
    [Fact]
    public void Third_expert_direct_dash_seeds_rapid_state_in_source_rng_range()
    {
        var random = new SequenceRandom(2);
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, Candidate(7, 400f, 300f));
        NpcSnapshot eye = CreateEye(new NpcAiState(3f, 2f, 89f, 2f), life: 1000, velocityX: 10f);

        Assert.True(stepper.TryStepState(in eye, out NpcStateUpdate next));

        Assert.Equal(1, random.Consumed);
        Assert.Equal(3f, next.Ai.Ai1);
        Assert.Equal(0f, next.Ai.Ai2);
        Assert.Equal(2f, next.Ai.Ai3);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTarget, next.Target);
        Assert.Equal(10f * 0.97f * 0.98f, next.VelocityX, 5);
    }

    [Fact]
    public void Rapid_launch_uses_live_player_velocity_and_exact_four_noncritical_rng_rolls()
    {
        var random = new SequenceRandom(0, 0, 0, 0);
        VanillaNpcTargetCandidate target = Candidate(7, 500f, 400f) with
        {
            VelocityX = 5f,
            VelocityY = 0f
        };
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, target);
        NpcSnapshot eye = CreateEye(new NpcAiState(3f, 3f, -1f, -2f), life: 1000);

        Assert.True(stepper.TryStepState(in eye, out NpcStateUpdate next));

        Assert.Equal(4, random.Consumed);
        Assert.Equal((ushort)7, next.Target);
        Assert.Equal(4f, next.Ai.Ai1);
        Assert.Equal(-1f, next.Ai.Ai2);
        Assert.Equal(-2f, next.Ai.Ai3);
        Assert.Equal(13.57600f, next.VelocityX, 4);
        Assert.Equal(22.17413f, next.VelocityY, 4);
    }

    [Fact]
    public void Critical_rapid_launch_consumes_ten_rng_rolls_and_preserves_twenty_pixel_base_speed()
    {
        var random = new SequenceRandom(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, Candidate(7, 400f, 400f));
        NpcSnapshot eye = CreateEye(new NpcAiState(3f, 3f, 0f, 1f), life: 100);

        Assert.True(stepper.TryStepState(in eye, out NpcStateUpdate next));

        Assert.Equal(10, random.Consumed);
        Assert.Equal(4f, next.Ai.Ai1);
        float speed = MathF.Sqrt(next.VelocityX * next.VelocityX + next.VelocityY * next.VelocityY);
        Assert.Equal(20f, speed, 4);
    }

    [Fact]
    public void Low_life_state_five_tick_seventy_seeds_negative_rapid_counter_and_retargets()
    {
        var random = new SequenceRandom(-2);
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, Candidate(7, 300f, 400f));
        NpcSnapshot eye = CreateEye(new NpcAiState(3f, 5f, 69f, 0f), life: 300);

        Assert.True(stepper.TryStepState(in eye, out NpcStateUpdate next));

        Assert.Equal(1, random.Consumed);
        Assert.Equal((ushort)7, next.Target);
        Assert.Equal(3f, next.Ai.Ai1);
        Assert.Equal(-1f, next.Ai.Ai2);
        Assert.Equal(-2f, next.Ai.Ai3);
        Assert.True(next.VelocityY > 0f);
    }

    [Fact]
    public void Rapid_state_four_holds_at_slowdown_boundary_while_player_top_left_is_within_two_hundred_pixels()
    {
        var random = new SequenceRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, Candidate(7, 160f, 160f));
        NpcSnapshot eye = CreateEye(new NpcAiState(3f, 4f, 19f, 1f), life: 1000, velocityX: 8f);

        Assert.True(stepper.TryStepState(in eye, out NpcStateUpdate next));

        Assert.Equal(19f, next.Ai.Ai2);
        Assert.Equal(4f, next.Ai.Ai1);
        Assert.Equal(8f, next.VelocityX, 5);
        Assert.Equal(0, random.Consumed);
    }

    [Fact]
    public void Fifth_rapid_dash_cycle_returns_to_phase_two_hover()
    {
        var random = new SequenceRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, Candidate(7, 800f, 600f));
        NpcSnapshot eye = CreateEye(new NpcAiState(3f, 4f, 32f, 4f), life: 1000, velocityX: 8f);

        Assert.True(stepper.TryStepState(in eye, out NpcStateUpdate next));

        Assert.Equal(0f, next.Ai.Ai1);
        Assert.Equal(0f, next.Ai.Ai2);
        Assert.Equal(0f, next.Ai.Ai3);
        Assert.Equal(8f * 0.95f, next.VelocityX, 5);
    }

    [Fact]
    public void Low_life_fourth_rapid_seed_above_player_resets_to_hover_without_rng()
    {
        var random = new SequenceRandom();
        VanillaNpcTargetingAiStepper stepper = CreateStepper(random, Candidate(7, 300f, 100f));
        NpcSnapshot eye = CreateEye(new NpcAiState(3f, 3f, 0f, 4f), life: 300);

        Assert.True(stepper.TryStepState(in eye, out NpcStateUpdate next));

        Assert.Equal(0f, next.Ai.Ai1);
        Assert.Equal(0f, next.Ai.Ai2);
        Assert.Equal(0f, next.Ai.Ai3);
        Assert.Equal(0, random.Consumed);
    }

    [Fact]
    public void Player_slot_snapshot_lookup_enriches_candidate_velocity_before_boss_ai_reads_it()
    {
        var random = new SequenceRandom();
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), random: random);
        stepper.SetPlayerSnapshotLookup(new FixedSlotLookup(7, 3.5f, -1.25f));
        stepper.SetCandidates([Candidate(7, 300f, 200f)]);

        Assert.True(stepper.TryGetCandidate(7, out VanillaNpcTargetCandidate candidate));
        Assert.Equal(3.5f, candidate.VelocityX, 5);
        Assert.Equal(-1.25f, candidate.VelocityY, 5);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(
        SequenceRandom random,
        VanillaNpcTargetCandidate candidate)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), random: random);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: true);
        stepper.SetCandidates([candidate]);
        return stepper;
    }

    private static NpcSnapshot CreateEye(
        NpcAiState ai,
        int life,
        float velocityX = 0f,
        float velocityY = 0f) =>
        new(
            Handle: new NpcHandle(0, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: VanillaNpcIds.EyeOfCthulhu.Value,
            NetId: checked((short)VanillaNpcIds.EyeOfCthulhu.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: velocityX,
            VelocityY: velocityY,
            Target: 7,
            Ai: ai,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                Life = life,
                LifeMax = 2800,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                Scale = 1f,
                NoGravity = true,
                NoTileCollide = true
            });

    private static VanillaNpcTargetCandidate Candidate(byte slot, float centerX, float centerY) =>
        new(slot, centerX, centerY, 0, Active: true, Dead: false, Ghost: false, NoAggro: false);

    private sealed class SequenceRandom(params int[] values) : IVanillaNpcRandom
    {
        private int _index;

        public int Consumed => _index;

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Assert.True(_index < values.Length, $"Unexpected RNG call #{_index + 1} for [{inclusiveMin}, {exclusiveMax}).");
            int value = values[_index++];
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }

    private sealed class FixedSlotLookup(byte slot, float velocityX, float velocityY) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId requested, out PlayerStateSnapshot snapshot)
        {
            if (requested.Value != slot)
            {
                snapshot = default;
                return false;
            }

            snapshot = new PlayerStateSnapshot(
                Player: new PlayerHandle(new PlayerSlotId(slot), new PlayerSessionGeneration(1)),
                Revision: new PlayerStateRevision(1),
                Team: 0,
                ControlFlags: 0,
                MovementFlags: 0,
                MiscFlags1: 0,
                MiscFlags2: 0,
                SelectedItem: 0,
                PositionX: 0f,
                PositionY: 0f,
                VelocityX: velocityX,
                VelocityY: velocityY,
                MountType: 0,
                PotionOfReturnOriginalPositionX: 0f,
                PotionOfReturnOriginalPositionY: 0f,
                PotionOfReturnHomePositionX: 0f,
                PotionOfReturnHomePositionY: 0f,
                CameraTargetX: 0f,
                CameraTargetY: 0f);
            return true;
        }
    }
}
