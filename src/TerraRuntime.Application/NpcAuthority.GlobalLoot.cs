using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class NpcAuthority
{
    private VanillaSeasonalItemDropContext1458 CaptureSeasonalItemContext()
    {
        if (naturalSpawnWorldFacts is not { UseLocalSeasonCalendar: true })
            return new(naturalSpawnWorldFacts?.Halloween == true, naturalSpawnWorldFacts?.XMas == true,
                naturalSpawnWorldFacts?.TenthAnniversaryWorld == true);
        DateTime localDate = DateTime.Now;
        return new(
            VanillaSeasonalCalendar1458.IsHalloween(localDate, naturalSpawnWorldFacts?.Halloween == true),
            VanillaSeasonalCalendar1458.IsChristmas(localDate, naturalSpawnWorldFacts?.XMas == true),
            naturalSpawnWorldFacts?.TenthAnniversaryWorld == true);
    }

    private RuntimeNpcGlobalLootWorldFacts1458 CaptureGlobalLootWorldFacts()
    {
        var world = naturalSpawnWorldFacts!.Value;
        var season = CaptureSeasonalItemContext();
        // Journey's creative difficulty slider has no retained owner here. Only rules that read it
        // are fenced; known-false holiday rules do not need a guessed multiplier.
        float? difficulty = world.GameMode is 0 or 1 or 2 ? CaptureDifficulty() : null;
        return new(worldTiles!.Dimensions.WidthTiles, worldTiles.Dimensions.HeightTiles,
            difficulty, world.HardMode, world.RemixWorld, season.Halloween, season.XMas,
            world.RockLayer, world.WorldSurface, world.DownedBoss3, world.DownedMechBossAny);
    }
}
