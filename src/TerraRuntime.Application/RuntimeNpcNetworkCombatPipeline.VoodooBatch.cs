using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    // Runtime planning budget, above the 672 sections of an ordinary 8400x2400 large world.
    // Ad-hoc larger worlds fail before allocating a dependency vector or changing the doll.
    private const int MaximumDollTerrainSections = 1024;
    private Action<PreparedDollDeath>? captureDollDeath;
    private PreparedDollDeath? replayingDollDeath;

    private sealed record PreparedDollDeath(NpcSnapshot Pending, RuntimeNpcDeathDropPlan1458 Plan,
        VanillaBossRecoveryDailyState1458 Daily, RuntimeNpcDeathPrelude1458 Prelude,
        ulong PreludeRevision, bool LootAllowed);

    private sealed record DollStep(NpcSnapshot Victim, PreparedDollDeath Death,
        RuntimeNpcDeathPrelude1458 Credit, ulong CreditRevision, VanillaUnifiedRandom1458 BeforeSelection,
        VanillaUnifiedRandom1458 AfterSelection, NpcAiSpawnIntent? Wall, VanillaUnifiedRandom1458 AfterWall);

    internal bool TryBurnGuideDollBatch(in WorldItemSnapshot doll, out int wallsBorn)
    {
        wallsBorn = 0;
        var sourceDoll = doll;
        if (pendingDeathPlan is not null || replayingDollDeath is not null ||
            !IsCurrentBurnableDoll(in doll) || worldTiles is null ||
            !npcs.TryCaptureDeathMutationSerial(out ulong npcSerial)) return false;
        bool hasGuide = false;
        for (int slot = 0; slot < npcs.Capacity; slot++)
            if (npcs.TryGetActive((byte)slot, out var actor) && actor.TypeIdentity == VanillaNpcIds.Guide)
            { hasGuide = true; break; }
        if (!hasGuide)
        {
            // Source consumes the item and returns without consulting loot/player/name/spawn facts.
            using var removal = worldItems.CreateAllocationPreview();
            return removal.TryRemoveSource(in doll) && removal.TryClaim() &&
                npcs.TryCaptureDeathMutationSerial(out ulong current) && current == npcSerial &&
                IsCurrentBurnableDoll(in doll) && removal.TryCommitNext(out _, out _);
        }
        var dimensions = worldTiles.Dimensions;
        if (dimensions.SectionCount > MaximumDollTerrainSections) return false;
        var terrainVersions = new long[dimensions.SectionCount];
        for (int y = 0; y < dimensions.SectionRows; y++)
            for (int x = 0; x < dimensions.SectionColumns; x++)
            {
                long version = worldTiles.GetSectionVersion(new(x, y));
                if ((version & 1L) != 0) return false;
                terrainVersions[y * dimensions.SectionColumns + x] = version;
            }
        var initialRandom = random.SourceRandom.Clone();
        var beforeProgression = progression.CaptureSnapshot();
        ulong beforePrelude = deathPrelude.Revision;
        var beforeWorld = CaptureGlobalLootWorld(); var beforeLow = CaptureSpecificLowTiles();
        ulong inventorySerial = playerAuthority.NpcLootInventorySerial;
        var beforeSeason = seasonalItemContextSource?.Invoke();
        var beforeClock = CaptureDollClock();
        var beforeDaily = (bossRecoveryDaily.EyeKilled, bossRecoveryDaily.WallKilled);
        if (!TryBuildGuideDollWallSpawn(in doll, out var initialWall)) return false;
        var census = new PlayerStateSnapshot[plannedPlayers.Length]; var present = new bool[census.Length];
        for (int i = 0; i < census.Length; i++) present[i] = players.TryGetPlayer(new((byte)i), out census[i]);
        var batchRandom = initialRandom.Clone();
        var detachedNpcs = npcs.CreateDeathPreview(new SystemVanillaNpcRandom(batchRandom));
        var spawnContext = detachedNpcs.CapturedDeathSpawnContext;
        var detachedItems = worldItems.CreateDeathPreview();
        var detachedPrelude = deathPrelude.CreatePreview();
        var detachedProgression = progression.CreateDeathPreview();
        var detachedClock = worldClock?.CreateDeathPreview();
        var detached = new RuntimeNpcNetworkCombatPipeline(detachedNpcs, detachedItems, players, playerAuthority,
            tickProvider, null, new(detachedItems), null, detachedClock, detachedProgression, expertMode, masterMode,
            worldTiles, crimsonWorld, skyblockLowTiles, isThereAWorldSurface, evilBossDownedBaseline,
            planteraDownedBaseline: planteraDownedBaseline, zenithWorld: zenithWorld, lootRandom: batchRandom,
            seasonalItemContext: beforeSeason is { } season ? () => season : null, deathPrelude: detachedPrelude,
            onlyShimmerOceanWorlds: onlyShimmerOceanWorlds, mechanicalLootBaseline: mechanicalLootBaseline,
            lootRemixWorld: lootRemixWorld, globalLootWorld: beforeWorld, npcSpecificLowTiles: () => beforeLow,
            requireOwnedPlayerHealth: requireOwnedPlayerHealth, rawPlayerSlots: rawPlayerSlots,
            npcSpecificDropExtraGel: npcSpecificDropExtraGel, npcSpecificGoodWorld: npcSpecificGoodWorld,
            townNameSource: townNameSource, townLootLanguage: townLootLanguage);
        detached.bossRecoveryDaily.CopyFrom(bossRecoveryDaily);
        if (!interactions.CopyDeathPreviewTo(detached.interactions)) return false;
        var sourceInteractions = new Dictionary<NpcHandle, PlayerSlotId[]>();
        Span<PlayerSlotId> interactionBuffer = stackalloc PlayerSlotId[plannedPlayers.Length];
        for (int i = 0; i < npcs.Capacity; i++)
            if (npcs.TryGetActive((byte)i, out var actor))
            {
                if (!interactions.TryCopyInteractingSlots(actor.Handle, interactionBuffer, out int count)) return false;
                var slots = interactionBuffer[..count].ToArray(); sourceInteractions.Add(actor.Handle, slots);
            }
        TerraRuntime.Core.Npcs.NpcRawPlayerSlotSnapshot1458? sourceRaw = null;
        if (!present.Any(static value => value) && rawPlayerSlots is not null && rawPlayerSlots.TryCapture(0, out var raw))
            sourceRaw = raw;
        Span<WorldItemAllocationPlayer1458> views = stackalloc WorldItemAllocationPlayer1458[census.Length];
        if (!TryCaptureAllocationViews(views, out int viewCount)) return false;
        using var allocation = worldItems.CreateAllocationPreview(views[..viewCount]);
        if (!allocation.TryRemoveSource(in doll) || !detachedItems.TryRemove(doll.Handle.Slot, out _)) return false;
        var steps = new List<DollStep>(npcs.Capacity);
        var names = new Dictionary<NpcHandle, string?>();
        bool planningFailed = false;
        PreparedDollDeath? captured = null;
        detached.captureDollDeath = state =>
        {
            var retained = state.Plan.RetainForBatch(worldItems, random.SourceRandom, allocation);
            if (retained is null) planningFailed = true;
            else captured = state with { Plan = retained };
        };
        bool Stage(NpcSnapshot victim, VanillaUnifiedRandom1458 beforeSelection, bool guide)
        {
            // Generic missing-table fallback cannot establish that a registered town reward is empty.
            // Registered town rules, including exact resident names, belong to the death planner.
            if (VanillaTownNpcLootFacts1458.HasRegisteredSpecificRules(victim.TypeIdentity) &&
                !VanillaTownNpcLootRules1458.TryGet(victim.TypeIdentity, out _)) return false;
            if (VanillaTownNpcLootRules1458.RequiresName(victim.TypeIdentity))
            {
                string? name = townNameSource?.Invoke(victim.Handle);
                if (name is null) return false;
                names[victim.Handle] = name;
            }
            var selection = batchRandom.Clone();
            ulong creditRevision = detachedPrelude.Revision;
            var credit = detachedPrelude.CreatePreview();
            if (!credit.TryRegisterScriptedKill(in victim) || !detachedPrelude.TryPublish(credit, creditRevision)) return false;
            captured = null;
            if (detached.TryStrikeEnvironment(victim.Handle, 9999, 10f, victim.Simulation.DirectionX) !=
                RuntimeTownNpcMeleeDamageResult1458.Killed || captured is null || planningFailed) return false;
            NpcAiSpawnIntent? wall = null;
            if (guide)
            {
                if (!detached.TryBuildGuideDollWallSpawn(in sourceDoll, out wall)) return false;
                if (wall is { } spawn && (!npcs.IsSpawnRandomSource(random.SourceRandom) ||
                    !detachedNpcs.TrySpawnIntent(in spawn, out _))) return false;
            }
            steps.Add(new(victim, captured, credit, creditRevision, beforeSelection, selection, wall, batchRandom.Clone()));
            return true;
        }
        int remaining = doll.Stack; bool hadGuide = false;
        // Source rereads each live slot after every strike and in-loop Wall birth.
        for (int i = 0; i < detachedNpcs.Capacity; i++)
            if (detachedNpcs.TryGetActive((byte)i, out var guide) && guide.TypeIdentity == VanillaNpcIds.Guide)
            {
                hadGuide = true;
                if (!Stage(guide, batchRandom.Clone(), true)) return false;
                remaining--;
            }
        var victims = new List<NpcHandle>(detachedNpcs.Capacity);
        if (hadGuide && remaining > 0)
            for (int i = 0; i < detachedNpcs.Capacity; i++)
                if (detachedNpcs.TryGetActive((byte)i, out var victim) && IsDollTownVictim(in victim)) victims.Add(victim.Handle);
        while (remaining > 0 && victims.Count > 0)
        {
            var beforeSelection = batchRandom.Clone();
            int chosen = batchRandom.Next(victims.Count); var handle = victims[chosen]; victims.RemoveAt(chosen);
            if (!detachedNpcs.TryGet(handle, out var victim) || !Stage(victim, beforeSelection, false)) return false;
            remaining--;
        }
        // This whole operation has exactly one item claim. Every external dependency is checked
        // after planning and claiming, before the first removal or source RNG adoption.
        bool Current()
        {
            if (!random.SourceRandom.HasSameState(initialRandom) ||
                !npcs.TryCaptureDeathMutationSerial(out ulong current) || current != npcSerial ||
                !npcs.TryCaptureDeathSpawnContext(out var context) || context != spawnContext ||
                !IsCurrentBurnableDoll(in sourceDoll) || progression.CaptureSnapshot() != beforeProgression ||
                deathPrelude.Revision != beforePrelude || CaptureGlobalLootWorld() != beforeWorld ||
                CaptureSpecificLowTiles() != beforeLow || playerAuthority.NpcLootInventorySerial != inventorySerial ||
                seasonalItemContextSource?.Invoke() != beforeSeason || CaptureDollClock() != beforeClock) return false;
            if ((bossRecoveryDaily.EyeKilled, bossRecoveryDaily.WallKilled) != beforeDaily ||
                !TryBuildGuideDollWallSpawn(in sourceDoll, out var currentWall) || currentWall != initialWall) return false;
            if (sourceRaw is { } raw && !rawPlayerSlots!.IsCurrent(in raw)) return false;
            Span<PlayerSlotId> slots = stackalloc PlayerSlotId[plannedPlayers.Length];
            foreach (var entry in sourceInteractions)
                if (!interactions.TryCopyInteractingSlots(entry.Key, slots, out int count) ||
                    !slots[..count].SequenceEqual(entry.Value)) return false;
            foreach (var entry in names) if (townNameSource?.Invoke(entry.Key) != entry.Value) return false;
            for (int i = 0; i < census.Length; i++)
            {
                bool now = players.TryGetPlayer(new((byte)i), out var player);
                if (now != present[i] || (now && player != census[i])) return false;
            }
            // Recapture callback-owned facts after names/player census, then finish with direct owners.
            if (CaptureGlobalLootWorld() != beforeWorld || CaptureSpecificLowTiles() != beforeLow ||
                seasonalItemContextSource?.Invoke() != beforeSeason) return false;
            // No callback/provider runs after these final direct-owner comparisons. A name/player
            // callback above can legitimately reenter and mutate progression, time or Wall terrain.
            if (progression.CaptureSnapshot() != beforeProgression || deathPrelude.Revision != beforePrelude ||
                playerAuthority.NpcLootInventorySerial != inventorySerial || CaptureDollClock() != beforeClock ||
                (bossRecoveryDaily.EyeKilled, bossRecoveryDaily.WallKilled) != beforeDaily) return false;
            for (int y = 0; y < dimensions.SectionRows; y++)
                for (int x = 0; x < dimensions.SectionColumns; x++)
                    if (worldTiles.GetSectionVersion(new(x, y)) !=
                        terrainVersions[y * dimensions.SectionColumns + x]) return false;
            return random.SourceRandom.HasSameState(initialRandom) &&
                npcs.TryCaptureDeathMutationSerial(out current) && current == npcSerial && allocation.IsCurrent;
        }
        if (planningFailed || !worldItems.IsDeathPreviewSourceCurrent(detachedItems) ||
            !allocation.TryClaim() || !Current()) return false;
        if (!allocation.TryCommitNext(out _, out _))
            throw new InvalidOperationException("Accepted whole burn lost its source removal claim.");
        foreach (DollStep step in steps)
        {
            if (!random.SourceRandom.HasSameState(step.BeforeSelection))
                throw new InvalidOperationException("Accepted victim selection lost its source RNG cursor.");
            random.SourceRandom.CopyStateFrom(step.AfterSelection);
            if (!deathPrelude.TryPublish(step.Credit, step.CreditRevision))
                throw new InvalidOperationException("Accepted scripted kill lost its prelude revision.");
            replayingDollDeath = step.Death;
            try
            {
                if (TryStrikeEnvironment(step.Victim.Handle, 9999, 10f, step.Victim.Simulation.DirectionX) !=
                    RuntimeTownNpcMeleeDamageResult1458.Killed)
                    throw new InvalidOperationException("Accepted whole burn lost its prepared death.");
            }
            finally { replayingDollDeath = null; CancelPendingDeathPlan(); }
            if (step.Wall is { } wall)
            {
                if (!npcs.TrySpawnIntent(in wall, out _))
                    throw new InvalidOperationException("Accepted whole burn lost its in-loop Wall birth.");
                wallsBorn++;
            }
            if (!random.SourceRandom.HasSameState(step.AfterWall))
                throw new InvalidOperationException("Accepted whole burn diverged from its source continuation.");
        }
        return true;
    }

    private static bool IsDollTownVictim(in NpcSnapshot npc) =>
        VanillaTownNpcFacts1458.IsHousingEligible(npc.TypeIdentity) ||
        npc.TypeIdentity == VanillaNpcIds.OldMan || npc.TypeIdentity == VanillaNpcIds.TravellingMerchant ||
        npc.TypeIdentity == VanillaNpcIds.SkeletonMerchant;

    private (double?, bool?, bool?, bool?, bool?, long?) CaptureDollClock() =>
        (worldClock?.Time, worldClock?.DayTime, worldClock?.BloodMoonActive,
         worldClock?.PumpkinMoonActive, worldClock?.SnowMoonActive, worldClock?.MoonEventProgressRevision);
}
