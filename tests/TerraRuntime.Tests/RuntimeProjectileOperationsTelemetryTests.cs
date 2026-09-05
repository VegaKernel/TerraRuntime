using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Application.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileOperationsTelemetryTests
{
    [Fact]
    public void Committed_projectiles_are_grouped_by_spawner_and_type()
    {
        var telemetry = new RuntimeProjectileOperationsTelemetry();
        var store = new RuntimeProjectileStore(capacity: 8, commitSink: telemetry);

        ProjectileStateUpdate first = CreateUpdate(spawner: 3, type: 1, positionX: 100f, velocityX: 2f, damage: 25);
        ProjectileStateUpdate second = CreateUpdate(spawner: 3, type: 1, positionX: 300f, velocityX: 4f, damage: 40);
        ProjectileStateUpdate third = CreateUpdate(spawner: 7, type: 1, positionX: 900f, velocityX: -2f, damage: 30);

        Assert.True(store.TrySpawn(1, in first, out ProjectileSnapshot firstSpawn));
        Assert.True(store.TrySpawn(2, in second, out ProjectileSnapshot secondSpawn));
        Assert.True(store.TrySpawn(3, in third, out _));

        RuntimeProjectilesSnapshot snapshot = telemetry.CaptureSnapshot();
        Assert.Equal(3, snapshot.ActiveProjectiles);
        Assert.Equal(2, snapshot.Groups.Length);
        Assert.Equal(3, snapshot.CommittedSpawns);

        RuntimeProjectileGroupSnapshot grouped = snapshot.Groups.Span[0];
        Assert.Equal((byte)3, grouped.Spawner);
        Assert.Equal(1, grouped.Type);
        Assert.Equal(2, grouped.Count);
        Assert.Equal(200f, grouped.AveragePositionX);
        Assert.Equal(3f, grouped.AverageVelocityX);
        Assert.Equal((short)40, grouped.MaxDamage);

        ProjectileStateUpdate moved = first with { PositionX = 500f, VelocityX = 6f, Damage = 60 };
        Assert.True(store.TryUpdate(firstSpawn.Handle, in moved, out _));
        snapshot = telemetry.CaptureSnapshot();
        grouped = snapshot.Groups.Span[0];
        Assert.Equal(400f, grouped.AveragePositionX);
        Assert.Equal(5f, grouped.AverageVelocityX);
        Assert.Equal((short)60, grouped.MaxDamage);
        Assert.Equal(1, snapshot.CommittedUpdates);

        Assert.True(store.TryDespawn(secondSpawn.Handle, out _));
        snapshot = telemetry.CaptureSnapshot();
        Assert.Equal(2, snapshot.ActiveProjectiles);
        Assert.Equal(1, snapshot.CommittedDespawns);

        ProjectileStateUpdate replacement = CreateUpdate(spawner: 3, type: 2, positionX: 700f, velocityX: 1f, damage: 10);
        Assert.True(store.TrySpawn(2, in replacement, out ProjectileSnapshot replacementSpawn));
        Assert.True(replacementSpawn.Handle.Generation.Value > secondSpawn.Handle.Generation.Value);
        Assert.False(store.TryUpdate(secondSpawn.Handle, in second, out _));

        snapshot = telemetry.CaptureSnapshot();
        Assert.Equal(3, snapshot.ActiveProjectiles);
        Assert.Equal(3, snapshot.Groups.Length);
        Assert.Contains(snapshot.Groups.ToArray(), group => group.Spawner == 3 && group.Type == 2 && group.Count == 1);
    }

    private static ProjectileStateUpdate CreateUpdate(
        byte spawner,
        int type,
        float positionX,
        float velocityX,
        short damage) =>
        new(
            Type: new ProjectileTypeId(type),
            Spawner: spawner,
            PositionX: positionX,
            PositionY: 200f,
            VelocityX: velocityX,
            VelocityY: -1f,
            Ai: new ProjectileAiState(1f, 2f, 3f),
            BannerIdToRespondTo: 0,
            Damage: damage,
            KnockBack: 2.5f,
            OriginalDamage: damage);
}
