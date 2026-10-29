using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    // Transport ownership only: retained births have no live frame yet, so its owner cannot
    // protect a newly reused physical slot from an earlier fanout's delayed catch callback.
    private readonly NpcHandle[] observedCatchSlotOwners = new NpcHandle[MaxNpcSlots];

    private void ObserveCatchSlotOwner(in NpcSnapshot snapshot)
    {
        ref var current = ref observedCatchSlotOwners[snapshot.Handle.Slot];
        if (!current.IsAssigned || current.Generation.Value < snapshot.Handle.Generation.Value)
            current = snapshot.Handle;
    }

    public void NpcRetirementCommitted(in NpcSnapshot caught, bool spawnNeedsSyncing)
    {
        if (activeHistoricalPublication is { } prepared && prepared.MatchesCatch(in caught, spawnNeedsSyncing))
        {
            prepared.TryPublishHead();
            return;
        }
        var current = observedCatchSlotOwners[caught.Handle.Slot];
        if (current.IsAssigned && current.Generation.Value > caught.Handle.Generation.Value) return;
        if (!RuntimeNpcPacketProjection.TryCreate(in caught, RuntimeNpcSyncKind.Despawn, out var inactive) ||
            !TerrariaNpcUpdateEncoder.TryEncode(inactive with { SpawnNeedsSyncing = spawnNeedsSyncing }, out var frame))
        {
            Interlocked.Increment(ref unsupportedCommits);
            return;
        }
        ObserveCatchSlotOwner(in caught);
        ObserveGemBunnyHome(NpcStateCommitKind.Despawn, in caught);
        Volatile.Write(ref baselineFrames[caught.Handle.Slot], null);
        Volatile.Write(ref despawnFrames[caught.Handle.Slot], null);
        if (caught.Handle.Slot < retainedBirthJournals.Length)
            retainedBirthJournals[caught.Handle.Slot] = null;
        ClearLiveFrame(caught.Handle);
        if (UsesUndeadNetworkPhase1458(in caught)) ResetSourceProximity(in caught);
        Broadcast(frame, UsesUndeadNetworkPhase1458(in caught) ? caught.Handle : null);
    }

    internal void PublishStatueCatchSmoke(in NpcSnapshot caught, byte[] frame)
    {
        // CatchNPC's statue smoke is independent of AI style. The catch owner
        // already admitted the live generation; Mystic Frog takes another path.
        if (caught.Simulation.SpawnedFromStatue &&
            !VanillaNpcCatchCatalog1458.IsMysticFrog(caught.TypeIdentity) &&
            VanillaNpcCatchCatalog1458.TryGetCatchItem(caught.TypeIdentity, out _))
            Broadcast(frame);
    }
}
