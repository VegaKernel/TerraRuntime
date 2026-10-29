using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    private readonly GemBunnyHomeStamp[] gemBunnyHomes = new GemBunnyHomeStamp[RuntimeTownNpcStateStore.MaximumTownNpcs];
    private struct GemBunnyHomeStamp
    {
        internal NpcHandle Owner;
        internal NpcRevision Revision;
        internal bool Retired;
        internal int SourceOldHomeX, SourceOldHomeY;
        internal bool SourceOldHomeless;
    }

    internal void ObserveGemBunnyHome(NpcStateCommitKind kind, in NpcSnapshot snapshot)
    {
        if (snapshot.Handle.Slot >= gemBunnyHomes.Length) return;
        ref var stamp = ref gemBunnyHomes[snapshot.Handle.Slot];
        if (stamp.Owner.IsAssigned && (stamp.Owner.Generation.Value > snapshot.Handle.Generation.Value ||
            stamp.Owner == snapshot.Handle && (stamp.Retired || stamp.Revision.Value > snapshot.Revision.Value))) return;
        bool cohort = HasGroundCritterHome(snapshot.Type);
        bool townHome = HasQualifiedTownHome(snapshot.Type);
        bool gem = cohort && snapshot.Simulation.AiStyleOverride?.Value != 3;
        if (stamp.Owner.IsAssigned && (stamp.Owner != snapshot.Handle || kind == NpcStateCommitKind.Despawn || !gem && !townHome))
            Volatile.Write(ref townHomeBaselineFrames[snapshot.Handle.Slot], null);
        // Retain the last observed revision when a squirrel changes style, so a
        // delayed older AI7 event cannot restore its retired home baseline.
        if (!cohort && !HasQualifiedSourceHomeHistory(snapshot.Type) && !stamp.Owner.IsAssigned) return;
        bool sameOwner = stamp.Owner == snapshot.Handle;
        var old = stamp;
        // NPC constructor old-home fields survive SetDefaults/Transform and WorldFile.LoadNPCs.
        // A silent join baseline must not acknowledge an UpdateHomeTileState source transition.
        stamp = new() { Owner = snapshot.Handle, Revision = snapshot.Revision, Retired = kind == NpcStateCommitKind.Despawn,
            SourceOldHomeX = sameOwner ? old.SourceOldHomeX : -1,
            SourceOldHomeY = sameOwner ? old.SourceOldHomeY : -1,
            SourceOldHomeless = sameOwner && old.SourceOldHomeless };
        if (stamp.Retired || !gem) return;
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
        if (!HasGroundCritterHome(before.Type) || after.Type != before.Type ||
            after.Simulation.TownNpc != false || before.Handle.Slot >= 200) return false;
        // The turning phase still executes AI7 home updates before changing style.
        // Later AI3 phases do not publish source UpdateHomeTileState transitions.
        if (before.Simulation.AiStyleOverride?.Value == 3) return true;
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

    private static bool HasGroundCritterHome(int type) =>
        VanillaGroundCritterAi1458.IsSupported(type) ||
        VanillaNpcTransformation1458.TryGetGroundCritterFamily(new(type), out _);
}
