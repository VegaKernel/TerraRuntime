using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;

namespace TerraRuntime.Application;

internal enum NpcDeathDropPhase1458 : byte { Prelude, Imported, Recovery, Money, Healing }

// Bounded exact drop reservations retain physical states; preview never publishes items.
internal sealed class RuntimeNpcDeathDropPlan1458 : IDisposable
{
    private readonly PlannedDrop[] drops = new PlannedDrop[RuntimeWorldItemStore.VanillaCapacity];
    private readonly WorldItemDropReservation[] reservations = new WorldItemDropReservation[RuntimeWorldItemStore.VanillaCapacity];
    private readonly VanillaUnifiedRandom1458 originalRandom;
    private readonly VanillaUnifiedRandom1458 beforeRandom;
    private readonly VanillaUnifiedRandom1458?[] phaseRandom = new VanillaUnifiedRandom1458?[(int)NpcDeathDropPhase1458.Healing + 1];
    private readonly VanillaUnifiedRandom1458?[] beforePhaseRandom = new VanillaUnifiedRandom1458?[(int)NpcDeathDropPhase1458.Healing + 1];
    private RuntimeWorldItemStore? store;
    private int reserved;
    private int published;
    private int count;
    private int nextPhase;
    private bool accepted;
    private bool failed;

    public RuntimeNpcDeathDropPlan1458(NpcHandle owner, NpcRevision revision, VanillaUnifiedRandom1458 random)
    {
        if (!owner.IsAssigned || !revision.IsAssigned) throw new ArgumentException("Unowned death plan.");
        Owner = owner;
        Revision = revision;
        originalRandom = random;
        beforeRandom = random.Clone();
        Random = random.Clone();
    }

    public NpcHandle Owner { get; }
    public NpcRevision Revision { get; }
    public VanillaUnifiedRandom1458 Random { get; }
    public int Count => count;

    // Source HitEffect precedes the first loot phase and may create NPCs on the same trusted stream.
    public bool CanBeginDeathHitEffects() => !failed && accepted && store is not null && nextPhase == 0 &&
        originalRandom.HasSameState(beforeRandom);

    public bool TryStage(NpcDeathDropPhase1458 phase, in WorldItemDropStateUpdate state,
        ReadOnlySpan<PlayerHandle> recipients = default, int leaseTicks = 0)
    {
        if (failed || store is not null || accepted || count == drops.Length ||
            (int)phase != nextPhase || leaseTicks < 0 ||
            recipients.Length > byte.MaxValue || (leaseTicks == 0 && !recipients.IsEmpty))
        {
            failed = true;
            return false;
        }
        foreach (PlayerHandle recipient in recipients)
            if (!recipient.IsAssigned) { failed = true; return false; }
        drops[count++] = new(phase, state, recipients.ToArray(), leaseTicks);
        return true;
    }

    public bool BeginPreviewPhase(NpcDeathDropPhase1458 phase)
    {
        if (failed || store is not null || accepted || (int)phase != nextPhase) return false;
        beforePhaseRandom[nextPhase] = Random.Clone();
        return true;
    }

    public bool FinishPreviewPhase(NpcDeathDropPhase1458 phase)
    {
        if (failed || store is not null || accepted || (int)phase != nextPhase) return false;
        phaseRandom[nextPhase++] = Random.Clone();
        return true;
    }

    public bool TryReserve(RuntimeWorldItemStore items)
    {
        if (failed || nextPhase != phaseRandom.Length || store is not null || accepted ||
            !originalRandom.HasSameState(beforeRandom)) return false;
        store = items;
        while (reserved < count)
        {
            PlannedDrop drop = drops[reserved];
            WorldItemDropStateUpdate state = drop.State;
            if (!items.TryReserveDrop(in state, out reservations[reserved]))
            {
                failed = true;
                Dispose();
                return false;
            }
            reserved++;
        }
        return true;
    }

    // The caller checks the ORIGINAL NPC revision before performing its deterministic damage commit.
    public bool TryAccept(Func<NpcHandle, NpcRevision, bool> ownerIsCurrent)
    {
        if (failed || accepted || store is null || reserved != count ||
            !originalRandom.HasSameState(beforeRandom) || !ownerIsCurrent(Owner, Revision)) return false;
        accepted = true;
        nextPhase = 0;
        return true;
    }

    // HitEffect -> prelude -> imported -> owned death events -> recovery -> money -> healing.
    // Instanced callback adopts the existing
    // exact reservation and relays only to still-current generation-owned recipients.
    public bool TryPublishPhase(NpcDeathDropPhase1458 phase,
        Func<WorldItemDropReservation, WorldItemDropStateUpdate, PlayerHandle[], int, bool> adoptInstanced)
    {
        if (failed || !accepted || store is null || (int)phase != nextPhase ||
            !originalRandom.HasSameState(beforePhaseRandom[nextPhase]!)) return false;
        while (published < count && drops[published].Phase == phase)
        {
            PlannedDrop drop = drops[published];
            WorldItemDropReservation reservation = reservations[published];
            bool committed = drop.LeaseTicks == 0
                ? store.TryCommitReservedDrop(in reservation, out _)
                : adoptInstanced(reservation, drop.State, drop.Recipients, drop.LeaseTicks);
            if (!committed) throw new InvalidOperationException("An accepted single-writer death plan lost its reservation.");
            published++;
        }
        originalRandom.CopyStateFrom(phaseRandom[nextPhase++]!);
        return true;
    }

    public void Dispose()
    {
        if (store is null) return;
        while (reserved > published)
        {
            WorldItemDropReservation reservation = reservations[--reserved];
            if (!store.TryReleaseDropReservation(in reservation))
                throw new InvalidOperationException("Unpublished death-plan reservation lost ownership.");
        }
        store = null;
    }

    private readonly record struct PlannedDrop(NpcDeathDropPhase1458 Phase, WorldItemDropStateUpdate State,
        PlayerHandle[] Recipients, int LeaseTicks);
}
