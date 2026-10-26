using TerraRuntime.Core.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    // All existing source-world births share the same guard, including tree
    // bombs, cracked tiles and shaken actors. No type-specific trust is added.
    internal bool TryPublishToolWorldBirth(RuntimeProjectileStore.WorldSpawnPreparation.WorldSpawnPublication publication) =>
        replication is not null ? replication.TryPublishToolWorldBirth(publication) : publication.TryPublishNext();
}
