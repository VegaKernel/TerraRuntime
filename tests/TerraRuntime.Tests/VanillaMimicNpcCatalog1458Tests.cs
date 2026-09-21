using TerraRuntime.Contracts.Gameplay;
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
}
