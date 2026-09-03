using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class LateHardmodeBossParityTests
{
    [Fact]
    public void Sharkron_ai71_leaves_emergence_and_enters_source_charge_state()
    {
        var stepper = CreateStepper(dayTime: false);
        NpcSnapshot sharkron = CreateNpc(
            VanillaNpcIds.Sharkron,
            new NpcAiState(0f, 89f, 0f, 3f));

        Assert.True(stepper.TryStepState(in sharkron, out NpcStateUpdate next));

        Assert.Equal(1f, next.Ai.Ai0);
        Assert.Equal(1f, next.Ai.Ai1);
        Assert.True(next.Simulation.NoGravity);
        Assert.True(next.Simulation.NoTileCollide);
        Assert.True(next.Simulation.DontTakeDamage);
        Assert.Equal(16f, MathF.Sqrt(next.VelocityX * next.VelocityX + next.VelocityY * next.VelocityY), 4);
    }

    [Fact]
    public void Duke_state_three_plans_the_two_source_sharknado_bolts()
    {
        var stepper = CreateStepper(dayTime: false);
        NpcSnapshot duke = CreateNpc(
            VanillaNpcIds.DukeFishron,
            new NpcAiState(3f, 0f, 60f, 0f),
            life: 60_000);
        NpcStateUpdate proposed = Proposed(in duke, new NpcAiState(3f, 0f, 61f, 0f));
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[2];

        Assert.Equal(2, stepper.PlanProjectileSpawns(in duke, in proposed, intents));
        Assert.Equal(VanillaProjectileIds.SharknadoBolt, intents[0].Type);
        Assert.Equal(VanillaProjectileIds.SharknadoBolt, intents[1].Type);
        Assert.Equal(8f, intents[0].VelocityY);
        Assert.Equal(8f, intents[1].VelocityY);
        Assert.Equal(-intents[0].VelocityX, intents[1].VelocityX);
    }

    [Fact]
    public void Cultist_phase_two_plans_ancient_light_volley_and_ritual_projectile()
    {
        var stepper = CreateStepper(dayTime: false);
        NpcSnapshot cultist = CreateNpc(
            VanillaNpcIds.LunaticCultist,
            new NpcAiState(7f, 4f, 0f, 0f),
            life: 16_000);
        NpcStateUpdate lightNext = Proposed(in cultist, new NpcAiState(7f, 5f, 0f, 0f));
        Span<NpcAiSpawnIntent> spawns = stackalloc NpcAiSpawnIntent[5];

        Assert.Equal(5, stepper.PlanNpcSpawns(in cultist, in lightNext, spawns));
        for (int i = 0; i < spawns.Length; i++)
            Assert.Equal(VanillaNpcIds.AncientLight, spawns[i].Type);

        NpcSnapshot ritual = cultist with { Ai = new NpcAiState(5f, 30f, 0f, 0f) };
        NpcStateUpdate ritualNext = Proposed(in ritual, new NpcAiState(5f, 31f, 0f, 0f));
        Span<NpcAiProjectileIntent> projectiles = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(1, stepper.PlanProjectileSpawns(in ritual, in ritualNext, projectiles));
        Assert.Equal(VanillaProjectileIds.CultistRitual, projectiles[0].Type);
    }

    [Fact]
    public void Hit_ritual_clone_forces_owner_into_state_six_and_clone_dies()
    {
        var stepper = CreateStepper(dayTime: false);
        NpcSnapshot root = CreateNpc(
            VanillaNpcIds.LunaticCultist,
            new NpcAiState(5f, 150f, 0f, 0f),
            life: 32_000,
            slot: 5,
            localAi: new NpcAiState(1f, 0f, 0f, 0f));
        NpcSnapshot clone = CreateNpc(
            VanillaNpcIds.LunaticCultistClone,
            new NpcAiState(5f, 150f, 0f, 5f),
            life: 10_000,
            slot: 6,
            localAi: new NpcAiState(0f, 1f, 0f, 6f),
            justHit: true);
        stepper.SetNpcPeers([root, clone]);

        Assert.True(stepper.TryStepState(in root, out NpcStateUpdate rootNext));
        Assert.Equal(6f, rootNext.Ai.Ai0);
        Assert.Equal(1f, rootNext.Ai.Ai1);

        Assert.True(stepper.TryStepState(in clone, out NpcStateUpdate cloneNext));
        Assert.Equal(0, cloneNext.Simulation.Life);
        Assert.Equal(0, cloneNext.Simulation.TimeLeft);
    }

    [Fact]
    public void Daytime_empress_projectiles_use_source_enrage_damage_even_in_classic()
    {
        var stepper = CreateStepper(dayTime: true);
        NpcSnapshot empress = CreateNpc(
            VanillaNpcIds.EmpressOfLight,
            new NpcAiState(5f, 0f, 0f, 0f),
            life: 70_000);
        NpcStateUpdate proposed = Proposed(in empress, new NpcAiState(5f, 1f, 0f, 0f));
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[13];

        Assert.Equal(13, stepper.PlanProjectileSpawns(in empress, in proposed, intents));
        foreach (NpcAiProjectileIntent intent in intents)
        {
            Assert.Equal(VanillaProjectileIds.HallowBossLastingRainbow, intent.Type);
            Assert.Equal(9_999, intent.Damage);
        }
    }

    [Fact]
    public void Moon_lord_hand_advances_into_the_source_attack_sequence()
    {
        var stepper = CreateStepper(dayTime: false);
        NpcSnapshot root = CreateNpc(
            VanillaNpcIds.MoonLordCore,
            default,
            life: 50_000,
            slot: 5,
            localAi: new NpcAiState(1f, 0f, 0f, 0f));
        NpcSnapshot hand = CreateNpc(
            VanillaNpcIds.MoonLordHand,
            new NpcAiState(0f, 49f, 0f, 5f),
            life: 25_000,
            slot: 6,
            localAi: new NpcAiState(0f, 0f, 0f, 6f));
        stepper.SetNpcPeers([root, hand]);

        Assert.True(stepper.TryStepState(in hand, out NpcStateUpdate next));
        Assert.Equal(1f, next.Ai.Ai0);
        Assert.Equal(50f, next.Ai.Ai1);
    }

    [Fact]
    public void Moon_lord_core_opens_only_after_both_hands_and_head_are_retired()
    {
        var stepper = CreateStepper(dayTime: false);
        NpcSnapshot root = CreateNpc(
            VanillaNpcIds.MoonLordCore,
            new NpcAiState(0f, 0f, 0f, 0f),
            life: 50_000,
            slot: 5,
            localAi: new NpcAiState(0f, 0f, 0f, 1f));
        NpcSnapshot left = CreateNpc(
            VanillaNpcIds.MoonLordHand,
            new NpcAiState(-2f, 0f, 0f, 5f),
            life: 25_000,
            slot: 6,
            localAi: new NpcAiState(0f, 0f, 0f, 6f));
        NpcSnapshot right = CreateNpc(
            VanillaNpcIds.MoonLordHand,
            new NpcAiState(-2f, 0f, 1f, 5f),
            life: 25_000,
            slot: 7,
            localAi: new NpcAiState(0f, 0f, 0f, 6f));
        NpcSnapshot head = CreateNpc(
            VanillaNpcIds.MoonLordHead,
            new NpcAiState(-2f, 700f, 0f, 5f),
            life: 45_000,
            slot: 8,
            localAi: new NpcAiState(0f, 0f, 0f, 6f));
        stepper.SetNpcPeers([root, left, right, head]);

        Assert.True(stepper.TryStepState(in root, out NpcStateUpdate next));
        Assert.Equal(1f, next.Ai.Ai0);
        Assert.False(next.Simulation.DontTakeDamage);
    }

    [Fact]
    public void Moon_lord_core_missing_retired_part_fails_closed()
    {
        var stepper = CreateStepper(dayTime: false);
        NpcSnapshot root = CreateNpc(
            VanillaNpcIds.MoonLordCore,
            new NpcAiState(0f, 0f, 0f, 0f),
            life: 50_000,
            slot: 5,
            localAi: new NpcAiState(0f, 0f, 0f, 1f));
        NpcSnapshot left = CreateNpc(
            VanillaNpcIds.MoonLordHand,
            new NpcAiState(-2f, 0f, 0f, 5f),
            life: 25_000,
            slot: 6,
            localAi: new NpcAiState(0f, 0f, 0f, 6f));
        NpcSnapshot right = CreateNpc(
            VanillaNpcIds.MoonLordHand,
            new NpcAiState(-2f, 0f, 1f, 5f),
            life: 25_000,
            slot: 7,
            localAi: new NpcAiState(0f, 0f, 0f, 6f));
        stepper.SetNpcPeers([root, left, right]);

        Assert.True(stepper.TryStepState(in root, out NpcStateUpdate next));
        Assert.Equal(0f, next.Ai.Ai0);
        Assert.True(next.Simulation.DontTakeDamage);
    }

    [Fact]
    public void Retired_moon_lord_head_enters_minus_three_when_core_death_drama_starts()
    {
        var stepper = CreateStepper(dayTime: false);
        NpcSnapshot root = CreateNpc(
            VanillaNpcIds.MoonLordCore,
            new NpcAiState(2f, 10f, 0f, 0f),
            life: 50_000,
            slot: 5,
            localAi: new NpcAiState(0f, 0f, 0f, 1f));
        NpcSnapshot head = CreateNpc(
            VanillaNpcIds.MoonLordHead,
            new NpcAiState(-2f, 700f, 0f, 5f),
            life: 45_000,
            slot: 8,
            localAi: new NpcAiState(0f, 0f, 0f, 6f));
        stepper.SetNpcPeers([root, head]);

        Assert.True(stepper.TryStepState(in head, out NpcStateUpdate next));
        Assert.Equal(-3f, next.Ai.Ai0);
        Assert.Equal(701f, next.Ai.Ai1);
        Assert.True(next.Simulation.DontTakeDamage);
        Assert.Equal(0, next.Simulation.DamageOverride);
        Assert.Equal(0f, next.VelocityX);
        Assert.Equal(0f, next.VelocityY);
    }

    [Fact]
    public void Moon_lord_core_open_transition_does_not_synthesize_three_true_eyes()
    {
        var stepper = CreateStepper(dayTime: false);
        NpcSnapshot root = CreateNpc(
            VanillaNpcIds.MoonLordCore,
            new NpcAiState(0f, 0f, 0f, 0f),
            life: 50_000,
            slot: 5,
            localAi: new NpcAiState(0f, 0f, 0f, 1f));
        NpcStateUpdate proposed = new(
            root.Type,
            root.NetId,
            root.PositionX,
            root.PositionY,
            root.VelocityX,
            root.VelocityY,
            root.Target,
            new NpcAiState(1f, 0f, 0f, 0f),
            root.Simulation with { LocalAi = root.Simulation.LocalAi with { Ai2 = 1f } });
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[3];

        Assert.Equal(0, stepper.PlanNpcSpawns(in root, in proposed, intents));
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(bool dayTime)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new ZeroRandom());
        stepper.SetWorldConditions(dayTime, slimeRainActive: false);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(0, 500f, 300f, 0, true, false, false, false)
        ]);
        return stepper;
    }

    private static NpcSnapshot CreateNpc(
        NpcTypeId type,
        NpcAiState ai,
        int life = 10_000,
        byte slot = 1,
        NpcAiState localAi = default,
        bool justHit = false) =>
        new(
            Handle: new NpcHandle(slot, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: type.Value,
            NetId: checked((short)type.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 0,
            Ai: ai,
            Simulation: NpcSimulationState.Initial with
            {
                Life = life,
                LifeMax = life,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                Scale = 1f,
                LocalAi = localAi,
                JustHit = justHit
            });

    private static NpcStateUpdate Proposed(in NpcSnapshot source, NpcAiState ai) =>
        new(
            source.Type,
            source.NetId,
            source.PositionX,
            source.PositionY,
            source.VelocityX,
            source.VelocityY,
            source.Target,
            ai,
            source.Simulation);

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }

    private sealed class ZeroRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin;
    }
}
