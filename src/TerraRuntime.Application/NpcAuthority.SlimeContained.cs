using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class NpcAuthority
{
    private VanillaSlimeContainedFacts1458 CaptureSlimeContainedFacts() => new(
        worldTiles?.WorldSurfaceTiles ?? double.NaN,
        naturalSpawnWorldFacts?.RockLayer ?? double.NaN,
        worldClock?.GetGoodWorld ?? naturalSpawnWorldFacts?.GoodWorld ?? false,
        naturalSpawnWorldFacts?.RemixWorld ?? false,
        naturalSpawnWorldFacts?.NoTrapsWorld ?? false,
        naturalSpawnWorldFacts?.VampireSeed ?? false,
        naturalTownSpawnFacts?.GenuineParty ?? false,
        naturalSpawnWorldFacts?.NotTheBeesWorld ?? false,
        naturalSpawnSkyblockLowTiles,
        worldClock?.SlimeRainActive ?? false,
        (naturalSpawnWorldFacts?.HardMode ?? false) ||
            naturalSpawnProgression.IsCompleted(VanillaWorldProgressionId.Hardmode),
        naturalSpawnSkyblockNoHellstone,
        (naturalSpawnWorldFacts?.DownedBoss3 ?? false) ||
            naturalSpawnProgression.IsCompleted(VanillaWorldProgressionId.Skeletron),
        naturalSpawnSkyblockNoLifeCrystals,
        false,
        worldClock is null ? -1 : (int)worldClock.MoonPhase);
}
