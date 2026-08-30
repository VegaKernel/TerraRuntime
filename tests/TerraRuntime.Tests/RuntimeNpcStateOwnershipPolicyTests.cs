using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcStateOwnershipPolicyTests
{
    [Fact]
    public void Spawn_policy_materializes_definition_combat_lifetime_and_presentation_defaults()
    {
        NpcStateUpdate input = Create(type: 1, simulation: NpcSimulationState.Initial with { SpriteDirection = 0 });

        NpcStateUpdate materialized = RuntimeNpcStateOwnershipPolicy.MaterializeSpawnDefaults(in input);

        Assert.Equal(25, materialized.Simulation.Life);
        Assert.Equal(25, materialized.Simulation.LifeMax);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft, materialized.Simulation.TimeLeft);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultSpriteDirection, materialized.Simulation.SpriteDirection);
    }

    [Fact]
    public void State_only_update_preserves_same_type_combat_lifetime_and_sprite_state()
    {
        NpcStateUpdate previous = Create(
            type: 3,
            simulation: NpcSimulationState.Initial with
            {
                Life = 31,
                LifeMax = 45,
                TimeLeft = 123,
                SpriteDirection = 1
            });
        NpcStateUpdate stateOnly = Create(
            type: 3,
            simulation: NpcSimulationState.Initial with { SpriteDirection = 0 });

        NpcStateUpdate preserved = RuntimeNpcStateOwnershipPolicy.PreserveUnownedUpdateState(
            in stateOnly,
            in previous);

        Assert.Equal(31, preserved.Simulation.Life);
        Assert.Equal(45, preserved.Simulation.LifeMax);
        Assert.Equal(123, preserved.Simulation.TimeLeft);
        Assert.Equal(1, preserved.Simulation.SpriteDirection);
    }

    [Fact]
    public void Type_change_materializes_new_definition_instead_of_preserving_previous_combat_state()
    {
        NpcStateUpdate previous = Create(
            type: 1,
            simulation: NpcSimulationState.Initial with
            {
                Life = 5,
                LifeMax = 25,
                TimeLeft = 99,
                SpriteDirection = 1
            });
        NpcStateUpdate changedType = Create(
            type: 2,
            simulation: NpcSimulationState.Initial with { SpriteDirection = 0 });

        NpcStateUpdate materialized = RuntimeNpcStateOwnershipPolicy.PreserveUnownedUpdateState(
            in changedType,
            in previous);

        Assert.Equal(60, materialized.Simulation.Life);
        Assert.Equal(60, materialized.Simulation.LifeMax);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft, materialized.Simulation.TimeLeft);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultSpriteDirection, materialized.Simulation.SpriteDirection);
    }

    private static NpcStateUpdate Create(int type, NpcSimulationState simulation) =>
        new(
            Type: type,
            NetId: checked((short)type),
            PositionX: 0f,
            PositionY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: simulation);
}