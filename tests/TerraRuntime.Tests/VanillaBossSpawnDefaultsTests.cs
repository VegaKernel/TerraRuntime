using TerraRuntime.Gameplay.Npcs;
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

    [Theory]
    [InlineData(1f, 1, false, false, 60_000, 100, 150, 100, 1f)]
    [InlineData(2f, 1, false, false, 78_000, 140, 150, 100, 1f)]
    [InlineData(3f, 1, false, false, 99_450, 210, 150, 100, 1f)]
    [InlineData(2f, 2, false, false, 105_300, 140, 150, 100, 1f)]
    [InlineData(1f, 1, false, true, 60_000, 100, 75, 50, .5f)]
    [InlineData(2f, 1, true, false, 78_000, 140, 195, 130, 1.3f)]
    [InlineData(2f, 1, true, true, 78_000, 140, 195, 130, 1.3f)]
    public void Duke_spawn_defaults_follow_source_difficulty_player_balance_and_seed_precedence(
        float difficulty, int players, bool goodWorld, bool tenthAnniversary, int life, int damage, int width, int height, float scale)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.DukeFishron, out VanillaNpcDefinition duke));
        var context = new VanillaNpcSpawnContext(difficulty, players, GoodWorld: goodWorld)
        {
            TenthAnniversaryWorld = tenthAnniversary
        };

        Assert.True(VanillaNpcSpawnDefaults.TryResolve(in duke, in context, windowsArithmetic: true, out VanillaNpcSpawnDefaults actual));

        Assert.Equal(life, actual.LifeMax);
        Assert.Equal(damage, actual.Damage);
        Assert.Equal(50, actual.Defense);
        Assert.Equal(width, actual.Hitbox.Width);
        Assert.Equal(height, actual.Hitbox.Height);
        Assert.Equal(scale, actual.Scale);
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
