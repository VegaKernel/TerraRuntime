using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Generic source-backed world-item drop for simple tiles.
/// Vanilla Dirt tile 0 maps to DirtBlock item 2, Stone 1 to StoneBlock 3, Sand 53 to SandBlock 169, etc.
/// Position/velocity semantics match <see cref="VanillaDirtWorldItemDrop"/>: center the item in the 16x16 tile
/// and apply vanilla's random gravity ranges. For tiles without a known item mapping the helper returns false
/// so the caller can perform a bare KillTile without a drop reservation.
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

        // Resolve item half-size from catalog when available, otherwise 6 (12x12 Dirt default).
        float halfSize = 6f;
        if (VanillaItemDefinitionCatalog.TryGetRuntimeDefaults(itemType, out VanillaItemRuntimeDefaults defaults) && defaults.IsValid)
        {
            halfSize = Math.Min(defaults.Width, defaults.Height) * 0.5f;
        }

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
        if (tileType == VanillaTileIds.Dirt)
        {
            itemType = VanillaItemIds.DirtBlock;
            return true;
        }

        if (tileType == VanillaTileIds.Stone)
        {
            itemType = VanillaItemIds.StoneBlock;
            return true;
        }

        if (tileType == VanillaTileIds.Sand)
        {
            itemType = VanillaItemIds.SandBlock;
            return true;
        }

        if (tileType == VanillaTileIds.Grass)
        {
            // Grass tile drops DirtBlock in vanilla when broken with pickaxe (grass is dirt with grass).
            itemType = VanillaItemIds.DirtBlock;
            return true;
        }

        if (tileType == VanillaTileIds.Mud)
        {
            itemType = VanillaItemIds.DirtBlock; // fallback: mud has its own item but not yet catalogued
            return false;
        }

        // Try reverse lookup via placement catalog: find an item whose placement tile matches.
        // This covers cases where the tile was placed via an item we have catalogued but not
        // explicitly handled above. We scan the sparse catalog (dirt/stone/sand) for now.
        if (tileType == VanillaTileIds.SnowBlock)
        {
            // SnowBlock item not yet catalogued -> no drop
            itemType = default;
            return false;
        }

        itemType = default;
        return false;
    }
}
