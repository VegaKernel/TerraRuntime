using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 world-item state created by breaking one Dirt tile.
/// WorldGen maps tile type 0 to item 2 with stack 1. Item.NewItem centers the 12x12 item in the
/// broken 16x16 tile and uses Main.rand ranges [-30,31) and [-40,-15) for ordinary gravity.
/// </summary>
public static class VanillaDirtWorldItemDrop
{
    private const float TileSize = 16f;
    private const float DirtItemHalfSize = 6f;
    private const float SpawnCenterOffset = 8f;
    private const float VelocityScale = 0.1f;

    public static WorldItemDropStateUpdate Create(
        int tileX,
        int tileY,
        IWorldItemSpawnRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);

        float centerX = tileX * TileSize + SpawnCenterOffset;
        float centerY = tileY * TileSize + SpawnCenterOffset;
        float velocityX = random.NextInt32(-30, 31) * VelocityScale;
        float velocityY = random.NextInt32(-40, -15) * VelocityScale;

        return new WorldItemDropStateUpdate(
            PositionX: centerX - DirtItemHalfSize,
            PositionY: centerY - DirtItemHalfSize,
            VelocityX: velocityX,
            VelocityY: velocityY,
            Stack: 1,
            Prefix: 0,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: checked((short)VanillaItemIds.DirtBlock.Value),
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0);
    }
}
