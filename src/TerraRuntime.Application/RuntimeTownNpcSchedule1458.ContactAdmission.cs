using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
namespace TerraRuntime.Application;
internal sealed partial class RuntimeTownNpcSchedule1458
{
    // NPC.UpdateNPC.GetHurtByOtherNPCs owns contact damage before collision. This package does not
    // own that damage transaction, so an overlapping hostile cannot enter its quiet outer-physics path.
    internal static bool AdmitsOuterContact(in NpcSnapshot resident, ReadOnlySpan<NpcSnapshot> peers)
    {
        if (!TryContactRectangle(in resident, out var r)) return false;
        foreach (NpcSnapshot peer in peers)
        {
            if (!peer.IsActive || peer.Handle == resident.Handle || peer.Handle.Slot >= 200 ||
                peer.Type is 624 or 690 || VanillaTownNpcDangerCatalog1458.IsTurningCritter(peer.Type) ||
                peer.Simulation.Friendly == true) continue;
            if (!VanillaNpcDefinitionCatalog.TryGet(peer.TypeIdentity, peer.NetIdentity, out var definition) ||
                peer.Simulation.Friendly is null) return false;
            if ((peer.Simulation.DamageOverride ?? definition.Damage) <= 0) continue;
            if (!TryContactRectangle(in peer, out var p)) return false;
            if (r.X < p.X + p.Width && r.X + r.Width > p.X && r.Y < p.Y + p.Height && r.Y + r.Height > p.Y)
                return false;
        }
        return true;
    }
    private static bool TryContactRectangle(in NpcSnapshot npc, out (int X, int Y, int Width, int Height) rectangle)
    {
        rectangle = default;
        if (!float.IsFinite(npc.PositionX) || !float.IsFinite(npc.PositionY) ||
            !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(npc.Simulation, out var hitbox)) return false;
        rectangle = ((int)npc.PositionX, (int)npc.PositionY, hitbox.Width, hitbox.Height);
        return true;
    }
}
