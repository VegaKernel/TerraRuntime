using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcReplicationRegistry
{
    public void NpcBodyHealed(in NpcSnapshot npc, int amount)
    {
        if (amount <= 0 || !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(npc.Simulation, out var hitbox)) return;
        // NPC.HealEffect() uses the integer physical Hitbox, unlike AI82's explicit 50x50 rectangle.
        float x = (int)npc.PositionX + hitbox.Width / 2;
        float y = (int)npc.PositionY + hitbox.Height / 2;
        if (RuntimeNpcPacketProjection.TryCreate(in npc, RuntimeNpcSyncKind.Spawn, out var baseline) &&
            TerrariaNpcUpdateEncoder.TryEncode(in baseline, out var encoded))
            Volatile.Write(ref baselineFrames[npc.Handle.Slot], encoded);
        Broadcast(TerrariaCombatTextCodec.EncodeNumber(x, y, amount, new TerrariaRgbColor(100, 255, 100)));
    }
}
