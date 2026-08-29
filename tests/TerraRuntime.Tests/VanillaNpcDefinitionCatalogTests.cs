using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcDefinitionCatalogTests
{
    [Theory]
    [InlineData(1, 1, 24, 18, 7, 2, 25, 1f)]
    [InlineData(2, 2, 30, 32, 18, 2, 60, 0.8f)]
    [InlineData(3, 3, 18, 40, 14, 6, 45, 0.5f)]
    public void Verified_initial_definitions_match_official_1458_defaults(
        int type,
        int aiStyle,
        int width,
        int height,
        int damage,
        int defense,
        int lifeMax,
        float knockBackResist)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition));
        Assert.Equal(type, definition.Type.Value);
        Assert.Equal(aiStyle, definition.AiStyle.Value);
        Assert.Equal(width, definition.Width);
        Assert.Equal(height, definition.Height);
        Assert.Equal(damage, definition.Damage);
        Assert.Equal(defense, definition.Defense);
        Assert.Equal(lifeMax, definition.LifeMax);
        Assert.Equal(knockBackResist, definition.KnockBackResist);
        Assert.Equal(1f, definition.Scale);
    }

    [Theory]
    [InlineData(1, VanillaNpcBehaviorFamily.SlimeGround)]
    [InlineData(2, VanillaNpcBehaviorFamily.FlyingEye)]
    [InlineData(3, VanillaNpcBehaviorFamily.GroundFighter)]
    public void Verified_definitions_explicitly_opt_into_runtime_behavior_families(
        int type,
        VanillaNpcBehaviorFamily expectedFamily)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition));
        Assert.Equal(expectedFamily, definition.BehaviorFamily);
    }

    [Fact]
    public void Runtime_behavior_family_is_distinct_from_source_ai_style()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.BlueSlime, out VanillaNpcDefinition slime));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.DemonEye, out VanillaNpcDefinition eye));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Zombie, out VanillaNpcDefinition fighter));

        Assert.Equal(VanillaNpcAiStyles.Slime, slime.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.SlimeGround, slime.BehaviorFamily);
        Assert.Equal(VanillaNpcAiStyles.DemonEye, eye.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.FlyingEye, eye.BehaviorFamily);
        Assert.Equal(VanillaNpcAiStyles.Fighter, fighter.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.GroundFighter, fighter.BehaviorFamily);
    }

    [Fact]
    public void Named_npc_ids_address_the_same_verified_catalog()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.DemonEye, out VanillaNpcDefinition definition));
        Assert.Equal(VanillaNpcIds.DemonEye, definition.Type);
        Assert.Equal(VanillaNpcAiStyles.DemonEye, definition.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.FlyingEye, definition.BehaviorFamily);
    }

    [Fact]
    public void Vanilla_initial_lifecycle_defaults_are_explicit()
    {
        Assert.Equal((ushort)255, VanillaNpcDefinitionCatalog.DefaultTarget);
        Assert.Equal(750, VanillaNpcDefinitionCatalog.DefaultTimeLeft);
    }

    [Fact]
    public void Unverified_type_is_not_silently_fabricated()
    {
        Assert.False(VanillaNpcDefinitionCatalog.TryGet(4, out VanillaNpcDefinition definition));
        Assert.Equal(default, definition);
    }
}
