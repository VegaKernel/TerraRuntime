using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.JunglePlantsPart2</c> and the
/// <c>WorldGen.PlaceJunglePlant</c> it is the only generation-time caller of.
/// </summary>
/// <remarks>
/// <para>
/// This pass is genuinely a sampling loop, not a scan: it makes one hundred attempts per tile of world width,
/// each picking a column in the half of the world AWAY from the dungeon, dropping down it to the first active
/// cell, and offering a Jungle Plants 2 to the cell above when that cell is jungle grass. The two-wide form is
/// tried first with one of eight styles; only if that is refused is the three-wide form tried with one of
/// twelve.
/// </para>
/// <para>
/// <c>PlaceJunglePlant</c> has two footprints with different anchors. The two-wide form occupies the square up
/// and to the LEFT of the anchor, the three-wide form the box centred on it, and each requires every column of
/// its footprint to stand on solid jungle grass. Both clear whatever is in the way first, and both accept a
/// short list of growth - vines, thorns and the flat small pile - as "in the way" rather than "blocking".
/// </para>
/// </remarks>
internal sealed class JunglePlantPart2Pass1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    bool dungeonOnLeft,
    CancellationToken cancellation)
{
    private const ushort JungleGrass = 60;
    private const ushort PlantDetritus = 233;
    private const ushort SmallPiles = 185;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    public long Planted { get; private set; }

    public void Apply()
    {
        // The source offers the half of the world away from the dungeon: the column is always drawn from the
        // left half, and a dungeon on the left pushes it into the right half. The source's test is
        // dungeonSide <= DungeonSide.Left, and Left is -1 while the unset value None is 0, so a world whose
        // dungeon side was never decided keeps the left half.
        for (long attempt = 0; attempt < (long)width * 100; attempt++)
        {
            if ((attempt & 0x3FFF) == 0)
                cancellation.ThrowIfCancellationRequested();

            int column = random.Next(40, (width / 2) - 40);
            if (dungeonOnLeft)
                column += width / 2;

            int row = random.Next(height - 300);
            while (!IsActive(column, row) && row < height - 300)
                row++;

            if (!IsActive(column, row) || TypeAt(column, row) != JungleGrass)
                continue;

            row--;
            PlaceJunglePlant(column, row, PlantDetritus, random.Next(8), styleY: 0);
            if (TypeAt(column, row) != PlantDetritus)
                PlaceJunglePlant(column, row, PlantDetritus, random.Next(12), styleY: 1);
        }
    }

    /// <summary>
    /// Source <c>WorldGen.PlaceJunglePlant</c> for the one identity this pass uses. <paramref name="styleY"/>
    /// selects the footprint, not a frame row: zero is the three-wide form centred on the anchor, one is the
    /// two-wide form up and to the left of it.
    /// </summary>
    private void PlaceJunglePlant(int x, int y, ushort type, int styleX, int styleY)
    {
        if (styleY > 0)
        {
            PlaceTwoWide(x, y, type, styleX);
            return;
        }

        PlaceThreeWide(x, y, type, styleX);
    }

    private void PlaceTwoWide(int x, int y, ushort type, int styleX)
    {
        if (x < 5 || x > width - 5 || y < 5 || y > height - 5)
            return;

        for (int column = x - 1; column < x + 1; column++)
        {
            for (int row = y - 1; row < y + 1; row++)
            {
                if (IsBlocking(column, row, type))
                    return;
            }

            if (!IsSolid(column, y + 1) || TypeAt(column, y + 1) != JungleGrass)
                return;
        }

        for (int column = x - 1; column < x + 1; column++)
        {
            for (int row = y - 1; row < y + 1; row++)
                Clear(column, row);
        }

        short frameX = checked((short)(36 * styleX));
        WorldTile support = At(x, y + 1);
        Write(x - 1, y - 1, type, frameX, 36, in support);
        Write(x, y - 1, type, checked((short)(frameX + 18)), 36, in support);
        Write(x - 1, y, type, frameX, 54, in support);
        Write(x, y, type, checked((short)(frameX + 18)), 54, in support);
        Planted++;
    }

    private void PlaceThreeWide(int x, int y, ushort type, int styleX)
    {
        if (x < 5 || x > width - 5 || y < 5 || y > height - 5)
            return;

        for (int column = x - 1; column < x + 2; column++)
        {
            for (int row = y - 1; row < y + 1; row++)
            {
                if (IsBlocking(column, row, type))
                    return;
            }

            if (!IsSolid(column, y + 1) || TypeAt(column, y + 1) != JungleGrass)
                return;
        }

        for (int column = x - 1; column < x + 2; column++)
        {
            for (int row = y - 1; row < y + 1; row++)
                Clear(column, row);
        }

        short frameX = checked((short)(54 * styleX));
        WorldTile support = At(x, y + 1);
        for (int dx = 0; dx < 3; dx++)
        {
            Write(x - 1 + dx, y - 1, type, checked((short)(frameX + dx * 18)), 0, in support);
            Write(x - 1 + dx, y, type, checked((short)(frameX + dx * 18)), 18, in support);
        }

        Planted++;
    }

    /// <summary>
    /// The footprint test. Growth the source is willing to overwrite - jungle plants, vines, thorns, the other
    /// jungle plant family and a flat small pile - does not block; anything else active does.
    /// </summary>
    private bool IsBlocking(int x, int y, ushort type)
    {
        if (!Contains(x, y))
            return true;

        WorldTile tile = At(x, y);
        if (!tile.IsActive)
            return false;

        if (tile.Type is 61 or 703 or 62 or 655 or 69 or 74)
            return false;

        // The source exempts an existing plant detritus only when the tile being PLACED is one of the three
        // bulb identities, never when it is plant detritus itself. Exempting it here let the pass plant over
        // its own output, which changed which attempts were accepted and moved the shared stream.
        if (tile.Type == PlantDetritus && type is 236 or 702 or 238)
            return false;
        if (tile.Type == SmallPiles && tile.FrameY == 0)
            return false;

        return true;
    }

    /// <summary>
    /// The source clears with a no-item <c>KillTile</c>, so nothing drops during generation - but the dust it
    /// makes is still paid for out of the shared RNG, which is why this is not a plain write of zeroes.
    /// </summary>
    private void Clear(int x, int y)
    {
        if (!Contains(x, y) || !At(x, y).IsActive)
            return;

        GenerationKillTileDust1458.Consume(random, At(x, y).Type);
        ref WorldTile cell = ref At(x, y);
        cell.Flags &= ~WorldTileFlags.Active;
        cell.Shape = 0;
        cell.FrameX = -1;
        cell.FrameY = -1;
        cell.TileColor = 0;
        cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        cell.Type = 0;
    }

    private void Write(int x, int y, ushort type, short frameX, short frameY, in WorldTile support)
    {
        if (!Contains(x, y))
            return;

        ref WorldTile cell = ref At(x, y);
        cell.Flags |= WorldTileFlags.Active;
        cell.FrameX = frameX;
        cell.FrameY = frameY;
        cell.Type = type;
        cell.TileColor = support.TileColor;
        cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        cell.Flags |= support.Flags & (WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
    }

    private bool IsSolid(int x, int y)
    {
        if (!Contains(x, y))
            return true;

        WorldTile tile = At(x, y);
        return tile.IsActive && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) && !tile.IsActuated && tile.Shape == 0;
    }

    private bool IsActive(int x, int y) => Contains(x, y) && At(x, y).IsActive;

    private ushort TypeAt(int x, int y) => Contains(x, y) && At(x, y).IsActive ? At(x, y).Type : (ushort)0;

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
