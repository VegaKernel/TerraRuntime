using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public enum ProjectileStateCommitKind : byte
{
    Spawn = 0,
    Update = 1,
    Despawn = 2
}

/// <summary>
/// Receives immutable projectile snapshots only after authoritative store commits succeed. Despawn
/// supplies the final live snapshot before the slot is cleared so replication can preserve the exact
/// generation-safe identity and final wire position without observing mutable store internals.
/// </summary>
public interface IProjectileStateCommitSink
{
    void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot);
}
