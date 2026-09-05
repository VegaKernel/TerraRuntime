using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaQueenBeeVerticalTests
{
    [Fact]
    public void Definition_and_stinger_are_source_backed()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.QueenBee, out VanillaNpcDefinition queen));
        Assert.Equal(222, queen.Type.Value);
        Assert.Equal(43, queen.AiStyle.Value);
        Assert.Equal((66, 66, 30, 8, 3400), (queen.Width, queen.Height, queen.Damage, queen.Defense, queen.LifeMax));
        Assert.Equal(VanillaNpcBehaviorFamily.QueenBee, queen.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.NoClipFlight, queen.PhysicsFamily);
        Assert.True(queen.NoGravityAtSpawn);
        Assert.True(queen.NoTileCollideAtSpawn);

        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.QueenBeeStinger, out VanillaProjectileDefinition stinger));
        Assert.Equal((10, 10), (stinger.Width, stinger.Height));
        Assert.Equal(VanillaProjectileAiStyles.Arrow, stinger.AiStyle);
        Assert.True(stinger.TileCollide);
        Assert.True(stinger.CanCutTiles);
        Assert.True(VanillaProjectileFacts.IsHostile(VanillaProjectileIds.QueenBeeStinger));
    }

    [Fact]
    public void Enrage_and_expert_cadence_match_source_thresholds()
    {
        NpcSnapshot queen = Queen(life: 300, lifeMax: 3400, y: 200f, ai0: 3f, ai1: 0f);
        var target = new VanillaNpcTargetCandidate(7, 500f, 500f, 0, true, false, false, false);
        var context = new VanillaNpcBehaviorContext();
        context.SetWorldConditions(dayTime: true, slimeRainActive: false, goodWorld: true, expertMode: true);
        var environment = new FakeQueenEnvironment(worldSurfacePixels: 400d, jungle: false);
        Assert.Equal(2.5f, VanillaQueenBeeNpcBehaviorStrategy.ComputeEnrage(in queen, in target, context, environment));
        Assert.Equal(3, VanillaQueenBeeNpcBehaviorStrategy.GetStingerCadence(in queen, expertMode: true, enrage: 2.5f));
        Assert.Equal(-5, VanillaQueenBeeNpcBehaviorStrategy.GetBeeSummonThreshold(2.5f));
    }

    [Fact]
    public void Bee_spawn_intent_carries_source_local_ai_seed()
    {
        var random = new QueueRandom(0, 210);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        stepper.SetQueenBeeEnvironment(new FakeQueenEnvironment(50d, jungle: true));
        stepper.SetProjectileEnvironment(new AlwaysHitEnvironment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 400f, 400f, 0, true, false, false, false)]);
        NpcSnapshot source = Queen(life: 3400, lifeMax: 3400, y: 300f, ai0: 1f, ai1: 40f, ai2: 0f);
        source = source with { Target = 7 };
        var proposed = new NpcStateUpdate(source.Type, source.NetId, source.PositionX, source.PositionY, 0f, 0f, 7,
            source.Ai with { Ai1 = 0f, Ai2 = 1f }, source.Simulation);
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[2];
        Assert.Equal(1, stepper.PlanNpcSpawns(in source, in proposed, intents));
        Assert.True(intents[0].Type == VanillaNpcIds.Bee || intents[0].Type == VanillaNpcIds.SmallBee);
        Assert.Equal(60f, intents[0].InitialLocalAi.Ai0);
    }

    [Fact]
    public void Stinger_intent_is_server_owned_damage_11_and_300_ticks()
    {
        var random = new QueueRandom(0, 0, 0);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        stepper.SetQueenBeeEnvironment(new FakeQueenEnvironment(50d, jungle: true));
        stepper.SetProjectileEnvironment(new AlwaysHitEnvironment());
        stepper.SetCandidates([new VanillaNpcTargetCandidate(7, 400f, 600f, 0, true, false, false, false)]);
        NpcSnapshot source = Queen(life: 3400, lifeMax: 3400, y: 100f, ai0: 3f, ai1: 38f);
        source = source with { Target = 7 };
        var proposed = new NpcStateUpdate(source.Type, source.NetId, source.PositionX, source.PositionY, 0f, 0f, 7,
            source.Ai with { Ai1 = 39f }, source.Simulation);
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[2];
        Assert.Equal(1, stepper.PlanProjectileSpawns(in source, in proposed, intents));
        Assert.Equal(VanillaProjectileIds.QueenBeeStinger, intents[0].Type);
        Assert.Equal(11, intents[0].Damage);
        Assert.Equal(300, intents[0].TimeLeftOverride);
    }

    private static NpcSnapshot Queen(int life, int lifeMax, float y, float ai0, float ai1, float ai2 = 0f) =>
        new(new NpcHandle(1, new NpcGeneration(1)), new NpcRevision(1), 222, 222, 100f, y, 0f, 0f, 255,
            new NpcAiState(ai0, ai1, ai2, 0f), NpcSimulationState.Initial with { Life = life, LifeMax = lifeMax, TimeLeft = 750 });

    private sealed class FakeQueenEnvironment(double worldSurfacePixels, bool jungle) : IVanillaQueenBeeEnvironment
    {
        public double WorldSurfacePixels => worldSurfacePixels;
        public float WorldCenterX => 4200f;
        public bool IsPlayerInJungle(float playerCenterX, float playerCenterY) => jungle;
    }

    private sealed class AlwaysHitEnvironment : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(float a, float b, int c, int d, float e, float f, int g, int h) => true;
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }

    private sealed class QueueRandom(params int[] values) : IVanillaNpcRandom
    {
        private readonly Queue<int> values = new(values);
        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            if (values.Count == 0) return inclusiveMin;
            int value = values.Dequeue();
            return Math.Clamp(value, inclusiveMin, exclusiveMax - 1);
        }
    }
}
