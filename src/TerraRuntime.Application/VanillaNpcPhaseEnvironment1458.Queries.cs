using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class VanillaNpcPhaseEnvironment1458
{
    private sealed partial class Capture
    {
        private readonly VanillaFishWorldEnvironment1458 fish = new(tiles);

        public GroundCritterDanger1458 Danger => critter is { } input
            ? new(input.Danger.WithinRange, input.Danger.ThreatDirection, phase.Stinky) : default;

        public bool ShelterWeather => critter?.Weather.ReturnHomeRequested == true;

        private bool CritterInputsCurrent() => critter is not { } input ||
            input.Conditions(source) == input.Weather &&
            input.PlayerDanger().AsSpan().SequenceEqual(input.Players) &&
            RuntimeTownNpcDangerScanner1458.TryScan(tiles, in source, input.Peers, input.Players,
                status, false, false, out var observed) && observed == input.Danger;

        public void FindRestingSpot(int homeX, int homeY, bool dayTime, out int x, out int y) =>
            terrain.FindRestingSpot(homeX, homeY, dayTime, critter?.Peers ?? [], out x, out y);

        public GroundCritterTile1458 Tile(int x, int y) => terrain.Tile(x, y);
        public bool NavigationObstacle(int left, int right, int top, int bottom) =>
            terrain.NavigationObstacle(left, right, top, bottom);
        public bool Drowning(float x, float y, int width, int height) => terrain.Drowning(x, y, width, height);
        public bool TryGetWaterLine(int centerTileX, int centerTileY, out bool found, out float waterLineHeight) =>
            terrain.TryGetWaterLine(centerTileX, centerTileY, out found, out waterLineHeight);
        public GroundCritterStep1458 StepDown(float x, float y, float vx, float vy, int width, int height) =>
            terrain.StepDown(x, y, vx, vy, width, height);
        public GroundCritterStep1458 StepUp(float x, float y, float vx, float vy, int width, int height, bool matchPlatforms) =>
            terrain.StepUp(x, y, vx, vy, width, height, matchPlatforms);

        public VanillaFishBottomSlope1458 GetBottomSlope(float x, float y, int width, int height)
        {
            terrain.FenceBody(x, y, 0f, 0f, width, height);
            return fish.GetBottomSlope(x, y, width, height);
        }

        public bool HasDeepLiquidAboveAndActiveTileBelow(float x, float y, int width, int height)
        {
            terrain.FenceBody(x, y, 0f, 0f, width, height);
            return fish.HasDeepLiquidAboveAndActiveTileBelow(x, y, width, height);
        }

        public bool CanHitLineStraightAbove(float x, float y, int width, int height, float distance) =>
            terrain.FenceLineOfSight(x, y, width, height, x, y - distance, width, height) &&
            fish.CanHitLineStraightAbove(x, y, width, height, distance);

        public bool TryGetWaterLineAtTop(float x, float y, int width, out float waterLineHeight)
        {
            waterLineHeight = 0f;
            if (!float.IsFinite(x) || !float.IsFinite(y) || width <= 0) return false;
            return terrain.TryGetWaterLine((int)((x + width * .5f) / 16f), (int)(y / 16f),
                out bool found, out waterLineHeight) && found;
        }
    }
}
