using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Depth-limited ordinary SpreadGrass for dirt/mud substrates, pinned to 1.4.5.8.
/// Shared by evil surface conversion and lake carving, on unpublished candidate tiles only.</summary>
internal sealed class GenerationGrass1458(WorldTileStore store, IWorldGenerationVanillaRandom random,
    CancellationToken cancellationToken)
{
    private readonly StoneBiomeTiles1458 framing = new(store, random);

    public void Apply(int x, int y, ushort dirt, ushort grass)
    {
        if (!((dirt == 0 && grass is 23 or 199) || (dirt == 59 && grass is 60 or 661 or 662)))
            throw new InvalidOperationException("Unverified generation grass conversion.");
        Spread(x,y,dirt,grass,0);
    }

    private void Spread(int x, int y, ushort dirt, ushort grass, int depth)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (x < 10 || y < 10 || x >= store.Dimensions.WidthTiles - 10 || y >= store.Dimensions.HeightTiles - 10) return;
        ref WorldTile cell = ref At(x,y);
        if (!cell.IsActive || cell.Type != dirt) return;
        if (dirt == 0 && ((x > store.Dimensions.WidthTiles * .45 && x <= store.Dimensions.WidthTiles * .55) ||
            x < 380 || x >= store.Dimensions.WidthTiles - 380)) return;
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
        // CanBeClearedDuringGeneration admits both Dirt0 and Mud59. Above-type27 rejects even when inactive.
        if (enclosed || (grass != 60 && At(x,y-1).Type == 27)) return;
        WorldTile above = At(x,y-1);
        if (above.IsActive && above.Type is 5 or 72 or 323 or >= 583 and <= 589 or 596 or 616 or 634)
            throw new InvalidOperationException("Generation grass requires unimplemented tree conversion/framing.");
        cell.Type = grass;
        for (int tx = x - 1; tx <= x + 1; tx++)
        for (int ty = y - 1; ty <= y + 1; ty++) framing.Frame(tx,ty);
        cell.TileColor = 0;
        cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        // grassSpread limits recursion depth, NOT the number of converted cells in the component.
        if (depth >= 1000) return;
        for (int tx = x - 1; tx <= x + 1; tx++)
        for (int ty = y - 1; ty <= y + 1; ty++)
            if (At(tx,ty).IsActive && At(tx,ty).Type == dirt) Spread(tx,ty,dirt,grass,depth+1);
    }

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x,y)];
}
