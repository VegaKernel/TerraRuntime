using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Item.NewItem state used by NPC.CatchNPC. DefaultToCapturedCritter fixes all captured-critter item hitboxes at
/// 12x12; Item.NewItem is called with a zero-size source rectangle at player center, then ordinary gravity velocity.
/// </summary>
public static class VanillaNpcCatchWorldItem1458
{
    private const float CapturedItemHalfSize = 6f;
    private const float VelocityScale = 0.1f;
    public const int ReservationTicks = 100;

    public static WorldItemDropStateUpdate Create(
        float playerCenterX,
        float playerCenterY,
        ItemTypeId itemType,
        IWorldItemSpawnRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (itemType.IsNone)
            throw new ArgumentException("Catch item type must be assigned.", nameof(itemType));

        return new WorldItemDropStateUpdate(
            PositionX: playerCenterX - CapturedItemHalfSize,
            PositionY: playerCenterY - CapturedItemHalfSize,
            VelocityX: random.NextInt32(-30, 31) * VelocityScale,
            VelocityY: random.NextInt32(-40, -15) * VelocityScale,
            Stack: 1,
            Prefix: VanillaPrefixIds.NoneValue,
            Ownership: WorldItemOwnershipMode.ReserveForLocalPlayer,
            ItemNetId: checked((short)itemType.Value),
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: ReservationTicks);
    }
}
