using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary registered SpawnPoint: sample the first occupied non-living-wood cell; do not carve terrain.</summary>
internal sealed class GenerationSpawnPoint1458(Workspace workspace, IWorldGenerationVanillaRandom random, double worldSurface, CancellationToken cancellation)
{
    private readonly WorldTileStore store = workspace.TileStore;
    internal WorldGenerationPoint Apply()
    {
        workspace.SetVanillaGenerationSolid(new TileTypeId(379), false);
        int center = workspace.WidthTiles / 2, radius = 5;
        WorldGenerationPoint selected = workspace.TryGetSpawn(out var previous) ? previous : new(0, 0);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            int column = center + random.Next(-radius, radius + 1);
            selected = Scan(column, selected); radius++;
            if (selected.Y < 3) throw new InvalidOperationException("Ordinary source spawn search reached its invalid liquid lookbehind boundary.");
            if (selected.Y > worldSurface) continue;
            bool wet = false;
            for (int row = selected.Y - 1; row >= selected.Y - 3; row--) wet |= store.Get(selected.X, row).LiquidAmount > 0;
            if (wet || NearLivingWood(selected.X, selected.Y)) continue;
            break;
        }
        // The second source loop is normally redundant, but retains the original ordering and bounds.
        radius = 10;
        while (selected.Y > worldSurface)
        {
            cancellation.ThrowIfCancellationRequested();
            selected = Scan(random.Next(center - radius, center + radius), selected); radius++;
        }
        workspace.SetVanillaGenerationSolid(new TileTypeId(379), true);
        return selected;
    }
    private WorldGenerationPoint Scan(int column, WorldGenerationPoint previous)
    {
        if ((uint)column >= (uint)workspace.WidthTiles) throw new InvalidOperationException("Ordinary source spawn search exhausted world columns.");
        for (int row = 0; row < workspace.HeightTiles; row++)
        {
            WorldTile cell = store.Get(column, row);
            if (!cell.IsActive || cell.Type == 379) continue;
            if (!workspace.TrySetSpawn(column, row)) throw new InvalidOperationException("Could not retain ordinary source spawn coordinates.");
            return new(column, row);
        }
        return previous;
    }
    private bool NearLivingWood(int x, int y)
    {
        for (int column = x - 10; column <= x + 10; column++) for (int row = y - 10; row <= y + 10; row++)
            if ((uint)column < (uint)workspace.WidthTiles && (uint)row < (uint)workspace.HeightTiles && store.Get(column, row) is { IsActive: true, Type: 379 }) return true;
        return false;
    }
}
