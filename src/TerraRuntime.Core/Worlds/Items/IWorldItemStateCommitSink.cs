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
/// A source creation Drop carries its one-time NewItem ownership argument;
/// the same handle's active snapshot has Ownership.None. Such a notification
/// is a publication projection, not an exact current-state preimage.
/// </summary>
public interface IWorldItemStateCommitSink
{
    // ReserveFor's packet22 hold is an offer to clients, not the server item's countdown.
    // It belongs to this notification only; ordinary snapshots and bootstrap carry no offer.
    void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot,
        int? reservationOffer = null, GameCommandSourceId excludedSource = default);
}

/// <summary>The source sentinel is a transient wire object, never a physical handle or join-baseline entity.</summary>
public readonly record struct WorldItemSentinelCommit1458(WorldItemDropStateUpdate Drop, WorldItemOwnerStateUpdate? Owner);

public interface IWorldItemSentinelCommitSink1458
{
    void WorldItemSentinelCommitted(in WorldItemSentinelCommit1458 commit);
}
