using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>Production world queries for source-backed AI_002 daylight and Pigron phasing state.</summary>
internal sealed class VanillaFlyingEyeWorldEnvironment : IVanillaFlyingEyeRetainedEnvironment1458, IVanillaEverscreamEnvironment
{
    private readonly WorldTileStore _tiles;

    public VanillaFlyingEyeWorldEnvironment(WorldTileStore tiles) =>
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public bool IsGraveyardAt(float centerX, float centerY) =>
        VanillaWorldGraveyardScene.IsFunctionalAt(_tiles, centerX, centerY);

    public bool CanHit(
        float sourcePositionX,
        float sourcePositionY,
        int sourceWidth,
        int sourceHeight,
        float targetPositionX,
        float targetPositionY,
        int targetWidth,
        int targetHeight) =>
        VanillaWorldCanHit.HasLineOfSight(
            _tiles,
            sourcePositionX,
            sourcePositionY,
            sourceWidth,
            sourceHeight,
            targetPositionX,
            targetPositionY,
            targetWidth,
            targetHeight);

    public bool SolidCollision(float positionX, float positionY, int width, int height) =>
        VanillaWorldSolidCollision.Intersects(_tiles, positionX, positionY, width, height);

    public bool TryCapture(in NpcSnapshot source, in VanillaNpcTargetCandidate current,
        in VanillaNpcTargetCandidate closest, out IVanillaFlyingEyeWorldFence1458 fence)
    {
        fence = default!;
        if (!VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(source.Simulation, out var body))
            return false;
        // Cover the source graveyard scan, actual-body LOS, and this tick's collision sweep.
        float sweepX = Math.Max(Math.Abs(source.VelocityX), Math.Abs(source.Simulation.OldVelocityX)) + 16f;
        float sweepY = Math.Max(Math.Abs(source.VelocityY), Math.Abs(source.Simulation.OldVelocityY)) + 16f;
        float left = Math.Min(source.PositionX - sweepX,
            Math.Min(current.CenterX - VanillaWorldGraveyardScene.ScanWidthTiles * 8f - 16f, closest.CenterX - closest.Width));
        float right = Math.Max(source.PositionX + body.Width + sweepX,
            Math.Max(current.CenterX + VanillaWorldGraveyardScene.ScanWidthTiles * 8f + 16f, closest.CenterX + closest.Width));
        float top = Math.Min(source.PositionY - sweepY,
            Math.Min(current.CenterY - VanillaWorldGraveyardScene.ScanHeightTiles * 8f - 16f, closest.CenterY - closest.Height));
        float bottom = Math.Max(source.PositionY + body.Height + sweepY,
            Math.Max(current.CenterY + VanillaWorldGraveyardScene.ScanHeightTiles * 8f + 16f, closest.CenterY + closest.Height));
        if (!float.IsFinite(left) || !float.IsFinite(right) || !float.IsFinite(top) || !float.IsFinite(bottom))
            return false;
        int x0 = (int)(Math.Clamp(left / 16f, 0, _tiles.Dimensions.WidthTiles - 1));
        int x1 = (int)(Math.Clamp(right / 16f, 0, _tiles.Dimensions.WidthTiles - 1));
        int y0 = (int)(Math.Clamp(top / 16f, 0, _tiles.Dimensions.HeightTiles - 1));
        int y1 = (int)(Math.Clamp(bottom / 16f, 0, _tiles.Dimensions.HeightTiles - 1));
        var first = TerrariaSectionGeometry.FromTile(_tiles.Dimensions, x0, y0);
        var last = TerrariaSectionGeometry.FromTile(_tiles.Dimensions, x1, y1);
        int columns = last.X - first.X + 1;
        int rows = last.Y - first.Y + 1;
        if ((long)columns * rows > 2048)
            return false;
        var versions = new long[columns * rows];
        for (int index = 0; index < versions.Length; index++)
        {
            long version = _tiles.GetSectionVersion(new(first.X + index % columns, first.Y + index / columns));
            if ((version & 1) != 0)
                return false;
            versions[index] = version;
        }
        fence = new RegionFence(_tiles, first, columns, versions);
        return fence.IsCurrent;
    }

    private sealed class RegionFence(WorldTileStore tiles, WorldSectionId first, int columns,
        long[] versions) : IVanillaFlyingEyeWorldFence1458
    {
        public bool IsCurrent
        {
            get
            {
                for (int index = 0; index < versions.Length; index++)
                    if (tiles.GetSectionVersion(new(first.X + index % columns, first.Y + index / columns)) != versions[index])
                        return false;
                return true;
            }
        }
    }
}
