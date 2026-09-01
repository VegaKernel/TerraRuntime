using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class NpcSimulationStateLocalAiTests
{
    [Fact]
    public void Finite_server_local_ai_is_committed_with_npc_revision()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcStateUpdate update = CreateUpdate(NpcSimulationState.Initial with
        {
            LocalAi = new NpcAiState(12f, 34f, 56f, 1f)
        });

        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot snapshot));
        Assert.Equal(new NpcAiState(12f, 34f, 56f, 1f), snapshot.Simulation.LocalAi);
        Assert.Equal(new NpcRevision(1), snapshot.Revision);
    }

    [Fact]
    public void Non_finite_server_local_ai_is_rejected_without_occupying_slot()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcStateUpdate update = CreateUpdate(NpcSimulationState.Initial with
        {
            LocalAi = new NpcAiState(float.NaN, 0f, 0f, 0f)
        });

        Assert.False(store.TrySpawn(0, in update, out _));
        Assert.Equal(0, store.ActiveCount);
    }

    private static NpcStateUpdate CreateUpdate(NpcSimulationState simulation) =>
        new(
            Type: 1,
            NetId: 1,
            PositionX: 0f,
            PositionY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: simulation);
}
