using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

// This adapter delegates motion to the shared collision implementation; it owns no NPC state.
// Each instance belongs to a speculative AI plan and fences every world section it actually reads.
internal sealed class VanillaGroundCritterTerrain1458(WorldTileStore tiles) : IVanillaGroundCritterTerrain1458
{
    private readonly Dictionary<WorldSectionId, long> sections = [];
    private bool valid = true;

    internal bool IsCurrent
    {
        get
        {
            if (!valid) return false;
            foreach (var section in sections)
                if (tiles.GetSectionVersion(section.Key) != section.Value) return false;
            return true;
        }
    }

    public GroundCritterTile1458 Tile(int x, int y)
    {
        WorldTile tile = Cell(x, y);
        bool active = tile.IsActive && !tile.IsActuated;
        return new(active && VanillaTileCollisionCatalog.IsSolid(tile.TileType),
            VanillaTileCollisionCatalog.IsSolidTop(tile.TileType), tile.Shape is 2 or 3,
            tile.TileType == VanillaTileIds.BlueDungeonBrick || tile.TileType == VanillaTileIds.GreenDungeonBrick ||
            tile.TileType == VanillaTileIds.PinkDungeonBrick,
            StrictSolid: active && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
                !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) && !tile.IsHalfBrick && tile.SourceSlope == 0);
    }

    internal int TileType(int x, int y) => Cell(x, y).TileType.Value;

    public bool TryGetWaterLine(int centerTileX, int centerTileY, out bool found, out float waterLineHeight)
    {
        found = false;
        waterLineHeight = 0f;
        if (centerTileX < 10 || centerTileY < 10 ||
            centerTileX >= tiles.Dimensions.WidthTiles - 10 || centerTileY >= tiles.Dimensions.HeightTiles - 10)
            return IsCurrent;
        // Collision.GetWaterLine examines this four-cell column, with integer
        // liquid/16 arithmetic. Reuse the fish adapter's exact existing query.
        for (int y = centerTileY - 2; y <= centerTileY + 1; y++) Cell(centerTileX, y);
        if (!IsCurrent) return false;
        found = VanillaFishWorldEnvironment1458.TryGetWaterLineAtTile(tiles, centerTileX, centerTileY, out waterLineHeight);
        return IsCurrent;
    }

    internal bool TryCanHit(float x, float y, int width, int height,
        in VanillaNpcRawPlayer1458 target, out bool visible)
    {
        visible = false;
        if (!target.IsValid || !float.IsFinite(x) || !float.IsFinite(y) || width <= 0 || height <= 0)
            return false;
        if (!FenceLineOfSight(x, y, width, height, target.PositionX, target.PositionY,
                target.Width, target.Height)) return false;
        visible = VanillaWorldCanHit.HasLineOfSight(tiles, x, y, width, height,
            target.PositionX, target.PositionY, target.Width, target.Height);
        return IsCurrent;
    }

    internal bool FenceLineOfSight(float x, float y, int width, int height,
        float targetX, float targetY, int targetWidth, int targetHeight)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(targetX) ||
            !float.IsFinite(targetY) || width < 0 || height < 0 || targetWidth < 0 || targetHeight < 0)
            return false;
        // CanHit and CanHitLine traverse center tiles and their neighbors.
        // Zero-size point queries are valid for CanHitLine.
        int left = Math.Clamp((int)(MathF.Min(x, targetX) / 16f) - 3, 0, tiles.Dimensions.WidthTiles - 1);
        int right = Math.Clamp((int)(MathF.Max(x + width, targetX + targetWidth) / 16f) + 3, 0, tiles.Dimensions.WidthTiles - 1);
        int top = Math.Clamp((int)(MathF.Min(y, targetY) / 16f) - 3, 0, tiles.Dimensions.HeightTiles - 1);
        int bottom = Math.Clamp((int)(MathF.Max(y + height, targetY + targetHeight) / 16f) + 3, 0, tiles.Dimensions.HeightTiles - 1);
        var first = TerrariaSectionGeometry.FromTile(tiles.Dimensions, left, top);
        var last = TerrariaSectionGeometry.FromTile(tiles.Dimensions, right, bottom);
        for (int sy = first.Y; sy <= last.Y; sy++)
            for (int sx = first.X; sx <= last.X; sx++) RetainSection(new(sx, sy));
        return IsCurrent;
    }

    internal bool HasConveyorAtBody(float x, float y, int width, int height)
    {
        int left = Math.Max(0, (int)((x - 1f) / 16f));
        int right = Math.Min(tiles.Dimensions.WidthTiles - 1, (int)((x + width + 1f) / 16f));
        int top = Math.Max(0, (int)((y - 1f) / 16f));
        int bottom = Math.Min(tiles.Dimensions.HeightTiles - 1, (int)((y + height + 1f) / 16f));
        for (int tx = left; tx <= right; tx++)
            for (int ty = top; ty <= bottom; ty++)
            {
                var tile = Cell(tx, ty);
                if (tile.IsActive && !tile.IsActuated &&
                    (tile.TileType == VanillaTileIds.ConveyorBeltLeft || tile.TileType == VanillaTileIds.ConveyorBeltRight))
                    return true;
            }
        return false;
    }

    public bool NavigationObstacle(int left, int right, int top, int bottom)
    {
        (left, right) = (Math.Min(left, right), Math.Max(left, right));
        if (left < 0 || right >= tiles.Dimensions.WidthTiles || top < 0 || bottom >= tiles.Dimensions.HeightTiles - 40)
            return true;
        for (int x = left; x <= right; x++)
            for (int y = top; y <= bottom; y++)
            {
                GroundCritterTile1458 tile = Tile(x, y);
                if (tile.Solid && !tile.SolidTop) return true;
            }
        return false;
    }

    public GroundCritterStep1458 StepDown(float x, float y, float vx, float vy, int width, int height)
    {
        FenceBody(x, y, vx, vy, width, height);
        var step = VanillaWorldPlayerStepCollision.StepDown(tiles, x, y, vx, vy, width, height);
        return new(x, step.PositionY, vx, vy);
    }

    public GroundCritterStep1458 StepUp(float x, float y, float vx, float vy, int width, int height, bool matchPlatforms)
    {
        FenceBody(x, y, vx, vy, width, height);
        var step = VanillaWorldPlayerStepCollision.StepUp(tiles, x, y, vx, width, height,
            holdsMatching: matchPlatforms, npcSpecialChecks: true);
        return new(x, step.PositionY, vx, vy);
    }

    // Collision.DrownCollision(gravDir:1, includeSlopes:true), TerrariaServer 1.4.5.8.
    // It samples a narrow head rectangle and excludes lava/Shimmer without treating actuated tiles
    // as a different liquid container. This is not ordinary body WetCollision.
    public bool Drowning(float x, float y, int width, int height)
    {
        int sampleWidth = Math.Min(10, width), sampleHeight = Math.Min(12, height);
        float headX = x + width / 2 - sampleWidth / 2, headY = y - 2f;
        int left = Math.Clamp((int)(x / 16f) - 1, 0, tiles.Dimensions.WidthTiles - 1);
        int right = Math.Clamp((int)((x + width) / 16f) + 2, 0, tiles.Dimensions.WidthTiles - 1);
        int top = Math.Clamp((int)(y / 16f) - 1, 0, tiles.Dimensions.HeightTiles - 40);
        int bottom = Math.Clamp((int)((y + height) / 16f) + 2, 0, tiles.Dimensions.HeightTiles - 40);
        for (int tx = left; tx < right; tx++)
            for (int ty = top; ty < bottom; ty++)
            {
                WorldTile tile = Cell(tx, ty);
                if (tile.LiquidAmount == 0 || tile.LiquidKind is WorldLiquidKind.Lava or WorldLiquidKind.Shimmer)
                    continue;
                if (ty == top && tile.IsActive && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
                    !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) && tile.Shape == 0)
                    continue;
                float empty = (256 - tile.LiquidAmount) / 32f * 2f;
                float liquidTop = ty * 16 + empty;
                int liquidHeight = 16 - (int)empty;
                if (headX + sampleWidth > tx * 16 && headX < tx * 16 + 16 &&
                    headY + sampleHeight > liquidTop && headY < liquidTop + liquidHeight)
                    return true;
            }
        return false;
    }

    internal void FindRestingSpot(int homeX, int homeY, bool dayTime,
        ReadOnlySpan<TerraRuntime.Contracts.Runtime.NpcSnapshot> peers, out int x, out int y)
    {
        x = homeX; y = homeY;
        if (homeX == -1 || homeY == -1) return;
        while (y < tiles.Dimensions.HeightTiles - 20)
        {
            WorldTile tile = Cell(x, y);
            if (tile.IsActive && !tile.IsActuated &&
                (VanillaTileCollisionCatalog.IsSolid(tile.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType) ||
                 VanillaTileIds.IsPlatform(tile.TileType))) break;
            y++;
        }
        if (dayTime) return;
        int bestDistance = -1, seatX = -1, seatY = -1;
        for (int cx = x - 7; cx <= x + 7; cx++)
            for (int cy = y + 2; cy >= y - 6; cy -= 2)
            {
                WorldTile tile = Cell(cx, cy);
                if (!tile.IsActive || !VanillaTileIds.IsNpcChair(tile.TileType)) continue;
                int distance = Math.Abs(cx - x) + Math.Abs(cy - y);
                if (bestDistance >= 0 && distance >= bestDistance) continue;
                bestDistance = distance; seatX = cx; seatY = cy;
            }
        if (bestDistance < 0) return;
        WorldTile chair = Cell(seatX, seatY);
        if (chair.TileType.Value is 15 or 497)
        {
            if (chair.FrameY % 40 != 0) seatY--;
            seatY += 2;
        }
        foreach (var peer in peers)
        {
            if (peer.Simulation.TownNpc != true || peer.Ai.Ai0 != 5f ||
                !TerraRuntime.Gameplay.Npcs.VanillaNpcDefinitionCatalog.TryGet(peer.TypeIdentity, peer.NetIdentity, out var definition) ||
                definition.AiStyle.Value != 7 || !definition.TryResolveHitbox(peer.Simulation, out var body)) continue;
            if ((int)((peer.PositionX + body.Width / 2f) / 16f) == seatX &&
                (int)((peer.PositionY + body.Height - 2f) / 16f) == seatY) return;
        }
        x = seatX; y = seatY;
    }

    internal void FenceBody(float x, float y, float vx, float vy, int width, int height)
    {
        int left = Math.Clamp((int)((x - Math.Abs(vx)) / 16f) - 4, 0, tiles.Dimensions.WidthTiles - 1);
        int right = Math.Clamp((int)((x + width + Math.Abs(vx)) / 16f) + 4, 0, tiles.Dimensions.WidthTiles - 1);
        int top = Math.Clamp((int)((y - Math.Abs(vy)) / 16f) - 8, 0, tiles.Dimensions.HeightTiles - 1);
        int bottom = Math.Clamp((int)((y + height + Math.Abs(vy)) / 16f) + 8, 0, tiles.Dimensions.HeightTiles - 1);
        for (int tx = left; tx <= right; tx++)
            for (int ty = top; ty <= bottom; ty++) Cell(tx, ty);
    }

    private WorldTile Cell(int x, int y)
    {
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles)
        { valid = false; return default; }
        var section = TerrariaSectionGeometry.FromTile(tiles.Dimensions, x, y);
        RetainSection(section);
        return tiles.Get(x, y);
    }

    private void RetainSection(WorldSectionId section)
    {
        long version = tiles.GetSectionVersion(section);
        if ((version & 1) != 0) valid = false;
        if (!sections.TryAdd(section, version) && sections[section] != version) valid = false;
    }
}
