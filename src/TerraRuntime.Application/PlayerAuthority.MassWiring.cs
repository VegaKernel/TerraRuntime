using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal bool TryPrepareMassWiringPlayer(ConnectionHandle connection, out MassWiringPlayerPreparation1458? preparation)
    {
        preparation = null;
        if (!membership.TryGet(connection, out var member) || !TryCaptureToolSourceWorld(out var world) ||
            !inventory.TryCaptureShopJournal(connection, out var image) || image is null || image.HasPendingCursorSource)
            return false;
        preparation = new(this, connection, member.CaptureSnapshot(), world, image, inventory.Serial);
        return preparation.IsCurrent;
    }

    internal sealed class MassWiringPlayerPreparation1458(PlayerAuthority owner, ConnectionHandle connection,
        PlayerStateSnapshot before, RuntimePlayerUpdateWorld1458 world,
        RuntimePlayerInventoryStore.ShopJournalCapture image, ulong serial)
    {
        private readonly short[] funding = image.Quantity;
        private RuntimePlayerInventoryStore.RemotePhasePreparation? payment;
        private bool sealedPlan, adopted;
        internal PlayerStateSnapshot Before => before;
        internal RuntimePlayerUpdateWorld1458 World => world;
        internal bool IsCurrent => !adopted && owner.inventory.Serial == serial && image.IsCurrent &&
            owner.membership.TryGet(connection, out var member) && member.CaptureSnapshot() == before &&
            owner.playerUpdateWorld == world && payment is not { IsCurrent: false };

        internal int Stock(int type)
        {
            int count = 0;
            for (int slot = 0; slot < 58; slot++)
                if (image.Before[slot] is { IsCanonical: true, Stack: > 0 } item &&
                    item.ItemType.Value == type && item.Prefix.Value == 0)
                    count += Math.Max(0, Math.Min((int)item.Stack, funding[slot]));
            return count;
        }

        internal bool TrySeal(int wires, int actuators)
        {
            if (sealedPlan || !IsCurrent || wires < 0 || actuators < 0) return false;
            var changes = new List<RemotePhaseInventoryMutation>();
            for (short slot = 0; slot < 58; slot++)
            {
                var item = image.Before[slot];
                if (!item.IsCanonical || item.IsEmpty || item.Prefix.Value != 0) continue;
                bool wire = item.ItemType.Value == VanillaWiringToolCatalog1458.WireItem;
                if (!wire && item.ItemType.Value != VanillaWiringToolCatalog1458.ActuatorItem) continue;
                int needed = wire ? wires : actuators;
                int quantity = Math.Min(needed, Math.Max(0, Math.Min((int)item.Stack, funding[slot])));
                if (quantity == 0) continue;
                var after = item.Stack == quantity ? default : item with { Stack = (short)(item.Stack - quantity) };
                changes.Add(new(connection, slot, item, after)
                { ConsumeFundedQuantity = true, WiringResourceDebit = true, WiringResourceQuantity = (short)quantity });
                if (wire) wires -= quantity; else actuators -= quantity;
            }
            if (wires != 0 || actuators != 0 || !IsCurrent ||
                !owner.inventory.TryPrepareRemotePhase(changes.ToArray(), serial, out payment) || payment is null) return false;
            sealedPlan = true;
            return IsCurrent;
        }

        internal bool TryAdoptUnpublished()
        {
            if (!sealedPlan || !IsCurrent || payment is null || !payment.TryAdoptUnpublished()) return false;
            adopted = true;
            return true;
        }
    }
}
