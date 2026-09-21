using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8
/// <c>GenPassNameID.SpreadingGrassOnSurfaceSunflowersEvilsOnSurfaceAndLavaCleanup</c>.
/// </summary>
/// <remarks>
/// <para>
/// Everything the registration's name mentions after the grass - the sunflowers, the evil conversion, the lava
/// cleanup - is inside its remix branch, so for an ordinary world this is two deterministic scans and nothing
/// else. It spends no shared RNG at all, which is worth stating plainly: the stream is in exactly the same
/// place after this pass as before it, whatever the world looks like.
/// </para>
/// <para>
/// The first scan re-skins the surface. Jungle grass creeps into the dirt in the square around it, and because
/// the scan runs in ascending columns the cells it converts are themselves scanned later, so one cell of
/// jungle grass at the surface walks right across the map. Exposed stone, clay or ore instead takes the
/// identity of whatever biome material lies within three cells of it - sand, mud, jungle grass, snow, ice or
/// either evil grass - and when there is none, it takes plain dirt. That last case is the one that shapes an
/// ordinary world: it is why a stone hill at the surface wears a skin of dirt, and it cascades the same way
/// the jungle does.
/// </para>
/// <para>
/// Three details decide the outcome and none of them is obvious. Sand wins the scan and cannot be overwritten
/// once it has been seen, while every other material simply replaces whatever the scan held before it, so the
/// answer for the other materials is the LAST one found rather than the nearest. The re-skin only happens at
/// all when the scan found a bare, unpapered cell strictly above the cell being re-skinned - a papered
/// overhang or a few rows of cover is enough to leave the stone alone. And two of the answers are then
/// re-decided: an evil grass turns into plain dirt when the cell has a block directly over it, and mud or
/// jungle grass inside the jungle's own columns is re-chosen from that same test, mud under cover and jungle
/// grass in the open.
/// </para>
/// <para>
/// The second scan walks each column from the top and starts an ordinary grass spread at the first dirt block
/// below a bare, unpapered gap. It stops the column as soon as it has passed a block below
/// <c>worldSurfaceHigh</c>, so it only ever reaches the surface itself and the overhangs above it, and a
/// papered column offers it nothing at all.
/// </para>
/// </remarks>
internal sealed class SurfaceGrassSpreadPass1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    double worldSurfaceHigh,
    int jungleMinX,
    int jungleMaxX,
    CancellationToken cancellation)
{
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;
    private const ushort CorruptGrass = 23;
    private const ushort ClayBlock = 40;
    private const ushort Sand = 53;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort Snow = 147;
    private const ushort Ice = 161;
    private const ushort CrimsonGrass = 199;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    /// <summary>How many cells the surface re-skin retyped.</summary>
    public long Reskinned { get; private set; }

    /// <summary>How many dirt blocks the column walk grew grass on.</summary>
    public long Grown { get; private set; }

    public void Apply()
    {
        var framing = new GenerationTileFraming1458(store, random);
        var grass = new GenerationGrass1458(
            store, random, cancellation, worldSurface, (x, y) => framing.SquareTileFrame(x, y));

        for (int x = 50; x < width - 50; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = 50; y <= worldSurface; y++)
            {
                if (!At(x, y).IsActive)
                    continue;

                ushort type = At(x, y).Type;
                if (type == JungleGrass)
                    Creep(x, y);
                else if (type is Stone or ClayBlock || StoneBiomeTiles1458.IsOre(type))
                    Reskin(x, y);
            }
        }

        for (int x = 10; x < width - 10; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            bool belowGap = true;
            for (int y = 0; y < worldSurface - 1.0; y++)
            {
                WorldTile cell = At(x, y);
                if (cell.IsActive)
                {
                    if (belowGap && cell.Type == Dirt)
                    {
                        long before = grass.Converted;
                        grass.Apply(x, y, Dirt, Grass);
                        Grown += grass.Converted - before;
                    }

                    if (y > worldSurfaceHigh)
                        break;

                    belowGap = false;
                }
                else if (cell.Wall == 0)
                {
                    belowGap = true;
                }
            }
        }
    }

    /// <summary>
    /// Jungle grass takes the dirt in the square around it: mud where the dirt has a block directly over it,
    /// jungle grass where it does not. Nothing is re-framed, because nothing here is frame-important.
    /// </summary>
    private void Creep(int x, int y)
    {
        for (int sx = x - 1; sx <= x + 1; sx++)
        for (int sy = y - 1; sy <= y + 1; sy++)
        {
            ref WorldTile near = ref At(sx, sy);
            if (!near.IsActive || near.Type != Dirt)
                continue;

            near.Type = At(sx, sy - 1).IsActive ? Mud : JungleGrass;
            Reskinned++;
        }
    }

    /// <summary>
    /// Exposed stone, clay or ore takes the identity of the biome material around it, or plain dirt when there
    /// is none.
    /// </summary>
    private void Reskin(int x, int y)
    {
        ushort found = 0;
        bool exposed = false;
        for (int sx = x - 3; sx <= x + 3; sx++)
        for (int sy = y - 3; sy <= y + 3; sy++)
        {
            WorldTile near = At(sx, sy);
            if (near.IsActive)
            {
                // Sand wins and sticks; every other material replaces whatever was held, so for those the
                // answer is the last one the scan meets rather than the nearest.
                if (near.Type == Sand || found == Sand)
                    found = Sand;
                else if (near.Type is Mud or JungleGrass or Snow or Ice or CrimsonGrass or CorruptGrass)
                    found = near.Type;
            }
            else if (sy < y && near.Wall == 0)
            {
                exposed = true;
            }
        }

        if (!exposed)
            return;

        bool covered = At(x, y - 1).IsActive;
        switch (found)
        {
            case CorruptGrass or CrimsonGrass:
                if (covered)
                    found = Dirt;
                break;
            case Mud or JungleGrass:
                if (x >= jungleMinX && x <= jungleMaxX)
                    found = covered ? Mud : JungleGrass;
                break;
        }

        At(x, y).Type = found;
        Reskinned++;
    }

    private ref WorldTile At(int x, int y)
    {
        if (!Contains(x, y))
            throw new NotSupportedException(
                "WorldGen.SpreadingGrassOnSurfaceSunflowersEvilsOnSurfaceAndLavaCleanup reads the cell at " +
                x + "," + y + ", outside the world, which the source does without a bounds check; a world " +
                "whose surface line sits within three rows of the bottom is not a case it survives.");

        return ref store.Tiles[store.GetUncheckedIndex(x, y)];
    }

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;
}
