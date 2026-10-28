using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaOrdinaryMeleeCohort1458Tests
{
    [Fact]
    public void Exact_common_melee_profiles_preserve_source_bodies_speed_bands_and_distinct_guards()
    {
        // NPC.SetDefaults / AI_003_Fighters 1.4.5.8. Dynamic Blood Zombie
        // distance and Face Monster Remix adjustments belong to the AI caller.
        (int Type, int Width, int Height, int Damage, int Defense, int Life, float Knockback,
            float Speed, bool Lunge, bool Day, bool Scale)[] rows =
        [
            (44,18,40,22,9,70,.5f,1f,false,true,false),
            (104,18,40,70,38,350,.4f,2f,true,true,false),
            (140,18,40,55,28,260,.4f,1.5f,false,true,false),
            (181,18,44,25,10,70,.4f,1.5f,false,false,false),
            (196,18,40,35,16,300,.4f,2f,true,true,false),
            (197,18,40,50,28,280,.45f,2f,false,true,false),
            (464,18,20,21,5,75,1f,1.5f,true,false,false),
            (470,16,34,21,5,75,1f,1.5f,true,false,false),
            (489,18,40,20,8,75,.4f,1f,false,true,true),
            (524,24,44,50,26,180,.6f,2f,true,false,false),
            (525,24,44,60,30,250,.6f-.1f,2f,true,false,false),
            (526,24,44,64,32,220,.6f-.1f,2f,true,false,false),
            (527,24,44,54,32,300,.6f-.2f,2f,true,false,false),
            (528,24,44,52,28,350,.35f,3f,false,false,false),
            (529,24,44,52,28,350,.35f,3f,false,false,false),
            (536,18,40,14,8,200,.5f,1f,false,true,false),
            (691,18,40,18,10,60,.45f,.85f,false,true,false)
        ];
        foreach (var row in rows)
        {
            var type = new NpcTypeId(row.Type);
            Assert.True(VanillaGroundFighterNpcCatalog.TryGetDefinition(type, out var definition), $"type={row.Type}");
            Assert.True(VanillaGroundFighterBehaviorCatalog.TryGet(type, out var profile));
            Assert.Equal((row.Width,row.Height,row.Damage,row.Defense,row.Life),
                (definition.Width,definition.Height,definition.Damage,definition.Defense,definition.LifeMax));
            Assert.Equal(row.Knockback, definition.KnockBackResist);
            Assert.Equal(row.Speed, profile.BaseMaximumHorizontalSpeed);
            Assert.Equal(row.Lunge, profile.CloseRangeLunge);
            Assert.Equal(row.Day, profile.DaySurfaceEncouragesDespawn);
            Assert.Equal(row.Scale, profile.ScaleAdjustsMaximumHorizontalSpeed);
            Assert.True(profile.IsValid);
            Assert.False(VanillaGroundFighterNpcCatalog.IsUndeadVariantParent(type));
        }
        foreach (int type in new[] { 385, 481, 590, 632, 198, 236, 185, 482, 508, 580, 582, 532 })
            Assert.DoesNotContain(rows, row => row.Type == type);
    }

    [Fact]
    public void Lamia_reversal_brakes_only_when_grounded_and_inside_the_source_speed_band()
    {
        foreach (int type in new[] {528,529})
        {
            Assert.True(VanillaGroundFighterBehaviorCatalog.TryGet(new(type), out var profile));
            var input = new VanillaZombieMotionInput(100f,99f,-1f,0f,1,1,0,default,1f,false,
                new VanillaZombieTargetRefresh(true,0,1,1))
            {
                BaseMaximumHorizontalSpeed = profile.BaseMaximumHorizontalSpeed,
                ScaleAdjustsMaximumHorizontalSpeed = profile.ScaleAdjustsMaximumHorizontalSpeed,
                ReversingVelocityDamping = profile.ReversingVelocityDamping,
                MotionProfile = profile.MotionProfile,
                TimeLeft = 750
            };
            Assert.True(VanillaZombieMotion.TryStep(input, out var grounded));
            Assert.Equal(-.99f + .07f, grounded.VelocityX);
            Assert.True(VanillaZombieMotion.TryStep(input with {VelocityY=2f}, out var airborne));
            Assert.Equal(-1f + .07f, airborne.VelocityX);
            Assert.True(VanillaZombieMotion.TryStep(input with {VelocityX=-4f}, out var overspeed));
            Assert.Equal(-4f * .8f, overspeed.VelocityX);
            Assert.True(VanillaZombieMotion.TryStep(input with {VelocityX=-4f,VelocityY=2f}, out var flyingOverspeed));
            Assert.Equal(-4f, flyingOverspeed.VelocityX);
        }
    }

    [Fact]
    public void Moss_zombie_does_not_acquire_the_ordinary_zombie_scale_speed_multiplier()
    {
        Assert.True(VanillaGroundFighterBehaviorCatalog.TryGet(new(691), out var profile));
        var input = new VanillaZombieMotionInput(100f,99f,.83f,0f,1,1,0,default,.5f,false,
            new VanillaZombieTargetRefresh(true,0,1,1))
        {
            BaseMaximumHorizontalSpeed=profile.BaseMaximumHorizontalSpeed,
            ScaleAdjustsMaximumHorizontalSpeed=profile.ScaleAdjustsMaximumHorizontalSpeed,
            MotionProfile=profile.MotionProfile,
            TimeLeft=750
        };
        Assert.True(VanillaZombieMotion.TryStep(input,out var result));
        Assert.Equal(.85f,result.VelocityX);
    }
}
