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

    [Theory]
    [InlineData(305, 2f, VanillaGroundFighterMotionProfile.MoonEventLeaper)]
    [InlineData(306, 1.25f, VanillaGroundFighterMotionProfile.MoonEventLeaper)]
    [InlineData(307, 2.25f, VanillaGroundFighterMotionProfile.MoonEventLeaper)]
    [InlineData(308, 1.5f, VanillaGroundFighterMotionProfile.MoonEventLeaper)]
    [InlineData(309, 1f, VanillaGroundFighterMotionProfile.MoonEventLeaper)]
    [InlineData(310, 2f, VanillaGroundFighterMotionProfile.Standard)]
    [InlineData(311, 1.25f, VanillaGroundFighterMotionProfile.Standard)]
    [InlineData(312, 2.25f, VanillaGroundFighterMotionProfile.Standard)]
    [InlineData(313, 1.5f, VanillaGroundFighterMotionProfile.Standard)]
    [InlineData(314, 1f, VanillaGroundFighterMotionProfile.Standard)]
    public void Moon_event_ai3_fighters_keep_their_source_movement_family(
        short type,
        float maximumHorizontalSpeed,
        VanillaGroundFighterMotionProfile motionProfile)
    {
        Assert.True(VanillaGroundFighterBehaviorCatalog.TryGet(new NpcTypeId(type), out var behavior));

        Assert.Equal(maximumHorizontalSpeed, behavior.BaseMaximumHorizontalSpeed, 5);
        Assert.Equal(.07f, behavior.HorizontalAcceleration, 5);
        Assert.Equal(motionProfile, behavior.MotionProfile);
    }

    [Fact]
    public void Snow_moon_type_349_uses_its_six_pixel_reversal_profile_after_transform()
    {
        Assert.True(VanillaGroundFighterBehaviorCatalog.TryGet(new NpcTypeId(349), out var behavior));
        Assert.Equal(6f, behavior.BaseMaximumHorizontalSpeed, 5);
        Assert.Equal(.07f, behavior.HorizontalAcceleration, 5);
        Assert.Equal(.99f, behavior.ReversingVelocityDamping, 5);
    }

    [Theory]
    [InlineData(326, 2f, false)]
    [InlineData(342, 1.5f, true)]
    [InlineData(343, 2f, false)]
    [InlineData(348, 2f, false)]
    [InlineData(349, 6f, false)]
    [InlineData(350, 1f, false)]
    [InlineData(351, 2f, false)]
    public void Snow_moon_ai3_fighters_keep_their_source_speed_and_scale_profiles(
        short type,
        float maximumHorizontalSpeed,
        bool scaleAdjustsMaximumHorizontalSpeed)
    {
        Assert.True(VanillaGroundFighterBehaviorCatalog.TryGet(new NpcTypeId(type), out var behavior));
        Assert.Equal(maximumHorizontalSpeed, behavior.BaseMaximumHorizontalSpeed, 5);
        Assert.Equal(scaleAdjustsMaximumHorizontalSpeed, behavior.ScaleAdjustsMaximumHorizontalSpeed);
    }

    [Fact]
    public void Moon_event_leaper_damps_then_relaunches_on_ground_and_steers_in_air()
    {
        var grounded = CreateInput(velocityX: .35f, velocityY: 0f);
        Assert.True(VanillaZombieMotion.TryStep(in grounded, out VanillaZombieMotionResult launch));
        Assert.Equal(2f, launch.VelocityX, 5);
        Assert.Equal(-7f, launch.VelocityY, 5);

        var airborne = CreateInput(velocityX: 0f, velocityY: -3f);
        Assert.True(VanillaZombieMotion.TryStep(in airborne, out VanillaZombieMotionResult steering));
        Assert.Equal(2f / 11f, steering.VelocityX, 5);
        Assert.Equal(-3f, steering.VelocityY, 5);
    }

    private static VanillaZombieMotionInput CreateInput(float velocityX, float velocityY) => new(
        PositionX: 100f,
        OldPositionX: 99f,
        VelocityX: velocityX,
        VelocityY: velocityY,
        DirectionX: 1,
        DirectionY: 1,
        Target: VanillaNpcDefinitionCatalog.DefaultTarget,
        Ai: default,
        Scale: 1f,
        TargetOverlaps: false,
        ClosestTarget: new VanillaZombieTargetRefresh(true, 3, 1, 1))
    {
        BaseMaximumHorizontalSpeed = 2f,
        HorizontalAcceleration = .07f,
        MotionProfile = VanillaGroundFighterMotionProfile.MoonEventLeaper,
        SpriteDirection = 1,
        TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft
    };
}
