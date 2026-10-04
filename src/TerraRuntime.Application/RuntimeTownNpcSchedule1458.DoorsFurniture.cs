using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTownNpcSchedule1458
{
    private sealed class TownDoorCloseRandom(IRuntimeTownNpcScheduleRandom1458 random) : IVanillaDoorCloseRandom1458
    {
        // WorldGen.genRand aliases Main.rand in 1.4.5.8: close-row choices share the AI stream.
        public int NextClosedDoorFrameColumn() => random.Next(3);
    }

    internal bool HasRememberedDoor(NpcHandle handle) =>
        doors.TryGetValue(handle.Slot, out RememberedDoor door) && door.Handle == handle;

    private bool TryOpenTownDoor(in NpcSnapshot before, int x, int y, int direction, TileTypeId type)
    {
        var preferred = new VanillaGroundFighterDoorOpeningIntent(x, y, direction, type);
        bool opened = doorSink.TryOpen(in preferred);
        if (!opened && type == VanillaTileIds.ClosedDoor)
        {
            var opposite = preferred with { DirectionX = -direction };
            opened = doorSink.TryOpen(in opposite);
        }
        if (opened) doors[before.Handle.Slot] = new RememberedDoor(before.Handle, x, y);
        return opened;
    }

    private void TryCloseRememberedDoor(in NpcSnapshot before, float x, float y, int width, int height, int direction)
    {
        if (!doors.TryGetValue(before.Handle.Slot, out RememberedDoor door) || door.Handle != before.Handle)
            return;
        float centerX = (x + width / 2) / 16f, centerY = (y + height / 2) / 16f;
        if (centerX <= door.X + 2 && centerX >= door.X - 2) return;
        WorldTile cell = Cell(door.X, door.Y);
        bool isDoor = cell.TileType == VanillaTileIds.OpenDoor;
        bool isGate = cell.TileType == VanillaTileIds.TallGateOpen;
        if ((!isDoor && !isGate) || doorSink.TryClose(door.X, door.Y, isGate, direction) ||
            centerX > door.X + 4 || centerX < door.X - 4 || centerY > door.Y + 4 || centerY < door.Y - 4)
            doors.Remove(before.Handle.Slot);
    }

    private void TryOfferWalkingFurniture(in NpcSnapshot before, NpcTypeId type,
        ReadOnlySpan<RuntimeTownPlayerSeat1458> seatedPlayers, ref float x, ref float y,
        ref float vx, ref float vy, ref int direction, ref NpcAiState ai, ref NpcAiState local, ref bool force)
    {
        if (ai.Ai0 != 1f || vy != 0f || !IsQuietFurnitureAdmission(in before)) return;
        // Source else-if semantics: a successful chair offer roll skips the furniture roll,
        // even if no usable chair exists. These are actual offers, never placeholder RNG burns.
        if (random.Next(300) == 0)
        {
            int tileX = (int)((x + GetWidth(type) / 2f) / 16f);
            int tileY = (int)((y + GetHeight(type) - 2f) / 16f);
            if (!Interior(tileX, tileY) || IsWalkingSeatOccupied(before.Handle, tileX, tileY, seatedPlayers)) return;
            WorldTile seat = Cell(tileX, tileY);
            if (!VanillaTileIds.IsNpcChair(seat.TileType) ||
                (seat.TileType == VanillaTileIds.Chairs && seat.FrameY is >= TavernkeepReservedChairFrameYStart and <= TavernkeepReservedChairFrameYEnd)) return;
            ai = ai with { Ai0 = 5f, Ai1 = SittingDelayBaseTicks + random.Next(SittingDelayRandomTicks) };
            direction = seat.FrameX != 0 ? 1 : -1;
            x = tileX * 16f + 8f + 2f * direction - GetWidth(type) / 2f;
            y = tileY * 16f + 16f - GetHeight(type);
            vx = vy = 0f;
            local = local with { Ai3 = 0f };
            force = true;
        }
        else if (random.Next(600) == 0 && !TouchesAvoidedFurniture(x, y, GetWidth(type), GetHeight(type)))
        {
            int tileX = (int)((x + GetWidth(type) / 2f + direction * 10f) / 16f);
            int tileY = (int)((y + GetHeight(type) / 2f) / 16f);
            WorldTile furniture = Cell(tileX, tileY);
            if (!Interior(tileX, tileY) || !furniture.IsActive || furniture.IsActuated ||
                !VanillaTownNpcNavigationCatalog1458.IsInteractable(furniture.TileType)) return;
            ai = ai with { Ai0 = 9f, Ai1 = 40 + random.Next(90) };
            vx = vy = 0f;
            local = local with { Ai3 = 0f };
            force = true;
        }
    }

    private bool IsQuietFurnitureAdmission(in NpcSnapshot before)
    {
        // Full danger/flee and NPC stinky-buff ownership remain separate. Admit the quiet
        // retained actor table conservatively; a possible hostile excludes these offers.
        Span<NpcSnapshot> active = stackalloc NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
        int count = npcs.CopyActive(active);
        for (int i = 0; i < count; i++)
        {
            NpcSnapshot npc = active[i];
            if (npc.Handle == before.Handle ||
                (npc.Simulation.Friendly ?? VanillaNpcChaseability1458.FriendlyAtSpawn(npc.Type))) continue;
            if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition) ||
                (npc.Simulation.DamageOverride ?? definition.Damage) > 0) return false;
        }
        return true;
    }

    private bool IsWalkingSeatOccupied(NpcHandle self, int x, int y, ReadOnlySpan<RuntimeTownPlayerSeat1458> seatedPlayers)
    {
        Span<NpcSnapshot> active = stackalloc NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
        int count = npcs.CopyActive(active);
        for (int i = 0; i < count; i++)
        {
            NpcSnapshot peer = active[i];
            if (peer.Handle != self && peer.Ai.Ai0 == 5f &&
                VanillaTownNpcFacts1458.TryGetDefinition(peer.TypeIdentity, out _) &&
                BottomTileX(in peer, peer.TypeIdentity) == x && BottomTileY(in peer, peer.TypeIdentity, -2f) == y)
                return true;
        }
        foreach (RuntimeTownPlayerSeat1458 player in seatedPlayers)
            if (player.Slot < byte.MaxValue &&
                (int)((player.Bounds.X + player.Bounds.Width / 2f) / 16f) == x &&
                (int)((player.Bounds.Y + player.Bounds.Height / 2f) / 16f) == y) return true;
        return false;
    }

    private bool TouchesAvoidedFurniture(float x, float y, int width, int height)
    {
        int left = (int)(x / 16f), right = (int)((x + width) / 16f);
        for (int tx = right == left ? left : left + 1; tx <= right; tx++)
            for (int ty = (int)(y / 16f); ty < (int)((y + height) / 16f); ty++)
                if (!Interior(tx, ty) || (Cell(tx, ty).IsActive &&
                    VanillaTownNpcNavigationCatalog1458.IsAvoided(Cell(tx, ty).TileType))) return true;
        return false;
    }

    private bool TryTickFurniture(in NpcSnapshot before, out NpcSnapshot committed)
    {
        float remaining = before.Ai.Ai1 - 1f;
        NpcAiState ai = before.Ai with { Ai1 = remaining };
        NpcAiState local = before.Simulation.LocalAi;
        if (remaining <= 0f)
        {
            ai = ai with { Ai0 = 0f, Ai1 = StandingDelayBaseTicks + random.Next(StandingDelayRandomTicks), Ai2 = 0f };
            local = local with { Ai3 = StandingLocalDelayBaseTicks + random.Next(StandingDelayRandomTicks) };
        }
        var update = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
            before.VelocityX * .8f, before.VelocityY, before.Target, ai, before.Simulation with { LocalAi = local });
        double surface = tiles.WorldSurfaceTiles ?? Math.Max(1d, tiles.Dimensions.HeightTiles / 3d);
        committed = default;
        return VanillaNpcWorldMotionAiStepper.TryFinishPhysics(tiles, surface, in before, in update, out var moved) &&
            npcs.TryUpdate(before.Handle, in moved, out committed, forceSync: remaining <= 0f);
    }

    private bool Interior(int x, int y) => x >= 1 && y >= 1 &&
        x < tiles.Dimensions.WidthTiles - 1 && y < tiles.Dimensions.HeightTiles - 1;
}
