using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaMimicNpcCatalog1458Tests
{
    [Theory]
    [InlineData(85, 80, 30, 500, .3f)]
    [InlineData(341, 100, 32, 900, .25f)]
    [InlineData(629, 80, 30, 500, .3f)]
    public void Ai025_mimics_keep_source_defaults(int type, int damage, int defense, int lifeMax, float knockBackResist)
    {
        Assert.True(VanillaMimicNpcCatalog1458.TryGetDefinition(new NpcTypeId(type), out VanillaNpcDefinition definition));
        Assert.Equal(new NpcAiStyleId(25), definition.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.MoonEventJumpingFighter, definition.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.GenericGround, definition.PhysicsFamily);
        Assert.Equal(24, definition.BaseWidth);
        Assert.Equal(24, definition.BaseHeight);
        Assert.Equal(damage, definition.Damage);
        Assert.Equal(defense, definition.Defense);
        Assert.Equal(lifeMax, definition.LifeMax);
        Assert.Equal(knockBackResist, definition.KnockBackResist, 5);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(type), out VanillaNpcDefinition resolved));
        Assert.Equal(definition, resolved);
    }

    [Theory]
    [InlineData(85, false, 1f, 1, 300, 30, 12, .3f)]
    [InlineData(629, false, 2f, 1, 600, 60, 12, .27f)]
    [InlineData(85, true, 2f, 1, 1000, 160, 30, .27f)]
    [InlineData(341, false, 3f, 1, 2700, 300, 32, .2f)]
    public void Mimic_spawn_defaults_apply_setdefaults_before_generic_difficulty_scaling(
        int type, bool hardMode, float difficulty, int players, int lifeMax, int damage, int defense, float knockBackResist)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(type), out VanillaNpcDefinition definition));
        var context = new VanillaNpcSpawnContext(difficulty, players, false) { HardMode = hardMode };
        Assert.True(VanillaNpcSpawnDefaults.TryResolve(in definition, in context, out VanillaNpcSpawnDefaults resolved));
        Assert.Equal(lifeMax, resolved.LifeMax);
        Assert.Equal(damage, resolved.Damage);
        Assert.Equal(defense, resolved.Defense);
        Assert.Equal(knockBackResist, resolved.KnockBackResist.GetValueOrDefault(), 5);

        var store = new RuntimeNpcStore();
        store.SetVanillaSpawnContextSource(() => context);
        Assert.True(store.TrySpawnIntent(new(new NpcTypeId(type), 1000, 1000, 0, 0, 0), out NpcSnapshot spawned));
        Assert.Equal(lifeMax, spawned.Simulation.LifeMax);
        Assert.Equal(lifeMax, spawned.Simulation.Life);
        Assert.Equal(lifeMax, spawned.Simulation.BaseLifeMax);
        Assert.Equal(damage, spawned.Simulation.BaseDamage);
        Assert.Equal(defense, spawned.Simulation.BaseDefense);
        Assert.Equal(knockBackResist, spawned.Simulation.KnockBackResist.GetValueOrDefault(), 5);
    }
}
