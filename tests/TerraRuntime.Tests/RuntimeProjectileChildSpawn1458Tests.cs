using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileChildSpawn1458Tests
{
    [Fact]
    public void Super_star_slash_spawn_matches_1458_geometry_damage_ai_and_trust()
    {
        var store = new RuntimeProjectileStore(capacity: 16);
        var owner = new PlayerHandle(new PlayerSlotId(3), new PlayerSessionGeneration(7));
        const float targetX = 420f;
        const float targetY = 260f;

        Assert.True(RuntimeProjectileChildSpawn1458.TrySpawnSuperStarSlash(
            store,
            owner,
            targetX,
            targetY,
            parentDamage: 100,
            new Random(12345),
            out ProjectileSnapshot slash));

        Assert.Equal(VanillaProjectileIds.SuperStarSlash, slash.Type);
        Assert.Equal(owner.Slot.Value, slash.Spawner);
        Assert.Equal((short)75, slash.Damage);
        Assert.Equal((short)75, slash.OriginalDamage);
        Assert.Equal(0f, slash.KnockBack);
        Assert.Equal(0f, slash.Ai.Ai0);
        Assert.Equal(targetY, slash.Ai.Ai1);
        Assert.Equal(0f, slash.Ai.Ai2);
        Assert.InRange(MathF.Sqrt(slash.VelocityX * slash.VelocityX + slash.VelocityY * slash.VelocityY), 5.9999f, 6.0001f);
        Assert.True(slash.VelocityY > 0f);
        Assert.InRange((slash.PositionX + 10f) + slash.VelocityX * 20f, targetX - 0.001f, targetX + 0.001f);
        Assert.InRange((slash.PositionY + 10f) + slash.VelocityY * 20f, targetY - 0.001f, targetY + 0.001f);
        Assert.True(store.TryGetLifecycle(slash.Handle, out ProjectileLifecycleState lifecycle));
        Assert.Equal(30, lifecycle.TimeLeft);
        Assert.True(store.IsCombatTrusted(slash.Handle));
        Assert.True(store.TryGetCombatTrustedOwner(slash.Handle, out PlayerHandle trustedOwner));
        Assert.Equal(owner, trustedOwner);
    }
}
