using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    private readonly GemBunnyHomeStamp[] gemBunnyHomes = new GemBunnyHomeStamp[RuntimeTownNpcStateStore.MaximumTownNpcs];
    private struct GemBunnyHomeStamp { internal NpcHandle Owner; internal NpcRevision Revision; internal bool Retired; }

    internal void ObserveGemBunnyHome(NpcStateCommitKind kind, in NpcSnapshot snapshot)
    {
        if (snapshot.Handle.Slot >= gemBunnyHomes.Length) return;
        ref var stamp = ref gemBunnyHomes[snapshot.Handle.Slot];
        if (stamp.Owner.IsAssigned && (stamp.Owner.Generation.Value > snapshot.Handle.Generation.Value ||
            stamp.Owner == snapshot.Handle && (stamp.Retired || stamp.Revision.Value > snapshot.Revision.Value))) return;
        bool gem = VanillaGemBunnyAi1458.IsSupported(snapshot.Type);
        if (stamp.Owner.IsAssigned && (stamp.Owner != snapshot.Handle || kind == NpcStateCommitKind.Despawn || !gem))
            Volatile.Write(ref townHomeBaselineFrames[snapshot.Handle.Slot], null);
        if (!gem) return;
        stamp = new() { Owner = snapshot.Handle, Revision = snapshot.Revision, Retired = kind == NpcStateCommitKind.Despawn };
        if (stamp.Retired) return;
        if (snapshot.Simulation.HomeTileX is not { } x || x is < short.MinValue or > short.MaxValue ||
            snapshot.Simulation.HomeTileY is not { } y || y is < short.MinValue or > short.MaxValue ||
            snapshot.Simulation.Homeless is not { } homeless) return;
        var state = new TerrariaNpcHomeState(snapshot.Handle.Slot, (short)x, (short)y,
            homeless ? (byte)TerrariaNpcHomeStatus.Homeless : (byte)TerrariaNpcHomeStatus.None);
        if (TerrariaNpcHomeCodec.TryEncode(in state, out var encoded) == TerrariaNpcHomeEncodeResult.Encoded)
            Volatile.Write(ref townHomeBaselineFrames[snapshot.Handle.Slot], encoded);
    }

    internal void RetireGemBunnyHome(NpcHandle owner)
    {
        if (owner.Slot >= gemBunnyHomes.Length) return;
        ref var stamp = ref gemBunnyHomes[owner.Slot];
        if (stamp.Owner != owner) return;
        stamp.Retired = true;
        Volatile.Write(ref townHomeBaselineFrames[owner.Slot], null);
    }

    // NPC.UpdateHomeTileState also sends60 for nonresident TownCritter actors. This is a live
    // metadata transition. SendNPCHousesAndTravelShop also replays it for AI7 nonresidents;
    // the existing home baseline storage above is guarded by the physical NPC generation.
    internal static bool TryPrepareGemBunnyHome(in NpcSnapshot before, in NpcStateUpdate after, out byte[]? frame)
    {
        frame = null;
        if (!VanillaGemBunnyAi1458.IsSupported(before.Type) || after.Type != before.Type ||
            after.Simulation.TownNpc != false || before.Handle.Slot >= 200) return false;
        if (before.Simulation.HomeTileX == after.Simulation.HomeTileX &&
            before.Simulation.HomeTileY == after.Simulation.HomeTileY &&
            before.Simulation.Homeless == after.Simulation.Homeless) return true;
        if (after.Simulation.HomeTileX is not { } x || x is < short.MinValue or > short.MaxValue ||
            after.Simulation.HomeTileY is not { } y || y is < short.MinValue or > short.MaxValue ||
            after.Simulation.Homeless is not { } homeless) return false;
        var state = new TerrariaNpcHomeState(before.Handle.Slot, (short)x, (short)y,
            homeless ? (byte)TerrariaNpcHomeStatus.Homeless : (byte)TerrariaNpcHomeStatus.None);
        if (TerrariaNpcHomeCodec.TryEncode(in state, out var encoded) != TerrariaNpcHomeEncodeResult.Encoded) return false;
        frame = encoded;
        return true;
    }

    internal void PublishGemBunnyHome(byte[] frame) => Broadcast(frame);
    internal void PublishGemBunnyBuffs(byte[] frame) => Broadcast(frame);
}
