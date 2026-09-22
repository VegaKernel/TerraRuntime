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
/// generation: the bounds guard, the inactive-cell cleanup, the platform frame table, <c>Check3x2</c> for the
/// large pile families (types <c>186</c> and <c>187</c>), <c>CheckJunglePlant</c> for the jungle plant family
/// (types <c>233</c>, <c>236</c>, <c>238</c> and <c>702</c>) and the generation-time part of <c>KillTile</c>.
/// The cosmetic branch is genuinely absent during generation - the source guards it with
/// <c>!generatingWorld</c> - so nothing is dropped there. Frame-important identities outside those families
/// keep their own validators in the source; this slice does not run them, and callers must not use it to frame
/// a square that can hold one.
/// </para>
/// <para>
/// It takes the pass's shared random because destroying an object is not free. <c>KillTile</c> spends draws on
/// the dust each cell makes and a dedicated server does not skip them, so an object this validator removes
/// moves the shared stream by its identity's cost times the number of cells it loses. See
/// <see cref="GenerationKillTileDust1458"/>.
/// </para>
/// </remarks>
internal sealed class GenerationTileFraming1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random)
{
    private const ushort LargePiles = 186;
    private const ushort LargePiles2 = 187;
    private const ushort PlantDetritus = 233;
    private const ushort JungleBulb = 236;
    private const ushort LifeFruit = 238;
    private const ushort JungleBulb702 = 702;
    private const ushort JungleGrass = 60;
    private const ushort LongMoss = 184;
    private const ushort FallenLog = 488;

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

        if (tile.Type is LargePiles or LargePiles2 or FallenLog)
        {
            Check3x2(i, j, tile.Type);
            return;
        }

        if (tile.Type is PlantDetritus or JungleBulb or LifeFruit or JungleBulb702)
        {
            CheckJunglePlant(i, j, tile.Type);
            return;
        }

        if (tile.Type is 3 or 24 or 61 or 71 or 73 or 74 or 110 or 113 or 201 or 637 or 703)
        {
            PlantCheck(i, j);
            return;
        }

        if (tile.Type == LongMoss)
        {
            FrameLongMoss(i, j);
            return;
        }

        if (VanillaTileIds.IsPlatform(tile.TileType))
            FramePlatform(i, j);
    }

    /// <summary>
    /// The platform arm of <c>TileFrameImportant</c>. A platform takes its horizontal frame from what stands
    /// either side of it, so a run of them only looks like a run once each one has been re-framed - which is
    /// why <c>PlaceTile</c> frames the square around every platform it lays rather than the platform alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The table is not symmetric and it is not only about the two side neighbours. A half-brick platform
    /// merges only with another half-brick of its own identity and with nothing else at all; a sloped platform
    /// picks from an entirely separate set of frames that depend on the cell diagonally below it and on whether
    /// the cell above forbids sloping; and a platform whose upper diagonal neighbour slopes toward it counts
    /// that neighbour as a side merge even though nothing is beside it.
    /// </para>
    /// <para>
    /// Two source details are reproduced rather than simplified. A neighbour in <c>Main.tileStone</c> is read
    /// as Stone before anything else looks at it, which matters because some of that set is not solid on its
    /// own and Stone is. And the merge culling exists to hide echo-coated blocks: it compares the invisible-block
    /// flag of each neighbour against this cell's, so during generation, where nothing is coated, it is inert -
    /// but it is implemented rather than assumed, because a coated block reaching a platform would otherwise
    /// frame wrongly.
    /// </para>
    /// <para>
    /// The source's trailing <c>HandleRopeEndFraming</c> is absent. It re-frames the ends of a rope running
    /// through the platform's column, and it is measured inert here: a platform with empty space above it
    /// resolves to an inactive cell that framing does nothing with, and a platform with solid cells above it
    /// finds no rope end within the five-row reach.
    /// </para>
    /// </remarks>
    private void FramePlatform(int i, int j)
    {
        ref WorldTile cell = ref At(i, j);
        ushort type = cell.Type;

        WorldTile left = Neighbour(i - 1, j);
        WorldTile right = Neighbour(i + 1, j);
        WorldTile lowerLeft = Neighbour(i - 1, j + 1);
        WorldTile lowerRight = Neighbour(i + 1, j + 1);
        WorldTile upperLeft = Neighbour(i - 1, j - 1);
        WorldTile upperRight = Neighbour(i + 1, j - 1);

        bool coated = (cell.Flags & WorldTileFlags.InvisibleBlock) != 0;
        bool cullLeft = Coated(left) != coated;
        bool cullRight = Coated(right) != coated;
        bool cullUpperLeft = Coated(upperLeft) != coated;
        bool cullUpperRight = Coated(upperRight) != coated;
        bool cullLowerLeft = Coated(lowerLeft) != coated;
        bool cullLowerRight = Coated(lowerRight) != coated;

        int leftIdentity = SideIdentity(left, type);
        int rightIdentity = SideIdentity(right, type);
        if (rightIdentity >= 0 && cullRight)
            rightIdentity = -1;
        if (leftIdentity >= 0 && cullLeft)
            leftIdentity = -1;
        if (rightIdentity >= 0 && !VanillaTileCollisionCatalog.IsSolid(new TileTypeId(rightIdentity)))
            rightIdentity = -1;
        if (leftIdentity >= 0 && !VanillaTileCollisionCatalog.IsSolid(new TileTypeId(leftIdentity)))
            leftIdentity = -1;

        bool hammered = cell.Shape == 1;
        if (leftIdentity == type && (left.Shape == 1) != hammered)
            leftIdentity = -1;
        if (rightIdentity == type && (right.Shape == 1) != hammered)
            rightIdentity = -1;
        if (leftIdentity != -1 && leftIdentity != type && hammered)
            leftIdentity = -1;
        if (rightIdentity != -1 && rightIdentity != type && hammered)
            rightIdentity = -1;

        // A platform sloping down toward this one from the upper diagonal merges as if it were beside it.
        if (leftIdentity == -1 && upperLeft.IsActive && upperLeft.Type == type && Slope(upperLeft) == 1 &&
            !cullUpperLeft)
            leftIdentity = type;
        if (rightIdentity == -1 && upperRight.IsActive && upperRight.Type == type && Slope(upperRight) == 2 &&
            !cullUpperRight)
            rightIdentity = type;

        // And a side neighbour sloping AWAY takes the other side's merge with it.
        if (leftIdentity == type && Slope(left) == 2 && rightIdentity != type)
            rightIdentity = -1;
        if (rightIdentity == type && Slope(right) == 1 && leftIdentity != type)
            leftIdentity = -1;

        bool forbidsSloping = ForbidsSloping(i, j - 1);
        cell.FrameX = checked((short)(Slope(cell) switch
        {
            1 => SlopedDownRight(type, left, right, lowerRight, upperLeft,
                cullLeft, cullRight, cullUpperLeft, cullLowerRight, forbidsSloping),
            2 => SlopedDownLeft(type, left, right, lowerLeft, upperRight,
                cullLeft, cullRight, cullUpperRight, cullLowerLeft, forbidsSloping),
            _ => Flat(type, left, right, leftIdentity, rightIdentity)
        }));
    }

    /// <summary>The ordinary, unsloped half of the platform table.</summary>
    private static int Flat(ushort type, in WorldTile left, in WorldTile right, int leftIdentity, int rightIdentity)
    {
        if (leftIdentity == type && rightIdentity == type)
        {
            return (Slope(left), Slope(right)) switch
            {
                (2, 1) => 252,
                (2, _) => 216,
                (_, 1) => 234,
                _ => 0
            };
        }

        if (leftIdentity == type && rightIdentity == -1)
            return Slope(left) != 2 ? 18 : 270;
        if (leftIdentity == -1 && rightIdentity == type)
            return Slope(right) != 1 ? 36 : 288;
        if (leftIdentity != type && rightIdentity == type)
            return 54;
        if (leftIdentity == type && rightIdentity != type)
            return 72;
        if (leftIdentity != type && leftIdentity != -1 && rightIdentity == -1)
            return 108;
        return leftIdentity != -1 || rightIdentity == type || rightIdentity == -1 ? 90 : 126;
    }

    /// <summary>
    /// A platform hammered so it descends to the right. It has a run of frames of its own, and the only thing
    /// that takes it out of them is a flat platform to its right or a cell above that refuses to be sloped.
    /// </summary>
    private static int SlopedDownRight(
        ushort type,
        in WorldTile left,
        in WorldTile right,
        in WorldTile lowerRight,
        in WorldTile upperLeft,
        bool cullLeft,
        bool cullRight,
        bool cullUpperLeft,
        bool cullLowerRight,
        bool forbidsSloping)
    {
        _ = type;
        if (VanillaTileIds.IsPlatform(right.TileType) && Slope(right) == 0 && right.Shape != 1 && !cullRight)
            return 468;
        if (forbidsSloping)
            return 468;

        bool supported = (left.IsActive && !cullLeft) ||
            (VanillaTileIds.IsPlatform(upperLeft.TileType) && Slope(upperLeft) == 1 && !cullUpperLeft);
        bool openBelow = (!lowerRight.IsActive || cullLowerRight) &&
            (!VanillaTileIds.IsPlatform(lowerRight.TileType) || Slope(lowerRight) == 2 || cullLowerRight);
        return openBelow ? (supported ? 360 : 432) : (supported ? 180 : 396);
    }

    /// <summary>The mirror of <see cref="SlopedDownRight"/>, for a platform that descends to the left.</summary>
    private static int SlopedDownLeft(
        ushort type,
        in WorldTile left,
        in WorldTile right,
        in WorldTile lowerLeft,
        in WorldTile upperRight,
        bool cullLeft,
        bool cullRight,
        bool cullUpperRight,
        bool cullLowerLeft,
        bool forbidsSloping)
    {
        _ = type;
        if (VanillaTileIds.IsPlatform(left.TileType) && Slope(left) == 0 && left.Shape != 1 && !cullLeft)
            return 450;
        if (forbidsSloping)
            return 450;

        bool supported = (right.IsActive && !cullRight) ||
            (VanillaTileIds.IsPlatform(upperRight.TileType) && Slope(upperRight) == 2 && !cullUpperRight);
        bool openBelow = (!lowerLeft.IsActive || cullLowerLeft) &&
            (!VanillaTileIds.IsPlatform(lowerLeft.TileType) || Slope(lowerLeft) == 1 || cullLowerLeft);
        return openBelow ? (supported ? 342 : 414) : (supported ? 144 : 378);
    }

    /// <summary>
    /// What the platform frame table sees to one side before the culling and half-brick rules narrow it: a
    /// stone-merging neighbour reads as Stone, another platform reads as this platform's own identity, and
    /// anything else reads as itself.
    /// </summary>
    private static int SideIdentity(in WorldTile neighbour, ushort platform)
    {
        if (!neighbour.IsActive)
            return -1;
        if (IsStoneMerging(neighbour.Type))
            return 1;
        return VanillaTileIds.IsPlatform(neighbour.TileType) ? platform : neighbour.Type;
    }

    /// <summary>Source <c>Main.tileStone</c>: the gems, their sparkling variants and Stone itself.</summary>
    private static bool IsStoneMerging(ushort type) =>
        type is 63 or 64 or 65 or 66 or 67 or 68 or 130 or 131 or 566;

    /// <summary>Source <c>WorldGen.ForbidsSloping</c>, read on the cell ABOVE the platform.</summary>
    private bool ForbidsSloping(int x, int y) => Neighbour(x, y).Type
        is 21 or 26 or 77 or 88 or 235 or 237 or 441 or 467 or 468 or 470 or 475 or 488 or 597;

    /// <summary>Vanilla's slope number, which <see cref="WorldTile.Shape"/> stores offset by one.</summary>
    private static int Slope(in WorldTile tile) => tile.Shape >= 2 ? tile.Shape - 1 : 0;

    /// <summary>Source <c>Tile.invisibleBlock</c>, which is what the merge culling compares.</summary>
    private static bool Coated(in WorldTile tile) => (tile.Flags & WorldTileFlags.InvisibleBlock) != 0;

    /// <summary>
    /// A neighbour outside the world reads as an empty cell. The source would read a null tile and abandon the
    /// framing entirely, which no generated platform reaches because framing never runs on the map's edge.
    /// </summary>
    private WorldTile Neighbour(int x, int y) => Contains(x, y) ? At(x, y) : default;

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

            // The fallen log has its own ground list and no boulder rule at all: it grows on the grasses, on
            // snow, on sand, on jungle grass and on mud, and on nothing else.
            if (type == FallenLog)
            {
                if (TypeAt(x, floor) is not (2 or 477 or 109 or 492 or 147 or 53 or 60 or 70))
                    broken = true;
                continue;
            }

            if (!SolidTileAllowBottomSlope(x, floor) || IsBoulder(x, floor))
            {
                broken = true;
                continue;
            }

            // The source keys a further ground requirement off the style, and only reaches it when the floor
            // cell is active: a pile cut for one substrate is destroyed when it no longer stands on it.
            if (!Contains(x, floor) || !At(x, floor).IsActive)
                continue;

            if (!StyleGroundMatches(type, style, At(x, floor).Type))
                broken = true;
        }

        // The grass pile's downgrade. The three Large Piles 2 styles cut for grass - frames 756 through 900 -
        // become the plain dirt pile when no column of the object stands on grass, hallowed grass or the
        // shimmered form of it. The source runs this whether or not the object is sound, and it runs BEFORE the
        // destruction below, so a broken grass pile is downgraded and then no longer matches the identity the
        // destruction loop is looking for. It survives as a dirt pile - but the re-framing that follows the
        // destruction still runs over it, which is how a downgraded pile gets validated against its new style.
        if (type == LargePiles2 && Contains(left, top) && Contains(left + 2, floor) &&
            At(left, top).FrameX is >= 756 and <= 900 &&
            !IsGrassFloor(left, floor) && !IsGrassFloor(left + 1, floor) && !IsGrassFloor(left + 2, floor))
        {
            for (int x = left; x < left + 3; x++)
            {
                for (int y = top; y < floor; y++)
                {
                    ref WorldTile cell = ref At(x, y);
                    cell.FrameX -= 378;
                    cell.Type = LargePiles;
                }
            }
        }

        // A fallen log is never destroyed while a world is being generated. The source puts it back - every
        // cell of its own footprint - and forces grass under all three columns, which is why a log placed on
        // stone ends up standing on a strip of grass it made itself.
        if (broken && type == FallenLog)
        {
            for (int x = left; x < left + 3; x++)
            {
                for (int y = top; y < floor; y++)
                {
                    ref WorldTile cell = ref At(x, y);
                    cell.Flags |= WorldTileFlags.Active;
                    cell.Type = FallenLog;
                    cell.FrameX = checked((short)((x - left) * 18));
                    cell.FrameY = checked((short)((y - top) * 18));
                }

                ref WorldTile ground = ref At(x, floor);
                ground.Flags |= WorldTileFlags.Active;
                ground.Type = 2;
                ground.Shape = 0;
            }

            return;
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
    /// The style groups whose ground the source constrains beyond "solid and not a boulder". Large Piles 2
    /// wants mud, jungle grass or mushroom grass under styles zero to five, the ash and hellstone family under
    /// six to eight, and the sand, hardened sand and sandstone conversion sets under twenty-nine to thirty-four;
    /// Large Piles wants snow or ice under twenty-six to thirty-one and mud under thirty-two to thirty-four.
    /// Everything else, including the living-tree canopy's styles forty-seven to fifty-one, carries no further
    /// requirement.
    /// </summary>
    private static bool StyleGroundMatches(ushort type, int style, ushort floor) => type switch
    {
        LargePiles => style switch
        {
            >= 26 and <= 31 => floor is 147 or 161 or 163 or 164 or 200 or 162 or 224,
            >= 32 and <= 34 => floor is 59 or 70,
            _ => true,
        },
        LargePiles2 => style switch
        {
            >= 0 and <= 5 => floor is 59 or 60 or 226,
            >= 6 and <= 8 => floor is 57 or 58 or 75 or 76,
            >= 29 and <= 34 => floor is 53 or 112 or 116 or 234 or 397 or 398 or 402 or 399 or 396 or 400
                or 403 or 401,
            _ => true,
        },
        _ => true,
    };

    /// <summary>The three grasses the grass pile is allowed to stand on. The source reads the raw identity.</summary>
    private bool IsGrassFloor(int x, int y) => TypeAt(x, y) is 2 or 477 or 492;

    /// <summary>
    /// Source <c>WorldGen.PlantCheck</c>. A plant belongs to the ground it stands on, and this is what enforces
    /// that: a plant whose support no longer matches its identity is either RETYPED to the plant that support
    /// does grow - corrupt grass gives corrupt plants, jungle grass gives jungle plants - or, when no identity
    /// fits, destroyed outright. The support has to be active, unsloped, not a half brick and not actuated;
    /// anything else reads as no support at all, which is a bad match for every plant and therefore fatal.
    /// </summary>
    /// <remarks>
    /// This is the reason placing a plant is not a local act. <c>PlaceTile</c> ends every successful placement
    /// with <c>SquareTileFrame</c>, so a plant put down next to a plant detritus that is standing on the wrong
    /// ground takes that whole object with it. Leaving this out does not just leave stale plants behind - it
    /// changes which cells the next patch finds occupied, and with them the shared RNG.
    /// </remarks>
    private void PlantCheck(int x, int y)
    {
        if (destroyingObject)
            return;

        x = Math.Clamp(x, 1, width - 2);
        y = Math.Clamp(y, 1, height - 2);

        ushort type = At(x, y).Type;
        int down = -1;
        if (y + 1 >= height)
        {
            down = type;
        }
        else
        {
            WorldTile below = At(x, y + 1);
            if (below.IsActive && !below.IsActuated && below.Shape == 0)
                down = below.Type;
        }

        // The second jungle bulb is the one identity that wants a solid support rather than a named one.
        if (type == JungleBulb702)
        {
            if (!SolidTileAllowBottomSlope(x, y + 1))
            {
                destroyingObject = true;
                KillTile(x, y);
                destroyingObject = false;
            }

            return;
        }

        if (!IsBadPlantMatch(down, type))
            return;

        short frameX = At(x, y).FrameX;
        int replacement = type;
        bool mushroomOrSpore = TryGetNewPlantType(down, ref replacement, ref frameX);
        if (replacement == type)
        {
            destroyingObject = true;
            KillTile(x, y);
            destroyingObject = false;
            return;
        }

        ref WorldTile cell = ref At(x, y);
        cell.Type = (ushort)replacement;
        cell.FrameX = frameX;
        if (mushroomOrSpore)
            cell.FrameX = replacement == 201 ? (short)270 : (short)144;
    }

    /// <summary>
    /// Source <c>WorldGen.PlantCheck_IsBadTypeMatch</c>: each plant identity names the supports it accepts, and
    /// the sea oat is the one whose test is written the other way round.
    /// </summary>
    private static bool IsBadPlantMatch(int down, ushort type)
    {
        bool matches =
            (type != 3 || down is 2 or 477 or 78 or 380 or 579) &&
            (type != 73 || down is 2 or 477 or 78 or 380 or 579) &&
            (type != 24 || down is 23 or 661) &&
            (type != 61 || down is 60 or 226) &&
            (type != 74 || down is 60 or 226) &&
            (type != 71 || down == 70) &&
            (type != 110 || down is 109 or 492) &&
            (type != 113 || down is 109 or 492) &&
            (type != 201 || down is 199 or 662);
        if (!matches)
            return true;

        return type == 637 && down != 633;
    }

    /// <summary>
    /// Source <c>WorldGen.PlantCheck_TryGetNewType</c>. The support picks the identity; the frame is carried
    /// across and folded back into the range the new identity has, and a mushroom or spore keeps its own frame
    /// rather than the one it carried. Leaving the identity unchanged is what tells the caller to destroy it.
    /// </summary>
    private static bool TryGetNewPlantType(int down, ref int type, ref short frameX)
    {
        bool mushroomOrSpore = false;
        if (type is 3 or 61 or 110 or 24)
            mushroomOrSpore = frameX == 144;
        if (type == 201)
            mushroomOrSpore = frameX == 270;
        if ((type is 3 or 73) && down != 2 && down != 477 && frameX >= 162)
            frameX = 126;
        if (type == 74 && down != 60 && down != 226 && frameX >= 162)
            frameX = 126;

        switch (down)
        {
            case 23:
            case 661:
                type = 24;
                if (frameX >= 162)
                    frameX = 126;
                break;
            case 199:
            case 662:
                type = 201;
                break;
            case 2:
            case 477:
                type = type == 113 ? 73 : 3;
                break;
            case 109:
            case 492:
                type = type == 73 ? 113 : 110;
                break;
            case 60:
            case 226:
                type = 61;
                while (frameX > 126)
                    frameX -= 126;
                break;
            case 70:
                type = 71;
                while (frameX > 72)
                    frameX -= 72;
                break;
        }

        return mushroomOrSpore;
    }

    /// <summary>
    /// Source <c>WorldGen.CheckJunglePlant</c>. The jungle plant family has two footprints and the cell's own
    /// frame says which one it is in: a frame row of thirty-six or more - or any of the three bulb identities -
    /// means the two-by-two form, anything else the three-by-two form. Either way the object's origin is
    /// re-derived from the frame, every cell of the footprint must carry exactly the frame that origin implies,
    /// and every column must stand on solid jungle grass; when any of that fails the whole object is destroyed.
    /// </summary>
    /// <remarks>
    /// The three-by-two destroy loop reaches one row FURTHER DOWN than the footprint it validated, so a
    /// same-type cell sitting in the support row goes with the object. The two-by-two loop does not. The item
    /// and NPC arms of the source - a Life Fruit summoning Plantera, a bulb dropping its seed - are absent
    /// because generation never reaches them: the only generation-time caller is a plant being overwritten, and
    /// the source suppresses drops for the whole of world generation.
    /// </remarks>
    private void CheckJunglePlant(int i, int j, ushort type)
    {
        if (destroyingObject)
            return;

        if (At(i, j).FrameY >= 36 || type is JungleBulb or LifeFruit or JungleBulb702)
        {
            CheckJunglePlantSquare(i, j, type);
            return;
        }

        CheckJunglePlantWide(i, j, type);
    }

    /// <summary>The two-by-two form: the bulbs frame from row zero, plant detritus from row thirty-six.</summary>
    private void CheckJunglePlantSquare(int i, int j, ushort type)
    {
        bool broken = false;
        int column = At(i, j).FrameX / 18;
        int style = 0;
        while (column > 1)
        {
            column -= 2;
            style++;
        }

        int left = i - column;
        int topRow = type is JungleBulb or LifeFruit or JungleBulb702 ? 0 : 36;

        int row = At(i, j).FrameY / 18;
        while (row > 1)
            row -= 2;
        int top = j - row;
        int styleFrame = style * 36;

        for (int x = left; x < left + 2; x++)
        {
            for (int y = top; y < top + 2; y++)
            {
                if (!Contains(x, y))
                {
                    broken = true;
                    continue;
                }

                WorldTile cell = At(x, y);
                if (!cell.IsActive || cell.Type != type ||
                    cell.FrameX != (x - left) * 18 + styleFrame ||
                    cell.FrameY != (y - top) * 18 + topRow)
                {
                    broken = true;
                }
            }

            // Only the second jungle bulb identity accepts a bottom slope under it; the rest want plain solid
            // jungle grass.
            if (type == JungleBulb702)
            {
                if (!SolidTileAllowBottomSlope(x, top + 2))
                    broken = true;
            }
            else if (!SolidTile(x, top + 2) || TypeAt(x, top + 2) != JungleGrass)
            {
                broken = true;
            }
        }

        if (!broken)
            return;

        destroyingObject = true;
        for (int x = left; x < left + 2; x++)
        {
            for (int y = top; y < top + 2; y++)
            {
                if (Contains(x, y) && At(x, y).IsActive && At(x, y).Type == type)
                    KillTile(x, y);
            }
        }

        destroyingObject = false;
    }

    /// <summary>The three-by-two form, whose destroy loop also reaches the support row.</summary>
    private void CheckJunglePlantWide(int i, int j, ushort type)
    {
        bool broken = false;
        int top = j - At(i, j).FrameY / 18;

        int column = At(i, j).FrameX / 18;
        int style = 0;
        while (column > 2)
        {
            column -= 3;
            style++;
        }

        int left = i - column;
        int styleFrame = style * 54;

        for (int x = left; x < left + 3; x++)
        {
            for (int y = top; y < top + 2; y++)
            {
                if (!Contains(x, y))
                {
                    broken = true;
                    continue;
                }

                WorldTile cell = At(x, y);
                if (!cell.IsActive || cell.Type != type ||
                    cell.FrameX != (x - left) * 18 + styleFrame ||
                    cell.FrameY != (y - top) * 18)
                {
                    broken = true;
                }
            }

            if (!SolidTile(x, top + 2) || TypeAt(x, top + 2) != JungleGrass)
                broken = true;
        }

        if (!broken)
            return;

        destroyingObject = true;
        for (int x = left; x < left + 3; x++)
        {
            for (int y = top; y < top + 3; y++)
            {
                if (Contains(x, y) && At(x, y).IsActive && At(x, y).Type == type)
                    KillTile(x, y);
            }
        }

        destroyingObject = false;
    }

    /// <summary>
    /// Source <c>WorldGen.KillTile</c> reduced to what generation does to an object it removes: spend the dust
    /// the tile makes, clear the cell, mark its frames unset rather than zero, drop nothing, and re-frame the
    /// square around it.
    /// </summary>
    /// <remarks>
    /// The dust is spent before anything is cleared and it is not free - see
    /// <see cref="GenerationKillTileDust1458"/> - so an object destroyed by a validator moves the shared RNG by
    /// its identity's cost times the number of cells it loses.
    /// </remarks>
    private void KillTile(int i, int j)
    {
        if (!Contains(i, j))
            return;

        ref WorldTile tile = ref At(i, j);
        if (!tile.IsActive)
            return;

        GenerationKillTileDust1458.Consume(random, tile.Type);
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
    /// Source <c>WorldGen.TileFrameImportant</c>'s case for long moss, which is where the Moss Grass pass
    /// actually decides what a strand looks like and whether it lives at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The draw comes FIRST and unconditionally, before any neighbour has been consulted, so a strand that is
    /// about to be destroyed still costs a value. Then the four neighbours are tried in a fixed order - below,
    /// above, left, right - and the FIRST one carrying a moss colour decides both the strand's colour column
    /// and which band of rows it hangs in. Each band is three rows wide and the drawn value is only written
    /// when the existing row is outside that band, so a strand that is re-framed without changing direction
    /// keeps the row it already had and the draw is spent for nothing.
    /// </para>
    /// <para>
    /// If no neighbour carries a moss colour the strand is KILLED, and that is not free: <c>KillTile</c> spends
    /// dust from the shared stream. Long moss on a lone block therefore costs more than long moss in a field.
    /// </para>
    /// </remarks>
    private void FrameLongMoss(int i, int j)
    {
        WorldTile above = At(i, j - 1);
        WorldTile below = At(i, j + 1);
        WorldTile left = At(i - 1, j);
        WorldTile right = At(i + 1, j);

        // Source reads each neighbour through a different guard: what hangs above must not be bottom-sloped,
        // what a strand stands under must be neither half brick nor top-sloped, and the sides are read bare.
        int aboveType = above.IsActive && above.Shape is not (4 or 5) ? above.Type : -1;
        int belowType = below.IsActive && below.Shape != 1 && below.Shape is not (2 or 3) ? below.Type : -1;
        int leftType = left.IsActive ? left.Type : -1;
        int rightType = right.IsActive ? right.Type : -1;

        short roll = (short)(random.Next(3) * 18);
        ref WorldTile cell = ref At(i, j);
        if (belowType >= 0 && MossColor(belowType) >= 0)
            Hang(ref cell, MossColor(belowType), 0, roll);
        else if (aboveType >= 0 && MossColor(aboveType) >= 0)
            Hang(ref cell, MossColor(aboveType), 54, roll);
        else if (leftType >= 0 && MossColor(leftType) >= 0)
            Hang(ref cell, MossColor(leftType), 108, roll);
        else if (rightType >= 0 && MossColor(rightType) >= 0)
            Hang(ref cell, MossColor(rightType), 162, roll);
        else
            KillTile(i, j);
    }

    /// <summary>The colour column is 22 pixels wide, not the ordinary 18.</summary>
    private static void Hang(ref WorldTile cell, int color, short band, short roll)
    {
        cell.FrameX = (short)(22 * color);
        if (cell.FrameY < band || cell.FrameY > band + 36)
            cell.FrameY = (short)(band + roll);
    }

    /// <summary>Source <c>WorldGen.GetTileMossColor</c>: a moss block and its brick share one colour.</summary>
    private static int MossColor(int type) => type switch
    {
        179 or 512 => 0,
        180 or 513 => 1,
        181 or 514 => 2,
        182 or 515 => 3,
        183 or 516 => 4,
        381 or 517 => 5,
        534 or 535 => 6,
        536 or 537 => 7,
        539 or 540 => 8,
        625 or 626 => 9,
        627 or 628 => 10,
        _ => -1
    };

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

    /// <summary>
    /// Source <c>WorldGen.SolidTile</c>: solid, NOT solid-top, unsloped, not a half brick and not actuated. Out
    /// of world reads as solid, which is how the source treats a null tile.
    /// </summary>
    private bool SolidTile(int i, int j)
    {
        if (!Contains(i, j))
            return true;

        WorldTile tile = At(i, j);
        return tile.IsActive && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) && tile.Shape == 0 && !tile.IsActuated;
    }

    private ushort TypeAt(int i, int j) => Contains(i, j) ? At(i, j).Type : (ushort)0;

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
