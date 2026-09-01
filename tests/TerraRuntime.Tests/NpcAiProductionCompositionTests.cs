using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class NpcAiProductionCompositionTests
{
    [Fact]
    public void Production_wrapper_chain_preserves_king_slime_spawn_planner_and_world_environment()
    {
        var store = new RuntimeNpcStore(capacity: 8);
        var random = new SequenceRandom(
            2,
            0, 0, -15, -30, 0,
            1, 2, 15, 0, 2);
        var targeting = new VanillaNpcTargetingAiStepper(
            new VanillaDemonEyeAiStepper(),
            kingSlimeEnvironment: null,
            random);
        VanillaNpcTargetCandidate[] candidates =
        [
            new VanillaNpcTargetCandidate(
                Slot: 7,
                CenterX: 300f,
                CenterY: 150f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ];
        targeting.SetCandidates(candidates);

        var actorIntent = new RuntimeNpcActorIntentStateStepper(
            targeting,
            new RuntimeNpcActorControlRegistry(store),
            new EmptyPlayerLookup());
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var worldMotion = new VanillaNpcWorldMotionAiStepper(actorIntent, tiles, worldSurfaceTiles: 33d);
        var checkActive = new VanillaNpcCheckActiveAiStepper(worldMotion);
        checkActive.SetCandidates(candidates);

        var state = new NpcStateUpdate(
            Type: VanillaNpcIds.KingSlime.Value,
            NetId: checked((short)VanillaNpcIds.KingSlime.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: new NpcAiState(-100f, 0f, 0f, 2000f),
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                Life = 1899,
                LifeMax = 2000,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                Scale = 1.25f,
                LocalAi = new NpcAiState(0f, 0f, 0f, 1f)
            });
        Assert.True(store.TrySpawn(0, in state, out NpcSnapshot king));

        var executor = new RuntimeNpcAiStateExecutor(store);
        NpcAiStateTickSummary summary = executor.Tick(checkActive);

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), summary);
        Assert.True(store.TryGet(king.Handle, out NpcSnapshot committedKing));
        Assert.Equal(1899f, committedKing.Ai.Ai3);
        Assert.Equal(3, store.ActiveCount);

        var snapshots = new NpcSnapshot[store.ActiveCount];
        Assert.Equal(3, store.CopyActive(snapshots));
        NpcSnapshot[] children = snapshots.Where(static npc => npc.Type == VanillaNpcIds.BlueSlime.Value).ToArray();
        Assert.Equal(2, children.Length);
        Assert.All(children, static child => Assert.Equal(-1f, child.Ai.Ai1));
    }

    private sealed class EmptyPlayerLookup : IRuntimePlayerSnapshotLookup
    {
        public bool TryGetPlayer(PlayerHandle player, out PlayerStateSnapshot snapshot)
        {
            snapshot = default;
            return false;
        }
    }

    private sealed class SequenceRandom : IVanillaNpcRandom
    {
        private readonly int[] _values;
        private int _index;

        public SequenceRandom(params int[] values) => _values = values;

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            int value = _index < _values.Length ? _values[_index++] : inclusiveMin;
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }
}
