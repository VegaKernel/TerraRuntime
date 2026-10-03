using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcSchedule1458
{
    private const float TownWalkSpeed = 1f;
    private const float TownWalkAcceleration = .07f;
    private const int HomeIdleBand = 25;
    private const int HomeIdleTurnBand = 50;
    private const int HomeWalkBand = 35;

    // Owns the dry ordinary-resident AI007 body, not the subsequent social/emote offer chain.
    // AI and terrain correction must precede the shared physics tail and its sole state commit.
    private bool TryTickOrdinaryMotion(in NpcSnapshot before, in RuntimeTownNpcHomeCommit home,
        bool shelterAtHome, out NpcSnapshot committed)
    {
        committed = default;
        if (before.Ai.Ai0 is not (0f or 1f) || before.Simulation.Wet ||
            !VanillaTownNpcFacts1458.TryGetHousingCategory(home.NpcType, out int category) ||
            category != VanillaTownNpcFacts1458.OrdinaryHousingCategory)
            return false;

        int width = GetWidth(home.NpcType), height = GetHeight(home.NpcType);
        // Liquid/drowning navigation and pre-integration lava damage retain their separate admission boundary.
        if (VanillaWorldCollision.TryGetWetContact(tiles, before.PositionX, before.PositionY,
                width, height, out _))
            return false;
        float x = before.PositionX, y = before.PositionY;
        float vx = before.VelocityX, vy = before.VelocityY;
        int direction = before.Simulation.DirectionX;
        if (direction == 0) direction = 1;
        NpcAiState ai = before.Ai;
        NpcAiState local = before.Simulation.LocalAi;
        bool force = false;
        int myTileX = (int)((x + width / 2) / 16f);

        if (ai.Ai0 == 0f)
        {
            if (local.Ai3 > 0f) local = local with { Ai3 = local.Ai3 - 1f };
            vx = vx > .1f ? vx - .1f : vx < -.1f ? vx + .1f : 0f;
            if (ai.Ai1 > 0f) ai = ai with { Ai1 = ai.Ai1 - 1f };
            int aheadX = (int)((x + width / 2 + 15 * direction) / 16f);
            int feetY = (int)((y + height - 16f) / 16f);
            if (ai.Ai1 <= 0f)
            {
                if (!AvoidDryFall(myTileX, home.HomeTileX, direction, aheadX, feetY))
                {
                    ai = ai with { Ai0 = 1f, Ai1 = 200 + random.Next(300), Ai2 = 0f };
                    local = local with { Ai3 = 0f };
                }
                else
                {
                    direction *= -1;
                    ai = ai with { Ai1 = 60 + random.Next(120) };
                }
                force = true;
            }
            // Original continues the idle branch after a transition to walking.
            if (myTileX < home.HomeTileX - HomeIdleBand || myTileX > home.HomeTileX + HomeIdleBand)
            {
                if (local.Ai3 == 0f &&
                    ((myTileX < home.HomeTileX - HomeIdleTurnBand && direction == -1) ||
                     (myTileX > home.HomeTileX + HomeIdleTurnBand && direction == 1)))
                {
                    direction *= -1;
                    force = true;
                }
            }
            else if (random.Next(80) == 0 && local.Ai3 == 0f)
            {
                local = local with { Ai3 = 200f };
                direction *= -1;
                force = true;
            }
        }
        else if (shelterAtHome)
        {
            ai = ai with { Ai0 = 0f, Ai1 = 200 + random.Next(200) };
            local = local with { Ai3 = 60f };
            force = true;
        }
        else
        {
            WorldTile bottom = Cell(myTileX, (int)((y + height + 1f) / 16f));
            bool dungeon = bottom.TileType == VanillaTileIds.BlueDungeonBrick ||
                bottom.TileType == VanillaTileIds.GreenDungeonBrick || bottom.TileType == VanillaTileIds.PinkDungeonBrick;
            float timer = ai.Ai1 - 1f;
            if (!dungeon && Math.Abs(myTileX - home.HomeTileX) > HomeWalkBand &&
                ((x < home.HomeTileX * 16f && direction == -1) ||
                 (x > home.HomeTileX * 16f && direction == 1)))
                timer -= 5f;
            ai = ai with { Ai1 = timer };
            if (timer <= 0f)
            {
                ai = ai with { Ai0 = 0f, Ai1 = 300 + random.Next(300) + random.Next(900), Ai2 = 0f };
                local = local with { Ai3 = 60f };
                force = true;
            }
            if (vx < -TownWalkSpeed || vx > TownWalkSpeed)
            {
                if (vy == 0f) { vx *= .8f; vy *= .8f; }
            }
            else if (vx < TownWalkSpeed && direction == 1)
                vx = Math.Min(TownWalkSpeed, vx + TownWalkAcceleration);
            else if (vx > -TownWalkSpeed && direction == -1)
            {
                vx -= TownWalkAcceleration;
                // Preserve the source's positive comparison in the leftward branch.
                if (vx > TownWalkSpeed) vx = TownWalkSpeed;
            }

            bool matchPlatforms = home.HomeTileY * 16 - 32 <= y;
            if (!matchPlatforms && vy == 0f)
                y = VanillaWorldPlayerStepCollision.StepDown(tiles, x, y, vx, vy, width, height).PositionY;
            if (vy >= 0f)
                y = VanillaWorldPlayerStepCollision.StepUp(tiles, x, y, vx, width, height, holdsMatching: matchPlatforms, npcSpecialChecks: true).PositionY;

            if (vy == 0f)
            {
                int aheadX = (int)((x + width / 2 + 15 * direction) / 16f);
                int feetY = (int)((y + height - 16f) / 16f);
                bool keepWalking = ShouldKeepWalking(in before, ai.Ai1, x, y, width, height);
                bool avoidFall = AvoidDryFall(myTileX, home.HomeTileX, direction, aheadX, feetY);
                int supports = 0;
                for (int offset = -1; offset <= 1; offset++)
                    if (Solid(Cell((int)((x + width / 2) / 16f) + offset, feetY + 1))) supports++;
                if (avoidFall && supports <= 2)
                {
                    force |= vx != 0f;
                    avoidFall = false;
                    keepWalking = false;
                    ai = ai with { Ai0 = 0f, Ai1 = 50 + random.Next(50), Ai2 = 0f };
                    local = local with { Ai3 = 40f };
                }
                if (x == local.Ai3) { direction *= -1; force = true; }
                local = local with { Ai3 = -1f };
                WorldTile ahead = Cell(aheadX, feetY), above = Cell(aheadX, feetY - 1), high = Cell(aheadX, feetY - 2);
                // Door state/occupancy and remembered closeDoor are separate, deliberately unadmitted here.
                if (!VanillaTileIds.IsClosedDoor(high.TileType) &&
                    ((vx < 0f && direction == -1) || (vx > 0f && direction == 1)))
                {
                    bool turn = false;
                    if (FullSolid(high) && (height / 16 >= 3 || FullSolid(above)))
                    {
                        if (!HasNavigationObstacle(aheadX - direction * 2, aheadX - direction, feetY - 5, feetY - 1) &&
                            !HasNavigationObstacle(aheadX, aheadX, feetY - 5, feetY - 3)) { vy = -6f; force = true; }
                        else turn = true;
                    }
                    else if (FullSolid(above))
                    {
                        if (!HasNavigationObstacle(aheadX - direction * 2, aheadX - direction, feetY - 4, feetY - 1) &&
                            !HasNavigationObstacle(aheadX, aheadX, feetY - 4, feetY - 2)) { vy = -5f; force = true; }
                        else turn = true;
                    }
                    else if (y + height - feetY * 16 > 20f && Solid(ahead) && ahead.Shape is not (2 or 3))
                    {
                        if (!HasNavigationObstacle(aheadX - direction * 2, aheadX, feetY - 3, feetY - 1)) { vy = -4.4f; force = true; }
                        else turn = true;
                    }
                    else if (avoidFall) turn = true;
                    if (turn) { direction *= -1; vx *= -1f; force = true; }
                    if (keepWalking) { ai = ai with { Ai1 = 90f }; force = true; }
                    if (vy < 0f) local = local with { Ai3 = x };
                }
            }
        }
        double surface = tiles.WorldSurfaceTiles ?? Math.Max(1d, tiles.Dimensions.HeightTiles / 3d);
        if (before.Ai.Ai0 == 1f && !shelterAtHome &&
            before.Simulation.OldPositionX == x && before.Simulation.OldPositionY == y &&
            VanillaTownNpcFacts1458.TryGetDefinition(home.NpcType, out VanillaNpcDefinition definition) &&
            VanillaNpcGravity.TryApply(in definition, before.PositionY, before.VelocityY, false,
                NpcLiquidContactKind.None, tiles.Dimensions.WidthTiles, surface, out VanillaNpcGravityResult gravity) &&
            before.Simulation.OldVelocityX == vx && before.Simulation.OldVelocityY == vy + gravity.Parameters.Gravity)
        {
            float stuck = local.Ai2;
            local = local with { Ai2 = stuck + 1f };
            if (stuck >= 30f)
            {
                local = local with { Ai2 = 0f };
                ai = ai with { Ai0 = 1f, Ai1 = 200 + random.Next(300), Ai2 = 0f };
                direction *= -1;
                vx *= -1f;
                force = true;
            }
        }
        var update = new NpcStateUpdate(before.Type, before.NetId, x, y, vx, vy, before.Target, ai,
            before.Simulation with { DirectionX = direction, DirectionY = -1, LocalAi = local });
        if (!VanillaNpcWorldMotionAiStepper.TryFinishPhysics(tiles, surface, in before, in update, out NpcStateUpdate moved))
            return false;
        return npcs.TryUpdate(before.Handle, in moved, out committed, forceSync: force);
    }

    private bool AvoidDryFall(int myX, int homeX, int direction, int aheadX, int feetY)
    {
        if (Math.Abs(myX - homeX) > HomeWalkBand && direction == Math.Sign(homeX - myX)) return false;
        for (int offset = -1; offset <= 4; offset++)
        {
            WorldTile tile = Cell(aheadX, feetY + offset);
            if (tile.LiquidAmount > 0) return true;
            if (Solid(tile)) return false;
        }
        return true;
    }
    private bool ShouldKeepWalking(in NpcSnapshot before, float timer, float x, float y, int width, int height)
    {
        if (timer >= 30f) return false;
        // Source PlotLine excludes the endpoint: vertical top-to-bottom and horizontal right-to-left.
        int left = (int)(x / 16f), right = (int)((x + width) / 16f);
        int firstColumn = right == left ? left : left + 1;
        for (int tileX = firstColumn; tileX <= right; tileX++)
            for (int tileY = (int)(y / 16f); tileY < (int)((y + height) / 16f); tileY++)
                if (tileX < 1 || tileY < 1 || tileX >= tiles.Dimensions.WidthTiles - 1 ||
                    tileY >= tiles.Dimensions.HeightTiles - 1 ||
                    (Cell(tileX, tileY).IsActive && VanillaTownNpcNavigationCatalog1458.IsAvoided(Cell(tileX, tileY).TileType)))
                    return true;
        Span<NpcSnapshot> active = stackalloc NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
        int count = npcs.CopyActive(active);
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot peer = active[index];
            if (peer.Handle == before.Handle || peer.VelocityX != 0f ||
                !(peer.Simulation.Friendly ?? VanillaNpcChaseability1458.FriendlyAtSpawn(peer.Type)) ||
                !VanillaNpcDefinitionCatalog.TryGet(peer.TypeIdentity, peer.NetIdentity, out VanillaNpcDefinition definition) ||
                !definition.TryResolveHitbox(peer.Simulation, out var hitbox)) continue;
            if (x - 20f < peer.PositionX + hitbox.Width && x + width + 20f > peer.PositionX &&
                y < peer.PositionY + hitbox.Height && y + height > peer.PositionY) return true;
        }
        return false;
    }
    private bool HasNavigationObstacle(int startX, int endX, int startY, int endY)
    {
        (startX, endX) = (Math.Min(startX, endX), Math.Max(startX, endX));
        if (startX < 0 || endX >= tiles.Dimensions.WidthTiles || startY < 0 ||
            endY >= tiles.Dimensions.HeightTiles - 40) return true;
        for (int x = startX; x <= endX; x++)
            for (int y = startY; y <= endY; y++)
                if (FullSolid(Cell(x, y))) return true;
        return false;
    }
    private WorldTile Cell(int x, int y) => (uint)x < (uint)tiles.Dimensions.WidthTiles &&
        (uint)y < (uint)tiles.Dimensions.HeightTiles ? tiles.Get(x, y) : default;
    private static bool Solid(in WorldTile tile) => tile.IsActive && !tile.IsActuated && VanillaTileCollisionCatalog.IsSolid(tile.TileType);
    private static bool FullSolid(in WorldTile tile) => Solid(in tile) && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
}
