using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Synchronous authoritative-thread lookup used by actor simulation. This is deliberately separate from the
/// asynchronous public snapshot reader so gameplay never posts a command back to its own game loop and deadlocks.
/// </summary>
public interface IRuntimePlayerSnapshotLookup
{
    bool TryGetPlayer(PlayerHandle player, out PlayerStateSnapshot snapshot);
}

/// <summary>
/// Authoritative-thread lookup that resolves a wire player slot to its current generation-safe occupation.
/// Projectile provenance uses this boundary so a reused byte slot cannot be attributed to a stale projectile.
/// </summary>
public interface IRuntimePlayerSlotSnapshotLookup
{
    bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot);
}
