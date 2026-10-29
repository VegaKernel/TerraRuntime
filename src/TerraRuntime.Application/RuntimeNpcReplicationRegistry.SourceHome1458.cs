using System.Buffers;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    internal readonly record struct SourceTownHomePhase1458(NpcHandle Owner, int Type, NpcRevision NpcRevision, NpcRevision Revision,
        int ExpectedX, int ExpectedY, bool ExpectedHomeless, int NextX, int NextY, bool NextHomeless,
        byte[] Baseline, byte[]? LateFrame);

    private static bool HasQualifiedTownHome(int type) =>
        type == VanillaNpcIds.TaxCollector.Value || type == VanillaNpcIds.TownSlimeYellow.Value;

    private static bool HasQualifiedSourceHomeHistory(int type) => HasQualifiedTownHome(type) ||
        type == VanillaNpcIds.DemonTaxCollector.Value || type == VanillaNpcIds.MysticFrog.Value;

    internal bool TryPrepareSourceTownHome(in NpcSnapshot before, in NpcStateUpdate final,
        in RuntimeTownNpcHomeCommit home, bool updatedByAi, out SourceTownHomePhase1458 plan)
    {
        plan = default;
        if (!HasQualifiedTownHome(before.Type) || final.Type != before.Type || final.NetId != before.NetId ||
            home.NpcSlot != before.Handle.Slot || home.NpcType != before.TypeIdentity ||
            home.HomeTileX is < short.MinValue or > short.MaxValue || home.HomeTileY is < short.MinValue or > short.MaxValue ||
            before.Handle.Slot >= gemBunnyHomes.Length || before.Revision.Value == ulong.MaxValue || final.Simulation.TownNpc != true ||
            final.Simulation.HomeTileX != home.HomeTileX || final.Simulation.HomeTileY != home.HomeTileY ||
            final.Simulation.Homeless is not bool homeless || homeless != (home.Status == TerrariaNpcHomeStatus.Homeless)) return false;
        var stamp = gemBunnyHomes[before.Handle.Slot];
        if (stamp.Owner != before.Handle || stamp.Retired || stamp.Revision.Value > before.Revision.Value) return false;
        var wire = home.ToWireState();
        if (TerrariaNpcHomeCodec.TryEncode(in wire, out var baseline) != TerrariaNpcHomeEncodeResult.Encoded) return false;
        bool dirty = !updatedByAi && (stamp.SourceOldHomeX != home.HomeTileX ||
            stamp.SourceOldHomeY != home.HomeTileY || stamp.SourceOldHomeless != homeless);
        plan = new(before.Handle, before.Type, before.Revision, stamp.Revision, stamp.SourceOldHomeX, stamp.SourceOldHomeY,
            stamp.SourceOldHomeless, home.HomeTileX, home.HomeTileY, homeless, baseline, dirty ? baseline : null);
        return true;
    }

    internal bool IsSourceTownHomeCurrent(in SourceTownHomePhase1458 plan)
    {
        if (!plan.Owner.IsAssigned || plan.Owner.Slot >= gemBunnyHomes.Length) return false;
        var stamp = gemBunnyHomes[plan.Owner.Slot];
        return stamp.Owner == plan.Owner && stamp.Revision == plan.Revision && !stamp.Retired &&
            stamp.SourceOldHomeX == plan.ExpectedX && stamp.SourceOldHomeY == plan.ExpectedY &&
            stamp.SourceOldHomeless == plan.ExpectedHomeless;
    }

    internal bool TryAdoptSourceTownHome(in SourceTownHomePhase1458 plan, in NpcSnapshot accepted)
    {
        if (accepted.Handle != plan.Owner || accepted.Type != plan.Type || accepted.NetId != plan.Type ||
            accepted.Revision.Value != plan.NpcRevision.Value + 1 ||
            accepted.Simulation.HomeTileX != plan.NextX || accepted.Simulation.HomeTileY != plan.NextY ||
            accepted.Simulation.Homeless != plan.NextHomeless || !IsSourceTownHomeCurrent(in plan)) return false;
        ref var stamp = ref gemBunnyHomes[accepted.Handle.Slot];
        stamp.SourceOldHomeX = plan.NextX; stamp.SourceOldHomeY = plan.NextY;
        stamp.SourceOldHomeless = plan.NextHomeless;
        Volatile.Write(ref townHomeBaselineFrames[accepted.Handle.Slot], plan.Baseline);
        return true;
    }

    // Explicit source UpdateHomeTileState acknowledgment; merely installing join bytes never calls this.
    internal bool TryAdoptSourceHomeUpdate(in NpcSnapshot accepted, byte[] encoded)
    {
        // Only these two qualified resident lineages consume the retained old-home history.
        // Nonresident critter join baselines and their existing wire paths remain independent.
        if (!CanAdoptSourceHomeUpdate(in accepted, encoded)) return false;
        if (!TryDecodeSourceHome(encoded, out var home)) return false;
        home.TryGetStatus(out var status);
        if (accepted.Simulation.HomeTileX != home.HomeTileX || accepted.Simulation.HomeTileY != home.HomeTileY ||
            accepted.Simulation.Homeless != (status == TerrariaNpcHomeStatus.Homeless)) return false;
        ref var stamp = ref gemBunnyHomes[accepted.Handle.Slot];
        stamp.SourceOldHomeX = home.HomeTileX; stamp.SourceOldHomeY = home.HomeTileY;
        stamp.SourceOldHomeless = status == TerrariaNpcHomeStatus.Homeless;
        return true;
    }

    internal bool CanAdoptSourceHomeUpdate(in NpcSnapshot actor, byte[] encoded) =>
        HasQualifiedSourceHomeHistory(actor.Type) && actor.Handle.Slot < gemBunnyHomes.Length &&
        TryDecodeSourceHome(encoded, out var home) && home.NpcSlot == actor.Handle.Slot &&
        gemBunnyHomes[actor.Handle.Slot].Owner == actor.Handle && !gemBunnyHomes[actor.Handle.Slot].Retired &&
        gemBunnyHomes[actor.Handle.Slot].Revision.Value <= actor.Revision.Value;

    private static bool TryDecodeSourceHome(byte[] encoded, out TerrariaNpcHomeState home)
    {
        home = default;
        var bytes = new ReadOnlySequence<byte>(encoded);
        if (TerrariaFrameDecoder.TryRead(ref bytes, out var frame) != TerrariaFrameReadResult.Frame || !bytes.IsEmpty ||
            TerrariaNpcHomeCodec.TryDecode(in frame, out home) != TerrariaNpcHomeDecodeResult.Decoded ||
            !home.TryGetStatus(out _)) return false;
        return true;
    }

    internal bool TryCaptureSourceTownHomeHistory(in NpcSnapshot npc, out int x, out int y, out bool homeless)
    {
        x = y = default; homeless = default;
        if (!HasQualifiedSourceHomeHistory(npc.Type) || npc.Handle.Slot >= gemBunnyHomes.Length) return false;
        var stamp = gemBunnyHomes[npc.Handle.Slot];
        if (stamp.Owner != npc.Handle || stamp.Retired || stamp.Revision.Value > npc.Revision.Value) return false;
        x = stamp.SourceOldHomeX; y = stamp.SourceOldHomeY; homeless = stamp.SourceOldHomeless;
        return true;
    }

    internal void PublishSourceTownHome(in SourceTownHomePhase1458 plan)
    {
        var stamp = gemBunnyHomes[plan.Owner.Slot];
        if (plan.LateFrame is not null && stamp.Owner == plan.Owner && !stamp.Retired &&
            stamp.SourceOldHomeX == plan.NextX && stamp.SourceOldHomeY == plan.NextY &&
            stamp.SourceOldHomeless == plan.NextHomeless) Broadcast(plan.LateFrame);
    }
}
