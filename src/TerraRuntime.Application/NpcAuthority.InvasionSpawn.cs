using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class NpcAuthority
{
    private enum InvasionSpawnAttempt { Ordinary, Continue, Stop }

    private InvasionSpawnAttempt TryNaturalInvasionSpawn(in VanillaNpcTargetCandidate player, float nearbyNpcCount)
    {
        if (invasion is null || !invasion.TryCapture(out var captured))
            return InvasionSpawnAttempt.Continue;
        var invasionState = captured.State;
        if (invasionState.Type == 0)
            return InvasionSpawnAttempt.Ordinary;
        if (worldTiles is null || worldClock is null || naturalSpawnWorldFacts is not { } facts ||
            naturalSpawnRandom is not SystemVanillaNpcRandom adapter ||
            adapter.SourceRandom is not VanillaUnifiedRandom1458 live ||
            !npcs.TryCreateNaturalSpawnPreview(live, out var preview) || preview is null)
            return InvasionSpawnAttempt.Continue;

        // Capture every player before source offers: active player count contributes to the final rate cap.
        var playerStates = new PlayerStateSnapshot?[byte.MaxValue];
        for (int slot = 0; slot < playerStates.Length; slot++)
            if (playerSnapshots.TryGetPlayer(new PlayerSlotId((byte)slot), out var state))
                playerStates[slot] = state;
        if (playerStates[player.Slot] is null)
            return InvasionSpawnAttempt.Continue;
        var clock = CaptureInvasionSpawnClock();
        var progression = naturalSpawnProgression.CaptureSnapshot();
        var sections = CaptureInvasionSpawnSections(in player, invasionState.Type == 3);
        var random = new SystemVanillaNpcRandom(preview.Random);
        var retained = new VanillaNpcRetainedSlot[RuntimeNpcStore.MaximumAddressableCapacity];
        int count = npcs.CopyRetainedSlots(retained);
        Span<RetainedInvasionTownSlot1458> town = stackalloc RetainedInvasionTownSlot1458[200];
        bool summonerActive = false, shipActive = false, captainActive = false;
        for (int slot = 0; slot < Math.Min(count, town.Length); slot++)
        {
            var npc = retained[slot];
            summonerActive |= npc.IsActive && npc.Type == 471;
            shipActive |= npc.IsActive && npc.Type == 491;
            captainActive |= npc.IsActive && npc.Type == 216;
            // A never-used physical slot has the source constructor's false flag. Imported identities
            // must provide their own retained flag; definition or role alone is not that ownership.
            bool? townFlag = npc.Type == 0 ? false : npc.Simulation.TownNpc;
            float? center = null;
            if (townFlag == true)
            {
                if (npc.Simulation.HitboxOverride is { IsValid: true } body)
                    center = npc.PositionX + body.Width * .5f;
                else if (VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, new(npc.NetId), out var definition) &&
                    definition.TryResolveHitbox(npc.Simulation, out var hitbox))
                    center = npc.PositionX + hitbox.Width * .5f;
            }
            town[slot] = new(npc.IsActive, townFlag, center);
        }
        double surface = worldTiles.WorldSurfaceTiles ?? facts.WorldSurface;
        float x = player.CenterX - player.HitboxWidth * .5f;
        float y = player.CenterY - player.HitboxHeight * .5f;
        // The second vertical operand is Main.spawnTileY, not a sampled enemy floor or player position.
        if (y >= surface * 16d + 1200d && facts.SpawnTileY is null)
            return InvasionSpawnAttempt.Continue;
        if (!RuntimeInvasionSpawn1458.TryShouldSpawn(in invasionState, x, y, surface,
            facts.SpawnTileY ?? 0, worldTiles.Dimensions.WidthTiles,
            town[..Math.Min(count, town.Length)], random, out bool eligible))
            return InvasionSpawnAttempt.Continue;

        if (!eligible)
        {
            // Retain the genuine town fallback draw before continuing the ordinary selector.
            return CanAdopt() && preview.TryAdoptOwned(out _)
                ? InvasionSpawnAttempt.Ordinary : InvasionSpawnAttempt.Continue;
        }
        // Martian selection and Skyblock's extra GetZombieSettings branch remain unowned.
        if (captured.State.Type is not (1 or 3) || naturalSpawnSkyblockLowTiles)
            return InvasionSpawnAttempt.Stop;
        GetNaturalSpawnBudget(in player, nearbyNpcCount, out int spawnRate, out int maxSpawns, invaders: true);
        if (nearbyNpcCount >= maxSpawns || random.NextInt32(0, spawnRate) != 0)
            return AdoptNoBirth();
        if (!TryFindVanillaNaturalSpawnFloor(in player, random, out int tileX, out int floorY))
            return AdoptNoBirth();
        bool hardMode = facts.HardMode || progression.IsCompleted(VanillaWorldProgressionId.Hardmode);
        NpcTypeId selected = invasionState.Type == 1
            ? RuntimeInvasionSpawn1458.SelectGoblin(hardMode, summonerActive, random)
            : RuntimeInvasionSpawn1458.SelectPirate(in invasionState, shipActive, captainActive,
                () => IsPirateShipRectangleBlocked(tileX, floorY), random);
        // Ships require linked cannons; Captain deaths require the not-yet-admitted Ghost lifecycle.
        // Preserve both selected identities without resampling or adopting the speculative stream.
        if (selected.Value == 491 || selected == VanillaNpcIds.PirateCaptain)
            return InvasionSpawnAttempt.Stop;
        if (!VanillaNpcDefinitionCatalog.TryGet(selected, out var admitted) || admitted.DefinitionOnly ||
            admitted.IsBoss || !VanillaNpcAiCoverageCatalog.TryGet(selected, out _))
            return InvasionSpawnAttempt.Stop; // Preserve selected identity: no substitute and no adoption.
        var update = new NpcStateUpdate(selected.Value, checked((short)selected.Value), 0f, 0f, 0f, 0f,
            player.Slot, default, NpcSimulationState.Initial with { TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft });
        if (!preview.TryStage(in update, tileX * 16f + 8f, floorY * 16f, out _))
            return InvasionSpawnAttempt.Stop;
        if (!CanAdopt() || !preview.TryAdoptOwned(out var born) || born is null)
            return InvasionSpawnAttempt.Stop;
        AppliedSpawns++;
        npcs.PublishPendingBirths();
        return InvasionSpawnAttempt.Stop;

        InvasionSpawnAttempt AdoptNoBirth() => CanAdopt() && preview.TryAdoptOwned(out _)
            ? InvasionSpawnAttempt.Continue : InvasionSpawnAttempt.Stop;

        bool CanAdopt()
        {
            // Sample the external spawn context first and recapture players before every final pure owner guard.
            if (!preview.ValidateContext())
                return false;
            for (int slot = 0; slot < playerStates.Length; slot++)
            {
                bool present = playerSnapshots.TryGetPlayer(new PlayerSlotId((byte)slot), out var current);
                if (present != playerStates[slot].HasValue || present && current != playerStates[slot]!.Value)
                    return false;
            }
            return invasion.IsCurrent(in captured) && naturalSpawnProgression.CaptureSnapshot() == progression &&
                CaptureInvasionSpawnClock() == clock && InvasionSpawnSectionsCurrent(sections) && preview.IsCurrentOwned();
        }
    }

    private (bool Day, double Time, bool Blood, bool Pumpkin, bool Snow, int Wave, float Rain) CaptureInvasionSpawnClock() =>
        (worldClock!.DayTime, worldClock.Time, worldClock.BloodMoonActive, worldClock.PumpkinMoonActive,
            worldClock.SnowMoonActive, worldClock.MoonEventWaveNumber, worldClock.MaxRain);

    private (WorldSectionId Section, long Version)[] CaptureInvasionSpawnSections(in VanillaNpcTargetCandidate player, bool pirate)
    {
        var tiles = worldTiles!;
        int x = Math.Clamp((int)(player.CenterX / 16f), 0, tiles.Dimensions.WidthTiles - 1);
        int y = Math.Clamp((int)(player.CenterY / 16f), 0, tiles.Dimensions.HeightTiles - 1);
        // Include the selected Pirate ship rectangle even when its offer is later refused:
        // floor search reaches +/-84 X and -52 Y; Collision.SolidTiles adds +/-20 X and -40 Y.
        int horizontal = pirate ? 104 : 87;
        int above = pirate ? 92 : 63;
        var first = TerrariaSectionGeometry.FromTile(tiles.Dimensions, Math.Max(0, x - horizontal), Math.Max(0, y - above));
        var last = TerrariaSectionGeometry.FromTile(tiles.Dimensions,
            Math.Min(tiles.Dimensions.WidthTiles - 1, x + horizontal), Math.Min(tiles.Dimensions.HeightTiles - 1, y + 63));
        var captured = new (WorldSectionId, long)[(last.X - first.X + 1) * (last.Y - first.Y + 1)];
        int index = 0;
        for (int sx = first.X; sx <= last.X; sx++)
            for (int sy = first.Y; sy <= last.Y; sy++)
            {
                var section = new WorldSectionId(sx, sy);
                captured[index++] = (section, tiles.GetSectionVersion(section));
            }
        return captured;
    }

    private bool IsPirateShipRectangleBlocked(int floorX, int floorY)
    {
        var tiles = worldTiles!;
        int left = floorX - 20, right = floorX + 20, top = floorY - 40, bottom = floorY - 10;
        // Source Collision.SolidTiles uses an inclusive rectangle and the bottom-world margin.
        // WorldTileStore owns a dense table, so a source null Tile cannot arise in this admitted context.
        if (left < 0 || right >= tiles.Dimensions.WidthTiles || top < 0 || bottom >= tiles.Dimensions.HeightTiles - 40)
            return true;
        for (int x = left; x <= right; x++)
            for (int y = top; y <= bottom; y++)
            {
                var tile = tiles.Get(x, y);
                if (tile.IsActive && !tile.IsActuated && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
                    !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType))
                    return true;
            }
        return false;
    }

    private bool InvasionSpawnSectionsCurrent((WorldSectionId Section, long Version)[] captured)
    {
        foreach (var entry in captured)
            if ((entry.Version & 1) != 0 || worldTiles!.GetSectionVersion(entry.Section) != entry.Version)
                return false;
        return true;
    }
}
