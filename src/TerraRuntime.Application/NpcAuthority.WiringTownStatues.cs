using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class NpcAuthority
{
    internal bool TryTeleportWiringResident(int style, int x, int y, Func<bool> currentSource)
    {
        if (style is not (40 or 41) || naturalSpawnRandom is not SystemVanillaNpcRandom adapter ||
            !npcs.IsSpawnRandomSource(adapter.SourceRandom)) return false;
        // Wiring.HitWireSingle(105), 1.4.5.8: retain the first fifty eligible
        // physical slots. Housing, gender metadata and distance do not filter this list.
        Span<NpcSnapshot> candidates = stackalloc NpcSnapshot[50];
        int count = 0;
        for (int slot = 0; slot < Math.Min(200, npcs.Capacity) && count < candidates.Length; slot++)
            if (npcs.TryGetActive((byte)slot, out var npc) && VanillaTownStatue1458.IsEligible(style, npc.TypeIdentity))
                candidates[count++] = npc;
        if (count == 0) return currentSource();
        var choice = adapter.SourceRandom.Clone();
        var selected = candidates[choice.Next(count)];
        WorldTownNpc? resident = null;
        if (naturalSpawnTownNpcs?.TryGet(selected.Handle.Slot, out var saved) == true)
        {
            if (saved.NetId != selected.NetId || naturalSpawnTownNpcs.CaptureResidentName(selected.Handle) is null) return false;
            resident = saved;
        }
        bool Current()
        {
            if (!currentSource()) return false;
            bool retained = naturalSpawnTownNpcs?.TryGet(selected.Handle.Slot, out _) == true;
            return resident is null ? !retained : retained &&
                naturalSpawnTownNpcs!.TryGet(selected.Handle.Slot, out var current) && current == resident &&
                naturalSpawnTownNpcs.CaptureResidentName(selected.Handle) is not null;
        }
        if (!VanillaNpcDefinitionCatalog.TryGet(selected.TypeIdentity, selected.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(selected.Simulation, out var body) ||
            !npcs.TryCreateAiSpawnPreview(in selected, adapter.SourceRandom, out var preview) || preview is null) return false;
        _ = preview.Random.Next(count);
        float targetX = x - body.Width / 2, targetY = y - body.Height - 1;
        var update = new NpcStateUpdate(selected.Type, selected.NetId, targetX, targetY,
            selected.VelocityX, selected.VelocityY, selected.Target, selected.Ai,
            selected.Simulation with { TeleportTime = 1f, TeleportStyle = 14 });
        if (!TerrariaEntityTeleportCodec1458.TryEncode(selected.Handle.Slot, true, targetX, targetY, 14, false, out var frame)) return false;
        RuntimeNpcReplicationRegistry.RetainedHistoricalPublication? publication = null;
        if (npcReplication is not null &&
            (!npcReplication.TryPrepareHistoricalCheckpoint(in selected, in update, npcs.HasPendingBirth(selected.Handle), [], out publication) ||
                !publication!.UseTeleportFrame(frame) || !publication.TrySealRecipients())) return false;
        // Main.TeleportEffect has no style14 branch: no particle RNG draws.
        // Direct adoption preserves pending birth and all unrelated NPC state.
        if (!preview.TryAdoptDirect(in update, Current, out var committed)) return false;
        // A checkpoint command can follow this switch in the same command batch,
        // before the next town AI tick. Retain the existing save image before65.
        if (resident is not null && !naturalSpawnTownNpcs!.TryUpdatePosition(committed.Handle.Slot, in committed))
            throw new InvalidOperationException("Preflighted town statue lost resident persistence adoption.");
        if (publication is not null)
        {
            if (!publication.TryAdoptBaseline(npcs, in committed))
                throw new InvalidOperationException("Preflighted town statue lost NPC baseline adoption.");
            publication.Publish(() => npcs.TryGet(committed.Handle, out var current) && current == committed);
        }
        AppliedUpdates++;
        return true;
    }
}
