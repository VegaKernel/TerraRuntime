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
        var sections = CaptureInvasionSpawnSections(in player);
        var random = new SystemVanillaNpcRandom(preview.Random);
        var retained = new VanillaNpcRetainedSlot[RuntimeNpcStore.MaximumAddressableCapacity];
        int count = npcs.CopyRetainedSlots(retained);
        Span<RetainedInvasionTownSlot1458> town = stackalloc RetainedInvasionTownSlot1458[200];
        bool summonerActive = false;
        for (int slot = 0; slot < Math.Min(count, town.Length); slot++)
        {
            var npc = retained[slot];
            summonerActive |= npc.IsActive && npc.Type == 471;
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
        // Pirate/Martian selectors and Skyblock's extra GetZombieSettings branch are not admitted.
        if (captured.State.Type != 1 || naturalSpawnSkyblockLowTiles)
            return InvasionSpawnAttempt.Stop;
        GetNaturalSpawnBudget(in player, nearbyNpcCount, out int spawnRate, out int maxSpawns, invaders: true);
        if (nearbyNpcCount >= maxSpawns || random.NextInt32(0, spawnRate) != 0)
            return AdoptNoBirth();
        if (!TryFindVanillaNaturalSpawnFloor(in player, random, out int tileX, out int floorY))
            return AdoptNoBirth();
        bool hardMode = facts.HardMode || progression.IsCompleted(VanillaWorldProgressionId.Hardmode);
        NpcTypeId selected = RuntimeInvasionSpawn1458.SelectGoblin(hardMode, summonerActive, random);
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

    private (WorldSectionId Section, long Version)[] CaptureInvasionSpawnSections(in VanillaNpcTargetCandidate player)
    {
        var tiles = worldTiles!;
        int x = Math.Clamp((int)(player.CenterX / 16f), 0, tiles.Dimensions.WidthTiles - 1);
        int y = Math.Clamp((int)(player.CenterY / 16f), 0, tiles.Dimensions.HeightTiles - 1);
        // Source-shaped existing floor search (84x52), body clearance and 169x124 scene scan.
        var first = TerrariaSectionGeometry.FromTile(tiles.Dimensions, Math.Max(0, x - 87), Math.Max(0, y - 63));
        var last = TerrariaSectionGeometry.FromTile(tiles.Dimensions,
            Math.Min(tiles.Dimensions.WidthTiles - 1, x + 87), Math.Min(tiles.Dimensions.HeightTiles - 1, y + 63));
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

    private bool InvasionSpawnSectionsCurrent((WorldSectionId Section, long Version)[] captured)
    {
        foreach (var entry in captured)
            if ((entry.Version & 1) != 0 || worldTiles!.GetSectionVersion(entry.Section) != entry.Version)
                return false;
        return true;
    }
}
