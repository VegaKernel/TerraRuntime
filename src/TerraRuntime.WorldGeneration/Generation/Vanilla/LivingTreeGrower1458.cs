using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>WorldGen.GrowLivingTree</c>, the trunk, roots, branches and canopy of
/// a living tree.
/// </summary>
/// <remarks>
/// The shape is not a tapered column. The trunk is grown upward from a randomly widened base and narrows by
/// alternating sides at random intervals, recording each narrowing as a branch anchor; the branches then grow
/// outward from those anchors with their own wobble and forks; a crown grows from the trunk top with its own
/// side limbs; roots grow downward and outward from the base; and finally every recorded anchor becomes a
/// canopy blob, either a circle for a crown anchor or a diamond for a branch anchor. That is why the runtime's
/// previous tapered column read as nothing like the original.
/// </remarks>
internal sealed class LivingTreeGrower1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    int underworldTop,
    CancellationToken cancellation)
{
    private const ushort LivingWood = 191;
    private const ushort LeafBlock = 192;
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort Sandstone = 40;
    private const ushort LivingWoodWall = 244;
    private const ushort PlantDetritus = 187;

    /// <summary>The source's fixed anchor capacities; overflowing either is a refusal, not a silent clamp.</summary>
    private const int MaximumBranchAnchors1458 = 1000;
    private const int MaximumCanopyAnchors1458 = 2000;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    /// <summary>
    /// Grows one living tree. <paramref name="patch"/> is the source's side-tree mode: a narrower base and a
    /// much smaller clearance box, used for the companions grown either side of a main tree.
    /// </summary>
    public bool TryGrow(int i, int j, bool patch = false)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Contains(i, j) || !Contains(i, j + 1))
            return false;
        if (!IsSolid(i, j + 1) || At(i, j).IsActive)
            return false;

        // The base must be ordinary ground. Every ore counts; living wood and leaves never do.
        ushort ground = At(i, j + 1).Type;
        if (ground != Dirt && ground != Grass && ground != Stone && ground != Sandstone && !IsOre(ground))
            return false;

        if (j < 150)
            return false;

        int left = i - random.Next(2, 3);
        int right = i + random.Next(2, 3);
        if (random.Next(5) == 0)
        {
            if (random.Next(2) == 0)
                left--;
            else
                right++;
        }

        int trunkWidth = right - left;
        bool wantsPassage = trunkWidth >= 4;
        int clearLeft = i - 50;
        int clearRight = i + 50;
        if (patch)
        {
            clearLeft = i - 20;
            clearRight = i + 20;
            left = i - random.Next(1, 3);
            right = i + random.Next(1, 3);
            // The source re-tests the OLD width here: trunkWidth is not recomputed after a patch narrows the
            // base, so a patch tree inherits the main tree's passage decision.
            wantsPassage = trunkWidth >= 4;
        }

        // Nothing may already stand in the column the trunk and canopy will occupy. A patch tolerates the
        // materials a neighbouring living tree already placed.
        for (int column = clearLeft; column <= clearRight; column++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int row = 5; row < j - 5; row++)
            {
                if (!Contains(column, row) || !At(column, row).IsActive)
                    continue;
                if (!patch)
                    return false;

                ushort type = At(column, row).Type;
                if (type != Grass && type != Dirt && type != Stone && type != LivingWood &&
                    type != LeafBlock && type != 383 && type != 384)
                {
                    return false;
                }
            }
        }

        var branchAnchorX = new int[MaximumBranchAnchors1458];
        var branchAnchorY = new int[MaximumBranchAnchors1458];
        var branchDirection = new int[MaximumBranchAnchors1458];
        var branchLength = new int[MaximumBranchAnchors1458];
        var canopyX = new int[MaximumCanopyAnchors1458];
        var canopyY = new int[MaximumCanopyAnchors1458];
        var canopyRound = new bool[MaximumCanopyAnchors1458];
        int branches = 0;
        int canopies = 0;

        int baseLeft = left;
        int baseRight = right;
        int trunkTop = GrowTrunk(
            ref left, ref right, j, branchAnchorX, branchAnchorY, branchDirection, branchLength, ref branches);

        GrowBranches(
            branchAnchorX, branchAnchorY, branchDirection, branchLength, branches,
            canopyX, canopyY, canopyRound, ref canopies);
        GrowCrown((left + right) / 2, trunkTop, trunkWidth, canopyX, canopyY, canopyRound, ref canopies);
        GrowRoots(baseLeft, baseRight, j, trunkWidth);
        GrowCanopy(canopyX, canopyY, canopyRound, canopies, trunkWidth);

        // A wide trunk drives a shaft into the ground, but only when the twenty rows below it are solid and
        // unwalled: the source abandons the passage the moment it sees open or already-claimed ground there.
        if (wantsPassage)
        {
            bool obstructed = false;
            for (int row = j; row < j + 20 && !(row >= worldSurface - 2.0) && !obstructed; row++)
            {
                for (int column = baseLeft; column <= baseRight; column++)
                {
                    if (Contains(column, row) && At(column, row).Wall == 0 && !IsSolid(column, row))
                    {
                        obstructed = true;
                        break;
                    }
                }
            }

            if (!obstructed)
            {
                var passage = new LivingTreePassage1458(
                    store, random, worldSurface, underworldTop, cancellation);
                int shaftLeft = baseLeft;
                int shaftRight = baseRight;
                passage.MakePassage(j, trunkWidth, ref shaftLeft, ref shaftRight, patch);
                ChestRequest = passage.ChestRequest;
            }
        }

        return true;
    }

    /// <summary>The secret room's chest, when the last grown tree opened one. The caller owns the loot.</summary>
    public (int X, int Y, int SignatureItem)? ChestRequest { get; private set; }

    /// <summary>
    /// Grows the trunk upward. Every few rows one side steps inward, and that step is recorded as a branch
    /// anchor with the trunk's width at the time, which is what makes lower branches longer than upper ones.
    /// The trunk stops when the two sides meet.
    /// </summary>
    private int GrowTrunk(
        ref int left,
        ref int right,
        int j,
        int[] anchorX,
        int[] anchorY,
        int[] direction,
        int[] length,
        ref int count)
    {
        int narrowingLeft = left;
        int narrowingRight = right;
        int row = j;
        bool growing = true;
        int sinceBranch = random.Next(-8, -4);
        int side = random.Next(2);
        int interval = random.Next(5, 15);

        while (growing && row > 5)
        {
            cancellation.ThrowIfCancellationRequested();
            sinceBranch++;
            if (sinceBranch > interval)
            {
                interval = random.Next(5, 15);
                sinceBranch = 0;
                if (count >= MaximumBranchAnchors1458 - 1)
                    break;

                anchorY[count] = row + random.Next(5);
                if (random.Next(5) == 0)
                    side = side == 0 ? 1 : 0;

                if (side == 0)
                {
                    direction[count] = -1;
                    anchorX[count] = left;
                    length[count] = right - left;
                    if (random.Next(2) == 0)
                        left++;
                    narrowingLeft++;
                    side = 1;
                }
                else
                {
                    direction[count] = 1;
                    anchorX[count] = right;
                    length[count] = right - left;
                    if (random.Next(2) == 0)
                        right--;
                    narrowingRight--;
                    side = 0;
                }

                if (narrowingLeft == narrowingRight)
                    growing = false;
                count++;
            }

            for (int column = left; column <= right; column++)
                WriteWood(column, row);

            row--;
        }

        return row;
    }

    /// <summary>
    /// Grows one branch from each recorded anchor except the last. A branch wanders one row in ten instead of
    /// advancing, and every few tiles it thickens by one cell above or below, recording a canopy anchor there.
    /// </summary>
    private void GrowBranches(
        int[] anchorX,
        int[] anchorY,
        int[] direction,
        int[] length,
        int count,
        int[] canopyX,
        int[] canopyY,
        bool[] canopyRound,
        ref int canopies)
    {
        for (int index = 0; index < count - 1; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            int x = anchorX[index] + direction[index];
            int y = anchorY[index];
            int remaining = (int)(length[index] * (1.0 + random.Next(20, 30) * 0.1));
            WriteWood(x, y + 1);

            int untilFork = random.Next(3, 5);
            while (remaining > 0)
            {
                remaining--;
                WriteWood(x, y);
                if (random.Next(10) == 0)
                    y = random.Next(2) != 0 ? y + 1 : y - 1;
                else
                    x += direction[index];

                if (untilFork > 0)
                {
                    untilFork--;
                }
                else if (random.Next(2) == 0)
                {
                    untilFork = random.Next(2, 5);

                    // The source tests the dungeon wall BEFORE it draws the side, so a branch crossing one
                    // thickens nowhere, records no canopy anchor and consumes no value.
                    if (!IsDungeonWall(WallAt(x, y)))
                    {
                        if (random.Next(2) == 0)
                        {
                            WriteWood(x, y);
                            WriteWood(x, y - 1);
                        }
                        else
                        {
                            WriteWood(x, y);
                            WriteWood(x, y + 1);
                        }

                        if (!TryAddCanopy(canopyX, canopyY, canopyRound, ref canopies, x, y, isRound: false))
                            return;
                    }
                }

                if (remaining == 0 && !TryAddCanopy(canopyX, canopyY, canopyRound, ref canopies, x, y, isRound: false))
                    return;
            }
        }
    }

    /// <summary>
    /// Grows the crown from the trunk top: a wobbling vertical stem with side limbs, each limb recording a round
    /// canopy anchor at its tip and sometimes a forked pair partway along.
    /// </summary>
    private void GrowCrown(
        int centerX,
        int trunkTop,
        int trunkWidth,
        int[] canopyX,
        int[] canopyY,
        bool[] canopyRound,
        ref int canopies)
    {
        int x = centerX;
        int y = trunkTop;
        int remaining = random.Next(trunkWidth * 3, trunkWidth * 5);
        int leftCooldown = 0;
        int rightCooldown = 0;

        while (remaining > 0 && y >= 30)
        {
            cancellation.ThrowIfCancellationRequested();
            WriteWood(x, y);
            if (leftCooldown > 0)
                leftCooldown--;
            if (rightCooldown > 0)
                rightCooldown--;

            for (int side = -1; side < 2; side++)
            {
                // The source's guard is (num25 >= 0 || leftCooldown != 0) && (num25 <= 0 || rightCooldown != 0):
                // for a left limb it is the LEFT cooldown that must have expired, and the draw only happens
                // once that is true. Inverting either comparison silently shifts the whole crown sideways.
                if (side == 0 ||
                    ((side >= 0 || leftCooldown != 0) && (side <= 0 || rightCooldown != 0)) ||
                    random.Next(2) != 0)
                {
                    continue;
                }

                int limbX = x;
                int limbY = y;
                int limbLength = random.Next(trunkWidth, trunkWidth * 3);
                if (side < 0)
                    leftCooldown = random.Next(3, 5);
                if (side > 0)
                    rightCooldown = random.Next(3, 5);

                int untilFork = 0;
                while (limbLength > 0)
                {
                    limbLength--;
                    limbX += side;
                    WriteWood(limbX, limbY);
                    if (limbLength == 0 &&
                        !TryAddCanopy(canopyX, canopyY, canopyRound, ref canopies, limbX, limbY, isRound: true))
                    {
                        return;
                    }

                    if (random.Next(5) == 0)
                    {
                        limbY = random.Next(2) != 0 ? limbY + 1 : limbY - 1;
                        WriteWood(limbX, limbY);
                    }

                    if (untilFork > 0)
                    {
                        untilFork--;
                        continue;
                    }

                    if (random.Next(3) != 0)
                        continue;

                    untilFork = random.Next(2, 4);
                    int forkX = limbX;
                    int forkY = random.Next(2) != 0 ? limbY + 1 : limbY - 1;
                    WriteWood(forkX, forkY);
                    if (!TryAddCanopy(canopyX, canopyY, canopyRound, ref canopies, forkX, forkY, isRound: true))
                        return;
                    if (!TryAddCanopy(
                            canopyX,
                            canopyY,
                            canopyRound,
                            ref canopies,
                            forkX + random.Next(-5, 6),
                            forkY + random.Next(-5, 6),
                            isRound: true))
                    {
                        return;
                    }
                }
            }

            if (!TryAddCanopy(canopyX, canopyY, canopyRound, ref canopies, x, y, isRound: false))
                return;

            if (random.Next(4) == 0)
            {
                x = random.Next(2) != 0 ? x + 1 : x - 1;
                WriteWood(x, y);
            }

            y--;
            remaining--;
        }
    }

    /// <summary>
    /// Grows the roots. Each base column first drives a short plug through one to five solid cells, then grows
    /// two to <c>trunkWidth</c> roots outward from the plug's end, each wandering horizontally and vertically
    /// and flattening out whenever it loses the ground under it. Roots never overwrite a living-wood wall.
    /// </summary>
    private void GrowRoots(int baseLeft, int baseRight, int j, int trunkWidth)
    {
        for (int column = baseLeft; column <= baseRight; column++)
        {
            cancellation.ThrowIfCancellationRequested();
            int plug = random.Next(1, 6);
            int row = j + 1;
            while (plug > 0 && row < height - 1)
            {
                if (IsSolid(column, row))
                    plug--;
                ForceWood(column, row);
                row++;
            }

            int plugEnd = row;
            int roots = random.Next(2, trunkWidth + 1);
            for (int root = 0; root < roots; root++)
            {
                cancellation.ThrowIfCancellationRequested();
                row = plugEnd;
                int center = (baseLeft + baseRight) / 2;
                int dx = column >= center ? 1 : -1;
                if (column == center || (trunkWidth > 6 && (column == center - 1 || column == center + 1)))
                    dx = 0;

                int initialDx = dx;
                int dy = 1;
                int x = column;
                int remaining = random.Next((int)(trunkWidth * 3.5), trunkWidth * 6);
                while (remaining > 0)
                {
                    remaining--;
                    x += dx;
                    WriteRoot(x, row);
                    row += dy;
                    WriteRoot(x, row);
                    if (Contains(x, row + 1) && !At(x, row + 1).IsActive)
                    {
                        dx = 0;
                        dy = 1;
                    }

                    if (random.Next(3) == 0)
                    {
                        dx = initialDx < 0
                            ? (dx == 0 ? -1 : 0)
                            : initialDx <= 0 ? random.Next(-1, 2) : dx == 0 ? 1 : 0;
                    }

                    if (random.Next(3) == 0)
                        dy = dy == 0 ? 1 : 0;
                }
            }
        }
    }

    /// <summary>
    /// Turns every recorded anchor into a leaf blob. A crown anchor gets a circle sized by the trunk width; a
    /// branch anchor gets a diamond whose vertical squash is re-rolled per blob, which is what gives the canopy
    /// its uneven, layered edge instead of a uniform ball.
    /// </summary>
    private void GrowCanopy(int[] canopyX, int[] canopyY, bool[] canopyRound, int count, int trunkWidth)
    {
        for (int index = 0; index < count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            int radius = random.Next(5, 8);
            radius = (int)(radius * (1.0 + trunkWidth * 0.05));
            if (canopyRound[index])
                radius = random.Next(6, 12) + trunkWidth;

            int left = canopyX[index] - radius * 2;
            int right = canopyX[index] + radius * 2;
            int top = canopyY[index] - radius * 2;
            int bottom = canopyY[index] + radius * 2;
            double squash = 2.0 - random.Next(5) * 0.1;

            for (int x = left; x <= right; x++)
            {
                for (int y = top; y <= bottom; y++)
                {
                    if (!CanPlaceLeaves(x, y))
                        continue;

                    if (canopyRound[index])
                    {
                        double dx = canopyX[index] - x;
                        double dy = canopyY[index] - y;
                        if (Math.Sqrt(dx * dx + dy * dy) < radius * 0.9)
                            WriteLeaves(x, y);
                    }
                    else if (Math.Abs(canopyX[index] - x) + Math.Abs(canopyY[index] - y) * squash < radius)
                    {
                        WriteLeaves(x, y);
                    }
                }

                // One column in thirty hangs plant detritus from the underside of the canopy.
                if (random.Next(30) == 0)
                {
                    int row = top;
                    if (Contains(x, row) && x >= 5 && row >= 5 && x < width - 5 && row < height - 5 &&
                        !At(x, row).IsActive)
                    {
                        while (row < bottom && Contains(x, row + 1) && !At(x, row + 1).IsActive)
                            row++;
                        if (Contains(x, row + 1) && At(x, row + 1).Type == LeafBlock)
                        {
                            GenerationDecorationPlacement1458.TryPlaceTile3x2(
                                store, random, x, row, PlantDetritus, random.Next(50, 52));
                        }
                    }
                }

                // A round crown blob never drops ground litter; a branch blob does, one column in fifteen.
                if (canopyRound[index] || random.Next(15) != 0)
                    continue;

                int floor = bottom;
                int limit = floor + 100;
                if (!Contains(x, floor) || At(x, floor).IsActive)
                    continue;

                while (floor < limit && Contains(x, floor + 1) && !At(x, floor + 1).IsActive)
                    floor++;
                if (!Contains(x, floor + 1) || At(x, floor + 1).Type == LeafBlock)
                    continue;

                if (random.Next(2) == 0)
                {
                    GenerationDecorationPlacement1458.TryPlaceTile3x2(
                        store, random, x, floor, PlantDetritus, random.Next(47, 50));
                    continue;
                }

                int pileSize = random.Next(2);
                int pileStyle = pileSize == 1 ? random.Next(59, 62) : 72;
                GenerationDecorationPlacement1458.TryPlaceSmallPile(store, x, floor, pileStyle, pileSize);
            }
        }
    }

    /// <summary>Source <c>GrowLivingTree_CanPlaceLeaves</c>.</summary>
    private bool CanPlaceLeaves(int x, int y)
    {
        if (!Contains(x, y) || x < 5 || y < 5 || x >= width - 5 || y >= height - 5)
            return false;

        WorldTile tile = At(x, y);
        if (tile.Wall == LivingWoodWall || tile.Wall == 78 || IsDungeonWall(tile.Wall))
            return false;
        if (!tile.IsActive)
            return true;
        return tile.Type != LivingWood && !IsCloud(tile.Type);
    }

    /// <summary>
    /// Records a canopy anchor. The source's arrays are fixed at two thousand entries and it simply writes past
    /// them; here an overflow ends the tree instead, which is a refusal to guess rather than a silent clamp.
    /// </summary>
    private static bool TryAddCanopy(int[] canopyX, int[] canopyY, bool[] round, ref int count, int x, int y, bool isRound)
    {
        if (count >= MaximumCanopyAnchors1458)
            return false;

        canopyX[count] = x;
        canopyY[count] = y;
        round[count] = isRound;
        count++;
        return true;
    }

    /// <summary>Writes living wood unless a dungeon wall protects the cell.</summary>
    private void WriteWood(int x, int y)
    {
        if (!Contains(x, y) || IsDungeonWall(At(x, y).Wall))
            return;

        ref WorldTile cell = ref At(x, y);
        cell.Type = LivingWood;
        cell.Flags |= WorldTileFlags.Active;
        if (cell.Shape == 1)
            cell.Shape = 0;
    }

    /// <summary>The root plug ignores dungeon walls; only the living-wood wall stops a spreading root.</summary>
    private void ForceWood(int x, int y)
    {
        if (!Contains(x, y))
            return;

        ref WorldTile cell = ref At(x, y);
        cell.Type = LivingWood;
        cell.Flags |= WorldTileFlags.Active;
        if (cell.Shape == 1)
            cell.Shape = 0;
    }

    private void WriteRoot(int x, int y)
    {
        if (!Contains(x, y) || At(x, y).Wall == LivingWoodWall)
            return;

        ref WorldTile cell = ref At(x, y);
        cell.Type = LivingWood;
        cell.Flags |= WorldTileFlags.Active;
        if (cell.Shape == 1)
            cell.Shape = 0;
    }

    private void WriteLeaves(int x, int y)
    {
        ref WorldTile cell = ref At(x, y);
        cell.Type = LeafBlock;
        cell.Flags |= WorldTileFlags.Active;
        if (cell.Shape == 1)
            cell.Shape = 0;
    }

    private bool IsSolid(int x, int y)
    {
        if (!Contains(x, y))
            return false;

        WorldTile tile = At(x, y);
        return tile.IsActive && !tile.IsActuated && tile.Shape == 0 &&
            VanillaTileCollisionCatalog.IsSolid(tile.TileType);
    }

    /// <summary>Source <c>TileID.Sets.Ore</c>, which is wider than the eight ores a pickaxe cares about.</summary>
    private static bool IsOre(ushort type) =>
        type is 7 or 166 or 6 or 167 or 9 or 168 or 8 or 169 or 22 or 204 or 37 or 58 or 107 or 221 or
            108 or 222 or 111 or 223 or 211;

    private static bool IsDungeonWall(ushort wall) => wall is 7 or 8 or 9 or 94 or 95 or 96 or 97 or 98 or 99;

    /// <summary>Out of world reads as no wall, which is how the source's border cells behave here.</summary>
    private ushort WallAt(int x, int y) => Contains(x, y) ? At(x, y).Wall : (ushort)0;

    private static bool IsCloud(ushort type) => type is 189 or 196 or 460;

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
