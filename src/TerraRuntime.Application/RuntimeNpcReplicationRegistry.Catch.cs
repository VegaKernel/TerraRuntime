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

    public void NpcCatchCommitted(in NpcSnapshot caught, bool spawnNeedsSyncing)
    {
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
        // Only the exact new Gem Bunny lifecycle owns this source-verified catch publication.
        if (caught.Simulation.SpawnedFromStatue && caught.Type is >= 646 and <= 652 && caught.Type == caught.NetId)
            Broadcast(frame);
    }
}
