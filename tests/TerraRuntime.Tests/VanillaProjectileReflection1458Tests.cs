using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaProjectileReflection1458Tests
{
    [Fact]
    public void Source_mutation_preserves_old_speed_and_quarters_damage()
    {
        ProjectileSnapshot projectile = Projectile(damage: 21, velocityX: 9f, velocityY: 1f);
        var lifecycle = new ProjectileLifecycleState(600, false)
        {
            OldVelocityX = 3f,
            OldVelocityY = 4f
        };
        var random = new SequenceRandom(100, 0);

        Assert.True(VanillaProjectileReflection1458.TryResolve(
            in projectile,
            lifecycle.OldVelocityX,
            lifecycle.OldVelocityY,
            ownerCenterX: 300f,
            ownerCenterY: 121f,
            random,
            out VanillaProjectileReflectionResult result));

        Assert.Equal((short)5, result.Damage);
        Assert.Equal(5f, MathF.Sqrt(result.VelocityX * result.VelocityX + result.VelocityY * result.VelocityY), 4);
        Assert.Equal(2, random.Calls);
    }

    [Fact]
    public void Current_admitted_arrow_and_thrown_styles_are_reflectable_but_boomerang_and_reflected_state_are_not()
    {
        ProjectileSnapshot arrow = Projectile(damage: 20, velocityX: 3f, velocityY: 4f);
        Assert.True(VanillaDefinitionCatalog.TryGet(arrow.Type, out VanillaProjectileDefinition arrowDefinition));
        var lifecycle = new ProjectileLifecycleState(600, false) { OldVelocityX = 3f, OldVelocityY = 4f };
        Assert.True(VanillaProjectileReflection1458.CanBeReflected(in arrow, lifecycle.Reflected, in arrowDefinition));

        Assert.True(VanillaDefinitionCatalog.TryGet(VanillaProjectileIds.EnchantedBoomerang, out VanillaProjectileDefinition boomerangDefinition));
        ProjectileSnapshot boomerang = arrow with { Type = VanillaProjectileIds.EnchantedBoomerang };
        Assert.False(VanillaProjectileReflection1458.CanBeReflected(in boomerang, lifecycle.Reflected, in boomerangDefinition));

        ProjectileLifecycleState reflected = lifecycle with { Reflected = true };
        Assert.False(VanillaProjectileReflection1458.CanBeReflected(in arrow, reflected.Reflected, in arrowDefinition));
    }

    private static ProjectileSnapshot Projectile(short damage, float velocityX, float velocityY) =>
        new(
            new ProjectileHandle(0, new ProjectileGeneration(1)),
            new ProjectileRevision(1),
            VanillaProjectileIds.WoodenArrowFriendly,
            Spawner: 0,
            PositionX: 110f,
            PositionY: 110f,
            VelocityX: velocityX,
            VelocityY: velocityY,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: damage,
            KnockBack: 1f,
            OriginalDamage: damage);

    private sealed class SequenceRandom(params int[] values) : IVanillaProjectileReflectionRandom
    {
        private int index;
        public int Calls => index;

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            if (index >= values.Length)
                throw new Xunit.Sdk.XunitException("Reflection RNG consumed more values than expected.");
            int value = values[index++];
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }
}
