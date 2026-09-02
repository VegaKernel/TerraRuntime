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
    public void Known_boomerang_family_stays_explicitly_unsupported_without_losing_world_boundary_semantics()
    {
        Assert.True(VanillaProjectileBehaviorProfileCatalog.TryGet(
            VanillaProjectileIds.EnchantedBoomerang,
            out VanillaProjectileBehaviorProfile profile));

        Assert.Equal(VanillaProjectileBehaviorFamily.Boomerang, profile.Family);
        Assert.Equal(VanillaProjectileAiStyles.Boomerang, profile.ExpectedAiStyle);
        Assert.False(profile.BehaviorImplemented);
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
            VanillaProjectileIds.GreenLaser,
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
            Assert.True(VanillaProjectileDefinitionCatalog.TryGet(type, out VanillaProjectileDefinition definition));
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
        Assert.True(VanillaProjectileDefinitionCatalog.TryGet(projectile.Type, out VanillaProjectileDefinition definition));
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
}
