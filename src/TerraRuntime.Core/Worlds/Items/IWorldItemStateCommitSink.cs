using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Worlds;

public enum WorldItemStateCommitKind : byte
{
    Drop = 0,
    Owner = 1,
    Remove = 2
}

/// <summary>
/// Observes successfully committed authoritative world-item state. Implementations must not mutate the store;
/// callbacks are invoked after the store releases its internal lock.
/// </summary>
public interface IWorldItemStateCommitSink
{
    void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot);
}
