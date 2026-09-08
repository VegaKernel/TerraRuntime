using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>The torch phase following fort construction in WorldGen.AddHellHouses, TerrariaServer 1.4.5.8.
/// This is not general player torch placement; its caller has already selected an empty house-wall cell.</summary>
internal static class HellFortLighting1458
{
    internal const ushort Torch = 4;
    internal const short HellTorchFrameY = 7 * 22;

    // Main.Initialize_TileAndNPCData2: all tileNoAttach assignments, including the 435..439 loop.
    private static ReadOnlySpan<ushort> NoAttach =>
    [3,4,10,13,14,15,16,17,18,19,20,21,27,50,86,87,88,89,90,91,92,93,94,95,96,97,98,99,
     101,102,110,114,134,387,388,390,427,435,436,437,438,439,441,467,468,469,486,487,488,
     489,490,497,564,565,568,569,570,572,580,590,593,594,595,615,620,704,707];

    internal static int AttemptCount(int width) => 200 * (width / 4200);

    public static int Generate(WorldTileStore store, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken)
    {
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles;
        int placedCount = 0;
        // The source divides integers BEFORE assigning a float: Small and Medium both request 200, Large 400.
        for (int torch = 0; torch < AttemptCount(width); torch++)
        for (int sample = 0; sample <= 1000; sample++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next((int)(width * 0.2), (int)(width * 0.8));
            int y = random.Next(height - 300, height - 20);
            if (!TryPlaceCandidate(store, x, y, out bool placed)) continue;
            if (placed) placedCount++;
            break;
        }
        return placedCount;
    }

    /// <returns>True when the source stops searching for this torch, including a wet PlaceTile rejection.</returns>
    internal static bool TryPlaceCandidate(WorldTileStore store, int brickX, int y, out bool placed)
    {
        placed = false;
        if (brickX < 9 || y < 9 || brickX + 8 >= store.Dimensions.WidthTiles || y + 8 >= store.Dimensions.HeightTiles)
            return false;
        WorldTile support = At(store, brickX, y);
        if (!support.IsActive || support.Type is not (75 or 76)) return false;
        // Left wall wins even if that side is obstructed. Vanilla does not retry the right side in that case.
        int direction = At(store, brickX - 1, y).Wall > 0 ? -1 : At(store, brickX + 1, y).Wall > 0 ? 1 : 0;
        int x = brickX + direction;
        if (At(store, x, y).IsActive || At(store, x, y + 1).IsActive) return false;
        for (int scanX = brickX - 8; scanX < brickX + 8; scanX++)
        for (int scanY = y - 8; scanY < y + 8; scanY++)
            if (At(store, scanX, scanY) is { IsActive: true, Type: Torch }) return false;

        ref WorldTile target = ref At(store, x, y);
        // Style 7 is not one of PlaceTile's liquid-admitted torch styles (8/11/17).
        if (target.LiquidAmount > 0) return true;
        WorldTile left = At(store, x - 1, y), right = At(store, x + 1, y);
        // Unknown active identities cannot decide the CheckTorch attachment frame.
        if ((left.IsActive && left.Type >= VanillaTileIds.Count) || (right.IsActive && right.Type >= VanillaTileIds.Count))
            return true;
        HellFortGenerator1458.ClearPlacementAnchor(ref target);
        target.Flags |= WorldTileFlags.Active;
        target.Type = Torch;
        target.FrameY = HellTorchFrameY;
        // Below is known inactive from the admission gate. CheckTorch still runs for frame-important tiles during
        // generation and prefers left/right attachment over the guaranteed background wall. No cosmetic RNG.
        target.FrameX = AttachesToSide(store, x - 1, y, leftSide: true) ? (short)22 :
            AttachesToSide(store, x + 1, y, leftSide: false) ? (short)44 : (short)0;
        placed = true;
        return true;
    }

    private static bool AttachesToSide(WorldTileStore store, int x, int y, bool leftSide)
    {
        WorldTile tile = At(store, x, y);
        int slope = tile.Shape >= 2 ? tile.Shape - 1 : 0;
        if (!tile.IsActive || (slope > 0 && slope % 2 == (leftSide ? 1 : 0))) return false;
        return (VanillaTileCollisionCatalog.IsSolid(tile.TileType) && !NoAttach.Contains(tile.Type)) ||
            tile.Type is 124 or 561 or 574 or 575 or 576 or 577 or 578 ||
            (IsTree(tile.Type) && At(store, x, y - 1).IsActive && IsTree(At(store, x, y - 1).Type) &&
             At(store, x, y + 1).IsActive && IsTree(At(store, x, y + 1).Type));
    }

    private static bool IsTree(ushort type) => type is 5 or 72 or 583 or 584 or 585 or 586 or 587 or 588 or 589 or 596 or 616 or 634;
    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
