using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Depth-limited ordinary SpreadGrass for dirt/mud substrates, pinned to 1.4.5.8.
/// Shared by evil surface conversion, lake carving and the surface grass walls.</summary>
/// <remarks>
/// <para>
/// Two things decide whether a cell may take grass, and which one applies depends on the grass. The evil
/// grasses are kept off the ocean shores and out of the middle of the map, where the spawn is; every other
/// grass, ordinary green included, is instead kept above the surface line, so nothing grows a lawn in a cave.
/// Both then share the rule that a cell walled in on all eight sides is refused - grass needs a face open to
/// the air - and that standing lava anywhere inside that square refuses it outright.
/// </para>
/// <para>
/// The framing that follows a conversion is the caller's, because the callers reach different ground. The
/// stone micro-biomes hand in a framer that knows speleothems; the surface passes hand in one that knows the
/// piles, plants and detritus that stand on a lawn. Either way the conversion re-frames the square around the
/// cell, not the cell alone, which is how a plant standing on newly-converted ground is re-examined.
/// </para>
/// </remarks>
internal sealed class GenerationGrass1458(WorldTileStore store, IWorldGenerationVanillaRandom random,
    CancellationToken cancellationToken, double worldSurface = double.MaxValue,
    Action<int, int>? frameSquare = null)
{
    private readonly StoneBiomeTiles1458 framing = new(store, random);

    /// <summary>How many cells the spreads run through this instance have converted.</summary>
    public long Converted { get; private set; }

    public void Apply(int x, int y, ushort dirt, ushort grass)
    {
        if (!((dirt == 0 && grass is 2 or 23 or 199) || (dirt == 59 && grass is 60 or 661 or 662) ||
              (dirt == 1 && grass is (>= 179 and <= 183) or 381 or 534 or 536 or 539 or 625 or 627)))
            throw new InvalidOperationException("Unverified generation grass conversion.");
        Spread(x,y,dirt,grass,0);
    }

    private void Spread(int x, int y, ushort dirt, ushort grass, int depth)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (x < 10 || y < 10 || x >= store.Dimensions.WidthTiles - 10 || y >= store.Dimensions.HeightTiles - 10) return;
        ref WorldTile cell = ref At(x,y);
        if (!cell.IsActive || cell.Type != dirt) return;
        // The evil grasses take the shore and centre refusal; everything else takes the surface refusal, and
        // the surface refusal only applies over dirt - mud keeps its jungle grass at any depth.
        if (grass is 23 or 199)
        {
            if ((x > store.Dimensions.WidthTiles * .45 && x <= store.Dimensions.WidthTiles * .55) ||
                x < 380 || x >= store.Dimensions.WidthTiles - 380) return;
        }
        else if (dirt == 0 && y >= worldSurface) return;
        bool enclosed = true;
        for (int tx = x - 1; tx <= x + 1; tx++)
        for (int ty = y - 1; ty <= y + 1; ty++)
        {
            WorldTile neighbour = At(tx,ty);
            if (neighbour.IsActive && neighbour.Type >= VanillaTileIds.Count)
                throw new InvalidOperationException("Generation grass encountered an unknown tile identity.");
            if (!neighbour.IsActive || neighbour.Type == 484 || !VanillaTileCollisionCatalog.IsSolid(neighbour.TileType)) enclosed = false;
            // Source breaks only the inner loop: later columns can change enclosure again.
            if (neighbour.LiquidKind == WorldLiquidKind.Lava && neighbour.LiquidAmount > 0) { enclosed = true; break; }
        }
        // CanBeClearedDuringGeneration admits both Dirt0 and Mud59. Above-type27 rejects even when inactive,
        // and only for the grasses that grow something on top of it - ordinary green grass does not.
        if (enclosed || (grass is 23 or 199 or 661 or 662 or 109 && At(x,y-1).Type == 27)) return;
        // Source TryConvertingOrKillingTreesAboveIfTheyWouldBecomeInvalid: a tree above the converted floor
        // survives when its growth profile still accepts the new floor, and is KILLED otherwise - which is not
        // free, because KillTile spends dust from the shared stream. GemTreeGroundTest accepts every moss
        // identity, so a gem tree standing on stone that takes moss is left exactly as it is. Every other tree
        // this guard names needs grass, jungle grass, snow, mushroom grass, sand or ash beneath it, so none of
        // them can be standing on the stone a moss conversion changes; the throw keeps that assumption honest.
        WorldTile above = At(x,y-1);
        if (above.IsActive && above.Type is 5 or 72 or 323 or >= 583 and <= 589 or 596 or 616 or 634 &&
            !(above.Type is >= 583 and <= 589 && IsMossConversion(grass)))
        {
            throw new InvalidOperationException(
                $"Generation grass requires unimplemented tree conversion/framing: {above.Type} above {x},{y} taking {grass}.");
        }
        cell.Type = grass;
        Converted++;
        if (frameSquare is null)
            for (int tx = x - 1; tx <= x + 1; tx++)
            for (int ty = y - 1; ty <= y + 1; ty++) framing.Frame(tx,ty);
        else
            frameSquare(x, y);
        cell.TileColor = 0;
        cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        // grassSpread limits recursion depth, NOT the number of converted cells in the component.
        if (depth >= 1000) return;
        for (int tx = x - 1; tx <= x + 1; tx++)
        for (int ty = y - 1; ty <= y + 1; ty++)
            if (At(tx,ty).IsActive && At(tx,ty).Type == dirt) Spread(tx,ty,dirt,grass,depth+1);
    }

    /// <summary>Source <c>TileID.Sets.Conversion.Moss</c>.</summary>
    private static bool IsMossConversion(ushort type) =>
        type is 179 or 180 or 181 or 182 or 183 or 381 or 534 or 536 or 539 or 625 or 627;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x,y)];
}
