using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class NpcAuthority
{
    internal bool TryWiringTeleport(in NpcSnapshot before, int padX, int padBottom, int dx, int dy, Func<bool> current)
    {
        var npc = before;
        if (npc.Simulation.TeleportTime is not (>= 0f and <= 1f) ||
            naturalSpawnRandom is not SystemVanillaNpcRandom adapter || VanillaWiringTeleport1458.IsImmune(npc.TypeIdentity) ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(npc.Simulation, out var body) || npc.Simulation.LifeMax is not > 5 ||
            definition.Role == NpcArchetypeRole.Boss || npc.Simulation.NoTileCollide ||
            !VanillaWiringTeleport1458.Touches(padX, padBottom, npc.PositionX, npc.PositionY, body.Width, body.Height) ||
            !npcs.TryCreateAiSpawnPreview(in npc, adapter.SourceRandom, out var preview) || preview is null) return false;
        var resident = naturalSpawnTownNpcs?.TryGet(npc.Handle.Slot, out var saved) == true ? saved : null;
        if (resident is not null && (resident.NetId != npc.NetId || naturalSpawnTownNpcs!.CaptureResidentName(npc.Handle) is null)) return false;
        bool InputsCurrent() => current() && (resident is null ? naturalSpawnTownNpcs?.TryGet(npc.Handle.Slot, out _) != true :
            naturalSpawnTownNpcs!.TryGet(npc.Handle.Slot, out var after) && after == resident && naturalSpawnTownNpcs.CaptureResidentName(npc.Handle) is not null);
        float x = npc.PositionX + dx, y = npc.PositionY + dy;
        var update = new NpcStateUpdate(npc.Type, npc.NetId, x, y, npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai,
            npc.Simulation with { TeleportTime = 1f, TeleportStyle = 0 });
        if (!TerrariaEntityTeleportCodec1458.TryEncode(npc.Handle.Slot, true, x, y, 0, false, out var frame)) return false;
        RuntimeNpcReplicationRegistry.RetainedHistoricalPublication? publication = null;
        if (npcReplication is not null &&
            (!npcReplication.TryPrepareHistoricalCheckpoint(in npc, in update, npcs.HasPendingBirth(npc.Handle), [], out publication) ||
                !publication!.UseTeleportFrame(frame) || !publication.TrySealRecipients())) return false;
        VanillaWiringTeleport1458.ConsumeNpcEffects(new SystemVanillaNpcRandom(preview.Random), body.Width, body.Height, npc.Simulation.TeleportTime ?? 0f);
        if (!preview.TryAdoptDirect(in update, InputsCurrent, out var committed)) return false;
        if (resident is not null && !naturalSpawnTownNpcs!.TryUpdatePosition(committed.Handle.Slot, in committed))
            throw new InvalidOperationException("Prepared teleporter lost resident persistence.");
        if (publication is not null)
        {
            if (!publication.TryAdoptBaseline(npcs, in committed)) throw new InvalidOperationException("Prepared teleporter lost NPC baseline.");
            publication.Publish(() => npcs.TryGet(committed.Handle, out var now) && now == committed);
        }
        AppliedUpdates++;
        return true;
    }
}
