using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class ServerPlayerAuthority
{
    private RuntimeNpcRawPlayerSlots1458? npcRawSlots;
    internal ulong MembershipSerial { get; private set; } = 1;

    internal void BindNpcRawPlayerSlots(RuntimeNpcRawPlayerSlots1458 owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (npcRawSlots is not null)
            throw new InvalidOperationException("NPC raw server-player slots are already bound.");
        int count = states.CopySnapshots(snapshots);
        for (int i = 0; i < count; i++)
            if (snapshots[i].Player.Slot.Value != byte.MaxValue && !owner.TryAttach(snapshots[i].Player))
                throw new InvalidOperationException("NPC raw server-player slot attachment failed.");
        npcRawSlots = owner;
    }

    private void AttachNpcRawSlot(PlayerHandle player)
    {
        if (MembershipSerial == ulong.MaxValue ||
            (player.Slot.Value != byte.MaxValue && npcRawSlots is not null && !npcRawSlots.TryAttach(player)))
            throw new InvalidOperationException("NPC raw server-player slot attachment changed during commit.");
        MembershipSerial++;
    }

    private void ResetNpcRawSlot(PlayerHandle player)
    {
        if (MembershipSerial == ulong.MaxValue ||
            (player.Slot.Value != byte.MaxValue && npcRawSlots is not null && !npcRawSlots.TryReset(player)))
            throw new InvalidOperationException("NPC raw server-player slot reset changed during final detach.");
        MembershipSerial++;
    }
}
