using System.Runtime.ExceptionServices;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private RuntimeNpcStore.DeathBirth? plannedCaptainBirth;
    private RuntimeNpcStore.TerminalRemoval? plannedCaptainRemoval;
    private RuntimeInvasionCapture1458 plannedCaptainInvasion;
    private InvasionTransition1458 plannedCaptainCredit;
    private RuntimeInvasionCapture1458 acceptedCaptainInvasion;
    private RuntimeNpcReplicationRegistry.RetainedBirthJournal? plannedCaptainBirthJournal;
    private RuntimeNpcReplicationRegistry.RetainedDeathPublication? plannedCaptainDeathPublication;

    private bool CaptureCaptainInvasion(int type = 216)
    {
        plannedCaptainInvasion = default;
        plannedCaptainCredit = default;
        return invasion is null || invasion.TryCapture(out plannedCaptainInvasion) &&
            VanillaInvasionLifecycle1458.TryCreditDeath(plannedCaptainInvasion.State,
                type, out plannedCaptainCredit) &&
            invasion.CanAdopt(in plannedCaptainInvasion, in plannedCaptainCredit);
    }

    private bool IsCaptainInvasionCurrent() => invasion is null ||
        invasion.CanAdopt(in plannedCaptainInvasion, in plannedCaptainCredit);

    private bool TryRetainCaptainOwners(in NpcSnapshot projected)
    {
        plannedCaptainDeathPublication = null;
        if (!(plannedDeathSpawnSerial is { } serial && previewDeathNpcs is not null &&
            IsCaptainInvasionCurrent() && npcs.TryPrepareCaptainDeathBirth(in projected,
                previewDeathNpcs, serial, out plannedCaptainBirth) &&
            npcs.TryPrepareProjectedTerminalRemoval(in projected, out plannedCaptainRemoval))) return false;
        if (npcReplication is not null && !npcReplication.TryPrepareRetainedDeath(
                plannedCaptainRemoval!, out plannedCaptainDeathPublication)) return false;
        return npcReplication is null || plannedCaptainBirth!.Child is not { } child ||
            npcReplication.TryPrepareRetainedBirth(in child, out plannedCaptainBirthJournal);
    }

    private void AdoptCaptainOwners(in NpcSnapshot committed)
    {
        if (plannedCaptainBirth?.CanAdopt(in committed) != true ||
            plannedCaptainRemoval?.IsCurrent != true || !IsCaptainInvasionCurrent() ||
            pendingDeathPlan?.CanAdoptUnpublished() != true ||
            deathPrelude.Revision != plannedPreludeRevision ||
            progression.CaptureSnapshot() != plannedDeathProgression)
            throw new InvalidOperationException("A prepared Captain death lost a pure owner after HP adoption.");
        // Item publication is allocated inside its validated adoption before the first write.
        // The remaining sealed owners only assign prebuilt values; no provider or callback runs here.
        if (!pendingDeathPlan.TryAdoptUnpublished() ||
            !deathPrelude.TryAdoptUnpublished(plannedPrelude!, plannedPreludeRevision,
                out preparedDeathPreludePublication) ||
            !plannedCaptainBirth.TryAdoptUnpublished(in committed) ||
            !plannedCaptainRemoval.TryAdoptUnpublished() ||
            invasion is not null && !invasion.TryAdopt(in plannedCaptainInvasion, in plannedCaptainCredit,
                out acceptedCaptainInvasion))
            throw new InvalidOperationException("Validated Captain owners changed without a callback.");
        if (plannedDaily is not null) bossRecoveryDaily.CopyFrom(plannedDaily);
        if (worldClock is not null && plannedUnpublishedClock is not null)
            worldClock.AdoptDeathProgress(plannedUnpublishedClock);
        if (plannedCaptainBirthJournal is not null && !plannedCaptainBirthJournal.TryAdoptBaseline(npcs))
            throw new InvalidOperationException("Validated Captain child baseline could not be adopted.");
        npcReplication?.RetirePreparedDeathBaseline(committed.Handle);
        if (pendingProjectileStrike is { } strike) strike.AcceptedDeath = pendingDeathPlan;
        preparedProjectileCommittedNpc = committed;
    }

    private void PublishCaptainDeath(in NpcSnapshot committed, in TerrariaNpcDamageState wire,
        GameCommandSourceId excludedSource, NpcDamagePrelude1458? prelude = null)
    {
        var drops = pendingDeathPlan!;
        if (!drops.TryBeginLinkedPublication()) return;
        ExceptionDispatchInfo? failure = null;
        void Drain(Action action)
        {
            try { action(); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        }
        var retainedWire = wire;
        var capturedCommitted = committed;
        // Every accepted component belongs to this invocation. A nested death through this
        // same pipeline may replace all planning fields during the first observer.
        var deathPreludePublication = preparedDeathPreludePublication;
        var birth = plannedCaptainBirth!;
        var birthJournal = plannedCaptainBirthJournal;
        var removal = plannedCaptainRemoval!;
        var terminalPublication = plannedCaptainDeathPublication;
        var credit = plannedCaptainCredit;
        var acceptedInvasion = acceptedCaptainInvasion;
        // Accepted owners now belong to the local journal. A nested command can prepare a
        // distinct death without disposing this journal or inheriting its projectile checkpoint.
        pendingDeathPlan = null;
        pendingProjectileStrike = null;
        plannedCaptainBirth = null;
        plannedCaptainRemoval = null;
        plannedCaptainBirthJournal = null;
        plannedCaptainDeathPublication = null;
        preparedDeathPreludePublication = null;
        Drain(() => prelude?.PublishBeforeStrike(capturedCommitted));
        if (terminalPublication is not null) Drain(() => terminalPublication.TryPublishPendingBirth());
        else Drain(() => removal.TryPublishPendingBirthBeforeStrike());
        if (removal.IsAcceptedCurrent)
            Drain(() => npcReplication?.TryPublishDamage(excludedSource, in retainedWire));
        Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Prelude, lootDelivery.Adopt));
        Drain(() => deathPreludePublication?.TryPublish());
        Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Imported, lootDelivery.Adopt));
        // DoDeathEvents Captain birth lies after specific drops and before Money/Healing.
        Drain(() => birth.TryPublish());
        // A separate operations observer may throw before the fanout reaches replication.
        // The retained network component still drains once against its exact accepted child.
        Drain(() => birthJournal?.TryPublish());
        Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Recovery, lootDelivery.Adopt));
        Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Money, lootDelivery.Adopt));
        Drain(() => drops.TryPublishPhase(NpcDeathDropPhase1458.Healing, lootDelivery.Adopt));
        if (credit.Effects.Progress && invasion!.IsCurrent(in acceptedInvasion))
            Drain(() => invasionProgressPublisher!(acceptedInvasion));
        if (terminalPublication is not null) Drain(() => terminalPublication.TryPublishTerminal());
        else Drain(() => removal.TryPublish());
        interactions.Forget(committed.Handle);
        drops.Dispose();
        failure?.Throw();
    }
}
