using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private RuntimeNpcStore.TerminalRemoval? plannedMoonRemoval;
    private RuntimeNpcReplicationRegistry.RetainedDeathPublication? plannedMoonTerminalPublication;
    private MoonEventDeathEffects1458 plannedMoonDeathEffects;
    private VanillaWorldProgressionId? plannedMoonMilestone;
    private bool plannedMoonMarkMilestone;
    private bool plannedMoonLantern;
    private RuntimeInvasionCapture1458 plannedMoonInvasion;
    private InvasionTransition1458 plannedMoonLanternTransition;
    private bool plannedMoonMoneyCleared;

    private bool CaptureMoonDeath(in NpcSnapshot dead, bool allowLoot)
    {
        plannedMoonRemoval = null;
        plannedMoonTerminalPublication = null;
        plannedMoonDeathEffects = default;
        plannedMoonMoneyCleared = false;
        Array.Clear(plannedMoonPlayerBodies);
        plannedMoonMarkMilestone = false;
        plannedMoonLantern = false;
        plannedMoonMilestone = dead.Type switch
        {
            325 => VanillaWorldProgressionId.MourningWood,
            327 => VanillaWorldProgressionId.Pumpking,
            344 => VanillaWorldProgressionId.Everscream,
            345 => VanillaWorldProgressionId.IceQueen,
            346 => VanillaWorldProgressionId.SantaNk1,
            _ => null
        };
        if (plannedMoonMilestone is not { } milestone) return true;
        plannedMoonMarkMilestone = allowLoot && (dead.Type is 325 or 327
            ? worldClock?.PumpkinMoonActive == true : worldClock?.SnowMoonActive == true);
        if (!plannedMoonMarkMilestone || dead.Type is not (327 or 345)) return true;

        // NPC.SetEventFlagCleared consults the loaded flag as well as runtime clears.
        // Only Pumpking(5) and Ice Queen(20) schedule Lantern Night; the other three
        // Moon game-event IDs are explicit no-ops in OnGameEventClearedForTheFirstTime.
        bool? alreadyCleared = plannedDeathProgression.IsCompleted(milestone)
            ? true : progressionBaseline?.IsComplete(milestone);
        if (!alreadyCleared.HasValue) return false;
        plannedMoonLantern = !alreadyCleared.Value;
        if (!plannedMoonLantern) return true;
        if (invasion is null || !invasion.TryCapture(out plannedMoonInvasion)) return false;
        plannedMoonLanternTransition = new(plannedMoonInvasion.State with { LanternNextNight = true }, default);
        return invasion.CanAdopt(in plannedMoonInvasion, in plannedMoonLanternTransition);
    }

    private bool IsMoonDeathCurrent() => AreMoonPlayerBodiesCurrent() &&
        (plannedMoonMilestone is not { } milestone
            ? progression.CaptureSnapshot() == plannedDeathProgression
            : progression.CanAdoptCompletedMilestone(in plannedDeathProgression, milestone, plannedMoonMarkMilestone)) &&
        (!plannedMoonLantern || invasion!.CanAdopt(in plannedMoonInvasion, in plannedMoonLanternTransition));

    private bool IsMoonPreviewProgressionValid(in RuntimeWorldProgressionMutationSnapshot preview)
    {
        var expected = plannedDeathProgression;
        if (plannedMoonMarkMilestone && plannedMoonMilestone is { } milestone)
            expected = expected with { CompletedMask = expected.CompletedMask | (1UL << (int)milestone) };
        return preview == expected && IsMoonDeathCurrent();
    }

    private void ApplyMoonBossDeathEffects()
    {
        if (plannedMoonMarkMilestone && plannedMoonMilestone is { } milestone)
            DeathProgression.MarkCompleted(milestone);
    }

    private bool TryRetainMoonDeathOwners(in NpcSnapshot dead) =>
        IsMoonDeathCurrent() && npcs.TryPrepareProjectedTerminalRemoval(in dead, out plannedMoonRemoval) &&
        (npcReplication is null || npcReplication.TryPrepareRetainedDeath(plannedMoonRemoval!, out plannedMoonTerminalPublication));

    private void AdoptMoonDeathOwners(in NpcSnapshot committed)
    {
        if (plannedMoonRemoval?.IsCurrent != true || !IsMoonDeathCurrent() ||
            pendingDeathPlan?.CanAdoptUnpublished() != true ||
            deathPrelude.Revision != plannedPreludeRevision ||
            CaptureStrikeClock() != plannedStrikeClock || CaptureStrikeDaily() != plannedStrikeDaily)
            throw new InvalidOperationException("A retained Moon death lost a validated owner after HP adoption.");
        // Every remaining operation uses sealed scalar/item/terminal owners, without
        // providers or observers. Unrelated progression fields are never copied from a preview.
        if (!pendingDeathPlan.TryAdoptUnpublished() ||
            !deathPrelude.TryAdoptUnpublished(plannedPrelude!, plannedPreludeRevision, out preparedDeathPreludePublication) ||
            plannedMoonMilestone is { } milestone &&
                !progression.TryAdoptCompletedMilestone(in plannedDeathProgression, milestone, plannedMoonMarkMilestone) ||
            plannedMoonLantern && !invasion!.TryAdopt(in plannedMoonInvasion, in plannedMoonLanternTransition, out _) ||
            !plannedMoonRemoval.TryAdoptUnpublished())
            throw new InvalidOperationException("Validated Moon death owners changed without a callback.");
        if (plannedDaily is not null) bossRecoveryDaily.CopyFrom(plannedDaily);
        if (worldClock is not null && plannedUnpublishedClock is not null)
            worldClock.AdoptDeathProgress(plannedUnpublishedClock);
        npcSpawnCycle?.SuppressNext();
        npcReplication?.RetirePreparedDeathBaseline(committed.Handle);
        if (pendingProjectileStrike is { } strike) strike.AcceptedDeath = pendingDeathPlan;
    }

    private void PublishMoonDeath(in NpcSnapshot committed, in TerrariaNpcDamageState wire,
        NpcDamagePrelude1458? prelude)
    {
        var drops = pendingDeathPlan!;
        if (!drops.TryBeginLinkedPublication()) return;
        var removal = plannedMoonRemoval!;
        var terminalPublication = plannedMoonTerminalPublication;
        var effects = plannedMoonDeathEffects;
        var preludePublication = preparedDeathPreludePublication;
        var acceptedNpc = committed;
        var acceptedWire = wire;
        // Accepted state belongs to this invocation. A nested different death may now
        // reuse the planning envelope without cancelling or replaying this publication.
        pendingDeathPlan = null;
        pendingProjectileStrike = null;
        plannedMoonRemoval = null;
        plannedMoonTerminalPublication = null;
        plannedMoonMilestone = null;
        preparedDeathPreludePublication = null;
        plannedDaily = null;
        plannedPrelude = null;
        plannedUnpublishedClock = null;
        plannedLootAllowed = false;
        ExceptionDispatchInfo? failure = null;
        void Drain(Action action)
        {
            try { action(); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        }
        Drain(() => worldStateAdopted?.Invoke());
        Drain(() => prelude?.PublishBeforeStrike(acceptedNpc));
        if (terminalPublication is not null) Drain(() => terminalPublication.TryPublishPendingBirth());
        else Drain(() => removal.TryPublishPendingBirthBeforeStrike());
        if (removal.IsAcceptedCurrent) Drain(() => npcReplication?.TryPublishDamage(default, in acceptedWire));
        Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Prelude, lootDelivery.Adopt));
        Drain(() => preludePublication?.TryPublish());
        Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Imported, lootDelivery.Adopt));
        Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Recovery, lootDelivery.Adopt));
        Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Money, lootDelivery.Adopt));
        Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Healing, lootDelivery.Adopt));
        // Source checkDead: NPCLoot, active=false, CheckProgressFrost/PumpkinMoon,
        // then the strike's final packet23. No ordinary boss celebration is added.
        if (effects.MoonIcon != 0) Drain(() => npcReplication?.PublishMoonEventDeathEffects(in effects));
        if (terminalPublication is not null) Drain(() => terminalPublication.TryPublishTerminal());
        else Drain(() => removal.TryPublish());
        interactions.Forget(committed.Handle);
        drops.Dispose();
        failure?.Throw();
    }
}
