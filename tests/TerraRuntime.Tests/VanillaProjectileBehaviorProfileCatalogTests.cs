using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class VanillaProjectileBehaviorProfileCatalogTests
{
    [Fact]
    public void Source_backed_projectiles_are_explicitly_classified_by_runtime_behavior_family()
    {
        ProjectileTypeId[] basicArrows =
        [
            VanillaProjectileIds.WoodenArrowFriendly,
            VanillaProjectileIds.FireArrow,
            VanillaProjectileIds.UnholyArrow,
            VanillaProjectileIds.JestersArrow,
            VanillaProjectileIds.Bullet,
            VanillaProjectileIds.SilverBullet,
            VanillaProjectileIds.Seed,
            VanillaProjectileIds.ConfettiGun,
            VanillaProjectileIds.ConfettiMelee,
            VanillaProjectileIds.BoneArrowFromMerchant,
            VanillaProjectileIds.SoundGun,
            VanillaProjectileIds.BoneShard
        ];

        foreach (ProjectileTypeId type in basicArrows)
        {
            Assert.True(VanillaProjectileBehaviorProfileCatalog.TryGet(type, out VanillaProjectileBehaviorProfile profile));
            Assert.Equal(VanillaProjectileBehaviorFamily.BasicArrow, profile.Family);
            Assert.Equal(VanillaProjectileAiStyles.Arrow, profile.ExpectedAiStyle);
            Assert.True(profile.BehaviorImplemented);
            Assert.True(profile.RequiresDefaultAi2);
            Assert.False(profile.RejectServerOwned);
            Assert.False(profile.ExemptFromPreAiWorldBounds);
        }

        ProjectileTypeId[] thrown =
        [
            VanillaProjectileIds.Bone,
            VanillaProjectileIds.Shuriken,
            VanillaProjectileIds.ThrowingKnife,
            VanillaProjectileIds.PoisonedKnife,
            VanillaProjectileIds.RottenEgg,
            VanillaProjectileIds.StarAnise,
            VanillaProjectileIds.NurseSyringeHurt,
            VanillaProjectileIds.SantaBombs,
            VanillaProjectileIds.BoneDagger,
            VanillaProjectileIds.Waffle,
            VanillaProjectileIds.MeleeBone
        ];

        foreach (ProjectileTypeId type in thrown)
        {
            Assert.True(VanillaProjectileBehaviorProfileCatalog.TryGet(type, out VanillaProjectileBehaviorProfile profile));
            Assert.Equal(VanillaProjectileBehaviorFamily.Thrown, profile.Family);
            Assert.Equal(VanillaProjectileAiStyles.Thrown, profile.ExpectedAiStyle);
            Assert.True(profile.BehaviorImplemented);
            Assert.False(profile.RequiresDefaultAi2);
            Assert.False(profile.RejectServerOwned);
            Assert.False(profile.ExemptFromPreAiWorldBounds);
        }

        for (int rawType = VanillaProjectileIds.GrenadeI.Value; rawType <= VanillaProjectileIds.ProximityMineIV.Value; rawType++)
        {
            var type = new ProjectileTypeId(rawType);
            Assert.True(VanillaProjectileBehaviorProfileCatalog.TryGet(type, out VanillaProjectileBehaviorProfile profile));
            Assert.Equal(VanillaProjectileBehaviorFamily.Bomb, profile.Family);
            Assert.Equal(VanillaProjectileAiStyles.Bomb, profile.ExpectedAiStyle);
            Assert.True(profile.BehaviorImplemented);
            Assert.True(profile.RequiresDefaultAi2);
            Assert.False(profile.RejectServerOwned);
            Assert.False(profile.ExemptFromPreAiWorldBounds);
        }
    }

    [Fact]
    public void Controlled_magic_missiles_use_explicit_ai9_player_owned_profile()
    {
        foreach (ProjectileTypeId type in new[] { VanillaProjectileIds.MagicMissile, VanillaProjectileIds.Flamelash, VanillaProjectileIds.RainbowRodBullet })
        {
            Assert.True(VanillaProjectileBehaviorProfileCatalog.TryGet(type, out VanillaProjectileBehaviorProfile profile));
            Assert.Equal(VanillaProjectileBehaviorFamily.ControlledMagicMissile, profile.Family);
            Assert.Equal(VanillaProjectileAiStyles.MagicMissile, profile.ExpectedAiStyle);
            Assert.True(profile.BehaviorImplemented);
            Assert.True(profile.RequiresDefaultAi2);
            Assert.True(profile.RejectServerOwned);
            Assert.False(profile.ExemptFromPreAiWorldBounds);
        }
    }

    [Fact]
    public void Hostile_boss_projectiles_use_explicit_source_backed_runtime_families()
    {
        ProjectileTypeId[] straight =
        [
            VanillaProjectileIds.WallOfFleshEyeLaser,
            VanillaProjectileIds.ProbePinkLaser,
            VanillaProjectileIds.RetinazerDeathLaser,
            VanillaProjectileIds.GolemEyeBeam
        ];

        foreach (ProjectileTypeId type in straight)
        {
            Assert.True(VanillaProjectileBehaviorProfileCatalog.TryGet(type, out VanillaProjectileBehaviorProfile profile));
            Assert.Equal(VanillaProjectileBehaviorFamily.HostileStraightArrow, profile.Family);
            Assert.Equal(VanillaProjectileAiStyles.Arrow, profile.ExpectedAiStyle);
            Assert.True(profile.RequiresDefaultAi2);
            Assert.False(profile.RejectServerOwned);
        }

        foreach (ProjectileTypeId type in new[] { VanillaProjectileIds.PlanteraSeed, VanillaProjectileIds.PlanteraPoisonSeed })
        {
            Assert.True(VanillaProjectileBehaviorProfileCatalog.TryGet(type, out VanillaProjectileBehaviorProfile profile));
            Assert.Equal(VanillaProjectileBehaviorFamily.PlanteraSeed, profile.Family);
            Assert.Equal(VanillaProjectileAiStyles.Arrow, profile.ExpectedAiStyle);
        }

        Assert.True(VanillaProjectileBehaviorProfileCatalog.TryGet(
            VanillaProjectileIds.GolemFireball, out VanillaProjectileBehaviorProfile fireball));
        Assert.Equal(VanillaProjectileBehaviorFamily.GolemFireball, fireball.Family);
        Assert.Equal(VanillaProjectileAiStyles.Fireball, fireball.ExpectedAiStyle);
    }

    [Fact]
    public void Green_laser_keeps_its_owner_gated_exception_in_profile_metadata()
    {
        Assert.True(VanillaProjectileBehaviorProfileCatalog.TryGet(
            VanillaProjectileIds.GreenLaser,
            out VanillaProjectileBehaviorProfile profile));

        Assert.Equal(VanillaProjectileBehaviorFamily.BasicArrow, profile.Family);
        Assert.Equal(VanillaProjectileAiStyles.Arrow, profile.ExpectedAiStyle);
        Assert.True(profile.BehaviorImplemented);
        Assert.True(profile.RequiresDefaultAi2);
        Assert.True(profile.RejectServerOwned);
        Assert.False(profile.ExemptFromPreAiWorldBounds);
    }

    [Fact]
    public void Enchanted_boomerang_family_is_source_backed_and_preserves_world_boundary_semantics()
    {
        Assert.True(VanillaProjectileBehaviorProfileCatalog.TryGet(
            VanillaProjectileIds.EnchantedBoomerang,
            out VanillaProjectileBehaviorProfile profile));

        Assert.Equal(VanillaProjectileBehaviorFamily.Boomerang, profile.Family);
        Assert.Equal(VanillaProjectileAiStyles.Boomerang, profile.ExpectedAiStyle);
        Assert.True(profile.BehaviorImplemented);
        Assert.True(profile.ExemptFromPreAiWorldBounds);
    }

    [Fact]
    public void Every_profiled_type_agrees_with_its_source_backed_definition_ai_style()
    {
        ProjectileTypeId[] types =
        [
            VanillaProjectileIds.WoodenArrowFriendly,
            VanillaProjectileIds.FireArrow,
            VanillaProjectileIds.UnholyArrow,
            VanillaProjectileIds.JestersArrow,
            VanillaProjectileIds.EnchantedBoomerang,
            VanillaProjectileIds.Bullet,
            VanillaProjectileIds.SilverBullet,
            VanillaProjectileIds.GrenadeI,
            VanillaProjectileIds.RocketI,
            VanillaProjectileIds.ProximityMineI,
            VanillaProjectileIds.GrenadeII,
            VanillaProjectileIds.RocketII,
            VanillaProjectileIds.ProximityMineII,
            VanillaProjectileIds.GrenadeIII,
            VanillaProjectileIds.RocketIII,
            VanillaProjectileIds.ProximityMineIII,
            VanillaProjectileIds.GrenadeIV,
            VanillaProjectileIds.RocketIV,
            VanillaProjectileIds.ProximityMineIV,
            VanillaProjectileIds.GreenLaser,
            VanillaProjectileIds.WallOfFleshEyeLaser,
            VanillaProjectileIds.ProbePinkLaser,
            VanillaProjectileIds.RetinazerDeathLaser,
            VanillaProjectileIds.MagicMissile,
            VanillaProjectileIds.Flamelash,
            VanillaProjectileIds.RainbowRodBullet,
            VanillaProjectileIds.GolemFireball,
            VanillaProjectileIds.GolemEyeBeam,
            VanillaProjectileIds.PlanteraSeed,
            VanillaProjectileIds.PlanteraPoisonSeed,
            VanillaProjectileIds.Bone,
            VanillaProjectileIds.Shuriken,
            VanillaProjectileIds.ThrowingKnife,
            VanillaProjectileIds.Seed,
            VanillaProjectileIds.PoisonedKnife,
            VanillaProjectileIds.ConfettiGun,
            VanillaProjectileIds.ConfettiMelee,
            VanillaProjectileIds.RottenEgg,
            VanillaProjectileIds.StarAnise,
            VanillaProjectileIds.BoneArrowFromMerchant,
            VanillaProjectileIds.NurseSyringeHurt,
            VanillaProjectileIds.SantaBombs,
            VanillaProjectileIds.BoneDagger,
            VanillaProjectileIds.Waffle,
            VanillaProjectileIds.SoundGun,
            VanillaProjectileIds.MeleeBone,
            VanillaProjectileIds.BoneShard
        ];

        foreach (ProjectileTypeId type in types)
        {
            Assert.True(VanillaDefinitionCatalog.TryGet(type, out VanillaProjectileDefinition definition));
            Assert.True(VanillaProjectileBehaviorProfileCatalog.TryGet(type, out VanillaProjectileBehaviorProfile profile));
            Assert.Equal(definition.AiStyle, profile.ExpectedAiStyle);
        }
    }

    [Fact]
    public void Behavior_execution_fails_closed_when_profile_and_definition_ai_style_disagree()
    {
        ProjectileSnapshot projectile = new(
            Handle: default,
            Revision: default,
            Type: VanillaProjectileIds.Shuriken,
            Spawner: 0,
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 4f,
            VelocityY: 0f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 10,
            KnockBack: 1f,
            OriginalDamage: 10);
        Assert.True(VanillaDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
        Assert.True(VanillaProjectileBehaviorProfileCatalog.TryGet(projectile.Type, out VanillaProjectileBehaviorProfile profile));
        VanillaProjectileDefinition mismatched = definition with { AiStyle = VanillaProjectileAiStyles.Arrow };

        Assert.False(VanillaProjectileBehaviorStepper.TryStep(
            in projectile,
            in mismatched,
            in profile,
            default,
            out _));
    }

    [Fact]
    public void Unprofiled_projectile_does_not_infer_behavior_from_an_ai_style()
    {
        Assert.False(VanillaProjectileBehaviorProfileCatalog.TryGet(new ProjectileTypeId(7), out _));
    }

    [Theory]
    [InlineData(384, (int)VanillaProjectileBehaviorFamily.Sharknado, 64)]
    [InlineData(385, (int)VanillaProjectileBehaviorFamily.SharknadoBolt, 65)]
    [InlineData(386, (int)VanillaProjectileBehaviorFamily.Sharknado, 64)]
    [InlineData(464, (int)VanillaProjectileBehaviorFamily.CultistIceMist, 86)]
    [InlineData(452, (int)VanillaProjectileBehaviorFamily.PhantasmalEye, 82)]
    [InlineData(454, (int)VanillaProjectileBehaviorFamily.PhantasmalSphere, 83)]
    [InlineData(455, (int)VanillaProjectileBehaviorFamily.PhantasmalDeathray, 84)]
    [InlineData(872, (int)VanillaProjectileBehaviorFamily.HallowBossLastingRainbow, 173)]
    [InlineData(873, (int)VanillaProjectileBehaviorFamily.HallowBossRainbowStreak, 171)]
    [InlineData(874, (int)VanillaProjectileBehaviorFamily.HallowBossDeathAurora, 0)]
    [InlineData(919, (int)VanillaProjectileBehaviorFamily.FairyQueenLance, 179)]
    [InlineData(922, (int)VanillaProjectileBehaviorFamily.QueenSlimeSmash, 135)]
    [InlineData(923, (int)VanillaProjectileBehaviorFamily.FairyQueenSunDance, 180)]
    public void Late_boss_projectiles_have_explicit_source_backed_profiles(
        int rawType, int family, int aiStyle)
    {
        var type = new ProjectileTypeId(rawType);
        Assert.True(VanillaProjectileBehaviorProfileCatalog.TryGet(type, out VanillaProjectileBehaviorProfile profile));
        Assert.Equal((VanillaProjectileBehaviorFamily)family, profile.Family);
        Assert.Equal(new ProjectileAiStyleId(aiStyle), profile.ExpectedAiStyle);
        Assert.True(profile.BehaviorImplemented);
        Assert.False(profile.RejectServerOwned);
        Assert.True(VanillaDefinitionCatalog.TryGet(type, out VanillaProjectileDefinition definition));
        Assert.Equal(definition.AiStyle, profile.ExpectedAiStyle);
    }

}
