using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// The generation-time slice of TerrariaServer 1.4.5.8 <c>WorldGen.SquareTileFrame</c> that
/// <c>WorldGen.PlaceTile</c> runs after it has placed an object.
/// </summary>
/// <remarks>
/// <para>
/// Framing is not cosmetic here. <c>PlaceTile</c> ends every object placement with
/// <c>SquareTileFrame(i, j)</c>, which re-frames the nine cells around the anchor, and for the three-by-two
/// pile family that reaches <c>Check3x2</c>. <c>Check3x2</c> re-derives the object's origin from the cell's own
/// frame and destroys the whole object when any of its six cells no longer carries the exact frame that origin
/// implies. A new object that overlaps an older one therefore deletes the older one, and the deletion runs
/// through <c>KillTile</c>, which leaves the cell inactive with frames of <c>-1</c> rather than zero.
/// </para>
/// <para>
/// That deletion is observable and it feeds back into the caller: the living-tree canopy hangs its detritus by
/// walking down a column until it meets an active cell, so a cell that a later placement deleted changes where
/// the next walk stops, whether the placement is offered at all, and therefore whether its style draw happens.
/// Leaving this out does not merely leave stale frames behind - it moves the shared RNG.
/// </para>
/// <para>
/// This is a bounded slice, not the whole primitive. It carries the parts <c>TileFrame</c> reaches during world
/// generation for the three-by-two pile family (types <c>186</c> and <c>187</c>): the bounds guard, the
/// inactive-cell cleanup, <c>Check3x2</c> and the generation-time part of <c>KillTile</c>. The cosmetic branch
/// is genuinely absent during generation - the source guards it with <c>!generatingWorld</c> - so nothing is
/// dropped there. Frame-important identities outside that family keep their own validators in the source; this
/// slice does not run them, and callers must not use it to frame a square that can hold one.
/// </para>
/// </remarks>
internal sealed class GenerationTileFraming1458(WorldTileStore store)
{
    private const ushort SmallPiles = 186;
    private const ushort PlantDetritus = 187;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    /// <summary>
    /// Source <c>WorldGen.destroyObject</c>. It is a field, not a parameter: <c>Check3x2</c> raises it before it
    /// kills an object so the <c>SquareTileFrame</c> each <c>KillTile</c> triggers cannot recurse back into the
    /// same validation, and lowers it before re-framing the block around what it removed.
    /// </summary>
    private bool destroyingObject;

    /// <summary>Source <c>WorldGen.SquareTileFrame</c>: only the centre call resets the frame.</summary>
    public void SquareTileFrame(int i, int j, bool resetFrame = true)
    {
        TileFrame(i - 1, j - 1);
        TileFrame(i - 1, j);
        TileFrame(i - 1, j + 1);
        TileFrame(i, j - 1);
        TileFrame(i, j, resetFrame);
        TileFrame(i, j + 1);
        TileFrame(i + 1, j - 1);
        TileFrame(i + 1, j);
        TileFrame(i + 1, j + 1);
    }

    /// <summary>
    /// Source <c>WorldGen.TileFrame</c> as far as generation reaches it. The five-cell border is refused
    /// outright, an inactive cell is stripped of half brick, block paint and slope on the way past, and an
    /// active three-by-two pile is validated. The cosmetic tail is skipped because the source skips it while
    /// <c>generatingWorld</c> is set.
    /// </summary>
    public void TileFrame(int i, int j, bool resetFrame = false)
    {
        if (i <= 5 || j <= 5 || i >= width - 5 || j >= height - 5)
            return;

        ref WorldTile tile = ref At(i, j);
        if (!tile.IsActive)
        {
            tile.Shape = 0;
            tile.TileColor = 0;
            tile.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            return;
        }

        if (tile.Type is SmallPiles or PlantDetritus)
        {
            Check3x2(i, j, tile.Type);
            return;
        }

        if (VanillaTileIds.IsPlatform(tile.TileType))
            FramePlatform(i, j);
    }

    /// <summary>
    /// The platform arm of <c>TileFrameImportant</c>, reduced to unsloped, unhammered platforms. A platform
    /// takes its horizontal frame from what stands either side of it, so a run of them only looks like a run
    /// once each one has been re-framed - which is why <c>PlaceTile</c> frames the square around every platform
    /// it lays rather than the platform alone.
    /// </summary>
    /// <remarks>
    /// The source's sloped and half-brick branches are not ported and this refuses them outright, because their
    /// frame table folds in rope ends, merge culling and bottom-corner probes that nothing in generation
    /// reaches. The source's <c>tileStone</c> remap is also absent: it only ever rewrites a neighbour's
    /// identity to Stone, and the frame table compares identities for equality with the platform's own, so the
    /// remap cannot change the outcome for any neighbour that is solid in the first place.
    /// </remarks>
    private void FramePlatform(int i, int j)
    {
        ref WorldTile cell = ref At(i, j);
        if (cell.Shape != 0)
        {
            throw new NotSupportedException(
                "WorldGen.TileFrameImportant frames a sloped or hammered platform from a table that is not " +
                "ported. Extend GenerationTileFraming1458 before sloping a generated platform.");
        }

        ushort type = cell.Type;
        int left = NeighbourIdentity(i - 1, j, type);
        int right = NeighbourIdentity(i + 1, j, type);

        cell.FrameX = (left, right) switch
        {
            _ when left == type && right == type => 0,
            _ when left == type && right == -1 => 18,
            _ when left == -1 && right == type => 36,
            _ when left != type && right == type => 54,
            _ when left == type && right != type => 72,
            _ when left != type && left != -1 && right == -1 => 108,
            _ when left != -1 || right == type || right == -1 => 90,
            _ => 126,
        };
    }

    /// <summary>What the platform frame table sees to one side: its own identity, a solid neighbour, or nothing.</summary>
    private int NeighbourIdentity(int x, int y, ushort platform)
    {
        if (!Contains(x, y))
            return -1;

        WorldTile neighbour = At(x, y);
        if (!neighbour.IsActive)
            return -1;

        int identity = VanillaTileIds.IsPlatform(neighbour.TileType) ? platform : neighbour.Type;
        if (!VanillaTileCollisionCatalog.IsSolid(new TileTypeId(identity)))
            return -1;

        // The framed platform is unhammered here, so a hammered neighbour of any identity does not merge.
        if (neighbour.Shape == 1)
            return -1;

        return identity;
    }

    /// <summary>
    /// Source <c>WorldGen.Check3x2</c> for the pile family. The origin is re-derived from the cell's own frame,
    /// every cell of the implied object must carry exactly the frame that origin gives it, and every column must
    /// stand on solid ground that is not a boulder. Anything else destroys the object and re-frames the block
    /// around it, which can cascade into a neighbour.
    /// </summary>
    private void Check3x2(int i, int j, ushort type)
    {
        if (destroyingObject)
            return;

        bool broken = false;
        int top = j;
        int styleRow = At(i, j).FrameY / 36;
        top -= At(i, j).FrameY % 36 / 18;

        int column = At(i, j).FrameX / 18;
        int style = 0;
        while (column > 2)
        {
            column -= 3;
            style++;
        }

        int left = i - column;
        int styleFrame = style * 54;
        int floor = top + 2;

        for (int x = left; x < left + 3; x++)
        {
            for (int y = top; y < floor; y++)
            {
                if (!Contains(x, y))
                {
                    broken = true;
                    continue;
                }

                WorldTile cell = At(x, y);
                if (!cell.IsActive || cell.Type != type ||
                    cell.FrameX != (x - left) * 18 + styleFrame ||
                    cell.FrameY != (y - top) * 18 + styleRow * 36)
                {
                    broken = true;
                }
            }

            if (!SolidTileAllowBottomSlope(x, floor) || IsBoulder(x, floor))
            {
                broken = true;
                continue;
            }

            // The source keys a further ground requirement off the style. Only the styles this runtime can
            // reach are ported; the rest would need vanilla's snow, ice, mud and sand conversion sets, and
            // guessing them would put a silent divergence where a loud failure belongs.
            if (StyleGroundIsUnported(type, style))
            {
                throw new NotSupportedException(
                    $"WorldGen.Check3x2 constrains the ground under tile {type} style {style}, which is not " +
                    "ported. Extend GenerationTileFraming1458 before placing that style.");
            }
        }

        if (!broken)
            return;

        destroyingObject = true;
        for (int x = left; x < left + 3; x++)
        {
            for (int y = top; y < floor; y++)
            {
                if (Contains(x, y) && At(x, y).IsActive && At(x, y).Type == type)
                    KillTile(x, y);
            }
        }

        destroyingObject = false;
        for (int x = left - 1; x < left + 4; x++)
        {
            for (int y = top - 1; y < top + 4; y++)
                TileFrame(x, y);
        }
    }

    /// <summary>
    /// The style groups whose ground the source constrains beyond "solid and not a boulder": for Plant
    /// Detritus, mud and jungle grass under styles zero to five, ash and hellstone under six to eight and the
    /// sand family under twenty-nine to thirty-four; for Large Piles, snow and ice under twenty-six to
    /// thirty-one and mud under thirty-two to thirty-four. Everything else, including the living-tree canopy's
    /// styles forty-seven to fifty-one, carries no further requirement.
    /// </summary>
    private static bool StyleGroundIsUnported(ushort type, int style) => type switch
    {
        PlantDetritus => style is (>= 0 and <= 8) or (>= 29 and <= 34),
        SmallPiles => style is >= 26 and <= 34,
        _ => true,
    };

    /// <summary>
    /// Source <c>WorldGen.KillTile</c> reduced to what generation does to a pile: clear the cell, mark its
    /// frames unset rather than zero, drop nothing, and re-frame the square around it.
    /// </summary>
    private void KillTile(int i, int j)
    {
        if (!Contains(i, j))
            return;

        ref WorldTile tile = ref At(i, j);
        if (!tile.IsActive)
            return;

        tile.Flags &= ~WorldTileFlags.Active;
        tile.Shape = 0;
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.TileColor = 0;
        tile.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        tile.Type = 0;
        tile.Flags &= ~WorldTileFlags.Inactive;
        SquareTileFrame(i, j);
    }

    /// <summary>
    /// Source <c>WorldGen.SolidTileAllowBottomSlope</c>. Out of world reads as solid, which is what keeps the
    /// source from destroying an object that reaches the border.
    /// </summary>
    private bool SolidTileAllowBottomSlope(int i, int j)
    {
        if (!Contains(i, j))
            return true;

        WorldTile tile = At(i, j);
        return tile.IsActive &&
            (VanillaTileCollisionCatalog.IsSolid(tile.TileType) ||
             VanillaTileCollisionCatalog.IsSolidTop(tile.TileType)) &&
            tile.Shape is not (2 or 3) &&
            tile.Shape != 1 &&
            !tile.IsActuated;
    }

    /// <summary>Source <c>InvalidTileForPilesOrSpeleothems</c>, which is the boulder set alone.</summary>
    private bool IsBoulder(int i, int j)
    {
        if (!Contains(i, j))
            return false;

        WorldTile tile = At(i, j);
        return tile.IsActive && tile.Type is 138 or 664;
    }

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
