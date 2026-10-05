using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

/// <summary>
/// NPC-only source player-array readback. Final detach means RemoteClient.Reset,
/// not a fabricated tick of approved-but-not-yet-reset inactive geometry.
/// </summary>
internal sealed class RuntimeNpcRawPlayerSlots1458 : INpcRawPlayerSlotLookup1458
{
    private readonly IRuntimePlayerSlotSnapshotLookup players;
    private readonly Func<ulong>? membershipSerial;
    private readonly Func<ulong>? serverPlayerSerial;
    private readonly PlayerHandle[] live = new PlayerHandle[256];
    private readonly ulong[] lifetime = new ulong[256];
    private ulong serial = 1;

    internal RuntimeNpcRawPlayerSlots1458(IRuntimePlayerSlotSnapshotLookup players,
        Func<ulong>? membershipSerial = null, Func<ulong>? serverPlayerSerial = null)
    {
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.membershipSerial = membershipSerial;
        this.serverPlayerSerial = serverPlayerSerial;
        Array.Fill(lifetime, 1UL);
    }

    internal bool TryAttach(PlayerHandle player)
    {
        if (!player.IsAssigned || player.Slot.Value == byte.MaxValue ||
            live[player.Slot.Value].IsAssigned || !CanChange(player.Slot.Value))
            return false;
        live[player.Slot.Value] = player;
        Changed(player.Slot.Value);
        return true;
    }

    internal bool TryReset(PlayerHandle player)
    {
        if (!player.IsAssigned || player.Slot.Value == byte.MaxValue ||
            live[player.Slot.Value] != player || !CanChange(player.Slot.Value))
            return false;
        live[player.Slot.Value] = default;
        Changed(player.Slot.Value);
        return true;
    }

    public bool TryCapture(byte slot, out NpcRawPlayerSlotSnapshot1458 snapshot)
    {
        snapshot = default;
        ulong before = serial;
        ulong clients = membershipSerial?.Invoke() ?? 0;
        ulong controlled = serverPlayerSerial?.Invoke() ?? 0;
        var handle = live[slot];
        var facts = VanillaNpcRawPlayer1458.Constructor(slot);
        PlayerStateSnapshot? retained = null;
        if (slot != byte.MaxValue && players.TryGetPlayer(new PlayerSlotId(slot), out var player))
        {
            if (!handle.IsAssigned || player.Player != handle || !player.Revision.IsAssigned)
                return false;
            var size = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) : (20f, 42f);
            facts = new(slot, true, player.IsDead,
                (player.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0,
                player.PositionX, player.PositionY,
                (int)size.Item1, (int)size.Item2, 0, false, player.ItemAnimation ?? 0);
            retained = player;
        }
        else if (handle.IsAssigned)
        {
            // A tracked live session may not be reinterpreted as an absent constructor.
            return false;
        }
        if (!facts.IsValid || serial != before ||
            (membershipSerial?.Invoke() ?? 0) != clients ||
            (serverPlayerSerial?.Invoke() ?? 0) != controlled)
            return false;

        snapshot = new(facts, before, lifetime[slot], retained, clients, controlled);
        return true;
    }

    public bool IsCurrent(in NpcRawPlayerSlotSnapshot1458 snapshot) =>
        TryCapture(snapshot.Facts.Slot, out var current) && current == snapshot;

    private bool CanChange(byte slot) => serial < ulong.MaxValue && lifetime[slot] < ulong.MaxValue;

    private void Changed(byte slot)
    {
        lifetime[slot]++;
        serial++;
    }
}
