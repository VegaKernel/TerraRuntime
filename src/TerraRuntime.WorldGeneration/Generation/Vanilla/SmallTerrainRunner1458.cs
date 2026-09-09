using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>The ordinary, small-strength TileRunner slice used by Underworld, roots, deposits and webs.
/// It admits only the verified web override; mud walls, secret seeds and large-strength acceleration remain excluded.</summary>
internal sealed class SmallTerrainRunner1458(WorldTileStore store, IWorldGenerationVanillaRandom random,
    double worldSurface, VanillaLiquidLines1458 lines, CancellationToken cancellationToken,
    WorldTileRegion undergroundDesert = default, double? rockLayer = null)
{
    internal const int Mud = 59;
    internal const int AirCavity = -1;
    public void Run(int originX, int originY, double strength, int steps, int type,
        bool addTile = false, double speedX = 0, double speedY = 0, bool noYChange = false, int ignoreTileType = -1,
        bool overRide = true)
    {
        bool web = type == 51 && addTile && !noYChange && !overRide && speedX is -1 or 1 && speedY == -1 &&
            strength is >= 4 and < 11 && steps is >= 2 and < 4 && ignoreTileType == -1;
        bool gem = type is >= 63 and <= 68;
        bool deposit = gem || type is 123 or 6 or 7 or 8 or 9 or 166 or 167 or 168 or 169 or 22 or 204;
        bool entranceOpening = type == AirCavity && !addTile && !noYChange && overRide &&
            speedX == 0 && speedY == -1 && strength is >= 25 and < 35 &&
            steps is >= 10 and < 20 && ignoreTileType == -1;
        bool mudDeposit = type == Mud && !noYChange && !addTile && speedX == 0 && speedY == 0 &&
            strength is >= 2 and < 6 && steps is >= 2 and < 40 && ignoreTileType == 53 &&
            rockLayer is double rock && double.IsFinite(rock) && rock >= 0;
        if ((!web && !deposit && !entranceOpening && type is not (UnderworldTerrain1458.Ash or UnderworldTerrain1458.Hellstone or UnderworldTerrain1458.LiquidCavity or Mud)) ||
            !double.IsFinite(strength) || strength <= 0 || (!entranceOpening && strength >= 30) || steps is <= 0 or > 1000 ||
            !double.IsFinite(speedX) || !double.IsFinite(speedY) ||
            (!web && type is not (UnderworldTerrain1458.Ash or Mud) && (addTile || noYChange)) ||
            (!overRide && !web) ||
            (deposit && (speedX != 0 || speedY != 0)) ||
            (gem && (strength is < 2 or >= 6 || steps is < 3 or >= 7)) ||
            (ignoreTileType != -1 && !mudDeposit) ||
            (type == Mud && !mudDeposit && (addTile || !noYChange || speedX != 0 || speedY != 2 ||
                strength is < 10 or >= 20 || steps is < 10 or >= 20 || originY < 0)))
            throw new ArgumentOutOfRangeException(nameof(type));
        double x = originX, y = originY;
        double vx = random.Next(-10, 11) * 0.1, vy = random.Next(-10, 11) * 0.1;
        if (speedX != 0 || speedY != 0) { vx = speedX; vy = speedY; }
        _ = random.Next(4); // Ordinary TileRunner still samples its unused secret-seed liquid decision.
        for (int remaining = steps; remaining > 0; remaining--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type == Mud && y < 0) remaining = 0;
            double current = strength * ((double)remaining / steps);
            int left = Math.Max(1, (int)(x - current * 0.5));
            int right = Math.Min(store.Dimensions.WidthTiles - 1, (int)(x + current * 0.5));
            int top = Math.Max(1, (int)(y - current * 0.5));
            int bottom = Math.Min(store.Dimensions.HeightTiles - 1, (int)(y + current * 0.5));
            for (int tx = left; tx < right; tx++)
            for (int ty = top; ty < bottom; ty++)
            {
                ref WorldTile tile = ref UnderworldTerrain1458.At(store, tx, ty);
                if (tile.IsActive && (tile.Type >= VanillaTileIds.Count ||
                    (VanillaWorldFrameImportance326.IsFrameImportant(tile.Type) && !VanillaProjectileTileCutFacts.IsCuttable(tile.TileType)) ||
                    tile.Type == ignoreTileType))
                    continue;
                if (Math.Abs(tx - x) + Math.Abs(ty - y) >= strength * 0.5 * (1 + random.Next(-10, 11) * 0.015)) continue;
                if (type is AirCavity or UnderworldTerrain1458.LiquidCavity)
                {
                    if (tile.IsActive && tile.TileType == VanillaTileIds.Sand) continue;
                    if (type == UnderworldTerrain1458.LiquidCavity && tile.IsActive && (ty < lines.WaterLine || ty > lines.LavaLine))
                    {
                        tile.LiquidAmount = byte.MaxValue;
                        tile.LiquidKind = ty > lines.LavaLine ? WorldLiquidKind.Lava : WorldLiquidKind.Water;
                    }
                    tile.Flags &= ~WorldTileFlags.Active;
                    continue;
                }

                if (!overRide || !tile.IsActive || CanReplace(tile.TileType, type, tx, ty))
                {
                    tile.Type = (ushort)type;
                    if (web) tile.Shape = 0; // TileID.Sets.SaveSlopes[51] is false.
                }
                // Other admitted positive types have SaveSlopes=true. Activation/liquid clearing is independent
                // of the replacement gate, but frame-important objects were skipped before consuming RNG.
                if (addTile)
                {
                    tile.Flags |= WorldTileFlags.Active;
                    tile.LiquidAmount = 0;
                    UnderworldTerrain1458.ClearLavaFlag(ref tile);
                }
                if (noYChange && type != Mud && ty < worldSurface) tile.Wall = (ushort)VanillaWallIds.DirtUnsafe.Value;
                if (type == Mud && ty > lines.WaterLine && tile.LiquidAmount > 0)
                {
                    tile.LiquidAmount = 0;
                    UnderworldTerrain1458.ClearLavaFlag(ref tile);
                }
            }
            x += vx; y += vy;
            vx = Math.Clamp(vx + random.Next(-10, 11) * 0.05, -1, 1);
            if (!noYChange) vy = Math.Clamp(vy + random.Next(-10, 11) * 0.05, -1, 1);
            else if (type != Mud && current < 3) vy = Math.Clamp(vy, -1, 1);
            if (mudDeposit)
            {
                vy = Math.Clamp(vy, -0.5, 0.5);
                if (y < rockLayer!.Value + 100) vy = 1;
                if (y > store.Dimensions.HeightTiles - 300) vy = -1;
            }
        }
    }

    private bool CanReplace(TileTypeId old, int replacement, int x, int y) => old.Value switch
    {
        // TileRunner overrides its general generation-clear table for the desert pair and Ore[58].
        396 or 397 => replacement is UnderworldTerrain1458.Hellstone or 6 or 7 or 8 or 9 or 166 or 167 or 168 or 169 or 22 or 204,
        // Main.tileStone[63..68] restricts active replacement to stone, including pre-existing gems.
        _ when replacement is >= 63 and <= 68 && old.Value != 1 => false,
        45 or 147 or 189 or 190 or 196 or 460 or 717 or 718 or 719 => false,
        53 when replacement == Mud => x < undergroundDesert.X || x >= undergroundDesert.ExclusiveRight ||
            y < undergroundDesert.Y || y >= undergroundDesert.ExclusiveBottom,
        53 when y < worldSurface => false,
        1 when replacement == Mud => y >= worldSurface + random.Next(-50, 50),
        367 or 368 when replacement == Mud => false,
        _ => WorldSmoothingCatalog1458.CanBeClearedDuringGeneration(old)
    };
}
