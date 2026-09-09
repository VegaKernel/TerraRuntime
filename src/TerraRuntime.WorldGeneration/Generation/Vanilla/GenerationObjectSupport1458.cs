using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Shared generation-time attachment facts from Main/TileID/TileObjectData, TerrariaServer 1.4.5.8.</summary>
internal static class GenerationObjectSupport1458
{
    private static ReadOnlySpan<ushort> NoAttach =>
    [3,4,10,13,14,15,16,17,18,19,20,21,27,50,86,87,88,89,90,91,92,93,94,95,96,97,98,99,
     101,102,110,114,134,387,388,390,427,435,436,437,438,439,441,467,468,469,486,487,488,
     489,490,497,564,565,568,569,570,572,580,590,593,594,595,615,620,704,707];

    internal static bool DisallowsAttachment(ushort type) => NoAttach.Contains(type);
    internal static bool IsBoulder(ushort type) => type is 138 or 484 or 664 or 665 or 711 or 712 or 713 or 714 or 715 or 716;
    internal static bool BreakableWhenPlacing(ushort type) => type is 324 or 186 or 187 or 185 or 165 or 530 or 233 or 227 or 485 or 81 or 624;

    internal static bool SupportsChest(WorldTile tile, bool crackedBricksSolid)
    {
        if (!tile.IsActive || tile.IsActuated) return false;
        bool solid = (crackedBricksSolid || tile.Type is not (481 or 482 or 483)) && VanillaTileCollisionCatalog.IsSolid(tile.TileType);
        bool top = VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
        bool valid = tile.Type != 127 && !IsBoulder(tile.Type);
        if (solid && !top && !DisallowsAttachment(tile.Type) && tile.Shape == 0 && valid) return true;
        if (tile.Type == 19)
        {
            int frame = tile.FrameX / 18;
            return tile.Shape != 1 && (frame is >= 0 and <= 7 or >= 12 and <= 16 or >= 25 and <= 26);
        }
        if (solid && top) return true;
        return solid && !top && tile.Shape is 4 or 5 && valid;
    }
}
