using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private RuntimeNpcRawPlayerSlots1458? npcRawSlots;
    internal ulong MembershipSerial => membership.Serial;
    internal ulong NpcLootInventorySerial => inventory.Serial;

    internal void BindNpcRawPlayerSlots(RuntimeNpcRawPlayerSlots1458 owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (npcRawSlots is not null)
            throw new InvalidOperationException("NPC raw player slots are already bound.");
        foreach (var member in membership.Members)
            if (member.Connection.Player.Slot.Value != byte.MaxValue && !owner.TryAttach(member.Connection.Player))
                throw new InvalidOperationException("NPC raw player slot attachment failed.");
        npcRawSlots = owner;
    }

    private void AttachNpcRawSlot(PlayerHandle player)
    {
        if (player.Slot.Value != byte.MaxValue && npcRawSlots is not null && !npcRawSlots.TryAttach(player))
            throw new InvalidOperationException("NPC raw player slot attachment changed during commit.");
    }

    private void ResetNpcRawSlot(PlayerHandle player)
    {
        if (player.Slot.Value != byte.MaxValue && npcRawSlots is not null && !npcRawSlots.TryReset(player))
            throw new InvalidOperationException("NPC raw player slot reset changed during final detach.");
    }
}
