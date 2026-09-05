using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Core.Worlds;

/// <summary>
/// Source-backed world-item drops for the ordinary single-cell terrain slice admitted by packet 17.
/// Raw identities are pinned to TerrariaServer 1.4.5.8 Item.SetDefaults/createTile facts; grass variants intentionally
/// collapse to their underlying dirt/mud block, matching pickaxe removal rather than seed placement.
/// </summary>
public static class VanillaTileWorldItemDrop
{
    private const float TileSize = 16f;
    private const float SpawnCenterOffset = 8f;
    private const float VelocityScale = 0.1f;

    public static bool TryCreate(
        TileTypeId tileType,
        int tileX,
        int tileY,
        IWorldItemSpawnRandom random,
        out WorldItemDropStateUpdate drop)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!TryGetItemForTile(tileType, out ItemTypeId itemType))
        {
            drop = default;
            return false;
        }

        float halfSize = 6f;
        if (VanillaDefinitionCatalog.TryGetRuntimeDefaults(itemType, out VanillaItemRuntimeDefaults defaults) && defaults.IsValid)
            halfSize = Math.Min(defaults.Width, defaults.Height) * 0.5f;

        float centerX = tileX * TileSize + SpawnCenterOffset;
        float centerY = tileY * TileSize + SpawnCenterOffset;
        float velocityX = random.NextInt32(-30, 31) * VelocityScale;
        float velocityY = random.NextInt32(-40, -15) * VelocityScale;

        drop = new WorldItemDropStateUpdate(
            PositionX: centerX - halfSize,
            PositionY: centerY - halfSize,
            VelocityX: velocityX,
            VelocityY: velocityY,
            Stack: 1,
            Prefix: VanillaPrefixIds.NoneValue,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: checked((short)itemType.Value),
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0);
        return true;
    }

    private static bool TryGetItemForTile(TileTypeId tileType, out ItemTypeId itemType)
    {
        int itemId = tileType.Value switch
        {
            0 => 2,
            1 => 3,
            2 or 23 or 109 or 199 => 2,
            25 => 61,
            53 => 169,
            57 => 172,
            59 or 60 or 70 => 176,
            112 => 370,
            116 => 408,
            117 => 409,
            123 => 424,
            147 => 593,
            161 => 664,
            163 => 833,
            164 => 834,
            200 => 835,
            203 => 836,
            224 => 1103,
            225 => 1129,
            226 => 1101,
            234 => 1246,
            396 => 3271,
            397 => 3272,
            398 => 3274,
            399 => 3275,
            400 => 3276,
            401 => 3277,
            402 => 3338,
            403 => 3339,
            404 => 3347,
            407 => 3380,
            408 => 3460,
            _ => 0
        };

        if (itemId <= 0 || !VanillaItemIds.TryCreate(itemId, out itemType))
        {
            itemType = default;
            return false;
        }
        return true;
    }
}
