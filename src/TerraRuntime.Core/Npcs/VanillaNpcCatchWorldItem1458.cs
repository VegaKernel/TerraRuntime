using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// NPC.CatchNPC truncates player center for the zero-size Item.NewItem source rectangle.
/// WorldItem has a physical 16x16 body independently of captured-item catalog dimensions.
/// </summary>
public static class VanillaNpcCatchWorldItem1458
{
    private const float CapturedItemHalfSize = 8f;
    private const float VelocityScale = 0.1f;
    public const int ReservationTicks = 100;

    public static WorldItemDropStateUpdate Create(
        float playerCenterX,
        float playerCenterY,
        ItemTypeId itemType,
        IWorldItemSpawnRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!float.IsFinite(playerCenterX) || !float.IsFinite(playerCenterY))
            throw new ArgumentOutOfRangeException(nameof(playerCenterX), "Capture origin must be finite.");
        if (itemType.IsNone)
            throw new ArgumentException("Catch item type must be assigned.", nameof(itemType));

        return new WorldItemDropStateUpdate(
            PositionX: (int)playerCenterX - CapturedItemHalfSize,
            PositionY: (int)playerCenterY - CapturedItemHalfSize,
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
