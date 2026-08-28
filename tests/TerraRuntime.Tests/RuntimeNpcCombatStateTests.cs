using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcCombatStateTests
{
    [Fact]
    public void Known_vanilla_spawn_materializes_definition_life_and_active_time()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate update = CreateBlueSlime();

        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot spawned));

        Assert.Equal(25, spawned.Simulation.Life);
        Assert.Equal(25, spawned.Simulation.LifeMax);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft, spawned.Simulation.TimeLeft);
    }

    [Fact]
    public void State_only_ai_update_preserves_existing_combat_and_lifetime_state()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate update = CreateBlueSlime();
        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot spawned));
        NpcStateUpdate stateOnly = update with
        {
            PositionX = 12f,
            Simulation = NpcSimulationState.Initial
        };

        Assert.True(store.TryUpdate(spawned.Handle, in stateOnly, out NpcSnapshot updated));

        Assert.Equal(25, updated.Simulation.Life);
        Assert.Equal(25, updated.Simulation.LifeMax);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft, updated.Simulation.TimeLeft);
    }

    [Fact]
    public void Explicit_damage_and_lifetime_update_replaces_owned_state()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate update = CreateBlueSlime();
        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot spawned));
        NpcStateUpdate damaged = update with
        {
            Simulation = NpcSimulationState.Initial with
            {
                Life = 10,
                LifeMax = 25,
                TimeLeft = 10
            }
        };

        Assert.True(store.TryUpdate(spawned.Handle, in damaged, out NpcSnapshot updated));

        Assert.Equal(10, updated.Simulation.Life);
        Assert.Equal(25, updated.Simulation.LifeMax);
        Assert.Equal(10, updated.Simulation.TimeLeft);
    }

    [Fact]
    public void Explicit_zero_lifetime_remains_expired_instead_of_rematerializing()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate update = CreateBlueSlime();
        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot spawned));
        NpcStateUpdate expired = update with
        {
            Simulation = NpcSimulationState.Initial with { TimeLeft = 0 }
        };

        Assert.True(store.TryUpdate(spawned.Handle, in expired, out NpcSnapshot updated));

        Assert.Equal(0, updated.Simulation.TimeLeft);
    }

    [Fact]
    public void Invalid_combat_or_lifetime_range_is_rejected_without_advancing_revision()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate update = CreateBlueSlime();
        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot spawned));
        NpcStateUpdate invalidCombat = update with
        {
            Simulation = NpcSimulationState.Initial with
            {
                Life = 26,
                LifeMax = 25
            }
        };
        NpcStateUpdate invalidLifetime = update with
        {
            Simulation = NpcSimulationState.Initial with { TimeLeft = -2 }
        };

        Assert.False(store.TryUpdate(spawned.Handle, in invalidCombat, out _));
        Assert.False(store.TryUpdate(spawned.Handle, in invalidLifetime, out _));
        Assert.True(store.TryGet(spawned.Handle, out NpcSnapshot unchanged));
        Assert.Equal(new NpcRevision(1), unchanged.Revision);
        Assert.Equal(25, unchanged.Simulation.Life);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft, unchanged.Simulation.TimeLeft);
    }

    private static NpcStateUpdate CreateBlueSlime() =>
        new(
            Type: 1,
            NetId: 1,
            PositionX: 0f,
            PositionY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);
}
