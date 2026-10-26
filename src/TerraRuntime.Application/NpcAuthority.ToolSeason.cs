using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class NpcAuthority
{
    internal bool TryCaptureToolSeason(WorldTileStore expectedTiles,
        out VanillaSeasonalItemDropContext1458 season)
    {
        season = default;
        if (!ReferenceEquals(worldTiles, expectedTiles) || naturalSpawnWorldFacts is null) return false;
        // Reuse the existing forced/calendar seasonal owner. Missing world facts
        // are not a known-false Halloween observation for a tree transaction.
        season = CaptureSeasonalItemContext();
        return true;
    }
}
