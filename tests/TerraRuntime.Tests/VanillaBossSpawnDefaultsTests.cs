using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaBossSpawnDefaultsTests
{
    [Theory]
    [InlineData(4, 2800)]
    [InlineData(5, 8)]
    public void No_clip_boss_family_materializes_source_backed_spawn_state(int rawType, int lifeMax)
    {
        var store = new RuntimeNpcStore();
        var update = new NpcStateUpdate(
            Type: rawType,
            NetId: checked((short)rawType),
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);

        Assert.True(store.TrySpawnVanilla(in update, out NpcSnapshot snapshot));
        Assert.Equal(lifeMax, snapshot.Simulation.Life);
        Assert.Equal(lifeMax, snapshot.Simulation.LifeMax);
        Assert.True(snapshot.Simulation.NoGravity);
        Assert.True(snapshot.Simulation.NoTileCollide);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTimeLeft, snapshot.Simulation.TimeLeft);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultSpriteDirection, snapshot.Simulation.SpriteDirection);
    }

    [Fact]
    public void Ordinary_npc_does_not_inherit_boss_flight_flags()
    {
        var store = new RuntimeNpcStore();
        var update = new NpcStateUpdate(
            Type: VanillaNpcIds.Zombie.Value,
            NetId: checked((short)VanillaNpcIds.Zombie.Value),
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);

        Assert.True(store.TrySpawnVanilla(in update, out NpcSnapshot snapshot));
        Assert.False(snapshot.Simulation.NoGravity);
        Assert.False(snapshot.Simulation.NoTileCollide);
    }
}
