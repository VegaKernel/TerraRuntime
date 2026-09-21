using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.QuickCleanup</c>.
/// </summary>
/// <remarks>
/// <para>
/// One whole-map scan doing five unrelated things to every cell, in this order: the ocean's liquid is forced
/// to water, sand at the ocean surface grows a column of dirt under it, the hive and temple walls retype what
/// stands in them and re-decide its liquid by depth, sand overhanging open space borrows a wall from around
/// it, and every sloped or half-brick cell is either flattened or dropped depending on what holds it up.
/// </para>
/// <para>
/// Only the sand column spends shared RNG, and it spends it in an unusual place: the draw that decides how
/// long the column may be sits inside the loop's own CONDITION, so it is redrawn on every step and again on
/// the step that ends the loop. The column's length is therefore not one sample but a race between a fresh
/// sample each step and the step counter, and the number of values consumed depends on how far the column got
/// - which is the same shape as the pyramid tunnel that draws per column per step.
/// </para>
/// <para>
/// The pass also makes two of its own rules temporarily untrue. It sets traps and active stone blocks
/// non-solid for the duration, which changes what the support tests below see - a sloped block standing on a
/// trap is dropped - and puts them back afterwards. What it cannot change is <c>TileID.Sets.SaveSlopes</c>:
/// that set is built once during content setup from the ORIGINAL solidity, so a trap still counts as an
/// identity that keeps its slope even while the pass is treating it as air.
/// </para>
/// <para>
/// Three smaller details are worth naming because they are easy to model wrongly. The trap test beside a
/// sloped cell reads the neighbour's identity without asking whether the neighbour is still there, so a cell
/// that some earlier pass deactivated but left typed as a trap still drops its neighbours. The wall the sand
/// borrows is not the nearest one: the search breaks out of its inner loop only, so the answer is the first
/// wall found in the LAST column that has one. And the hive's liquid rule splits on depth rather than on
/// amount - above the rock layer it is emptied, below it is filled to the brim and turned to lava.
/// </para>
/// <para>
/// The ordinary-world path is what is ported. <c>notTheBees</c>, <c>dualDungeonsSeed</c> and the round
/// landmasses seed each remove or redirect part of this pass, and none of them is set in an ordinary
/// generation.
/// </para>
/// </remarks>
internal sealed class QuickCleanupPass1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    double rockLayer,
    int beachDistance,
    CancellationToken cancellation)
{
    private const ushort Dirt = 0;
    private const ushort Sand = 53;
    private const ushort Mud = 59;
    private const ushort Sandstone = 397;
    private const ushort Trap = 137;
    private const ushort ActiveStoneBlock = 130;
    private const ushort Pearlstone = 225;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;
    private readonly double oceanLevel = (worldSurface + rockLayer) / 2.0 + 40.0;

    /// <summary>How many cells the ocean's liquid normalisation turned back into water.</summary>
    public long Freshened { get; private set; }

    /// <summary>How many cells the ocean's sand columns wrote.</summary>
    public long SandFill { get; private set; }

    /// <summary>How many cells the hive and temple walls retyped.</summary>
    public long Retyped { get; private set; }

    /// <summary>How many sloped or half-brick cells were dropped for want of support.</summary>
    public long Dropped { get; private set; }

    public void Apply()
    {
        for (int x = 20; x < width - 20; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = 20; y < height - 20; y++)
            {
                bool ocean = OceanDepths(x, y);
                if (ocean && At(x, y).LiquidAmount > 0 && At(x, y).LiquidKind != WorldLiquidKind.Water)
                {
                    At(x, y).LiquidKind = WorldLiquidKind.Water;
                    Freshened++;
                }

                if (y < worldSurface && ocean && At(x, y).IsActive && At(x, y).Type == Sand)
                    GrowSandColumn(x, y);

                if (At(x, y).Wall is 187 or 216)
                    Repaper(x, y);

                if (y < worldSurface && At(x, y).IsActive && At(x, y).Type == Sand &&
                    At(x, y + 1).Wall == 0 && !SolidTile(x, y + 1))
                {
                    BorrowWall(x, y);
                }

                Flatten(x, y);
            }
        }
    }

    /// <summary>
    /// Sand at the ocean surface is given a column of dirt under it. The length is drawn fresh on every step
    /// of the loop, including the step that ends it, and the column also stops as soon as it would reach
    /// sandstone or more sand - looking one and two rows further down as well as at the row it is about to
    /// write.
    /// </summary>
    private void GrowSandColumn(int x, int y)
    {
        ref WorldTile anchor = ref At(x, y);
        if (anchor.Shape is 4 or 5)
            anchor.Shape = 0;

        for (int row = y + 1; row < y + random.Next(4, 7) && RowIsClear(x, row); row++)
        {
            ref WorldTile cell = ref At(x, row);
            cell.Type = Dirt;
            cell.Flags |= WorldTileFlags.Active;
            cell.Shape = 0;
            SandFill++;
        }
    }

    private bool RowIsClear(int x, int row)
    {
        if (Blocked(x, row, 0))
            return false;

        return !Blocked(x, row + 1, 1) && !Blocked(x, row + 2, 1);
    }

    /// <summary>
    /// The row about to be written refuses sandstone and sand; the two rows below it refuse hardened sand as
    /// well, which is the one asymmetry in the three tests.
    /// </summary>
    private bool Blocked(int x, int row, int extra)
    {
        if (!Contains(x, row))
            return false;

        WorldTile cell = At(x, row);
        if (!cell.IsActive)
            return false;

        return cell.Type is Sandstone or Sand || (extra == 1 && cell.Type == 495);
    }

    /// <summary>
    /// Behind a hive or temple wall, mud, gems and the two stone-cave blocks all become sandstone, and the
    /// liquid is decided by depth rather than by what it is: emptied above the rock layer, filled to the brim
    /// and turned to lava below it.
    /// </summary>
    private void Repaper(int x, int y)
    {
        ref WorldTile cell = ref At(x, y);
        if (cell.Type is Mud or 123 or 224)
        {
            cell.Type = Sandstone;
            Retyped++;
        }

        // A separate test in the source, not an else: it reads the identity the first one may have just
        // written, and Sandstone is in neither list, so the two cannot both fire.
        if (cell.Type is 368 or 367)
        {
            cell.Type = Sandstone;
            Retyped++;
        }

        if (y <= rockLayer)
        {
            cell.LiquidAmount = 0;
        }
        else if (cell.LiquidAmount > 0)
        {
            cell.LiquidAmount = byte.MaxValue;
            cell.LiquidKind = WorldLiquidKind.Lava;
        }
    }

    /// <summary>
    /// Sand hanging over open unpapered space papers that space, and itself if it is bare, with a wall taken
    /// from the box around it - the first wall found in the LAST column that has one, because the search only
    /// breaks out of its inner loop.
    /// </summary>
    private void BorrowWall(int x, int y)
    {
        ushort found = 0;
        for (int sx = x - 3; sx <= x + 3; sx++)
        {
            for (int sy = y - 3; sy <= y + 3; sy++)
            {
                if (!Contains(sx, sy) || At(sx, sy).Wall == 0)
                    continue;

                found = At(sx, sy).Wall;
                break;
            }
        }

        if (found == 0)
            return;

        At(x, y + 1).Wall = found;
        if (At(x, y).Wall == 0)
            At(x, y).Wall = found;
    }

    /// <summary>
    /// Anything that cannot keep a slope is flattened outright; anything that can is dropped when the block
    /// that should hold its cut face is missing, or when a trap stands beside it.
    /// </summary>
    private void Flatten(int x, int y)
    {
        ref WorldTile cell = ref At(x, y);
        if (!cell.IsActive || !SaveSlopes(cell.Type))
        {
            cell.Shape = 0;
            return;
        }

        if (VanillaTileIds.IsPlatform(cell.TileType) ||
            !WorldSmoothingCatalog1458.CanBeClearedDuringGeneration(cell.TileType))
        {
            return;
        }

        bool topCut = cell.Shape is 2 or 3 || cell.Shape == 1;
        bool bottomCut = cell.Shape is 4 or 5;
        if (topCut)
        {
            // Pearlstone keeps its half brick; every other identity, and Pearlstone's own slopes, do not.
            if (cell.Type == Pearlstone && cell.Shape == 1)
                return;

            if (!SolidTile(x, y + 1) || TrapBeside(x, y))
            {
                cell.Flags &= ~WorldTileFlags.Active;
                Dropped++;
            }

            return;
        }

        if (!bottomCut)
            return;

        if (!SolidTile(x, y - 1) || TrapBeside(x, y))
        {
            cell.Flags &= ~WorldTileFlags.Active;
            Dropped++;
        }
    }

    /// <summary>
    /// The source reads the neighbour's identity alone, so a cell an earlier pass deactivated without
    /// clearing its type still counts as a trap here.
    /// </summary>
    private bool TrapBeside(int x, int y) =>
        (Contains(x + 1, y) && At(x + 1, y).Type == Trap) ||
        (Contains(x - 1, y) && At(x - 1, y).Type == Trap);

    /// <summary>
    /// Source <c>TileID.Sets.SaveSlopes</c>, which <c>PostSetupContent</c> builds once from the original tile
    /// solidity plus the non-solid identities that keep a slope anyway. It is NOT affected by the solidity
    /// this pass turns off for its own duration.
    /// </summary>
    private static bool SaveSlopes(ushort type) =>
        VanillaTileCollisionCatalog.IsSolid(new TileTypeId(type)) ||
        type is 131 or 351 or 336 or 340 or 342 or 341 or 343 or 344;

    /// <summary>
    /// Source <c>WorldGen.SolidTile</c> under this pass's own temporary solidity override: a trap and an
    /// active stone block read as air for as long as the pass runs.
    /// </summary>
    private bool SolidTile(int x, int y)
    {
        if (!Contains(x, y))
            return true;

        WorldTile tile = At(x, y);
        if (!tile.IsActive || tile.Type is Trap or ActiveStoneBlock)
            return false;

        return VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) && tile.Shape == 0 && !tile.IsActuated;
    }

    /// <summary>Source <c>WorldGen.oceanDepths</c>: above the ocean's floor line and outside the beaches.</summary>
    private bool OceanDepths(int x, int y) =>
        y <= oceanLevel && (x < beachDistance || x > width - beachDistance);

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
