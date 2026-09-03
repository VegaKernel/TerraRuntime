using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaGroundFighterNpcCatalogTests
{
    public static IEnumerable<object[]> AdditionalDefinitions =>
    [
        [VanillaNpcIds.GoblinPeon, 18, 38, 12, 4, 60, 0.8f, 0.9f, 1.5f, false, false],
        [VanillaNpcIds.GoblinThief, 18, 38, 20, 6, 80, 0.7f, 0.95f, 2f, false, false],
        [VanillaNpcIds.GoblinWarrior, 18, 38, 25, 8, 110, 0.5f, 1.1f, 1f, false, false],
        [VanillaNpcIds.AngryBones, 18, 40, 26, 8, 80, 0.8f, 1f, 1.5f, false, true],
        [VanillaNpcIds.DoctorBones, 18, 40, 20, 10, 500, 0.5f, 1f, 1f, false, false],
        [VanillaNpcIds.TheGroom, 18, 40, 14, 8, 200, 0.5f, 1f, 1f, false, false],
        [VanillaNpcIds.GoblinScout, 18, 40, 20, 6, 80, 0.7f, 0.95f, 1.5f, false, false],
        [VanillaNpcIds.ArmoredSkeleton, 18, 40, 40, 28, 260, 0.4f, 1f, 2f, false, true],
        [VanillaNpcIds.BaldZombie, 18, 40, 15, 5, 40, 0.5f, 1f, 0.95f, true, false],
        [VanillaNpcIds.ZombieEskimo, 18, 40, 16, 8, 50, 0.45f, 1f, 1f, false, false],
        [VanillaNpcIds.UndeadViking, 18, 40, 24, 10, 70, 0.5f, 1f, 1.5f, false, false],
        [VanillaNpcIds.PincushionZombie, 18, 40, 16, 8, 50, 0.45f, 1f, 1.1f, true, false],
        [VanillaNpcIds.SlimedZombie, 18, 40, 13, 6, 40, 0.55f, 1f, 0.9f, true, false],
        [VanillaNpcIds.SwampZombie, 18, 40, 13, 8, 45, 0.45f, 1f, 1.2f, true, false],
        [VanillaNpcIds.TwiggyZombie, 18, 40, 16, 4, 45, 0.55f, 1f, 0.8f, true, false],
        [VanillaNpcIds.FemaleZombie, 18, 40, 12, 4, 38, 0.6f, 1f, 0.87f, true, false],
    ];

    [Theory]
    [MemberData(nameof(AdditionalDefinitions))]
    public void Additional_ai003_hostiles_keep_source_backed_defaults_and_motion_profile(
        NpcTypeId type,
        int width,
        int height,
        int damage,
        int defense,
        int lifeMax,
        float knockBackResist,
        float scale,
        float maximumHorizontalSpeed,
        bool scaleAdjustsSpeed,
        bool closeRangeLunge)
    {
        Assert.True(VanillaGroundFighterNpcCatalog.TryGetDefinition(type, out VanillaNpcDefinition definition));
        Assert.Equal(VanillaNpcAiStyles.Fighter, definition.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.GroundFighter, definition.BehaviorFamily);
        Assert.Equal(VanillaNpcPhysicsFamily.GroundFighter, definition.PhysicsFamily);
        Assert.Equal(NpcArchetypeRole.Ordinary, definition.Role);
        Assert.Equal(width, definition.BaseWidth);
        Assert.Equal(height, definition.BaseHeight);
        Assert.Equal(damage, definition.Damage);
        Assert.Equal(defense, definition.Defense);
        Assert.Equal(lifeMax, definition.LifeMax);
        Assert.Equal(knockBackResist, definition.KnockBackResist, 5);
        Assert.Equal(scale, definition.Scale, 5);

        Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(type, out VanillaGroundFighterBehaviorParameters behavior));
        Assert.True(behavior.IsValid);
        Assert.Equal(maximumHorizontalSpeed, behavior.BaseMaximumHorizontalSpeed, 5);
        Assert.Equal(0.07f, behavior.HorizontalAcceleration, 5);
        Assert.Equal(scaleAdjustsSpeed, behavior.ScaleAdjustsMaximumHorizontalSpeed);
        Assert.Equal(closeRangeLunge, behavior.CloseRangeLunge);
    }

    [Fact]
    public void Existing_zombie_and_skeleton_remain_source_backed_members_of_shared_catalog()
    {
        Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(VanillaNpcIds.Zombie, out var zombie));
        Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(VanillaNpcIds.Skeleton, out var skeleton));

        Assert.Equal(1f, zombie.BaseMaximumHorizontalSpeed, 5);
        Assert.Equal(1.5f, skeleton.BaseMaximumHorizontalSpeed, 5);
        Assert.True(zombie.ScaleAdjustsMaximumHorizontalSpeed);
        Assert.True(skeleton.ScaleAdjustsMaximumHorizontalSpeed);
        Assert.Equal(18, VanillaGroundFighterNpcCatalog.DefinitionCount);
        Assert.Equal(16, VanillaGroundFighterNpcCatalog.AdditionalDefinitionCount);
    }
}
