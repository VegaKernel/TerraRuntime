using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaMoonEventGroundFighterCatalog1458Tests
{
    [Theory]
    [InlineData(305, 18, 40, 60, 18, 500, .4f, 1f)]
    [InlineData(309, 18, 40, 52, 26, 450, .5f, 1.1f)]
    [InlineData(326, 18, 40, 100, 32, 1200, .2f, 1f)]
    [InlineData(343, 38, 78, 140, 50, 3500, 0f, 1f)]
    [InlineData(351, 18, 90, 100, 40, 2500, .1f, 1f)]
    public void SetDefaults_match_the_source_ai3_moon_event_entries(
        short type, int width, int height, int damage, int defense, int life, float knockBackResist, float scale)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(type), out VanillaNpcDefinition definition));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(new NpcTypeId(type), out _));
        Assert.True(VanillaGroundFighterBehaviorCatalog.TryGet(new NpcTypeId(type), out _));
        Assert.Equal(VanillaNpcAiStyles.Fighter, definition.AiStyle);
        Assert.Equal(width, definition.BaseWidth);
        Assert.Equal(height, definition.BaseHeight);
        Assert.Equal(damage, definition.Damage);
        Assert.Equal(defense, definition.Defense);
        Assert.Equal(life, definition.LifeMax);
        Assert.Equal(knockBackResist, definition.KnockBackResist);
        Assert.Equal(scale, definition.Scale);
    }
}
