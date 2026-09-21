using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.CaveWallVariety</c> ("Wall Variety").
/// </summary>
/// <remarks>
/// <para>
/// This is the pass that owns the rock, lava and jungle cave wall families, which is why a world without it
/// has none of them at all. It samples a random cell below the surface line, and if that cell is a block with
/// open space directly above it, floods the open space to see whether it is a closed pocket. A pocket of the
/// right size that touches none of the forbidden materials is then papered - the pocket itself AND one ring of
/// the rock around it - with a wall family chosen from how deep it sits, or from jungle grass if the block it
/// was entered from is jungle grass.
/// </para>
/// <para>
/// Three numbers shape it. The quota is three hundred pockets scaled by the world's area against a Small
/// world, so a Small world paints exactly three hundred. The flood gives up after a thousand cells, which is
/// what refuses a cavern rather than a pocket - and the flood reports whether it CLOSED, not how big it got,
/// so a cavern is refused because its queue still had somewhere to go. And a pocket under fifty-one cells is
/// refused for being too small.
/// </para>
/// <para>
/// The one thing here that a careful reimplementation gets wrong is the origin. The flood starts one row ABOVE
/// the sampled block, and the shape it records is relative to that; the painting is then performed from the
/// sampled block itself. So everything is written one row BELOW where it was found, and a nine-by-nine pocket
/// comes out as an eleven-by-eleven block of wall shifted down one. That is not a rounding detail - it is the
/// difference between papering the pocket and papering the pocket's floor.
/// </para>
/// <para>
/// Two source redundancies are kept as written rather than simplified, because a future reader will otherwise
/// assume a port dropped something. The stone branch tests the flooded cell against seven materials and then
/// against six of the same seven; the second set is a subset of the first, so the pair means exactly what the
/// jungle branch's single test means. And the jungle branch's own test is that same six-material set. The two
/// branches therefore refuse identical pockets.
/// </para>
/// <para>
/// One structural hazard worth naming: the pass has no budget for a sample that was never a candidate. Only a
/// candidate that FAILS its checks costs the failure budget, and a point that is not a block, or has no open
/// space above it, or is neither stone nor jungle grass, costs nothing at all. A region with no such block
/// anywhere makes the official delegate loop forever. An ordinary world always has one, so this reproduces the
/// loop as written rather than inventing a bound for it.
/// </para>
/// </remarks>
internal sealed class CaveWallVarietyPass1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    double rockLayer,
    int lavaLine,
    WorldGenerationPoint shimmerPosition,
    CancellationToken cancellation)
{
    /// <summary>Source <c>ShapeFloodFill</c>'s default maximum action count as this pass asks for it.</summary>
    private const int FloodBudget = 1000;

    /// <summary>Source <c>WorldGen.shimmerSafetyDistance</c>.</summary>
    private const int ShimmerSafetyDistance = 150;

    private const int FailureBudget = 100000;
    private const ushort Stone = 1;
    private const ushort JungleGrass = 60;

    private static readonly int[] OutlineOffsets =
        [1, 0, -1, 0, 0, 1, 0, -1, 1, 1, 1, -1, -1, 1, -1, -1];

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    private readonly GenerationWallFraming1458 framing = new(store, random);

    private readonly HashSet<(int X, int Y)> shape = [];
    private readonly Queue<(int X, int Y)> frontier = new();
    private readonly HashSet<(int X, int Y)> visited = [];

    /// <summary>How many pockets were papered.</summary>
    public long Pockets { get; private set; }

    /// <summary>How many wall cells the papering laid.</summary>
    public long Painted { get; private set; }

    public void Apply()
    {
        int quota = (int)(300.0 * ((double)(width * height) / 5040000.0));
        int failures = FailureBudget;
        while (quota > 0 && failures > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            (int x, int y) = SampleAwayFromShimmer();

            if (!At(x, y).IsActive)
                continue;

            bool jungle = At(x, y).Type == JungleGrass;
            ushort wall = 0;
            if (jungle)
            {
                wall = (ushort)(204 + random.Next(4));
            }
            else if (At(x, y).Type == Stone && At(x, y - 1).Wall == 0)
            {
                // Above the rock layer it is rock wall; at or below the lava line it is the lava family;
                // between the two it is the deeper rock family.
                wall = y < rockLayer
                    ? (ushort)(196 + random.Next(4))
                    : y >= lavaLine
                        ? (ushort)(208 + random.Next(4))
                        : (ushort)(212 + random.Next(4));
            }

            if (wall == 0 || At(x, y - 1).IsActive)
                continue;

            shape.Clear();
            bool closed = Flood(x, y - 1, jungle, out bool forbidden);
            if (shape.Count > 50 && closed && !forbidden)
            {
                PaintOutline(x, y, wall);
                Pockets++;
                quota--;
            }
            else
            {
                failures--;
            }
        }
    }

    /// <summary>
    /// Source <c>WorldGen.RandomWorldPoint(worldSurface, 2, 190, 2)</c> with the shimmer retry around it: the
    /// column is drawn first and the row second, and a point inside the shimmer's safety radius redraws both.
    /// </summary>
    private (int X, int Y) SampleAwayFromShimmer()
    {
        int x = random.Next(2, width - 2);
        int y = random.Next((int)worldSurface, height - 190);
        while (Distance(x, y) < ShimmerSafetyDistance)
        {
            x = random.Next(2, width - 2);
            y = random.Next((int)worldSurface, height - 190);
        }

        return (x, y);
    }

    private double Distance(int x, int y)
    {
        double dx = x - shimmerPosition.X;
        double dy = y - shimmerPosition.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Source <c>ShapeFloodFill</c> with this pass's action chain folded in. The chain admits a cell only when
    /// it is not a solid block, records it relative to the flood's own origin, and - without affecting whether
    /// the flood continues - raises the forbidden flag when the cell touches a material this pass refuses to
    /// paper around. The return is whether the flood CLOSED rather than how far it got: a queue with somewhere
    /// left to go when the budget ran out reads as an open cavern.
    /// </summary>
    private bool Flood(int originX, int originY, bool jungle, out bool forbidden)
    {
        frontier.Clear();
        visited.Clear();
        forbidden = false;
        frontier.Enqueue((originX, originY));
        int budget = FloodBudget;
        while (frontier.Count > 0 && budget > 0)
        {
            (int x, int y) cell = frontier.Dequeue();
            if (visited.Contains(cell) || IsSolidOrSloped(cell.x, cell.y))
                continue;

            shape.Add((cell.x - originX, cell.y - originY));
            if (Touches(cell.x, cell.y, jungle))
                forbidden = true;

            visited.Add(cell);
            budget--;
            if (cell.x + 1 < width - 1)
                frontier.Enqueue((cell.x + 1, cell.y));
            if (cell.x - 1 >= 1)
                frontier.Enqueue((cell.x - 1, cell.y));
            if (cell.y + 1 < height - 1)
                frontier.Enqueue((cell.x, cell.y + 1));
            if (cell.y - 1 >= 1)
                frontier.Enqueue((cell.x, cell.y - 1));
        }

        while (frontier.Count > 0)
        {
            (int X, int Y) item = frontier.Dequeue();
            if (!visited.Contains(item))
            {
                frontier.Enqueue(item);
                break;
            }
        }

        return frontier.Count == 0;
    }

    /// <summary>
    /// The forbidden-material test. The stone branch runs it twice over two sets, the second a subset of the
    /// first, so both branches come to the same answer; the pair is kept because the source has it.
    /// </summary>
    private bool Touches(int x, int y, bool jungle)
    {
        if (!jungle && !TouchesAny(x, y, [JungleGrass, 147, 161, 396, 397, 70, 191]))
            return false;

        return TouchesAny(x, y, [147, 161, 396, 397, 70, 191]);
    }

    private bool TouchesAny(int x, int y, ReadOnlySpan<ushort> types)
    {
        for (int i = 0; i < OutlineOffsets.Length; i += 2)
        {
            int nx = x + OutlineOffsets[i];
            int ny = y + OutlineOffsets[i + 1];
            if (!Contains(nx, ny))
                continue;

            WorldTile near = At(nx, ny);
            if (!near.IsActive)
                continue;

            foreach (ushort type in types)
            {
                if (near.Type == type)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Source <c>ModShapes.OuterOutline</c> with diagonals and the interior. The origin is the sampled block,
    /// one row below the flood's own origin, so every cell is written one row lower than it was found.
    /// </summary>
    private void PaintOutline(int originX, int originY, ushort wall)
    {
        foreach ((int X, int Y) datum in shape)
        {
            Place(originX + datum.X, originY + datum.Y, wall);
            for (int i = 0; i < OutlineOffsets.Length; i += 2)
            {
                if (shape.Contains((datum.X + OutlineOffsets[i], datum.Y + OutlineOffsets[i + 1])))
                    continue;

                Place(originX + datum.X + OutlineOffsets[i], originY + datum.Y + OutlineOffsets[i + 1], wall);
            }
        }
    }

    /// <summary>
    /// Source <c>Modifiers.SkipWalls(87, 86, 244)</c> then <c>Actions.PlaceWall</c>. The wall framing that
    /// follows is not cosmetic here: it is where most of this pass's shared RNG goes, several hundred values
    /// per pocket. See <see cref="GenerationWallFraming1458"/>.
    /// </summary>
    private void Place(int x, int y, ushort wall)
    {
        if (!Contains(x, y))
            return;

        if (At(x, y).Wall is 87 or 86 or 244)
            return;

        framing.PlaceWall(x, y, wall);
        Painted++;
    }

    /// <summary>
    /// Source <c>WorldGen.SolidOrSlopedTile</c> with the default <c>includePlatforms: false</c>. A slope does
    /// not make a block non-solid here, which is the whole point of the name.
    /// </summary>
    private bool IsSolidOrSloped(int x, int y)
    {
        if (!Contains(x, y))
            return false;

        WorldTile tile = At(x, y);
        return tile.IsActive && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) && !tile.IsActuated;
    }

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
