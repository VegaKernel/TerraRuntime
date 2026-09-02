using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaKingSlimeProductionTests
{
    [Fact]
    public void Targeting_stepper_runs_verified_king_slime_family_with_world_environment()
    {
        var environment = new FixedEnvironment();
        var stepper = new VanillaNpcTargetingAiStepper(
            new VanillaDemonEyeAiStepper(),
            environment,
            new SequenceRandom());
        stepper.SetCandidates([Candidate(7, 300f, 150f)]);
        NpcSnapshot npc = CreateKingSlime();

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal((ushort)7, next.Target);
        Assert.Equal(-98f, next.Ai.Ai0);
        Assert.Equal(0f, next.Ai.Ai1);
        Assert.Equal(1.25f, next.Simulation.Scale, 5);
        Assert.Equal(1f, next.Simulation.LocalAi.Ai3);
        Assert.False(next.Simulation.NoGravity);
        Assert.False(next.Simulation.NoTileCollide);
    }

    [Fact]
    public void Teleport_transition_consumes_resolved_bottom_and_commits_local_ai_atomically()
    {
        var environment = new FixedEnvironment
        {
            Destination = new VanillaKingSlimeTeleportDestination(500f, 600f)
        };
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), environment);
        stepper.SetCandidates([Candidate(7, 300f, 150f)]);
        NpcSnapshot npc = CreateKingSlime() with
        {
            Target = 7,
            Ai = new NpcAiState(-100f, 0f, 300f, 2000f),
            Simulation = CreateKingSlime().Simulation with
            {
                LocalAi = new NpcAiState(0f, 0f, 0f, 1f)
            }
        };

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(5f, next.Ai.Ai1);
        Assert.Equal(1f, next.Ai.Ai0);
        Assert.Equal(500f, next.Simulation.LocalAi.Ai1);
        Assert.Equal(600f, next.Simulation.LocalAi.Ai2);
        Assert.False(next.Simulation.Hidden);
        Assert.False(next.Simulation.DontTakeDamage);
    }

    [Fact]
    public void Five_percent_life_crossing_plans_source_ordered_vanilla_blue_slime_burst()
    {
        var random = new SequenceRandom(
            3,
            0, 0, -15, -30, 2,
            1, 2, 0, -10, 1,
            3, 4, 15, 0, 0);
        var stepper = new VanillaNpcTargetingAiStepper(
            new VanillaDemonEyeAiStepper(),
            new FixedEnvironment(),
            random);
        NpcSnapshot source = CreateKingSlime() with
        {
            Ai = new NpcAiState(-100f, 0f, 0f, 2000f),
            Simulation = CreateKingSlime().Simulation with
            {
                Life = 1899,
                LifeMax = 2000,
                Scale = 1f,
                LocalAi = new NpcAiState(0f, 0f, 0f, 1f)
            }
        };
        NpcStateUpdate proposed = new(
            Type: VanillaNpcIds.KingSlime.Value,
            NetId: checked((short)VanillaNpcIds.KingSlime.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 7,
            Ai: new NpcAiState(-100f, 0f, 0f, 1899f),
            Simulation: source.Simulation);
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[3];

        int count = stepper.PlanNpcSpawns(in source, in proposed, intents);

        Assert.Equal(3, count);
        Assert.All(intents.ToArray(), intent => Assert.Equal(VanillaNpcIds.BlueSlime, intent.Type));
        Assert.Equal(100, intents[0].BottomX);
        Assert.Equal(100, intents[0].BottomY);
        Assert.Equal(-1.5f, intents[0].VelocityX, 5);
        Assert.Equal(-3f, intents[0].VelocityY, 5);
        Assert.Equal(-2000f, intents[0].InitialAi.Ai0);
        Assert.Equal(-1f, intents[0].InitialAi.Ai1);
        Assert.Equal(0f, intents[2].InitialAi.Ai0);
    }

    [Fact]
    public void Spawn_intent_applier_preserves_king_slime_child_initial_ai()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        var intent = new NpcAiSpawnIntent(
            VanillaNpcIds.BlueSlime,
            BottomX: 200,
            BottomY: 300,
            VelocityX: 1.2f,
            VelocityY: -2.3f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget)
        {
            InitialAi = new NpcAiState(-2000f, -1f, 0f, 0f)
        };

        Assert.True(store.TrySpawnIntent(in intent, out NpcSnapshot spawned));
        Assert.Equal(-2000f, spawned.Ai.Ai0);
        Assert.Equal(-1f, spawned.Ai.Ai1);
        Assert.Equal(1.2f, spawned.VelocityX, 5);
        Assert.Equal(-2.3f, spawned.VelocityY, 5);
    }

    [Fact]
    public void World_environment_uses_verified_outer_ring_before_inner_ring()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(40, 50, SolidTile(1));
        var environment = new VanillaKingSlimeWorldEnvironment(tiles, new SequenceRandom(0));
        NpcSnapshot npc = CreateKingSlime();
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition));
        VanillaNpcTargetCandidate target = Candidate(7, 50 * 16f, 50 * 16f);

        Assert.True(environment.TryResolveTeleport(in npc, in definition, in target, antiCheese: false, out VanillaKingSlimeTeleportDestination destination));

        Assert.Equal(40 * 16f + 8f, destination.BottomX);
        Assert.Equal(50 * 16f, destination.BottomY);
    }

    [Fact]
    public void Anti_cheese_teleport_falls_back_to_player_bottom()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var environment = new VanillaKingSlimeWorldEnvironment(tiles, new SequenceRandom());
        NpcSnapshot npc = CreateKingSlime();
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition));
        VanillaNpcTargetCandidate target = Candidate(7, 700f, 800f);

        Assert.True(environment.TryResolveTeleport(in npc, in definition, in target, antiCheese: true, out VanillaKingSlimeTeleportDestination destination));

        Assert.Equal(700f, destination.BottomX);
        Assert.Equal(821f, destination.BottomY);
    }

    private static NpcSnapshot CreateKingSlime() =>
        new(
            Handle: new NpcHandle(1, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: VanillaNpcIds.KingSlime.Value,
            NetId: checked((short)VanillaNpcIds.KingSlime.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                Life = 2000,
                LifeMax = 2000,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                Scale = 1.25f
            });

    private static VanillaNpcTargetCandidate Candidate(byte slot, float centerX, float centerY) =>
        new(slot, centerX, centerY, 0, Active: true, Dead: false, Ghost: false, NoAggro: false);

    private static WorldTile SolidTile(ushort type) =>
        new()
        {
            Type = type,
            Flags = WorldTileFlags.Active
        };

    private sealed class FixedEnvironment : IVanillaKingSlimeEnvironment
    {
        public VanillaKingSlimeTeleportDestination Destination { get; init; } = new(400f, 500f);
        public float WorldPixelWidth => 16_000f;
        public float WorldPixelHeight => 8_000f;
        public bool CanHitLine(float fromX, float fromY, float toX, float toY) => true;

        public bool TryResolveTeleport(
            in NpcSnapshot npc,
            in VanillaNpcDefinition definition,
            in VanillaNpcTargetCandidate target,
            bool antiCheese,
            out VanillaKingSlimeTeleportDestination destination)
        {
            destination = Destination;
            return true;
        }
    }

    private sealed class SequenceRandom(params int[] values) : IVanillaNpcRandom
    {
        private int _index;

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            int value = _index < values.Length ? values[_index++] : inclusiveMin;
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }
}
