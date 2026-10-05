using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Npcs.Loot;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    // Runtime admission ceiling bounds money split retries independently of source overflow allocation.
    private const int MaximumMoneySplitAttempts = 4096;
    private RuntimeNpcDeathDropPlan1458? pendingDeathPlan;
    private VanillaBossRecoveryDailyState1458? plannedDaily;
    private RuntimeNpcStore? previewDeathNpcs;
    private RuntimeWorldClock? previewDeathClock;
    private RuntimeWorldProgressionMutations? previewDeathProgression;
    private VanillaBossRecoveryDailyState1458? previewDaily;
    private bool deathPreviewFailed;
    private VanillaUnifiedRandom1458? deathPreviewLiveRandom;
    private bool deathAdmissionRejected;
    private RuntimeNpcDeathPrelude1458? plannedPrelude;
    private ulong plannedPreludeRevision;
    private RuntimeWorldProgressionMutationSnapshot plannedDeathProgression;
    private bool plannedLootAllowed;
    private ulong? plannedDeathSpawnSerial;
    private VanillaNpcSpawnContext? plannedDeathSpawnContext;
    private TerraRuntime.Core.Npcs.NpcRawPlayerSlotSnapshot1458? plannedHealingFallback;
    private readonly PlayerStateSnapshot[] plannedPlayers = new PlayerStateSnapshot[VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots];
    private readonly bool[] plannedPlayerPresence = new bool[VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots];
    private bool IsPreviewingDeath => lootDelivery.Preview is not null;
    private RuntimeNpcStore DeathNpcs => previewDeathNpcs ?? npcs;
    private RuntimeWorldClock? DeathClock => IsPreviewingDeath ? previewDeathClock : worldClock;
    private RuntimeWorldProgressionMutations DeathProgression => previewDeathProgression ?? progression;
    private VanillaBossRecoveryDailyState1458 DeathDaily => previewDaily ?? bossRecoveryDaily;

    private void ExecuteOwnedDeathEvents(in NpcSnapshot dead, bool eaterBoss)
    {
        if (!IsPreviewingDeath && pendingDeathPlan is not null && !plannedLootAllowed) return;
        AdvanceSlimeRainDeath(in dead);
        AdvanceMoonEventDeath(in dead);
        if (dead.TypeIdentity == VanillaNpcIds.KingSlime)
            ApplyKingSlimeDeathEffects(in dead);
        else if (dead.TypeIdentity == VanillaNpcIds.EyeOfCthulhu)
            DeathProgression.MarkCompleted(VanillaWorldProgressionId.EyeOfCthulhu);
        else if (dead.TypeIdentity == VanillaNpcIds.SkeletronHead)
            ApplySkeletronDeathEffects();
        else if (dead.TypeIdentity == VanillaNpcIds.QueenBee)
            ApplyQueenBeeDeathEffects();
        else if (dead.TypeIdentity == VanillaNpcIds.Deerclops)
            ApplyDeerclopsDeathEffects();
        else if (dead.TypeIdentity == VanillaNpcIds.WallOfFlesh)
            ApplyWallOfFleshDeathEffects(in dead);
        else if (IsHardmodeBossRoot(dead.TypeIdentity))
            ApplyHardmodeBossDeathEffects(in dead);
        else if (eaterBoss || dead.TypeIdentity == VanillaNpcIds.BrainOfCthulhu)
            ApplyEvilBossDeathEffects(eaterBoss);
    }

    private bool TryAdmitLethalDeath(in NpcSnapshot pending)
    {
        deathAdmissionRejected = false;
        if (replayingDollDeath is { } retainedDollDeath)
        {
            if (pending != retainedDollDeath.Pending) { deathAdmissionRejected = true; return false; }
            pendingDeathPlan = retainedDollDeath.Plan;
            plannedDaily = retainedDollDeath.Daily;
            plannedPrelude = retainedDollDeath.Prelude;
            plannedPreludeRevision = retainedDollDeath.PreludeRevision;
            plannedLootAllowed = retainedDollDeath.LootAllowed;
            return true;
        }
        if (pendingDeathPlan is not null) { deathAdmissionRejected = true; return false; }
        NpcSnapshot dead = pending;
        if (pending.TypeIdentity == VanillaNpcIds.WallOfFleshEye && TryResolveWallOfFleshRoot(in pending, out var wall))
            dead = wall with { Simulation = wall.Simulation with { Life = 0 } };
        else if (IsDestroyerMember(pending.TypeIdentity) && TryResolveDestroyerRoot(in pending, out var destroyer))
            dead = destroyer with { Simulation = destroyer.Simulation with { Life = 0 } };
        if (!TryPrepareDeathPlan(in dead)) { deathAdmissionRejected = true; return false; }
        return true;
    }

    private bool TryPrepareDeathPlan(in NpcSnapshot dead)
    {
        if (pendingDeathPlan is not null || dead.Simulation.MoneyValue is not float value ||
            dead.Simulation.ExtraMoneyValue is not int extra || dead.Simulation.Midas is not bool midas)
            return false;
        plannedDeathProgression = progression.CaptureSnapshot();
        plannedGlobalLootWorld = CaptureGlobalLootWorld();
        plannedSpecificLowTiles = CaptureSpecificLowTiles();
        plannedSpecificPlayers = null;
        plannedSpecificInventorySerial = playerAuthority.NpcLootInventorySerial;
        plannedHealingFallback = null;
        plannedGuideNameOwner = default;
        plannedGuideName = null;
        plannedDeathSpawnSerial = null;
        if (dead.TypeIdentity == VanillaNpcIds.MotherSlime || dead.TypeIdentity == VanillaNpcIds.Slimer ||
            pendingTownStrike is not null)
        {
            if (!npcs.TryCaptureDeathMutationSerial(out ulong spawnSerial)) return false;
            plannedDeathSpawnSerial = spawnSerial;
        }
        for (int slot = 0; slot < plannedPlayers.Length; slot++)
            plannedPlayerPresence[slot] = players.TryGetPlayer(new((byte)slot), out plannedPlayers[slot]);
        var liveRandom = random.SourceRandom;
        var plan = new RuntimeNpcDeathDropPlan1458(dead.Handle, dead.Revision, liveRandom);
        if (pendingTownStrike is { } townStrike)
        {
            if (!liveRandom.HasSameState(townStrike.Before)) return false;
            plan.Random.CopyStateFrom(townStrike.After);
            plan.RetainStrikePrelude();
        }
        bool completed = false;
        float oldLuck = random.Luck;
        try
        {
            deathPreviewLiveRandom = liveRandom;
            random.UseSource(plan.Random);
            lootDelivery.Preview = plan;
            previewDeathNpcs = npcs.CreateDeathPreview(random);
            plannedDeathSpawnContext = previewDeathNpcs.CapturedDeathSpawnContext;
            previewDeathClock = worldClock?.CreateDeathPreview();
            previewDeathProgression = progression.CreateDeathPreview();
            previewDaily = bossRecoveryDaily.CreatePreview();
            deathPreviewFailed = false;
            if (!previewDeathNpcs.TryGet(dead.Handle, out var retained) || retained.Revision != dead.Revision) return false;
            var deathState = new NpcStateUpdate(dead.Type, dead.NetId, dead.PositionX, dead.PositionY,
                dead.VelocityX, dead.VelocityY, dead.Target, dead.Ai, dead.Simulation);
            if (!previewDeathNpcs.TryUpdate(dead.Handle, in deathState, out var previewDead)) return false;
            bool eaterBoss = VanillaEaterOfWorldsLifecycle.IsSegment(dead.TypeIdentity) &&
                VanillaEaterOfWorldsLifecycle.IsLastActiveSegment(previewDeathNpcs, in previewDead, npcFamilyBuffer);
            bool hasClosest = TryFindClosestPlayer(in dead, out var closest);
            // FindClosest returns raw slot0 when no player is active. Only the concrete
            // constructor/final-reset owner proves its Life100/statLifeMax2=100 predicate.
            TerraRuntime.Core.Npcs.NpcRawPlayerSlotSnapshot1458 rawFallback = default;
            bool constructorHealthKnown = !hasClosest && rawPlayerSlots is not null &&
                rawPlayerSlots.TryCapture(0, out rawFallback) &&
                !rawFallback.Facts.Active && rawFallback.LivePlayer is null;
            if (constructorHealthKnown) plannedHealingFallback = rawFallback;
            float luck = hasClosest ? closest.Luck : 0f;
            if (!float.IsFinite(luck)) return false;
            random.Luck = luck;
            ExecuteNpcDeathHitEffects(in previewDead);
            if (deathPreviewFailed) return false;
            plannedPreludeRevision = deathPrelude.Revision;
            var prelude = deathPrelude.CreatePreview();
            lootDelivery.Phase = NpcDeathDropPhase1458.Prelude;
            if (!plan.BeginPreviewPhase(NpcDeathDropPhase1458.Prelude) ||
                !TryCaptureDeathPreludeContext(in dead, eaterBoss, out var preludeContext) ||
                !prelude.TryApply(in dead, in preludeContext, plan.Random, out bool allowLoot) ||
                !plan.FinishPreviewPhase(NpcDeathDropPhase1458.Prelude)) return false;
            lootDelivery.Phase = NpcDeathDropPhase1458.Imported;
            if (!plan.BeginPreviewPhase(NpcDeathDropPhase1458.Imported) ||
                (allowLoot && !TryExecuteImportedLoot(in dead, eaterBoss)) ||
                !plan.FinishPreviewPhase(NpcDeathDropPhase1458.Imported)) return false;

            if (allowLoot) ExecuteOwnedDeathEvents(in dead, eaterBoss);
            if (deathPreviewFailed) return false;
            lootDelivery.Phase = NpcDeathDropPhase1458.Recovery;
            if (!plan.BeginPreviewPhase(NpcDeathDropPhase1458.Recovery)) return false;
            if (allowLoot) DropBossRecoveryItemsIfEligible(in dead, eaterBoss);
            if (!plan.FinishPreviewPhase(NpcDeathDropPhase1458.Recovery)) return false;
            lootDelivery.Phase = NpcDeathDropPhase1458.Money;
            if (!plan.BeginPreviewPhase(NpcDeathDropPhase1458.Money)) return false;
            if (MoneyClearedBeforeDeathPhase(in dead, eaterBoss)) value = 0f;
            var context = new VanillaNpcMoneyContext1458(value, extra, luck, midas, DeathClock?.BloodMoonActive == true);
            if (!VanillaNpcDefinitionCatalog.TryGet(dead.TypeIdentity, dead.NetIdentity, out var definition)) return false;
            var origin = ResolveNpcLootOrigin(in dead, in definition);
            var moneySink = new MoneyPlanningSink(lootDelivery, origin, random);
            if (allowLoot && !VanillaNpcMoneyLoot1458.TryPlan(in context, random, moneySink, MaximumMoneySplitAttempts)) return false;
            if (!plan.FinishPreviewPhase(NpcDeathDropPhase1458.Money)) return false;
            lootDelivery.Phase = NpcDeathDropPhase1458.Healing;
            if (!plan.BeginPreviewPhase(NpcDeathDropPhase1458.Healing)) return false;
            var healing = new VanillaNpcHealingContext1458(dead.TypeIdentity, dead.NetIdentity,
                dead.Simulation.LifeMax, dead.Simulation.DamageOverride ?? definition.Damage,
                hasClosest && (requireOwnedPlayerHealth
                    ? closest.NpcLife is int life && closest.DerivedLifeMax is int maximum && life < maximum
                    : closest.HasHealth && closest.Life < (closest.DerivedLifeMax ?? closest.MaxLife)),
                hasClosest && closest.HasMana && closest.Mana < closest.MaxMana, expertMode,
                LifeEligibilityKnown: !requireOwnedPlayerHealth ||
                    (hasClosest && closest.NpcLife.HasValue && closest.DerivedLifeMax.HasValue) || constructorHealthKnown);
            if ((allowLoot && !VanillaNpcHealingLoot1458.TryExecute(in healing, in origin, random, lootDelivery)) ||
                !plan.FinishPreviewPhase(NpcDeathDropPhase1458.Healing)) return false;
            random.UseSource(liveRandom);
            Span<WorldItemAllocationPlayer1458> allocationViews = stackalloc WorldItemAllocationPlayer1458[
                VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots];
            if (!TryCaptureAllocationViews(allocationViews, out int viewCount) ||
                !plan.TryReserve(worldItems, allocationViews[..viewCount]) || !plan.TryAccept(IsCurrentDeathOwner)) return false;
            plannedDaily = previewDaily;
            plannedPrelude = prelude;
            plannedLootAllowed = allowLoot;
            pendingDeathPlan = plan;
            captureDollDeath?.Invoke(new(dead, plan, plannedDaily!, prelude, plannedPreludeRevision, allowLoot));
            completed = true;
            return true;
        }
        finally
        {
            random.UseSource(liveRandom); random.Luck = oldLuck;
            lootDelivery.Preview = null;
            previewDeathNpcs = null; previewDeathClock = null; previewDeathProgression = null; previewDaily = null; deathPreviewLiveRandom = null;
            if (!completed)
            {
                plan.Dispose(); plannedPrelude = null; plannedPreludeRevision = 0; plannedLootAllowed = false;
            }
        }
    }

    private bool TryCaptureAllocationViews(Span<WorldItemAllocationPlayer1458> destination, out int count)
    {
        count = 0;
        // Source GetPlayerViewRects uses every active physical player, including dead players. Its fixed
        // 2320x1600 rectangle is centered on the actual mounted body, rather than client camera coordinates.
        for (int slot = 0; slot < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots; slot++)
        {
            if (!players.TryGetPlayer(new((byte)slot), out var player) || !player.Player.IsAssigned) continue;
            if (player.Player.Slot.Value != slot || !float.IsFinite(player.PositionX) || !float.IsFinite(player.PositionY))
                return false;
            var size = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) :
                (VanillaPlayerHitboxFacts.BaseWidth, VanillaPlayerHitboxFacts.BaseHeight);
            destination[count++] = new((byte)slot, player.PositionX, player.PositionY, (int)size.Item1, (int)size.Item2);
        }
        return true;
    }

    private bool EnsurePreviewSpawnStream()
    {
        if (!IsPreviewingDeath || previewDeathNpcs?.HasGoodWorldSpawnContext != true ||
            npcs.IsSpawnRandomSource(deathPreviewLiveRandom!)) return true;
        deathPreviewFailed = true;
        return false;
    }

    private void CancelPendingDeathPlan()
    {
        pendingDeathPlan?.Dispose(); pendingDeathPlan = null; plannedDaily = null;
        plannedPrelude = null; plannedPreludeRevision = 0; plannedLootAllowed = false;
    }

    private bool IsCurrentDeathOwner(NpcHandle handle, NpcRevision revision) =>
        (plannedDeathSpawnSerial is null ||
            (npcs.TryCaptureDeathSpawnContext(out var spawnContext) && spawnContext == plannedDeathSpawnContext)) &&
        npcs.TryGet(handle, out var current) && current.Revision == revision && deathPrelude.Revision == plannedPreludeRevision &&
        progression.CaptureSnapshot() == plannedDeathProgression &&
        CaptureGlobalLootWorld() == plannedGlobalLootWorld && IsSpecificLootContextCurrent() &&
        IsGuideNameCurrent() && ArePlannedPlayersCurrent() && IsCurrentDebuffPrelude() &&
        (plannedDeathSpawnSerial is not { } serial ||
            (npcs.TryCaptureDeathMutationSerial(out ulong currentSerial) && currentSerial == serial)) &&
        (plannedHealingFallback is not { } fallback || rawPlayerSlots!.IsCurrent(in fallback));

    private bool ArePlannedPlayersCurrent()
    {
        for (int slot = 0; slot < plannedPlayers.Length; slot++)
        {
            bool present = players.TryGetPlayer(new((byte)slot), out var player);
            if (present != plannedPlayerPresence[slot] || (present && player != plannedPlayers[slot])) return false;
        }
        return true;
    }

    private bool MoneyClearedBeforeDeathPhase(in NpcSnapshot dead, bool eaterBoss)
    {
        if (VanillaMechanicalBossLootEvaluator.IsTwin(dead.TypeIdentity))
        {
            var other = dead.TypeIdentity == VanillaNpcIds.Retinazer ? VanillaNpcIds.Spazmatism : VanillaNpcIds.Retinazer;
            for (int i = 0; i < DeathNpcs.Capacity; i++)
                if (DeathNpcs.TryGetActive((byte)i, out var peer) && peer.TypeIdentity == other) return true;
        }
        // CommonCode.DropItemFromNPC instanced rewards clear value before recipient materialization.
        return expertMode && (eaterBoss || VanillaBossRecovery1458.IsAdmittedRoot(dead.TypeIdentity));
    }

    private sealed class MoneyPlanningSink(RuntimeNpcLootDelivery1458 delivery, NpcLootWorldItemOrigin origin,
        INpcLootRollSource rolls) : INpcMoneyPlanningSink1458
    {
        public bool TryMaterialize(ItemTypeId type, int stack, INpcMoneyRandom1458 random)
        {
            if (stack <= 0 || stack > short.MaxValue) return false;
            var drop = new NpcLootDrop(type, (short)stack);
            return delivery.TryWorld(in origin, in drop, rolls);
        }
    }
}
