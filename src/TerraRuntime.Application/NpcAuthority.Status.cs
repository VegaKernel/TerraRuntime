using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class NpcAuthority
{
    private readonly RuntimeNpcBuffStatus1458 npcBuffStatus;
    private readonly Action<NpcHandle> npcBuffListChanged;
    internal RuntimeNpcBuffStatus1458 NpcBuffStatus => npcBuffStatus;
    public long AppliedNpcBuffs { get; private set; }
    public long RejectedNpcBuffs { get; private set; }

    private void PublishNpcBuffList(NpcHandle handle) => npcReplication?.PublishNpcBuffs(handle);

    private void ApplyClientBuff(ClientNpcBuffRuntimeCommand command)
    {
        // Packet53 has no NPC generation on wire. Resolve its current slot on the authoritative loop,
        // then retain that exact handle; neither a stale connection nor a later slot reuse owns the buff.
        if (!players.IsCurrent(command.Connection) || !TerrariaNpcBuffCodec.IsValid(command.State) ||
            !npcs.TryGetActive((byte)command.State.NpcSlot, out NpcSnapshot npc) ||
            !npcBuffStatus.TryApply(npc.Handle, new BuffTypeId(command.State.BuffType), command.State.Duration))
        {
            RejectedNpcBuffs++;
            return;
        }
        AppliedNpcBuffs++;
        npcReplication?.PublishNpcBuffs(npc.Handle);
    }
}
