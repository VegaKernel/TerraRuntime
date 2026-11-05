using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// The independently observed source conversion sets for natural stone/grass/golf grass/
/// ice/sand/hardened sand/sandstone/jungle grass, complete source moss set,
/// matching wall and first four NewWall identities, torch-4 styles and four thorn
/// identities and color-only special paint from TerrariaServer.exe 1.4.5.8
/// <c>WorldGen.Convert</c> types 1/2/4.
/// </summary>
internal sealed class GenerationWorldConvert1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    GenerationTileFraming1458 tileFraming)
{
    private readonly GenerationWallFraming1458 wallFraming = new(store, random);
    private readonly GenerationWorldTreeConversion1458 treeConversion = new(store);

    internal void ConvertAt(int x, int y, int conversionType, bool rainbowSandAndBlackSandWalls)
    {
        if (x < 1 || y < 1 || x >= store.Dimensions.WidthTiles - 1 ||
            y >= store.Dimensions.HeightTiles - 1)
            return;
        if (conversionType is not (1 or 2 or 4))
            throw new NotSupportedException("This conversion type needs its own source-backed table.");

        ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(x, y)];
        if (!tile.IsActive && tile.Wall == 0)
            return;
        if (tile.IsActive && tile.Type == 4 && tile.FrameY < 0)
            throw new NotSupportedException("Negative torch frame needs a direct conversion fixture.");
        ushort newTile = tile.Type switch
        {
            // WorldGen.Convert (1/2/4) leaves active Dirt0 unchanged, but its
            // independently eligible wall can still convert and frame. Only
            // active wall0/1 is qualified; inactive tile0 retains its older wall cohorts.
            0 when !tile.IsActive || tile.Wall is 0 or 1 => 0,
            // These occupied source pairs carry no tile conversion for 1/2/4.
            // Keep the admission tied to their independently observed walls.
            226 when tile.Wall == 87 => 226,
            41 when tile.Wall == 7 => 41,
            1 or 25 or 117 or 203 => conversionType switch { 1 => 25, 2 => 117, _ => 203 },
            2 or 23 or 109 or 199 => conversionType switch { 1 => 23, 2 => 109, _ => 199 },
            477 or 492 => conversionType switch { 1 => 23, 2 => 492, _ => 199 },
            161 or 163 or 164 or 200 => conversionType switch { 1 => 163, 2 => 164, _ => 200 },
            53 or 112 or 116 or 234 => conversionType switch { 1 => 112, 2 => 116, _ => 234 },
            4 => 4,
            179 or 180 or 181 or 182 or 183 or 381 or 534 or 536 or 539 or 625 or 627 =>
                conversionType switch { 1 => 25, 2 => 117, _ => 203 },
            397 or 398 or 399 or 402 => conversionType switch { 1 => 398, 2 => 402, _ => 399 },
            396 or 400 or 401 or 403 => conversionType switch { 1 => 400, 2 => 403, _ => 401 },
            60 or 661 or 662 => conversionType switch { 1 => 661, 2 => tile.Type, _ => 662 },
            32 or 69 or 352 or 655 => conversionType switch { 1 => 32, 2 => tile.Type, _ => 352 },
            56 => 56,
            _ => throw new NotSupportedException($"WorldGen.Convert has no qualified tile {tile.Type} mapping here.")
        };
        ushort newWall = tile.Wall switch
        {
            0 => 0,
            1 => conversionType switch { 1 => 3, 2 => 28, _ => 83 },
            2 when tile.IsActive && tile.Type == 25 => 2,
            3 when tile.IsActive && tile.Type == 25 => conversionType switch { 1 => 3, 2 => 28, _ => 83 },
            7 when tile.IsActive && tile.Type == 41 => 7,
            87 when tile.IsActive && tile.Type == 226 => 87,
            63 => conversionType switch { 1 => 69, 2 => 70, _ => 81 },
            216 => conversionType switch { 1 => 217, 2 => 219, _ => 218 },
            187 => conversionType switch { 1 => 220, 2 => 222, _ => 221 },
            >= 188 and <= 191 => (ushort)((conversionType switch { 1 => 188, 2 => 200, _ => 192 }) + tile.Wall - 188),
            _ => throw new NotSupportedException($"WorldGen.Convert has no qualified wall {tile.Wall} mapping here.")
        };
        bool changesTile = tile.IsActive && newTile != tile.Type;
        bool changesWall = newWall != tile.Wall;
        int torchStyle = tile.IsActive && tile.Type == 4 ? tile.FrameY / 22 : -1;
        int targetTorchStyle = conversionType switch { 1 => 18, 2 => 20, _ => 19 };
        bool changesTorch = torchStyle is 0 or 9 or 16 or 18 or 19 or 20 or 21 or 22 &&
            torchStyle != targetTorchStyle;
        if (rainbowSandAndBlackSandWalls &&
            (changesTile || changesTorch) &&
            (tile.Flags & (WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock)) != 0 ||
            rainbowSandAndBlackSandWalls && changesWall &&
            (tile.Flags & (WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall)) != 0)
            throw new NotSupportedException("Special paint coating conversion needs independent Main.rand ownership.");
        GenerationWorldTreeConversion1458.Plan treePlan = default;
        bool treeAbove = changesTile && store.Tiles[store.GetUncheckedIndex(x, y - 1)].IsActive;
        if (treeAbove)
            treePlan = treeConversion.Prepare(x, y, newTile);

        // WorldGen.Convert processes a wall first. SquareWallFrame spends one shared
        // draw for this ordinary wall; the later tile conversion uses the same RNG.
        if (newWall != tile.Wall)
        {
            tile.Wall = newWall;
            if (rainbowSandAndBlackSandWalls && tile.WallColor != 0)
            {
                tile.WallColor = 0;
                for (int dust = 0; dust < 10; dust++) random.Next(2);
            }
            wallFraming.SquareWallFrame(x, y);
        }
        if (tile.IsActive && conversionType == 2 && tile.Type is 32 or 69 or 352 or 655)
        {
            // Hallow does not produce a thorn identity. WorldGen.Convert invokes
            // KillTile after any wall conversion; dust for32/352 moves shared RNG.
            tileFraming.KillTile(x, y);
            return;
        }
        if (tile.IsActive && tile.Type == 4)
        {
            // TileID.Sets.Conversion.Torch's 1/2/4 arm admits only these style
            // bands. A rejected style is untouched; an already-matching style
            // does not invoke SquareTileFrame or consume its downstream work.
            int style = tile.FrameY / 22;
            if (style is 0 or 9 or 16 or 18 or 19 or 20 or 21 or 22)
            {
                int target = conversionType switch { 1 => 18, 2 => 20, _ => 19 };
                if (style != target)
                {
                    tile.FrameY = (short)(target * 22);
                    if (rainbowSandAndBlackSandWalls && tile.TileColor != 0)
                    {
                        for (int dust = 0; dust < 10; dust++) random.Next(2);
                        tile.TileColor = 0;
                    }
                    tileFraming.SquareTileFrame(x, y);
                }
            }
            return;
        }
        if (changesTile)
        {
            if (treeAbove)
                treeConversion.Apply(x, y, newTile, treePlan);
            tile.Type = newTile;
            if (rainbowSandAndBlackSandWalls && tile.TileColor != 0)
            {
                for (int dust = 0; dust < 10; dust++) random.Next(2);
                tile.TileColor = 0;
            }
            tileFraming.SquareTileFrame(x, y);
        }
    }
}
