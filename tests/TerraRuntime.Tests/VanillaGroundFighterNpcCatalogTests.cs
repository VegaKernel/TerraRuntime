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
        [new NpcTypeId(254), 18, 40, 40, 10, 180, .4f, 1f, 1.5f, false, false],
        [new NpcTypeId(255), 18, 40, 38, 16, 220, .3f, 1f, 1f, false, false],
        [new NpcTypeId(257), 44, 34, 38, 24, 230, .3f, 1f, 2f, false, false],
        [new NpcTypeId(258), 30, 24, 60, 16, 220, .3f, 1f, 3f, false, false],
        [VanillaNpcIds.VampireHumanoid, 18, 40, 80, 24, 750, 0.4f, 1f, 6f, false, false],
        [VanillaNpcIds.ZombieElf, 18, 40, 65, 18, 600, 0.4f, 1f, 1.75f, false, false],
        [VanillaNpcIds.ZombieElfBeard, 18, 40, 52, 24, 700, 0.2f, 1.05f, 1.25f, false, false],
        [VanillaNpcIds.ZombieElfGirl, 18, 40, 78, 14, 500, 0.25f, 0.9f, 2f, false, false],
        [VanillaNpcIds.ZombieDoctor, 18, 40, 20, 6, 40, 0.6f, 0.9f, 1f, false, false],
        [VanillaNpcIds.ZombieSuperman, 18, 40, 15, 8, 60, 0.5f, 1.05f, 1f, false, false],
        [VanillaNpcIds.ZombiePixie, 18, 40, 20, 14, 34, 0.3f, 1.1f, 1f, false, false],
        [VanillaNpcIds.SkeletonTopHat, 18, 40, 23, 0, 115, 0.65f, 1f, 1f, false, false],
        [VanillaNpcIds.SkeletonAstronaut, 18, 40, 18, 10, 65, 0.5f, 1f, 1f, false, false],
        [VanillaNpcIds.SkeletonAlien, 18, 40, 22, 10, 70, 0.4f, 1.05f, 1f, false, false],
        [VanillaNpcIds.ZombieXmas, 18, 40, 14, 6, 45, 0.5f, 1f, 1f, true, false],
        [VanillaNpcIds.ZombieSweater, 18, 40, 14, 6, 45, 0.5f, 1f, 1f, true, false],
        [VanillaNpcIds.ArmedZombie, 18, 40, 14, 6, 45, 0.5f, 1f, 1f, false, false],
        [VanillaNpcIds.ArmedZombieEskimo, 18, 40, 16, 8, 50, 0.45f, 1f, 1f, false, false],
        [VanillaNpcIds.ArmedZombiePincushion, 18, 40, 16, 8, 50, 0.45f, 1f, 1f, false, false],
        [VanillaNpcIds.ArmedZombieSlimed, 18, 40, 13, 6, 40, 0.55f, 1f, 1f, false, false],
        [VanillaNpcIds.ArmedZombieSwamp, 18, 40, 13, 8, 45, 0.45f, 1f, 1f, false, false],
        [VanillaNpcIds.ArmedZombieTwiggy, 18, 40, 16, 4, 45, 0.55f, 1f, 1f, false, false],
        [VanillaNpcIds.ArmedZombieCenx, 18, 40, 12, 4, 38, 0.6f, 1f, 1f, false, false],
        [VanillaNpcIds.ArmedTorchZombie, 18, 40, 14, 6, 45, 0.5f, 1f, 1f, false, false],
        [VanillaNpcIds.Crawdad, 28, 22, 28, 6, 50, 1f, 1f, 1f, true, false],
        [VanillaNpcIds.Crawdad2, 28, 22, 28, 6, 50, 1f, 1f, 1f, true, false],
        [VanillaNpcIds.Salamander, 24, 44, 18, 10, 65, 1f, 1f, 1f, false, false],
        [VanillaNpcIds.Salamander2, 24, 44, 18, 10, 65, 1f, 1f, 1f, false, false],
        [VanillaNpcIds.Salamander3, 24, 44, 18, 10, 65, 1f, 1f, 1f, false, false],
        [VanillaNpcIds.Salamander4, 24, 44, 18, 10, 65, 1f, 1f, 1f, false, false],
        [VanillaNpcIds.Salamander5, 24, 44, 18, 10, 65, 1f, 1f, 1f, false, false],
        [VanillaNpcIds.Salamander6, 24, 44, 18, 10, 65, 1f, 1f, 1f, false, false],
        [VanillaNpcIds.Salamander7, 24, 44, 18, 10, 65, 1f, 1f, 1f, false, false],
        [VanillaNpcIds.Salamander8, 24, 44, 18, 10, 65, 1f, 1f, 1f, false, false],
        [VanillaNpcIds.Salamander9, 24, 44, 18, 10, 65, 1f, 1f, 1f, false, false],
        [VanillaNpcIds.BigAngryBones, 18, 40, 34, 6, 70, .9f, 1f, 1f, false, true],
        [VanillaNpcIds.BigMuscleAngryBones, 18, 40, 28, 12, 70, .7f, 1f, 1f, false, true],
        [VanillaNpcIds.BigHelmetAngryBones, 18, 40, 24, 14, 120, .6f, 1f, 1f, false, true],
        [VanillaNpcIds.SkeletonSniper, 18, 40, 60, 28, 400, .4f, 1f, 1f, false, false],
        [VanillaNpcIds.TacticalSkeleton, 18, 40, 60, 28, 400, .4f, 1f, 1f, false, false],
        [VanillaNpcIds.SkeletonCommando, 18, 40, 60, 28, 400, .4f, 1f, 1f, false, false],
        [VanillaNpcIds.Paladin, 34, 62, 100, 50, 5000, 0f, 1f, 1f, false, false],
        [VanillaNpcIds.SkeletonArcher, 18, 40, 45, 14, 210, .55f, 1f, 1f, false, false],
        [VanillaNpcIds.GoblinArcher, 18, 40, 20, 6, 80, .7f, .95f, 1f, false, false],
        [VanillaNpcIds.IcyMerman, 18, 40, 60, 30, 280, .5f, 1f, 1f, false, false],
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
        Assert.Equal(82, VanillaGroundFighterNpcCatalog.DefinitionCount);
        Assert.Equal(80, VanillaGroundFighterNpcCatalog.AdditionalDefinitionCount);

        Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(VanillaNpcIds.VampireHumanoid, out var vampire));
        Assert.Equal(6f, vampire.BaseMaximumHorizontalSpeed, 5);
        Assert.Equal(.95f, vampire.ReversingVelocityDamping, 5);
    }

    [Theory]
    [InlineData(254, 1.5f)]
    [InlineData(255, 1f)]
    [InlineData(257, 2f)]
    [InlineData(258, 3f)]
    public void Source_day_surface_exempt_fighters_keep_their_distinct_profiles(int type, float speed)
    {
        Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(new NpcTypeId(type), out var behavior));

        Assert.False(behavior.DaySurfaceEncouragesDespawn);
        Assert.Equal(speed, behavior.BaseMaximumHorizontalSpeed, 5);
    }

    [Fact]
    public void Hardmode_dungeon_skeleton_fighters_keep_their_source_speed_band()
    {
        foreach ((int type, float speed) in new[] { (269, 2f), (270, 1f), (271, 1.5f), (272, 3f),
            (273, 1.25f), (274, 3f), (275, 3.25f), (276, 2f), (277, 2.75f), (278, 1.8f),
            (279, 1.3f), (280, 2.5f) })
        {
            Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(new NpcTypeId(type), out var behavior));
            Assert.Equal(speed, behavior.BaseMaximumHorizontalSpeed, 5);
            Assert.Equal(.07f, behavior.HorizontalAcceleration, 5);
        }
    }

    [Theory]
    [InlineData(338, 1.75f)]
    [InlineData(339, 1.25f)]
    [InlineData(340, 2f)]
    public void Snow_moon_zombie_elves_keep_their_source_ai003_speed_bands(int type, float speed)
    {
        Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(new NpcTypeId(type), out var behavior));

        Assert.Equal(speed, behavior.BaseMaximumHorizontalSpeed, 5);
        Assert.Equal(.07f, behavior.HorizontalAcceleration, 5);
        Assert.False(behavior.ScaleAdjustsMaximumHorizontalSpeed);
        Assert.Equal(VanillaGroundFighterMotionProfile.Standard, behavior.MotionProfile);
    }

    [Fact]
    public void Halloween_zombie_and_skeleton_variants_use_the_generic_source_ai003_profile()
    {
        foreach (NpcTypeId type in new[]
        {
            VanillaNpcIds.ZombieDoctor,
            VanillaNpcIds.ZombieSuperman,
            VanillaNpcIds.ZombiePixie,
            VanillaNpcIds.SkeletonTopHat,
            VanillaNpcIds.SkeletonAstronaut,
            VanillaNpcIds.SkeletonAlien
        })
        {
            Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(type, out var behavior));
            Assert.Equal(1f, behavior.BaseMaximumHorizontalSpeed, 5);
            Assert.Equal(.07f, behavior.HorizontalAcceleration, 5);
            Assert.False(behavior.ScaleAdjustsMaximumHorizontalSpeed);
            Assert.Equal(VanillaGroundFighterMotionProfile.Standard, behavior.MotionProfile);
        }
    }

    [Fact]
    public void Christmas_zombies_keep_the_source_scale_adjusted_ai003_profile()
    {
        foreach (NpcTypeId type in new[] { VanillaNpcIds.ZombieXmas, VanillaNpcIds.ZombieSweater })
        {
            Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(type, out var behavior));
            Assert.Equal(1f, behavior.BaseMaximumHorizontalSpeed, 5);
            Assert.Equal(.07f, behavior.HorizontalAcceleration, 5);
            Assert.True(behavior.ScaleAdjustsMaximumHorizontalSpeed);
            Assert.Equal(VanillaGroundFighterMotionProfile.Standard, behavior.MotionProfile);
        }
    }

    [Fact]
    public void Armed_zombies_keep_the_source_ai003_armed_melee_profile()
    {
        foreach (NpcTypeId type in new[]
        {
            VanillaNpcIds.ArmedZombie, VanillaNpcIds.ArmedZombieEskimo, VanillaNpcIds.ArmedZombiePincushion,
            VanillaNpcIds.ArmedZombieSlimed, VanillaNpcIds.ArmedZombieSwamp, VanillaNpcIds.ArmedZombieTwiggy,
            VanillaNpcIds.ArmedZombieCenx, VanillaNpcIds.ArmedTorchZombie
        })
        {
            Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(type, out var behavior));
            Assert.Equal(1f, behavior.BaseMaximumHorizontalSpeed, 5);
            Assert.Equal(.07f, behavior.HorizontalAcceleration, 5);
            Assert.False(behavior.ScaleAdjustsMaximumHorizontalSpeed);
            Assert.Equal(VanillaGroundFighterMotionProfile.ArmedZombie, behavior.MotionProfile);
        }
    }

    [Fact]
    public void Crawdad_pair_keeps_the_source_short_range_armed_melee_profile()
    {
        foreach (NpcTypeId type in new[] { VanillaNpcIds.Crawdad, VanillaNpcIds.Crawdad2 })
        {
            Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(type, out var behavior));
            Assert.True(behavior.ScaleAdjustsMaximumHorizontalSpeed);
            Assert.Equal(VanillaGroundFighterMotionProfile.Crawdad, behavior.MotionProfile);
        }
    }

    [Fact]
    public void Salamander_variants_keep_the_source_stationary_ranged_profile()
    {
        for (int rawType = 498; rawType <= 506; rawType++)
        {
            Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(new NpcTypeId(rawType), out var behavior));
            Assert.Equal(VanillaGroundFighterMotionProfile.Salamander, behavior.MotionProfile);
            Assert.Equal(1f, behavior.BaseMaximumHorizontalSpeed, 5);
            Assert.Equal(.07f, behavior.HorizontalAcceleration, 5);
        }
    }

    [Fact]
    public void Big_angry_bones_variants_keep_their_source_leap_profile()
    {
        foreach (NpcTypeId type in new[]
        {
            VanillaNpcIds.BigAngryBones,
            VanillaNpcIds.BigMuscleAngryBones,
            VanillaNpcIds.BigHelmetAngryBones
        })
        {
            Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(type, out var behavior));
            Assert.Equal(VanillaGroundFighterMotionProfile.Standard, behavior.MotionProfile);
            Assert.True(behavior.CloseRangeLunge);
        }
    }

    [Theory]
    [InlineData(78, 50, 16, 130, .6f, 1f, .05f, 1f)]
    [InlineData(79, 60, 18, 180, .5f, 1f, .05f, 1.5f)]
    [InlineData(80, 55, 18, 200, .55f, 1f, .05f, 1f)]
    [InlineData(630, 60, 18, 180, .5f, 1f, .05f, 1.5f)]
    public void Half_health_ai003_fighters_keep_source_defaults_and_enrage_profile(
        int type,
        int damage,
        int defense,
        int lifeMax,
        float knockBackResist,
        float speed,
        float acceleration,
        float halfHealthSpeedMultiplier)
    {
        Assert.True(VanillaGroundFighterNpcCatalog.TryGetDefinition(new NpcTypeId(type), out var definition));
        Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(new NpcTypeId(type), out var behavior));

        Assert.Equal(damage, definition.Damage);
        Assert.Equal(defense, definition.Defense);
        Assert.Equal(lifeMax, definition.LifeMax);
        Assert.Equal(knockBackResist, definition.KnockBackResist, 5);
        Assert.Equal(speed, behavior.BaseMaximumHorizontalSpeed, 5);
        Assert.Equal(acceleration, behavior.HorizontalAcceleration, 5);
        Assert.Equal(VanillaGroundFighterMotionProfile.HalfHealthBerserker, behavior.MotionProfile);
        Assert.Equal(halfHealthSpeedMultiplier, behavior.HalfHealthSpeedMultiplier, 5);
        Assert.Equal(.7f, behavior.OverspeedGroundDamping, 5);
    }

    [Fact]
    public void Fast_ai003_fighter_keeps_the_source_speed_acceleration_and_damping()
    {
        Assert.True(VanillaGroundFighterNpcCatalog.TryGetDefinition(new NpcTypeId(287), out var definition));
        Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(new NpcTypeId(287), out var behavior));

        Assert.Equal(90, definition.Damage);
        Assert.Equal(42, definition.Defense);
        Assert.Equal(1000, definition.LifeMax);
        Assert.Equal(.3f, definition.KnockBackResist, 5);
        Assert.Equal(5f, behavior.BaseMaximumHorizontalSpeed, 5);
        Assert.Equal(.2f, behavior.HorizontalAcceleration, 5);
        Assert.Equal(VanillaGroundFighterMotionProfile.Standard, behavior.MotionProfile);
        Assert.Equal(.7f, behavior.OverspeedGroundDamping, 5);
    }

    [Fact]
    public void Half_health_ai003_profile_uses_the_source_strict_health_threshold()
    {
        var fullHealth = CreateHalfHealthInput(life: 50);
        var lowHealth = CreateHalfHealthInput(life: 49);

        Assert.True(VanillaZombieMotion.TryStep(in fullHealth, out var fullHealthResult));
        Assert.True(VanillaZombieMotion.TryStep(in lowHealth, out var lowHealthResult));

        Assert.Equal(1.05f, fullHealthResult.VelocityX, 5);
        Assert.Equal(1.1f, lowHealthResult.VelocityX, 5);
    }

    [Theory]
    [InlineData(243, 30, 114, 60, 32, 4000, .05f, .07f, 1.5f, .15f)]
    [InlineData(251, 18, 40, 50, 30, 1000, .3f, .08f, 2f, .2f)]
    public void Missing_health_ai003_fighters_keep_source_defaults_and_linear_motion_profile(
        int type,
        int width,
        int height,
        int damage,
        int defense,
        int lifeMax,
        float knockBackResist,
        float acceleration,
        float speedBonus,
        float accelerationBonus)
    {
        Assert.True(VanillaGroundFighterNpcCatalog.TryGetDefinition(new NpcTypeId(type), out var definition));
        Assert.True(VanillaGroundFighterNpcCatalog.TryGetBehavior(new NpcTypeId(type), out var behavior));

        Assert.Equal(width, definition.BaseWidth);
        Assert.Equal(height, definition.BaseHeight);
        Assert.Equal(damage, definition.Damage);
        Assert.Equal(defense, definition.Defense);
        Assert.Equal(lifeMax, definition.LifeMax);
        Assert.Equal(knockBackResist, definition.KnockBackResist, 5);
        Assert.Equal(acceleration, behavior.HorizontalAcceleration, 5);
        Assert.Equal(VanillaGroundFighterMotionProfile.MissingHealthBerserker, behavior.MotionProfile);
        Assert.Equal(speedBonus, behavior.MissingHealthSpeedBonus, 5);
        Assert.Equal(accelerationBonus, behavior.MissingHealthAccelerationBonus, 5);
        Assert.Equal(.7f, behavior.OverspeedGroundDamping, 5);
    }

    private static VanillaZombieMotionInput CreateHalfHealthInput(int life) => new(
        PositionX: 100f,
        OldPositionX: 99f,
        VelocityX: 1f,
        VelocityY: 0f,
        DirectionX: 1,
        DirectionY: 1,
        Target: VanillaNpcDefinitionCatalog.DefaultTarget,
        Ai: default,
        Scale: 1f,
        TargetOverlaps: false,
        ClosestTarget: new VanillaZombieTargetRefresh(true, 3, 1, 1))
    {
        BaseMaximumHorizontalSpeed = 1f,
        HorizontalAcceleration = .05f,
        MotionProfile = VanillaGroundFighterMotionProfile.HalfHealthBerserker,
        HalfHealthSpeedMultiplier = 1.5f,
        OverspeedGroundDamping = .7f,
        Life = life,
        LifeMax = 100,
        TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft
    };
}
