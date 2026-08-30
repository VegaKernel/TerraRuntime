using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcDefinitionCatalogTests
{
    [Theory]
    [InlineData(1, 1, 24, 18, 7, 2, 25, 1f, 1f, false, false)]
    [InlineData(2, 2, 30, 32, 18, 2, 60, 0.8f, 1f, false, false)]
    [InlineData(3, 3, 18, 40, 14, 6, 45, 0.5f, 1f, false, false)]
    [InlineData(4, 4, 100, 110, 15, 12, 2800, 0f, 1f, true, true)]
    [InlineData(5, 5, 20, 20, 12, 0, 8, 1f, 1f, true, true)]
    [InlineData(21, 3, 18, 40, 20, 8, 60, 0.5f, 1f, false, false)]
    [InlineData(50, 15, 98, 92, 40, 10, 2000, 0f, 1.25f, false, false)]
    public void Verified_initial_definitions_match_official_1458_defaults(
        int type,
        int aiStyle,
        int baseWidth,
        int baseHeight,
        int damage,
        int defense,
        int lifeMax,
        float knockBackResist,
        float scale,
        bool noGravityAtSpawn,
        bool noTileCollideAtSpawn)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition));
        Assert.Equal(type, definition.Type.Value);
        Assert.Equal(aiStyle, definition.AiStyle.Value);
        Assert.Equal(baseWidth, definition.BaseWidth);
        Assert.Equal(baseHeight, definition.BaseHeight);
        Assert.Equal(damage, definition.Damage);
        Assert.Equal(defense, definition.Defense);
        Assert.Equal(lifeMax, definition.LifeMax);
        Assert.Equal(knockBackResist, definition.KnockBackResist);
        Assert.Equal(scale, definition.Scale);
        Assert.Equal((int)Math.Floor(baseWidth * (double)scale), definition.Width);
        Assert.Equal((int)Math.Floor(baseHeight * (double)scale), definition.Height);
        Assert.Equal(noGravityAtSpawn, definition.NoGravityAtSpawn);
        Assert.Equal(noTileCollideAtSpawn, definition.NoTileCollideAtSpawn);
    }

    [Theory]
    [InlineData(1, VanillaNpcBehaviorFamily.SlimeGround, VanillaNpcPhysicsFamily.SlimeGround)]
    [InlineData(2, VanillaNpcBehaviorFamily.FlyingEye, VanillaNpcPhysicsFamily.FlyingEye)]
    [InlineData(3, VanillaNpcBehaviorFamily.GroundFighter, VanillaNpcPhysicsFamily.GroundFighter)]
    [InlineData(4, VanillaNpcBehaviorFamily.EyeOfCthulhu, VanillaNpcPhysicsFamily.NoClipFlight)]
    [InlineData(5, VanillaNpcBehaviorFamily.Flyer, VanillaNpcPhysicsFamily.NoClipFlight)]
    [InlineData(21, VanillaNpcBehaviorFamily.GroundFighter, VanillaNpcPhysicsFamily.GroundFighter)]
    [InlineData(50, VanillaNpcBehaviorFamily.KingSlime, VanillaNpcPhysicsFamily.SlimeGround)]
    public void Verified_definitions_explicitly_opt_into_runtime_behavior_and_physics_families(
        int type,
        VanillaNpcBehaviorFamily expectedBehavior,
        VanillaNpcPhysicsFamily expectedPhysics)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition));
        Assert.Equal(expectedBehavior, definition.BehaviorFamily);
        Assert.Equal(expectedPhysics, definition.PhysicsFamily);
    }

    [Fact]
    public void King_slime_initial_hitbox_applies_source_scale_with_vanilla_flooring()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition));
        Assert.True(definition.TryResolveHitbox(definition.Scale, out VanillaNpcHitboxSize hitbox));
        Assert.Equal(122, hitbox.Width);
        Assert.Equal(115, hitbox.Height);
        Assert.Equal(122, definition.Width);
        Assert.Equal(115, definition.Height);
    }

    [Fact]
    public void Runtime_families_are_distinct_from_source_ai_style()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.BlueSlime, out VanillaNpcDefinition slime));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.DemonEye, out VanillaNpcDefinition eye));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Zombie, out VanillaNpcDefinition fighter));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.EyeOfCthulhu, out VanillaNpcDefinition boss));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.ServantOfCthulhu, out VanillaNpcDefinition servant));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Skeleton, out VanillaNpcDefinition skeleton));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition kingSlime));

        Assert.Equal(VanillaNpcAiStyles.Slime, slime.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.SlimeGround, slime.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.SlimeGround, slime.PhysicsFamily);
        Assert.Equal(VanillaNpcAiStyles.DemonEye, eye.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.FlyingEye, eye.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.FlyingEye, eye.PhysicsFamily);
        Assert.Equal(VanillaNpcAiStyles.Fighter, fighter.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.GroundFighter, fighter.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.GroundFighter, fighter.PhysicsFamily);
        Assert.Equal(VanillaNpcAiStyles.EyeOfCthulhu, boss.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.EyeOfCthulhu, boss.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.NoClipFlight, boss.PhysicsFamily);
        Assert.Equal(VanillaNpcAiStyles.Flyer, servant.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.Flyer, servant.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.NoClipFlight, servant.PhysicsFamily);
        Assert.Equal(VanillaNpcAiStyles.Fighter, skeleton.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.GroundFighter, skeleton.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.GroundFighter, skeleton.PhysicsFamily);
        Assert.Equal(VanillaNpcAiStyles.KingSlime, kingSlime.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.KingSlime, kingSlime.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.SlimeGround, kingSlime.PhysicsFamily);
    }

    [Fact]
    public void Vanilla_role_metadata_distinguishes_boss_from_ordinary_npcs()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.EyeOfCthulhu, out VanillaNpcDefinition eyeBoss));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition kingSlime));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.ServantOfCthulhu, out VanillaNpcDefinition servant));

        Assert.Equal(NpcArchetypeRole.Boss, eyeBoss.Role);
        Assert.True(eyeBoss.IsBoss);
        Assert.Equal(NpcArchetypeRole.Boss, kingSlime.Role);
        Assert.True(kingSlime.IsBoss);
        Assert.Equal(NpcArchetypeRole.Ordinary, servant.Role);
        Assert.False(servant.IsBoss);
    }

    [Fact]
    public void Named_npc_ids_address_the_same_verified_catalog()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.KingSlime, out VanillaNpcDefinition definition));
        Assert.Equal(VanillaNpcIds.KingSlime, definition.Type);
        Assert.Equal(VanillaNpcAiStyles.KingSlime, definition.AiStyle);
        Assert.Equal(NpcArchetypeRole.Boss, definition.Role);
    }

    [Fact]
    public void Vanilla_initial_lifecycle_defaults_are_explicit()
    {
        Assert.Equal((ushort)255, VanillaNpcDefinitionCatalog.DefaultTarget);
        Assert.Equal(750, VanillaNpcDefinitionCatalog.DefaultTimeLeft);
        Assert.Equal(-1, VanillaNpcDefinitionCatalog.DefaultSpriteDirection);
    }

    [Fact]
    public void Unverified_type_is_not_silently_fabricated()
    {
        Assert.False(VanillaNpcDefinitionCatalog.TryGet(999, out VanillaNpcDefinition definition));
        Assert.Equal(default, definition);
    }
}
