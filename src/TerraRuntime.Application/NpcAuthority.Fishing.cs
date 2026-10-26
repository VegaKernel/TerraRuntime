using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class NpcAuthority
{
    internal bool HasActiveFishingAngler
    {
        get
        {
            for (int slot = 0; slot < npcs.Capacity; slot++)
                if (npcs.TryGetActive((byte)slot, out var npc) && npc.Type == 369 && npc.Simulation.Life > 0) return true;
            return false;
        }
    }
    private readonly bool fishingCombatBookUsed;
    private readonly Action? fishingWorldStateAdopted;
    private Action? fishingWorldInfoPublisher;
    internal void BindFishingWorldInfoPublisher(Action publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        if (fishingWorldInfoPublisher is not null)
            throw new InvalidOperationException("Fished-world publication is already bound.");
        fishingWorldInfoPublisher = publisher;
    }
    internal bool TryCaptureFishingWorld(RuntimePlayerMember member, out FishingWorld1458 world,
        out FishingPowerContext1458 weather, out FishingBiomes1458 biomes)
    {
        world = default; weather = default; biomes = default;
        if (worldTiles is null || worldClock is null || naturalSpawnWorldFacts is not { } facts || npcSceneMetrics is null)
            return false;
        var scene = npcSceneMetrics.Scan((int)((member.PositionX + 10) / 16), (int)((member.PositionY + 21) / 16));
        if (scene.ZoneCorrupt) biomes |= FishingBiomes1458.Corruption;
        if (scene.ZoneCrimson) biomes |= FishingBiomes1458.Crimson;
        if (scene.ZoneJungle) biomes |= FishingBiomes1458.Jungle;
        if (scene.ZoneSnow) biomes |= FishingBiomes1458.Snow;
        if (scene.ZoneDungeon) biomes |= FishingBiomes1458.Dungeon;
        if (scene.ZoneBeach) biomes |= FishingBiomes1458.Beach;
        if (scene.ZoneHallow) biomes |= FishingBiomes1458.Hallow;
        if (scene.ZoneGlowshroom) biomes |= FishingBiomes1458.GlowingMushrooms;
        if (scene.ZoneDesert) biomes |= FishingBiomes1458.Desert;
        bool fishron = false;
        for (int i = 0; i < npcs.Capacity; i++)
            if (npcs.TryGetActive((byte)i, out var npc) && npc.Type == 370) { fishron = true; break; }
        world = new(worldTiles.Dimensions.WidthTiles, worldTiles.Dimensions.HeightTiles,
            worldTiles.WorldSurfaceTiles ?? facts.WorldSurface, facts.RockLayer,
            facts.HardMode || naturalSpawnProgression.IsCompleted(VanillaWorldProgressionId.Hardmode),
            facts.RemixWorld, facts.NotTheBeesWorld, worldClock.BloodMoonActive, worldClock.DayTime,
            facts.DownedBoss3 || naturalSpawnProgression.IsCompleted(VanillaWorldProgressionId.Skeletron),
            fishingCombatBookUsed, naturalTownSpawnFacts?.UnlockedSlimeRedSpawn == true || naturalSpawnProgression.IsSlimeRedSpawnUnlocked, fishron);
        weather = new(worldClock.DayTime, worldClock.Time, (int)worldClock.MoonPhase,
            worldClock.Raining, worldClock.FishingCloudAlpha, worldClock.BloodMoonActive, 0);
        return true;
    }

    internal bool TrySpawnFishingNpc(ConnectionHandle connection, int type, float x, float y)
    {
        if (type is not (370 or 586 or 587 or 618 or 620 or 621 or 682) ||
            !players.TryGet(connection, out var member) || member.IsDead || worldTiles is null ||
            !float.IsFinite(x) || !float.IsFinite(y) || x < 0 || y < 0 ||
            x > worldTiles.Dimensions.WidthTiles * 16f - 14 || y > worldTiles.Dimensions.HeightTiles * 16f - 14 ||
            type == 682 && (naturalSpawnProgression.IsSlimeRedSpawnUnlocked ||
                naturalTownSpawnFacts?.UnlockedSlimeRedSpawn == true || naturalSpawnTownNpcs is null) ||
            !npcs.TryCreateNaturalSpawnPreview(combat.SourceRandom, out var preview) || preview is null) return false;
        var actor = member.CaptureSnapshot();
        var progression = naturalSpawnProgression.CaptureSnapshot();
        var update = new NpcStateUpdate(checked((short)type), checked((short)type), 0, 0, 0, 0,
            byte.MaxValue, default, NpcSimulationState.Initial);
        NpcSnapshot staged;
        if (type == 370)
        {
            // NPC.SpawnOnPlayer's dedicated370 branch uses the first owned
            // bobber center+100 and ordinary NewNPC, not the generic boss-area
            // sampler, startSlot1 or SpawnBoss's timeLeft multiplier.
            if (!preview.TryStage(update, (int)(x + 7), (int)(y + 7) + 100, out staged)) return false;
        }
        else
        {
            // Player.ItemCheck truncates bobber.position, adds Nautilus64, then
            // packet130 divides by16. Dedicated MessageBuffer130 multiplies the
            // UInt16 tile coordinates by16 before NewNPC. Direct/SP ItemCheck
            // does not quantize, so its position is not the dedicated birth.
            int bottomX = (int)x / 16 * 16;
            int bottomY = ((int)y + (type == 618 ? 64 : 0)) / 16 * 16;
            if (!preview.TryStage(update, bottomX, bottomY, out staged)) return false;
        }
        VanillaTownNpcSpawnIdentity1458 identity = default;
        bool shimmered = false;
        if (type == 682)
        {
            if (!naturalSpawnTownNpcs!.CanAdoptFishedResident(staged)) return false;
            shimmered = naturalSpawnTownNpcs.IsShimmeredNpcType(new(682));
            var namesRandom = new SystemVanillaTownNpcRandom1458(new FishingRandom1458(preview.Random));
            identity = new VanillaTownNpcIdentityResolver1458(mainRandom: namesRandom, worldGenRandom: namesRandom)
                .Resolve(new(682), shimmered);
        }
        if (!preview.ValidateContext() || !players.TryGet(connection, out var currentPlayer) ||
            !ReferenceEquals(member, currentPlayer) || member.CaptureSnapshot() != actor || member.IsDead ||
            naturalSpawnProgression.CaptureSnapshot() != progression ||
            type == 682 && (!naturalSpawnTownNpcs!.CanAdoptFishedResident(staged) ||
                naturalSpawnTownNpcs.IsShimmeredNpcType(new(682)) != shimmered) ||
            !preview.TryAdoptUnpublished(out var born, out var publication) || born != staged || publication is null)
            return false;
        if (type == 682 && (!naturalSpawnTownNpcs!.TryAdoptFishedResident(staged, identity) ||
            !naturalSpawnProgression.MarkSlimeRedSpawnUnlocked()))
            throw new InvalidOperationException("Preflighted fished resident lost callback-free town/progression adoption.");
        AppliedSpawns++;
        try
        {
            if (type == 682)
            {
                if (fishingWorldInfoPublisher is { } publisher) publisher();
                else fishingWorldStateAdopted?.Invoke();
            }
        }
        finally
        {
            publication.PublishRetention();
            if (npcs.TryGet(staged.Handle, out var current) && current == staged)
            {
                npcs.TryPublishPendingBirth(staged.Handle);
                if (type == 370 && npcs.TryGet(staged.Handle, out current) && current == staged)
                    AnnounceBossSpawn(new(370));
            }
        }
        return true;
    }
}
