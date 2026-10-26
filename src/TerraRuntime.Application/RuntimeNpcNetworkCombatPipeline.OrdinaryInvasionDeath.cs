using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private RuntimeNpcStore.TerminalRemoval? plannedOrdinaryInvasionRemoval;
    private RuntimeInvasionCapture1458 plannedOrdinaryInvasion;
    private InvasionTransition1458 plannedOrdinaryInvasionCredit;
    private RuntimeInvasionCapture1458 acceptedOrdinaryInvasion;
    private RuntimeNpcReplicationRegistry.RetainedDeathPublication? plannedOrdinaryDeathPublication;

    private bool CaptureOrdinaryInvasionDeath(int type) => invasion is null ||
        invasion.TryCapture(out plannedOrdinaryInvasion) &&
        VanillaInvasionLifecycle1458.TryCreditDeath(plannedOrdinaryInvasion.State, type,
            out plannedOrdinaryInvasionCredit) && IsOrdinaryInvasionCurrent();

    private bool IsOrdinaryInvasionCurrent() => invasion is null ||
        invasion.CanAdopt(in plannedOrdinaryInvasion, in plannedOrdinaryInvasionCredit);

    private bool TryRetainOrdinaryInvasionOwners(in NpcSnapshot dead)
    {
        plannedOrdinaryDeathPublication = null;
        return npcs.TryPrepareProjectedTerminalRemoval(in dead, out plannedOrdinaryInvasionRemoval) &&
            IsOrdinaryInvasionCurrent() && (npcReplication is null ||
                npcReplication.TryPrepareRetainedDeath(plannedOrdinaryInvasionRemoval!, out plannedOrdinaryDeathPublication));
    }

    private bool CanExcludeUnownedDungeonSpirit(in NpcSnapshot dead)
    {
        // DoDeathEvents_SummonDungeonSpirit (1.4.5.8) rolls before its wall test.
        // A possible spirit birth has no retained producer here; only a captured
        // earlier false predicate can exclude its RNG and entity-allocation offers.
        if (dead.Simulation.LifeMax <= 100 || dead.Simulation.MoneyValue is <= 0f || dead.Target >= 255)
            return true;
        if (plannedGlobalLootWorld is { HardMode: false } &&
            !plannedDeathProgression.IsCompleted(VanillaWorldProgressionId.Hardmode)) return true;
        if (planteraDownedBaseline == false &&
            !plannedDeathProgression.IsCompleted(VanillaWorldProgressionId.Plantera)) return true;
        return plannedPlayerPresence[dead.Target] && plannedPlayers[dead.Target].Zones is { Dungeon: false };
    }

    private void AdoptOrdinaryInvasionOwners(in NpcSnapshot committed)
    {
        if (plannedOrdinaryInvasionRemoval?.IsCurrent != true || !IsOrdinaryInvasionCurrent() ||
            pendingDeathPlan?.CanAdoptUnpublished() != true ||
            deathPrelude.Revision != plannedPreludeRevision ||
            progression.CaptureSnapshot() != plannedDeathProgression ||
            CaptureStrikeClock() != plannedStrikeClock || CaptureStrikeDaily() != plannedStrikeDaily)
            throw new InvalidOperationException("An ordinary invasion death lost a retained owner after HP adoption.");
        // The existing item owner allocates its publication before writing. Every remaining
        // adoption is a direct sealed-owner assignment, without providers or observers.
        if (!pendingDeathPlan.TryAdoptUnpublished() ||
            !deathPrelude.TryAdoptUnpublished(plannedPrelude!, plannedPreludeRevision,
                out preparedDeathPreludePublication) ||
            !plannedOrdinaryInvasionRemoval.TryAdoptUnpublished() ||
            invasion is not null && !invasion.TryAdopt(in plannedOrdinaryInvasion,
                in plannedOrdinaryInvasionCredit, out acceptedOrdinaryInvasion))
            throw new InvalidOperationException("Validated invasion death owners changed without a callback.");
        npcSpawnCycle?.SuppressNext();
        if (plannedDaily is not null) bossRecoveryDaily.CopyFrom(plannedDaily);
        if (worldClock is not null && plannedUnpublishedClock is not null)
            worldClock.AdoptDeathProgress(plannedUnpublishedClock);
        npcReplication?.RetirePreparedDeathBaseline(committed.Handle);
        if (pendingProjectileStrike is { } strike) strike.AcceptedDeath = pendingDeathPlan;
    }

    private void PublishOrdinaryInvasionDeath(in NpcSnapshot committed, in TerrariaNpcDamageState wire,
        NpcDamagePrelude1458? prelude)
    {
        var drops = pendingDeathPlan!;
        if (!drops.TryBeginLinkedPublication()) return;
        var removal = plannedOrdinaryInvasionRemoval!;
        var credit = plannedOrdinaryInvasionCredit;
        var acceptedInvasion = acceptedOrdinaryInvasion;
        var preludePublication = preparedDeathPreludePublication;
        var npcPublication = plannedOrdinaryDeathPublication;
        var acceptedNpc = committed;
        var acceptedWire = wire;
        // Release the planning envelope before the first callback. All accepted work below
        // belongs to this invocation, even if an observer completes another death here.
        pendingDeathPlan = null;
        pendingProjectileStrike = null;
        plannedOrdinaryInvasionRemoval = null;
        plannedOrdinaryDeathPublication = null;
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
        // NetMessage.SendData(28) flushes an unannounced NewNPC generation first,
        // exposing its retained pre-strike state without restoring a dead join baseline.
        if (npcPublication is not null) Drain(() => npcPublication.TryPublishPendingBirth());
        else Drain(() => removal.TryPublishPendingBirthBeforeStrike());
        if (removal.IsAcceptedCurrent)
            Drain(() => npcReplication?.TryPublishDamage(default, in acceptedWire));
        Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Prelude, lootDelivery.Adopt));
        Drain(() => preludePublication?.TryPublish());
        Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Imported, lootDelivery.Adopt));
        Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Recovery, lootDelivery.Adopt));
        Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Money, lootDelivery.Adopt));
        Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Healing, lootDelivery.Adopt));
        // NPC.checkDead sends progress only after all source loot and active=false.
        if (credit.Effects.Progress && invasion!.IsCurrent(in acceptedInvasion))
            Drain(() => invasionProgressPublisher!(acceptedInvasion));
        if (npcPublication is not null) Drain(() => npcPublication.TryPublishTerminal());
        else Drain(() => removal.TryPublish());
        interactions.Forget(committed.Handle);
        drops.Dispose();
        failure?.Throw();
    }
}
