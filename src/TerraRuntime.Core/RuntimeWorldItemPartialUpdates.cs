using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Packet-neutral dropped-item state. Reservation/grab-owner fields are deliberately excluded so a packet-21
/// update cannot erase newer packet-22 state when the authoritative store merges the change.
/// </summary>
public readonly record struct WorldItemDropStateUpdate(
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
    byte EnemyGrabDelayTime)
{
    public PrefixId PrefixId => new(Prefix);

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
/// Packet-neutral owner/reservation state from packet 22. Position is included because Terraria sends a
/// position correction in the same packet; all packet-21 drop fields remain untouched by this update.
/// </summary>
public readonly record struct WorldItemOwnerStateUpdate(
    byte OwnerPlayerId,
    int TimeToKeepReservation,
    byte GrabDelayPlayer,
    int GrabDelayTime,
    float PositionX,
    float PositionY);
