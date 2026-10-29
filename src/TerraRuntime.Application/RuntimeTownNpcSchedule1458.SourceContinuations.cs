using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcSchedule1458
{
    private static bool IsYellowTownSlime(NpcTypeId type) => type == VanillaNpcIds.TownSlimeYellow;

    internal static bool OwnsTransformResidentContinuation(in NpcSnapshot npc) =>
        (npc.TypeIdentity == VanillaNpcIds.TaxCollector || IsYellowTownSlime(npc.TypeIdentity)) &&
        npc.Simulation.WetCount.HasValue && npc.Simulation.BreathCounter.HasValue;

    private static bool HasTransformResidentContext(in NpcSnapshot npc) =>
        npc.Simulation.WetCount is >= 0 and <= 10 && npc.Simulation.BreathCounter is >= 0 and <= 6 &&
        npc.Simulation.Breath is >= 0 and <= 200 && npc.Simulation.LavaWet is false &&
        npc.Simulation.HoneyWet.HasValue && npc.Simulation.ShimmerWet is false &&
        npc.Simulation.ShimmerTransparency == 0f &&
        (npc.Simulation.TeleportTime is 0f || npc.Simulation.TeleportTime == 1f && npc.Simulation.TeleportStyle == 13);

    private bool HasTransactionalResidentTerrain(in NpcSnapshot npc)
    {
        // Existing ordinary residents can open doors eagerly. This selected continuation may
        // still refuse during drowning/collision, so it cannot enter that live tile mutation path.
        if (HasRememberedDoor(npc.Handle) || Math.Abs(npc.VelocityX) > 32f || Math.Abs(npc.VelocityY) > 32f ||
            npc.PositionY > tiles.Dimensions.HeightTiles * 16f - 640f + GetHeight(npc.TypeIdentity)) return false;
        int left = (int)MathF.Floor((npc.PositionX - 48f) / 16f);
        int right = (int)MathF.Floor((npc.PositionX + GetWidth(npc.TypeIdentity) + 48f) / 16f);
        int top = (int)MathF.Floor((npc.PositionY - 80f) / 16f);
        int bottom = (int)MathF.Floor((npc.PositionY + GetHeight(npc.TypeIdentity) + 80f) / 16f);
        if (left < 1 || top < 1 || right >= tiles.Dimensions.WidthTiles - 1 || bottom >= tiles.Dimensions.HeightTiles - 1)
            return false;
        for (int x = left; x <= right; x++)
            for (int y = top; y <= bottom; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (!tile.IsActive) continue;
                if (tile.TileType == VanillaTileIds.PressurePlates || tile.TileType == VanillaTileIds.LandMine ||
                    !tile.IsActuated && (VanillaTileIds.IsClosedDoor(tile.TileType) ||
                        tile.TileType == VanillaTileIds.OpenDoor || tile.TileType == VanillaTileIds.TallGateOpen)) return false;
            }
        return true;
    }

    internal bool AvoidSourceResidentFall(int myX, int homeX, int direction, int aheadX, int feetY, int width, int height)
    {
        if (Math.Abs(myX - homeX) > HomeWalkBand && direction == Math.Sign(homeX - myX)) return false;
        bool avoid = true, lava = false;
        int depth = 0, landingY = 0;
        for (int down = -1; down <= 4; down++)
        {
            WorldTile tile = Cell(aheadX, feetY + down);
            if (tile.LiquidAmount > 0)
            {
                depth++;
                if (tile.LiquidKind == WorldLiquidKind.Lava) { lava = true; break; }
            }
            if (Solid(in tile))
            { if (depth > 0) landingY = feetY + down; avoid = false; break; }
        }
        avoid |= lava;
        if (depth >= Math.Ceiling(height / 16f)) avoid = true;
        if (!avoid && aheadX != 0 && landingY != 0)
            avoid = new VanillaGroundCritterTerrain1458(tiles).Drowning(aheadX * 16 + 8 - width / 2,
                landingY * 16 - height, width, height, includeSlopes: false);
        return avoid;
    }

    private NpcSnapshot PlanSourceLocalHome(in NpcSnapshot source, out bool updated)
    {
        updated = false;
        if (source.Simulation.HomeTileX != -1 || source.Simulation.HomeTileY != -1 || source.VelocityY != 0f)
            return source;
        updated = true;
        return source with { Simulation = source.Simulation with {
            HomeTileX = (int)((source.PositionX + GetWidth(source.TypeIdentity) / 2) / 16f),
            HomeTileY = (int)((source.PositionY + GetHeight(source.TypeIdentity) + 4f) / 16f) } };
    }

    private void TryOfferSlimeIdle(float vx, int chance, ref NpcAiState ai, ref NpcAiState local, ref bool force)
    {
        if (vx != 0f || random.Next(chance) != 0) return;
        // The first timer is a real source offer even though the slime specialization replaces it.
        _ = random.Next(300);
        ai = ai with { Ai0 = 20f, Ai1 = 180 + random.Next(240), Ai2 = 0f };
        local = local with { Ai3 = 0f };
        force = true;
    }

    private NpcStateUpdate FinishSlimeSwimming(in NpcStateUpdate source, ref bool force)
    {
        if (!IsYellowTownSlime(new(source.Type)) || !source.Simulation.Wet) return source;
        int direction = source.Simulation.DirectionX;
        float vx = source.VelocityX;
        int centerTile = (int)((source.PositionX + GetWidth(new(source.Type)) / 2f) / 16f);
        if (source.Simulation.CollideX || centerTile < 5 && direction == -1 ||
            centerTile > tiles.Dimensions.WidthTiles - 5 && direction == 1)
        { direction *= -1; vx *= -.25f; force = true; }
        float vy = source.VelocityY * .9f;
        vy -= .5f;
        return source with { VelocityX = vx, VelocityY = Math.Max(-15f, vy),
            Simulation = source.Simulation with { DirectionX = direction } };
    }

    private bool TryPlanSourceDrowning(in NpcStateUpdate source, out NpcStateUpdate next)
    {
        next = source;
        if (source.Simulation.Breath is not int breath || source.Simulation.BreathCounter is not int counter) return false;
        var terrain = new VanillaGroundCritterTerrain1458(tiles);
        bool submerged = terrain.Drowning(source.PositionX, source.PositionY,
            GetWidth(new(source.Type)), GetHeight(new(source.Type)), includeSlopes: false);
        if (submerged)
        {
            if (++counter >= 7)
            {
                counter = 0;
                // Exhaustion also owns a town damage/death and low-priority network transaction.
                if (breath <= 1) return false;
                breath--;
            }
            _ = random.Next(20);
        }
        else { breath = Math.Min(200, breath + 3); counter = 0; }
        next = source with { Simulation = source.Simulation with { Breath = breath, BreathCounter = counter } };
        return terrain.IsCurrent;
    }
}

internal sealed partial class RuntimeTownNpcCombat1458
{
    internal void PublishSourceLocalHome(in RuntimeTownNpcHomeCommit home) => contactReplication?.TryPublishTownHome(in home);

    internal bool TryPrepareSourceHome(in NpcSnapshot before, in NpcStateUpdate final,
        in RuntimeTownNpcHomeCommit home, bool updatedByAi,
        out RuntimeNpcReplicationRegistry.SourceTownHomePhase1458? plan)
    {
        plan = null;
        if (contactReplication is null) return true;
        if (!contactReplication.TryPrepareSourceTownHome(in before, in final, in home, updatedByAi, out var prepared)) return false;
        plan = prepared;
        return true;
    }

    internal bool IsSourceHomeCurrent(RuntimeNpcReplicationRegistry.SourceTownHomePhase1458? plan) =>
        plan is not { } prepared || contactReplication!.IsSourceTownHomeCurrent(in prepared);

    internal bool TryAdoptSourceHome(RuntimeNpcReplicationRegistry.SourceTownHomePhase1458? plan, in NpcSnapshot accepted) =>
        plan is not { } prepared || contactReplication!.TryAdoptSourceTownHome(in prepared, in accepted);

    internal void PublishLateSourceHome(RuntimeNpcReplicationRegistry.SourceTownHomePhase1458? plan)
    {
        if (plan is { } prepared) contactReplication!.PublishSourceTownHome(in prepared);
    }
}
