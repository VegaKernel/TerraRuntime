using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaSkeletronBehaviorTests
{
    [Fact]
    public void Source_defaults_admit_head_and_hand_as_distinct_ai_families()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.SkeletronHead, out VanillaNpcDefinition head));
        Assert.Equal(VanillaNpcAiStyles.SkeletronHead, head.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.SkeletronHead, head.BehaviorFamily);
        Assert.Equal(NpcArchetypeRole.Boss, head.Role);
        Assert.Equal((80, 102, 32, 10, 4400, 0f),
            (head.Width, head.Height, head.Damage, head.Defense, head.LifeMax, head.KnockBackResist));

        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.SkeletronHand, out VanillaNpcDefinition hand));
        Assert.Equal(VanillaNpcAiStyles.SkeletronHand, hand.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.SkeletronHand, hand.BehaviorFamily);
        Assert.Equal((52, 52, 20, 14, 600, 0f),
            (hand.Width, hand.Height, hand.Damage, hand.Defense, hand.LifeMax, hand.KnockBackResist));
    }

    [Fact]
    public void First_authoritative_head_tick_spawns_two_source_linked_hands()
    {
        var store = new RuntimeNpcStore(capacity: 8);
        NpcSnapshot head = Spawn(store, 0, VanillaNpcIds.SkeletronHead, ai: default);
        var stepper = CreateStepper();
        stepper.SetCandidates([Target(0, 300f, 300f)]);
        var executor = new RuntimeNpcAiStateExecutor(store);

        NpcAiStateTickSummary summary = executor.Tick(stepper);

        Assert.True(summary.Applied >= 1);
        var active = new NpcSnapshot[8];
        int count = store.CopyActive(active);
        Assert.Equal(3, count);
        NpcSnapshot[] hands = active[..count]
            .Where(static npc => npc.TypeIdentity == VanillaNpcIds.SkeletronHand)
            .OrderBy(static npc => npc.Ai.Ai0)
            .ToArray();
        Assert.Equal(2, hands.Length);
        Assert.Equal(-1f, hands[0].Ai.Ai0);
        Assert.Equal(head.Handle.Slot, hands[0].Ai.Ai1);
        Assert.Equal(0f, hands[0].Ai.Ai3);
        Assert.Equal(1f, hands[1].Ai.Ai0);
        Assert.Equal(head.Handle.Slot, hands[1].Ai.Ai1);
        Assert.Equal(150f, hands[1].Ai.Ai3);
    }

    [Fact]
    public void Expert_head_counts_live_hands_into_defense_and_enters_spin_after_800_ticks()
    {
        var store = new RuntimeNpcStore(capacity: 8);
        NpcSnapshot head = Spawn(store, 0, VanillaNpcIds.SkeletronHead, new NpcAiState(1f, 0f, 799f, 0f));
        NpcSnapshot left = Spawn(store, 1, VanillaNpcIds.SkeletronHand, new NpcAiState(-1f, 0f, 0f, 0f));
        NpcSnapshot right = Spawn(store, 2, VanillaNpcIds.SkeletronHand, new NpcAiState(1f, 0f, 0f, 150f));
        var stepper = CreateStepper();
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: true);
        stepper.SetCandidates([Target(0, 500f, 300f)]);
        stepper.SetNpcPeers([head, left, right]);

        Assert.True(stepper.TryStepState(in head, out NpcStateUpdate next));

        Assert.Equal(1f, next.Ai.Ai1);
        Assert.Equal(0f, next.Ai.Ai2);
        Assert.Equal(60, next.Simulation.DefenseOverride);
        Assert.True(next.Simulation.NoGravity);
        Assert.True(next.Simulation.NoTileCollide);
    }

    [Fact]
    public void Daytime_head_enters_9999_enrage_and_target_loss_enters_bounded_flee()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcSnapshot head = Spawn(store, 0, VanillaNpcIds.SkeletronHead, new NpcAiState(1f, 0f, 10f, 0f));
        var stepper = CreateStepper();
        stepper.SetCandidates([Target(0, 400f, 200f)]);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        stepper.SetNpcPeers([head]);

        Assert.True(stepper.TryStepState(in head, out NpcStateUpdate enraged));
        Assert.Equal(2f, enraged.Ai.Ai1);
        Assert.Equal(9999, enraged.Simulation.DefenseOverride);
        Assert.Equal(9999, enraged.Simulation.DamageOverride);

        NpcSnapshot far = head with { Target = 0 };
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetCandidates([Target(0, 5000f, 5000f)]);
        Assert.True(stepper.TryStepState(in far, out NpcStateUpdate fleeing));
        Assert.Equal(3f, fleeing.Ai.Ai1);
        Assert.Equal(50, fleeing.Simulation.TimeLeft);
    }

    [Fact]
    public void Hand_parent_loss_uses_source_50_tick_teardown_and_attack_timer_advances_0_to_1()
    {
        var store = new RuntimeNpcStore(capacity: 6);
        NpcSnapshot head = Spawn(store, 0, VanillaNpcIds.SkeletronHead, new NpcAiState(1f, 0f, 0f, 0f));
        NpcSnapshot hand = Spawn(store, 1, VanillaNpcIds.SkeletronHand, new NpcAiState(-1f, 0f, 0f, 299f));
        var stepper = CreateStepper();
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: false);
        stepper.SetCandidates([Target(0, 300f, 300f)]);
        stepper.SetNpcPeers([head, hand]);

        Assert.True(stepper.TryStepState(in hand, out NpcStateUpdate attack));
        Assert.Equal(1f, attack.Ai.Ai2);
        Assert.Equal(0f, attack.Ai.Ai3);

        NpcSnapshot orphan = hand with { Ai = new NpcAiState(-1f, 5f, 50f, 0f) };
        stepper.SetNpcPeers([orphan]);
        Assert.True(stepper.TryStepState(in orphan, out NpcStateUpdate dead));
        Assert.Equal(0, dead.Simulation.Life);
        Assert.Equal(0, dead.Simulation.TimeLeft);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper() =>
        new(new VanillaDemonEyeAiStepper(), random: new CenteredRandom());

    private static VanillaNpcTargetCandidate Target(byte slot, float x, float y) =>
        new(slot, x, y, Aggro: 0, Active: true, Dead: false, Ghost: false, NoAggro: false);

    private static NpcSnapshot Spawn(RuntimeNpcStore store, byte slot, NpcTypeId type, NpcAiState ai)
    {
        var update = new NpcStateUpdate(
            type.Value,
            checked((short)type.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 0,
            Ai: ai,
            Simulation: NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(slot, in update, out NpcSnapshot spawned));
        return spawned;
    }

    private sealed class CenteredRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) =>
            inclusiveMin <= 0 && exclusiveMax > 0 ? 0 : inclusiveMin;
    }
}
