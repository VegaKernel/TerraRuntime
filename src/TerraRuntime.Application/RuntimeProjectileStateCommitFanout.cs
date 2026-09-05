using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

/// <summary>
/// Keeps authoritative projectile commits available to independent replication and operations observers
/// without exposing RuntimeProjectileStore or making either consumer own the other.
/// </summary>
internal sealed class RuntimeProjectileStateCommitFanout(
    IProjectileStateCommitSink first,
    IProjectileStateCommitSink second) : IProjectileStateCommitSink
{
    private readonly IProjectileStateCommitSink first = first ?? throw new ArgumentNullException(nameof(first));
    private readonly IProjectileStateCommitSink second = second ?? throw new ArgumentNullException(nameof(second));

    public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
    {
        first.ProjectileStateCommitted(kind, in snapshot);
        second.ProjectileStateCommitted(kind, in snapshot);
    }
}
