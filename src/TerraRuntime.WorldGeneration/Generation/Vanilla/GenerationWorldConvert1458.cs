using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// The independently observed natural stone/grass/ice/sand, complete source moss set,
/// representative hardened-sand, sandstone and jungle grass, plus matching wall and
/// first four NewWall conversion-family identities and ordinary torch-4 styles of
/// TerrariaServer.exe 1.4.5.8 <c>WorldGen.Convert</c> for conversion types 1/2/4.
/// </summary>
internal sealed class GenerationWorldConvert1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    GenerationTileFraming1458 tileFraming)
{
    private readonly GenerationWallFraming1458 wallFraming = new(store, random);

    internal void ConvertAt(int x, int y, int conversionType, bool rainbowSandAndBlackSandWalls)
    {
        if (x < 1 || y < 1 || x >= store.Dimensions.WidthTiles - 1 ||
            y >= store.Dimensions.HeightTiles - 1)
            return;
        if (conversionType is not (1 or 2 or 4) || rainbowSandAndBlackSandWalls)
            throw new NotSupportedException("This conversion profile needs its own source-backed table and paint owner.");

        ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(x, y)];
        if (!tile.IsActive && tile.Wall == 0)
            return;
        if (tile.IsActive && tile.Type == 4 && tile.FrameY < 0)
            throw new NotSupportedException("Negative torch frame needs a direct conversion fixture.");
        ushort newTile = tile.Type switch
        {
            1 => conversionType switch { 1 => 25, 2 => 117, _ => 203 },
            2 => conversionType switch { 1 => 23, 2 => 109, _ => 199 },
            161 => conversionType switch { 1 => 163, 2 => 164, _ => 200 },
            53 => conversionType switch { 1 => 112, 2 => 116, _ => 234 },
            4 => 4,
            179 or 180 or 181 or 182 or 183 or 381 or 534 or 536 or 539 or 625 or 627 =>
                conversionType switch { 1 => 25, 2 => 117, _ => 203 },
            397 => conversionType switch { 1 => 398, 2 => 402, _ => 399 },
            396 => conversionType switch { 1 => 400, 2 => 403, _ => 401 },
            60 => conversionType switch { 1 => 661, 2 => 60, _ => 662 },
            56 => 56,
            0 when !tile.IsActive => 0,
            _ => throw new NotSupportedException($"WorldGen.Convert has no qualified tile {tile.Type} mapping here.")
        };
        ushort newWall = tile.Wall switch
        {
            0 => 0,
            1 => conversionType switch { 1 => 3, 2 => 28, _ => 83 },
            63 => conversionType switch { 1 => 69, 2 => 70, _ => 81 },
            216 => conversionType switch { 1 => 217, 2 => 219, _ => 218 },
            187 => conversionType switch { 1 => 220, 2 => 222, _ => 221 },
            >= 188 and <= 191 => (ushort)((conversionType switch { 1 => 188, 2 => 200, _ => 192 }) + tile.Wall - 188),
            _ => throw new NotSupportedException($"WorldGen.Convert has no qualified wall {tile.Wall} mapping here.")
        };
        if (tile.IsActive && newTile != tile.Type && store.Tiles[store.GetUncheckedIndex(x, y - 1)].IsActive)
            throw new NotSupportedException("Conversion under an active tile needs source tree ownership qualification.");

        // WorldGen.Convert processes a wall first. SquareWallFrame spends one shared
        // draw for this ordinary wall; the later tile conversion uses the same RNG.
        if (newWall != tile.Wall)
        {
            tile.Wall = newWall;
            wallFraming.SquareWallFrame(x, y);
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
                    tileFraming.SquareTileFrame(x, y);
                }
            }
            return;
        }
        if (tile.IsActive && newTile != tile.Type)
        {
            tile.Type = newTile;
            tileFraming.SquareTileFrame(x, y);
        }
    }
}
