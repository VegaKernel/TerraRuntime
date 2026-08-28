using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcSpriteDirectionStateTests
{
    [Fact]
    public void Spawn_materializes_unspecified_sprite_direction_to_vanilla_default()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate spawn = CreateZombie(NpcSimulationState.Initial with { SpriteDirection = 0 });

        Assert.True(store.TrySpawn(0, in spawn, out NpcSnapshot created));

        Assert.Equal(-1, created.Simulation.SpriteDirection);
    }

    [Fact]
    public void State_only_update_preserves_existing_sprite_direction()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate spawn = CreateZombie(NpcSimulationState.Initial with { SpriteDirection = 1 });
        Assert.True(store.TrySpawn(0, in spawn, out NpcSnapshot created));
        NpcStateUpdate stateOnly = CreateZombie(NpcSimulationState.Initial with { SpriteDirection = 0 });

        Assert.True(store.TryUpdate(created.Handle, in stateOnly, out NpcSnapshot updated));

        Assert.Equal(1, updated.Simulation.SpriteDirection);
    }

    private static NpcStateUpdate CreateZombie(NpcSimulationState simulation) =>
        new(
            Type: 3,
            NetId: 3,
            PositionX: 0f,
            PositionY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: simulation);
}
