using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// State-only projectile simulation step. World side effects such as spawning secondary projectiles,
/// item creation, tile mutation and combat resolution intentionally stay outside this primitive so those
/// effects can be represented as explicit authoritative commands instead of hidden global mutations.
/// </summary>
public interface IProjectileStateStepper
{
    bool TryStepState(in ProjectileSnapshot projectile, out ProjectileStateUpdate next);
}

/// <summary>Bounded accounting for one projectile state-transition pass.</summary>
public readonly record struct ProjectileStateTickSummary(
    int Examined,
    int Proposed,
    int Applied,
    int Rejected);

/// <summary>
/// Runs allocation-stable projectile state transitions against a pre-pass snapshot of the live table.
/// Every proposal is committed through the generation-safe store using the handle captured before the
/// pass, so reentrant despawn/slot-reuse cannot let stale simulation mutate a replacement projectile.
/// </summary>
public sealed class RuntimeProjectileStateExecutor
{
    private readonly RuntimeProjectileStore _projectiles;
    private readonly ProjectileSnapshot[] _snapshotBuffer;

    public RuntimeProjectileStateExecutor(RuntimeProjectileStore projectiles)
    {
        ArgumentNullException.ThrowIfNull(projectiles);
        _projectiles = projectiles;
        _snapshotBuffer = new ProjectileSnapshot[projectiles.Capacity];
    }

    public ProjectileStateTickSummary Tick(IProjectileStateStepper stepper)
    {
        ArgumentNullException.ThrowIfNull(stepper);

        int examined = _projectiles.CopyActive(_snapshotBuffer);
        int proposed = 0;
        int applied = 0;
        int rejected = 0;

        for (int index = 0; index < examined; index++)
        {
            ProjectileSnapshot projectile = _snapshotBuffer[index];
            if (!stepper.TryStepState(in projectile, out ProjectileStateUpdate next))
                continue;

            proposed++;
            if (_projectiles.TryUpdate(projectile.Handle, in next, out _))
                applied++;
            else
                rejected++;
        }

        return new ProjectileStateTickSummary(examined, proposed, applied, rejected);
    }
}
