using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Source TileRunner for retained roots, deposits and webs. Basic seed variation shares the early runner's
/// physical stream; extra seeds, special liquid cavities and potentially large-strength acceleration remain explicit frontiers.</summary>
internal sealed class SmallTerrainRunner1458(WorldTileStore store, IWorldGenerationVanillaRandom random,
    double worldSurface, VanillaLiquidLines1458 lines, CancellationToken cancellationToken,
    WorldTileRegion undergroundDesert = default, double? rockLayer = null, VanillaWorldSeedProfile1458 seedProfile = default)
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
        if (!seedProfile.SupportsSourceBackedResetAndTerrain || seedProfile.Secret != VanillaSecretWorldSeed1458.None || seedProfile.Has(VanillaSpecialWorldSeed1458.Skyblock))
            throw new NotSupportedException("This small terrain runner has no qualified extra-seed context.");
        double maximumScale = seedProfile.Has(VanillaSpecialWorldSeed1458.DrunkWorld) ? 1.8 :
            seedProfile.Has(VanillaSpecialWorldSeed1458.Remix) ? 1.5 : seedProfile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy) && type != UnderworldTerrain1458.Ash ? 2.2 : 1;
        if (strength * maximumScale > (entranceOpening ? 77 : 70)) throw new NotSupportedException("This bounded runner admits the measured small-brush and dungeon entrance domains.");
        if (!seedProfile.IsDefault && type == UnderworldTerrain1458.LiquidCavity &&
            (originY < store.Dimensions.HeightTiles - 300 || rockLayer is not double rockLevel || !double.IsFinite(rockLevel)))
            throw new NotSupportedException("Special liquid cavities require the bounded Underworld caller and retained Main layers.");
        VaryForSeed(random, seedProfile, ref strength, ref steps, type);
        double x = originX, y = originY;
        double vx = random.Next(-10, 11) * 0.1, vy = random.Next(-10, 11) * 0.1;
        if (speedX != 0 || speedY != 0) { vx = speedX; vy = speedY; }
        WorldLiquidKind liquidKind = ChooseLiquidForSeed(random, seedProfile);
        double remaining = steps;
        while (strength > 0 && remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (seedProfile.Has(VanillaSpecialWorldSeed1458.DrunkWorld) && random.Next(30) == 0)
            { x += random.Next(-100, 101) * .05; y += random.Next(-100, 101) * .05; }
            if (liquidKind == WorldLiquidKind.Lava && seedProfile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10) &&
                x > store.Dimensions.WidthTiles * .45 && y < store.Dimensions.WidthTiles * .55 && y < worldSurface) liquidKind = WorldLiquidKind.Water;
            if (type == Mud && y < 0) remaining = 0;
            double current = strength * (remaining / steps);
            remaining--;
            int left = Math.Max(1, (int)(x - current * 0.5));
            int right = Math.Min(store.Dimensions.WidthTiles - 1, (int)(x + current * 0.5));
            int top = Math.Max(1, (int)(y - current * 0.5));
            int bottom = Math.Min(store.Dimensions.HeightTiles - 1, (int)(y + current * 0.5));
            for (int tx = left; tx < right; tx++)
            {
                if (tx < 430 || tx >= store.Dimensions.WidthTiles - 430) liquidKind = WorldLiquidKind.Water;
                for (int ty = top; ty < bottom; ty++)
                {
                    if (seedProfile.Has(VanillaSpecialWorldSeed1458.DrunkWorld) && type == UnderworldTerrain1458.Ash && ty < store.Dimensions.HeightTiles - 300) continue;
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
                            tile.LiquidKind = liquidKind;
                        if (ty > lines.LavaLine && (!seedProfile.Has(VanillaSpecialWorldSeed1458.Remix) ||
                            (ty < rockLayer!.Value - 80 || ty > store.Dimensions.HeightTiles - 350) &&
                            !OceanDepths(tx, ty, store.Dimensions.WidthTiles, worldSurface, rockLayer.Value)))
                            tile.LiquidKind = WorldLiquidKind.Lava;
                        }
                        tile.Flags &= ~WorldTileFlags.Active;
                        continue;
                    }

                    if (!overRide || !tile.IsActive || CanReplace(tile.TileType, type, tx, ty))
                    {
                        tile.Type = (ushort)type;
                        if (web) tile.ClearSourceShape(); // TileID.Sets.SaveSlopes[51] clears independent half-brick and slope bits.
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
            }
            x += vx; y += vy;
            ApplyExtraSteps(random, seedProfile.Has(VanillaSpecialWorldSeed1458.DrunkWorld), current,
                ref remaining, ref x, ref y, ref vx, ref vy);
            vx += random.Next(-10, 11) * .05;
            if (seedProfile.Has(VanillaSpecialWorldSeed1458.DrunkWorld)) vx += random.Next(-10, 11) * .25;
            vx = Math.Clamp(vx, -1, 1);
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

    // Shared source TileRunner advancement:900 follows800, and350 is intentionally absent.
    internal static void ApplyExtraSteps(IWorldGenerationVanillaRandom random, bool drunk, double current,
        ref double remaining, ref double x, ref double y, ref double vx, ref double vy)
    {
        if (drunk && random.Next(3) == 0) return;
        ReadOnlySpan<double> thresholds = [50,100,150,200,250,300,400,500,600,700,800,900];
        foreach (double threshold in thresholds)
        {
            if (current <= threshold) break;
            x += vx; y += vy; remaining--;
            vy += random.Next(-10, 11) * .05;
            vx += random.Next(-10, 11) * .05;
        }
    }

    internal static bool OceanDepths(int x, int y, int width, double surface, double rock) =>
        y <= (surface + rock) / 2 + 40 && (x < 380 || x > width - 380);

    internal static void VaryForSeed(IWorldGenerationVanillaRandom random, VanillaWorldSeedProfile1458 profile,
        ref double strength, ref int steps, int type)
    {
        if (profile.Has(VanillaSpecialWorldSeed1458.DrunkWorld))
        {
            strength *= 1 + random.Next(-80, 81) * .01;
            steps = (int)(steps * (1 + random.Next(-80, 81) * .01));
        }
        else if (profile.Has(VanillaSpecialWorldSeed1458.Remix)) strength *= 1 + random.Next(-50, 51) * .01;
        else if (profile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy) && type != 57)
        {
            strength *= 1 + random.Next(-80, 81) * .015;
            steps += random.Next(3);
        }
    }

    internal static WorldLiquidKind ChooseLiquidForSeed(IWorldGenerationVanillaRandom random, VanillaWorldSeedProfile1458 profile)
    {
        if (profile.Has(VanillaSpecialWorldSeed1458.NotTheBees) && profile.Has(VanillaSpecialWorldSeed1458.TheConstant) &&
            !profile.Has(VanillaSpecialWorldSeed1458.Remix) && random.Next(3) == 0) return WorldLiquidKind.Honey;
        if (random.Next(4) != 0) return WorldLiquidKind.Water;
        if (profile.Has(VanillaSpecialWorldSeed1458.DrunkWorld) && profile.Has(VanillaSpecialWorldSeed1458.CelebrationMk10) &&
            profile.Has(VanillaSpecialWorldSeed1458.Remix) && !profile.Has(VanillaSpecialWorldSeed1458.NotTheBees)) return WorldLiquidKind.Shimmer;
        return profile.Has(VanillaSpecialWorldSeed1458.ForTheWorthy) ? WorldLiquidKind.Lava : WorldLiquidKind.Water;
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
