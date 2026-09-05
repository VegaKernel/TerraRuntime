using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Projectiles;

public enum ProjectileStateCommitKind : byte
{
    Spawn = 0,
    Update = 1,
    Despawn = 2,
    Remove = 3
}

/// <summary>
/// Receives immutable projectile snapshots only after authoritative store commits succeed. Despawn supplies
/// the final live snapshot for a removal that must be projected as packet 29. Remove supplies the same final
/// snapshot for a local authoritative removal whose network owner must not emit packet 29; network projections
/// must still clear baselines and wire identity for that generation.
/// </summary>
public interface IProjectileStateCommitSink
{
    void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot);
}
