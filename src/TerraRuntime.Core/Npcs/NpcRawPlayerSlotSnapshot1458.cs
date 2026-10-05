using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

internal readonly record struct NpcRawPlayerSlotSnapshot1458(
    VanillaNpcRawPlayer1458 Facts, ulong OwnerSerial, ulong LifetimeRevision,
    PlayerStateSnapshot? LivePlayer, ulong MembershipSerial = 0, ulong ServerPlayerSerial = 0, bool? ZoneGraveyard = null);

/// <summary>Concrete boundary between retained AI and the world-owned player-slot lifecycle.</summary>
internal interface INpcRawPlayerSlotLookup1458
{
    bool TryCapture(byte slot, out NpcRawPlayerSlotSnapshot1458 snapshot);
    bool IsCurrent(in NpcRawPlayerSlotSnapshot1458 snapshot);
}
