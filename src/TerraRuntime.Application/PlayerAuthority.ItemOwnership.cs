using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal RuntimeItemOwnerPlayerCapture1458[] CaptureItemOwnerPlayers()
    {
        var captures = new List<RuntimeItemOwnerPlayerCapture1458>();
        for (int slot = 0; slot < byte.MaxValue; slot++)
        {
            if (!membership.TryGet(new PlayerSlotId((byte)slot), out var member) ||
                !membership.TryCapture(member.Connection.Player, out var state)) continue;
            var inventoryImage = new RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
            bool hasInventory = inventory.TryCopyInventory(member.Connection, inventoryImage);
            bool hasProfile = transferProfiles.TryCapture(member.Connection, out var appearance, out var equipment, out var buffs);
            bool lifeMagnet = transferProfiles.GetBuffDuration(member.Connection, new BuffTypeId(105)) > 0;
            captures.Add(new(member.Connection, state, hasInventory, inventoryImage, hasProfile, appearance, equipment, buffs, lifeMagnet));
        }
        return captures.ToArray();
    }

    internal bool IsCurrentItemOwnerPlayers(RuntimeItemOwnerPlayerCapture1458[] captures)
    {
        var actual = CaptureItemOwnerPlayers();
        if (actual.Length != captures.Length) return false;
        for (int i = 0; i < actual.Length; i++)
        {
            var a = actual[i]; var b = captures[i];
            if (a.Connection != b.Connection || a.State != b.State || a.HasInventory != b.HasInventory ||
                a.HasProfile != b.HasProfile || a.Appearance != b.Appearance || a.LifeMagnet != b.LifeMagnet ||
                !a.Inventory.AsSpan().SequenceEqual(b.Inventory) || !a.Equipment.AsSpan().SequenceEqual(b.Equipment) ||
                (a.Buffs is null) != (b.Buffs is null) ||
                (a.Buffs is not null && !a.Buffs.AsSpan().SequenceEqual(b.Buffs))) return false;
        }
        return true;
    }
}

internal sealed record RuntimeItemOwnerPlayerCapture1458(ConnectionHandle Connection, PlayerStateSnapshot State,
    bool HasInventory, RuntimePlayerInventoryItem[] Inventory, bool HasProfile, PlayerAppearanceCommitRequest? Appearance,
    PlayerEquipmentCommitRequest[] Equipment, BuffTypeId[]? Buffs, bool LifeMagnet);
