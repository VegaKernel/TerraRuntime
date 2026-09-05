namespace TerraRuntime.Application.Operations;

internal readonly record struct RuntimeProjectileGroupSnapshot(
    byte Spawner,
    int Type,
    int Count,
    float AveragePositionX,
    float AveragePositionY,
    float AverageVelocityX,
    float AverageVelocityY,
    short MaxDamage,
    short MaxOriginalDamage,
    float MaxKnockBack);

internal readonly record struct RuntimeProjectilesSnapshot(
    int ActiveProjectiles,
    ReadOnlyMemory<RuntimeProjectileGroupSnapshot> Groups,
    long CommittedSpawns,
    long CommittedUpdates,
    long CommittedDespawns,
    DateTimeOffset CapturedAtUtc);
