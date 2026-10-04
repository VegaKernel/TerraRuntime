using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private RuntimeNpcStore? zoneNpcOwner;

    internal void SetZoneNpcOwner(RuntimeNpcStore npcs) => zoneNpcOwner = npcs ?? throw new ArgumentNullException(nameof(npcs));

    private void ApplyPlayerZones(PlayerZonesRuntimeCommand command)
    {
        if (!membership.TryGet(command.Connection, out RuntimePlayerMember? member)) return;
        // MessageBuffer36 invokes SpawnFaelings only on the false->true Shimmer edge. Its first
        // AnyNPCs(677) check is a genuine no-op. The remaining producer is not owned here.
        // An imported unknown previous zone cannot be assumed false or true on this edge.
        if (command.Zones.Shimmer && (member.Zones is not { } previous || !previous.Shimmer) &&
            !HasExistingShimmerfly()) return;
        if (!member.TryAdvanceRevision()) return;
        member.Zones = command.Zones;
        var zones = command.Zones;
        events?.PlayerZonesUpdated(command.Connection, in zones);
    }

    private bool HasExistingShimmerfly()
    {
        if (zoneNpcOwner is null) return false;
        for (int slot = 0; slot < VanillaNpcSpawnRules.PhysicalSlotCount; slot++)
            if (zoneNpcOwner.TryGetActive((byte)slot, out var npc) && npc.TypeIdentity == VanillaNpcIds.Shimmerfly) return true;
        return false;
    }
}
