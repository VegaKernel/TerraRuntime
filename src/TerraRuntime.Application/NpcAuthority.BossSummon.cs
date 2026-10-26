using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class NpcAuthority
{
    private void ApplyPaidBossSummon(ConnectionHandle connection, short type)
    {
        int item = type switch { 4 => 43, 134 => 556, 127 => 557, _ => 0 };
        // King Slime's extra CanHit chain, biome/fishing/altar summons and multi-root
        // Twins/Mechdusa births require their own complete owned producer.
        if (item == 0 || worldTiles is null || worldClock is null || worldClock.DayTime ||
            naturalSpawnWorldFacts is not { } facts || facts.SkyblockWorld || facts.ZenithWorld ||
            facts.RemixWorld || facts.GoodWorld || facts.InfectedSeed || facts.DualDungeonsSeed ||
            facts.GameMode is not (0 or 1 or 2)) return;
        var clock = (worldClock.DayTime, worldClock.Time);
        var progression = naturalSpawnProgression.CaptureSnapshot();
        if (!players.TryPrepareBossSummon(connection, item, out var payment)) return;
        var census = new NpcRawPlayerSlotSnapshot1458[255];
        var bodies = new RuntimePlayerProjectileUseCapture?[255];
        for (int slot = 0; slot < census.Length; slot++)
        {
            if (!rawPlayerSlots.TryCapture((byte)slot, out census[slot])) return;
            if (!census[slot].Facts.Active) continue;
            // ResizeHitbox can add26 for PortableStool4341/HandOfCreation5126 without
            // a mount. That body lifetime is not owned here. Capture every screen's
            // exact equipment profile and refuse unknown/clientless body provenance.
            if (census[slot].LivePlayer is not { HasMount: false } actor ||
                !players.TryCaptureProjectileUse(actor.Player, out var body) || body is not { HasProfile: true } ||
                body.Player != actor || body.Equipment.Any(equipment => equipment.ItemNetId is 4341 or 5126)) return;
            bodies[slot] = body;
        }
        if (census[connection.Player.Slot.Value].LivePlayer is not { } caster || caster.Player != connection.Player ||
            !TryGetPlayerTarget(connection.Player.Slot.Value, out var target) ||
            !npcs.TryCreateNaturalSpawnPreview(combat.SourceRandom, out var preview) || preview is null) return;
        // The preview's physical census detects births, deaths and replacements during every provider call.
        int activeCount = npcs.CopyActive(naturalSpawnNpcBuffer);
        for (int index = 0; index < activeCount; index++)
            if (naturalSpawnNpcBuffer[index].Type == type) return;
        var sections = CaptureInvasionSpawnSections(in target, false);
        // The shared capture spans center +/-87 X and +/-63 Y: it contains the source
        // top-left +/-84/52 sampling rectangle and its additional one-left/three-up clearance.
        if (!RuntimeBossSummonSpawn1458.TryFind(worldTiles, in caster, census,
            new SystemVanillaNpcRandom(preview.Random), out int x, out int y)) return;
        var update = new NpcStateUpdate(type, type, 0, 0, 0, 0, connection.Player.Slot.Value,
            default, NpcSimulationState.Initial);
        if (!preview.TryStageBoss(in update, x, y, out var staged) || !preview.ValidateContext()) return;
        for (int slot = 0; slot < census.Length; slot++)
            if (!rawPlayerSlots.IsCurrent(in census[slot])) return;
        for (int slot = 0; slot < bodies.Length; slot++)
            if (bodies[slot] is { } body && !players.IsCurrentProjectileUse(body)) return;
        if ((worldClock.DayTime, worldClock.Time) != clock || naturalSpawnProgression.CaptureSnapshot() != progression ||
            !InvasionSpawnSectionsCurrent(sections) ||
            !preview.IsCurrentOwned() || !payment!.IsCurrent || !payment.TryAdoptUnpublished()) return;
        try
        {
            if (!preview.TryAdoptOwned(out var accepted) || accepted != staged)
                throw new InvalidOperationException("Validated paid boss birth changed without a callback.");
            AppliedSpawns++;
            // The payment and accepted exact-generation birth precede both baseline and public observers.
            if (npcs.TryGet(staged.Handle, out var current) && current == staged)
            {
                npcs.TryPublishPendingBirth(staged.Handle);
                if (npcs.TryGet(staged.Handle, out current) && current == staged)
                    AnnounceBossSpawn(new(type));
            }
        }
        finally
        {
            // A baseline/packet observer may throw or replace the connection after acceptance.
            // Drain the exact old occupation's pending inventory notification without rebinding it.
            payment.Publish();
        }
    }
}

/// <summary>Ordinary, unmounted NPC.SpawnOnPlayer sampling; King/secret-seed special branches stay unadmitted.</summary>
internal static partial class RuntimeBossSummonSpawn1458
{
    internal static bool TryFind(WorldTileStore tiles, in PlayerStateSnapshot caster,
        ReadOnlySpan<NpcRawPlayerSlotSnapshot1458> players, IVanillaNpcRandom random, out int bottomX, out int bottomY)
    {
        bottomX = bottomY = 0;
        if (players.Length != 255 || caster.HasMount || !float.IsFinite(caster.PositionX) ||
            !float.IsFinite(caster.PositionY)) return false;
        int width = tiles.Dimensions.WidthTiles, height = tiles.Dimensions.HeightTiles;
        if (caster.PositionX < 0 || caster.PositionY < 0 || caster.PositionX >= width * 16f ||
            caster.PositionY >= height * 16f) return false;
        // Utils.ToTileCoordinates truncates pixels before its arithmetic shift.
        int cx = (int)caster.PositionX >> 4, cy = (int)caster.PositionY >> 4;
        int left = Math.Clamp(cx - 84, 0, width), right = Math.Clamp(cx + 84, 0, width);
        int top = Math.Clamp(cy - 52, 0, height), bottom = Math.Clamp(cy + 52, 0, height);
        if (left >= right || top >= bottom) return false;
        // Source NPC.SpawnOnPlayer retains a found candidate across an on-screen outer retry.
        bool found = false;
        int chosenX = 0, chosenY = 0;
        for (int attempt = 0; attempt < 500; attempt++)
        {
            bool final = attempt == 499;
            for (int sample = 0; sample < 50; sample++)
            {
                int x = random.NextInt32(left, right), y = random.NextInt32(top, bottom);
                var start = tiles.Get(x, y);
                if (Solid(start) || !final && VanillaWallDefinitionCatalog.TryGet(new(start.Wall), out var wall) && wall.IsHousingWall)
                    continue;
                for (int floor = y; floor < bottom; floor++)
                {
                    if (!Solid(tiles.Get(x, floor)) || !final && x >= cx - 62 && x < cx + 62 && y >= cy - 39 && y < cy + 39)
                        continue;
                    chosenX = x; chosenY = floor; found = true; break;
                }
                if (found && !final && !HasSpace(tiles, chosenX, chosenY)) found = false;
                if (found) break;
            }
            if (found && (final || OffScreen(chosenX, chosenY, players)))
            {
                bottomX = chosenX * 16 + 8; bottomY = chosenY * 16;
                return true;
            }
        }
        return false;
    }

    private static bool Solid(WorldTile tile) => tile.IsActive && !tile.IsActuated &&
        VanillaTileCollisionCatalog.IsSolid(tile.TileType);

    private static bool HasSpace(WorldTileStore tiles, int x, int y)
    {
        // Spawner.spawnSpaceX/Y = 2/3. WorldGen.InWorld(Rectangle) rejects the far edge too.
        if (x - 1 < 0 || x + 1 >= tiles.Dimensions.WidthTiles || y - 3 < 0 || y >= tiles.Dimensions.HeightTiles)
            return false;
        for (int dx = -1; dx < 1; dx++)
            for (int dy = -3; dy < 0; dy++)
            {
                var tile = tiles.Get(x + dx, y + dy);
                if (Solid(tile) || tile.LiquidAmount > 0 && tile.LiquidKind == WorldLiquidKind.Lava) return false;
            }
        return true;
    }

    private static bool OffScreen(int x, int y, ReadOnlySpan<NpcRawPlayerSlotSnapshot1458> players)
    {
        foreach (var slot in players)
        {
            if (!slot.Facts.Active) continue;
            if (slot.LivePlayer is not { HasMount: false } player) return false;
            int left = (int)(player.PositionX + 10f - 960f - 62f);
            int top = (int)(player.PositionY + 21f - 600f - 39f);
            if (x * 16 < left + 2044 && x * 16 + 16 > left &&
                y * 16 < top + 1278 && y * 16 + 16 > top) return false;
        }
        return true;
    }
}
