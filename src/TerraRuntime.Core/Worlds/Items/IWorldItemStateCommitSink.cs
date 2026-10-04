using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Worlds;

public enum WorldItemStateCommitKind : byte
{
    Drop = 0,
    Owner = 1,
    Remove = 2,
    OwnershipReleaseRequested = 3,
    Color = 4
}

/// <summary>
/// Observes successfully committed authoritative world-item state. Implementations must not mutate the store;
/// callbacks are invoked after the store releases its internal lock.
/// </summary>
public interface IWorldItemStateCommitSink
{
    void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot);
}

/// <summary>The source sentinel is a transient wire object, never a physical handle or join-baseline entity.</summary>
public readonly record struct WorldItemSentinelCommit1458(WorldItemDropStateUpdate Drop, WorldItemOwnerStateUpdate? Owner);

public interface IWorldItemSentinelCommitSink1458
{
    void WorldItemSentinelCommitted(in WorldItemSentinelCommit1458 commit);
}
