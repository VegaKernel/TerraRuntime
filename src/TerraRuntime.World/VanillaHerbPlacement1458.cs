using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>Terraria 1.4.5.8 WorldGen.PlaceSuitableHerbHere/PlaceAlch's deterministic single-cell arm.</summary>
public static class VanillaHerbPlacement1458
{
    private const int BeachDistanceTiles = 380;
    private const int FrameStepPixels = 18;

    public static bool TryPlan(int x, int worldWidth, in WorldTile before, in WorldTile support,
        out WorldTile after)
    {
        after = before;
        if (before.IsActive || !support.IsActive || support.IsActuated || support.Shape != 0) return false;
        int style = support.Type switch {
            2 or 109 => 0, 60 => 1, 0 or 59 => 2,
            23 or 661 or 25 or 203 or 199 or 662 => 3,
            53 or 116 when x >= BeachDistanceTiles && x <= worldWidth - BeachDistanceTiles => 4,
            57 or 633 => 5, 147 or 163 or 164 or 161 or 200 => 6, _ => -1 };
        if (style < 0 || before.LiquidAmount > 0 &&
            (style <= 3 || style == 5 && before.LiquidKind != WorldLiquidKind.Lava ||
             style is 4 or 6 && before.LiquidKind == WorldLiquidKind.Lava)) return false;
        after.Type = (ushort)VanillaTileIds.ImmatureHerbs.Value;
        after.Flags |= WorldTileFlags.Active;
        after.FrameX = (short)(style * FrameStepPixels); after.FrameY = 0;
        after.TileColor = support.TileColor;
        const WorldTileFlags coating = WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock;
        after.Flags = (after.Flags & ~coating) | (support.Flags & coating);
        return true;
    }
}
