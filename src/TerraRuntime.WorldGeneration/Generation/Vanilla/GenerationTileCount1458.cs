using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source <c>WorldGen.countTiles</c> and its <c>nextCount</c> walk, which measures the open space a point sits
/// in and reports what that space is made of.
/// </summary>
/// <remarks>
/// <para>
/// The size is not the only answer. The walk keeps five side counters - mushroom, stone, ice, sand and lava -
/// and its callers read those as closely as the size, so a port that returns only the size is useless.
/// </para>
/// <para>
/// Those counters are also why the traversal order matters. Only an OPEN cell is marked visited; a solid one
/// is examined and counted but never marked, so a block reached from three sides raises its counter three
/// times. And several conditions do not merely refuse a cell but slam the count to its ceiling, ending the
/// whole walk wherever it stands: the world border, a shimmer-bearing cell, the Dungeon-proof wall, and -
/// unless the caller asked for the jungle relaxation - ANY wall at all. That last one means an ordinary count
/// only ever measures unpapered space.
/// </para>
/// </remarks>
internal sealed class GenerationTileCount1458(WorldTileStore store)
{
    private const ushort DungeonProofWall = 244;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;
    private readonly HashSet<(int X, int Y)> counted = [];
    private readonly Stack<(int X, int Y)> pending = new();

    /// <summary>Source <c>WorldGen.maxTileCount</c>, the ceiling this walk stops at. Callers set it.</summary>
    public int MaxTileCount { get; set; }

    public int NumTileCount { get; private set; }

    public int ShroomCount { get; private set; }

    public int LavaCount { get; private set; }

    public int IceCount { get; private set; }

    public int SandCount { get; private set; }

    public int RockCount { get; private set; }

    /// <summary>The open cells the last walk marked, which is what a caller repaints.</summary>
    public IReadOnlyCollection<(int X, int Y)> Counted => counted;

    public int Count(int x, int y, bool jungle = false, bool lavaOk = false)
    {
        NumTileCount = 0;
        ShroomCount = 0;
        LavaCount = 0;
        IceCount = 0;
        SandCount = 0;
        RockCount = 0;
        counted.Clear();
        pending.Clear();
        pending.Push((x, y));

        // Source recurses left, right, up then down. A stack pushed in the reverse of that order pops them in
        // it and expands each fully before reaching the next, which is the same pre-order walk. Once the count
        // reaches its ceiling every remaining entry would return on the source's first line, so draining the
        // stack and breaking out of it are the same thing.
        while (pending.Count > 0 && NumTileCount < MaxTileCount)
        {
            (int cx, int cy) = pending.Pop();
            if (cx <= 1 || cx >= width - 1 || cy <= 1 || cy >= height - 1)
            {
                NumTileCount = MaxTileCount;
                break;
            }

            if (counted.Contains((cx, cy)))
                continue;

            ref WorldTile cell = ref At(cx, cy);
            if (cell.Wall == DungeonProofWall)
            {
                NumTileCount = MaxTileCount;
                break;
            }

            if (cell.LiquidKind == WorldLiquidKind.Shimmer && cell.LiquidAmount > 0)
            {
                NumTileCount = MaxTileCount;
                break;
            }

            if (!jungle)
            {
                if (cell.Wall != 0)
                {
                    NumTileCount = MaxTileCount;
                    break;
                }

                if (cell.LiquidKind == WorldLiquidKind.Lava && cell.LiquidAmount > 0)
                {
                    LavaCount++;
                    if (!lavaOk)
                    {
                        NumTileCount = MaxTileCount;
                        break;
                    }
                }
            }

            // A solid cell still feeds these counters, and still feeds them again the next time it is reached.
            if (cell.IsActive)
            {
                if (cell.Type == 70)
                    ShroomCount++;
                if (cell.Type == 1)
                    RockCount++;
                if (cell.Type is 147 or 161)
                    IceCount++;
                if (cell.Type is 53 or 396 or 397)
                    SandCount++;
            }

            if (IsSolid(cx, cy))
                continue;

            counted.Add((cx, cy));
            NumTileCount++;
            pending.Push((cx, cy + 1));
            pending.Push((cx, cy - 1));
            pending.Push((cx + 1, cy));
            pending.Push((cx - 1, cy));
        }

        return NumTileCount;
    }

    /// <summary>Source <c>WorldGen.SolidTile</c>: a whole, upright, unactuated block that is not a platform.</summary>
    private bool IsSolid(int x, int y)
    {
        WorldTile cell = At(x, y);
        return cell.IsActive && VanillaTileCollisionCatalog.IsSolid(cell.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(cell.TileType) && cell.Shape == 0 && !cell.IsActuated;
    }

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
