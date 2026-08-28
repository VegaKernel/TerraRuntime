using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Identifies one logical occupation of a reusable Terraria world-item slot.
/// Zero is reserved for an unassigned/default generation.
/// </summary>
public readonly record struct WorldItemGeneration
{
    public WorldItemGeneration(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    public bool IsAssigned => Value != 0;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Monotonic state revision for one exact world-item generation.
/// </summary>
public readonly record struct WorldItemRevision
{
    public WorldItemRevision(ulong value)
    {
        ArgumentOutOfRangeException.ThrowIfZero(value);
        Value = value;
    }

    public ulong Value { get; }

    public bool IsAssigned => Value != 0;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Generation-safe identity for one active world-item slot.
/// </summary>
public readonly record struct WorldItemHandle(short Slot, WorldItemGeneration Generation)
{
    public bool IsAssigned => Slot >= 0 && Generation.IsAssigned;

    public override string ToString() => $"item:{Slot}/generation:{Generation}";
}

/// <summary>
/// Terraria's two-bit item ownership behavior carried by packet 21.
/// </summary>
public enum WorldItemOwnershipMode : byte
{
    None = 0,
    ReserveForLocalPlayer = 1,
    GrabDelayForLocalPlayer = 2,
    GrabDelayForAllPlayers = 3
}

/// <summary>
/// Immutable protocol-neutral projection of one active world item.
/// Packet encoding remains owned by the protocol adapter. ItemNetId is retained as the current
/// compatibility/storage primitive; gameplay should cross it through <see cref="TryGetItemType"/>.
/// </summary>
public readonly record struct WorldItemSnapshot(
    WorldItemHandle Handle,
    WorldItemRevision Revision,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    short Stack,
    byte Prefix,
    WorldItemOwnershipMode Ownership,
    short ItemNetId,
    bool Shimmered,
    float ShimmerTime,
    byte EnemyGrabDelayTime,
    byte OwnerPlayerId,
    int TimeToKeepReservation,
    byte GrabDelayPlayer,
    int GrabDelayTime)
{
    public PrefixId PrefixId => new(Prefix);

    public bool IsActive =>
        Handle.IsAssigned &&
        Revision.IsAssigned &&
        Stack > 0 &&
        TryGetItemType(out _);

    /// <summary>
    /// Crosses the compatibility primitive into the version-pinned Terraria 1.4.5.8 item catalog.
    /// Empty item type zero is not a live world-item identity.
    /// </summary>
    public bool TryGetItemType(out ItemTypeId itemType)
    {
        if (!VanillaItemIds.TryCreate(ItemNetId, out itemType) || itemType.IsNone)
        {
            itemType = default;
            return false;
        }

        return true;
    }
}

/// <summary>
/// Read-only bootstrap/replication boundary for the authoritative world-item store.
/// Callers provide bounded storage; the runtime does not allocate an unbounded snapshot collection.
/// </summary>
public interface IWorldItemSnapshotReader
{
    int Capacity { get; }

    int CopyActive(Span<WorldItemSnapshot> destination);
}
