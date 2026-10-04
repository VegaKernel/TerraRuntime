using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcSchedule1458
{
    private bool TryPlanHomePrelude(in NpcSnapshot before, in RuntimeTownNpcHomeCommit home,
        in RuntimeTownNpcScheduleConditions1458 conditions, ReadOnlySpan<RuntimeTownPlayerBounds1458> players,
        out NpcSnapshot next, out int floorX, out int floorY, out int originTileX, out int originTileY, out bool force)
    {
        next = before; force = false;
        originTileX = BottomTileX(in before, home.NpcType);
        originTileY = BottomTileY(in before, home.NpcType, 1f);
        floorX = home.HomeTileX; floorY = FindHomeFloor(home.HomeTileX, home.HomeTileY);
        FindGoodRestingSpot(in before, home.NpcType, conditions.DayTime, originTileX, originTileY, ref floorX, ref floorY);
        if (!conditions.ReturnHomeRequested || home.Status != TerrariaNpcHomeStatus.HasRoom ||
            IsInGoodRestingSpot(conditions.DayTime, before.Ai.Ai0, originTileX, originTileY, floorX, floorY,
                home.NpcType, before.Simulation.Wet) || !IsTeleportSafe(in before, floorX, floorY, home.NpcType, players)) return true;
        foreach (int offset in (ReadOnlySpan<int>)[0, -1, 1])
        {
            int x = floorX + offset;
            if (HasSolidTiles(x - 1, x + 1, floorY - 3, floorY - 1)) continue;
            float px = x * 16f + 8f - GetWidth(home.NpcType) / 2;
            float py = floorY * 16f - GetHeight(home.NpcType) - .1f;
            float vx = 0f, vy = 0f;
            int direction = before.Simulation.DirectionX;
            NpcAiState ai = before.Ai, local = before.Simulation.LocalAi;
            force = true;
            TryPlanForcedSitting(in before, floorX, floorY, home.NpcType,
                ref px, ref py, ref vx, ref vy, ref direction, ref ai, ref local, ref force);
            next = before with { PositionX = px, PositionY = py, VelocityX = vx, VelocityY = vy,
                Ai = ai, Simulation = before.Simulation with { DirectionX = direction, LocalAi = local } };
            return true;
        }
        // The source requests housing reassignment here. Its paired housing transaction is unowned.
        return false;
    }

    private void TryPlanForcedSitting(in NpcSnapshot source, int floorX, int floorY, NpcTypeId type,
        ref float x, ref float y, ref float vx, ref float vy, ref int direction,
        ref NpcAiState ai, ref NpcAiState local, ref bool force)
    {
        if (IsSittingExcluded(type) || ai.Ai0 == 5f || (uint)floorX >= (uint)tiles.Dimensions.WidthTiles ||
            floorY <= 0 || floorY >= tiles.Dimensions.HeightTiles) return;
        WorldTile chair = tiles.Get(floorX, floorY - 1);
        if (!IsNpcChair(in chair) || chair.TileType == VanillaTileIds.Chairs &&
            chair.FrameY >= TavernkeepReservedChairFrameYStart && chair.FrameY <= TavernkeepReservedChairFrameYEnd ||
            IsSeatOccupied(floorX, floorY)) return;
        direction = chair.FrameX != 0 ? 1 : -1;
        x = floorX * 16f + 8f + 2f * direction - GetWidth(type) / 2;
        y = floorY * 16f - GetHeight(type);
        vx = vy = 0f;
        ai = ai with { Ai0 = 5f, Ai1 = SittingDelayBaseTicks + random.Next(SittingDelayRandomTicks) };
        local = local with { Ai3 = 0f };
        force = true;
    }
}
