using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Source-pinned frame-important identities whose runtime footprint is exactly one tile and whose 1.4.5.8
/// KillTile drop is fixed. The generic frame-important family stays fail-closed: only entries in this catalog may
/// use the single-cell removal path.
/// </summary>
public static class VanillaFrameImportantSingleCellCatalog1458
{
    public static bool IsSupported(TileTypeId type) =>
        type == VanillaTileIds.WaterCandle ||
        type == VanillaTileIds.Switches ||
        type == VanillaTileIds.TeamBlockRedPlatform ||
        type == VanillaTileIds.TeamBlockGreenPlatform ||
        type == VanillaTileIds.TeamBlockBluePlatform ||
        type == VanillaTileIds.TeamBlockYellowPlatform ||
        type == VanillaTileIds.TeamBlockPinkPlatform ||
        type == VanillaTileIds.TeamBlockWhitePlatform;
}
