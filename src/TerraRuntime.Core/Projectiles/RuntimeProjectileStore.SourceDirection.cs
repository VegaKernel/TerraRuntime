using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Projectiles;

public sealed partial class RuntimeProjectileStore
{
    internal bool TrySetSourceDirectionAfterSimulation(in ProjectileSnapshot committed,
        bool expired, int? direction)
    {
        if (direction is not (null or -1 or 1) || !IsCurrentHandleCandidate(committed.Handle)) return false;
        ref var state = ref _slots[committed.Handle.Slot];
        if (state.Generation != committed.Handle.Generation.Value ||
            (expired ? state.Active : !state.Active || Capture(committed.Handle.Slot, in state) != committed)) return false;
        // Called in the callback-free simulation tail, before any publication.
        // Null explicitly invalidates unrepresented manual/early-AI direction;
        // a raw packet update never calls this source observation surface.
        state.SourceDirection = direction;
        return true;
    }
}
