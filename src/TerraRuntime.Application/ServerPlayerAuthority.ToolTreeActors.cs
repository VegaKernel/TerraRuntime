using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class ServerPlayerAuthority
{
    internal bool TryCaptureToolTreeActor(byte slot, out PlayerStateSnapshot snapshot,
        out RuntimePlayerInventoryItem selected)
    {
        selected = default;
        if (!states.TryGet(new PlayerSlotId(slot), out snapshot) ||
            !states.TryGet(snapshot.Player, out var exact) || snapshot != exact ||
            !states.TryGetItem(snapshot.Player, snapshot.SelectedItem, out var item)) return false;
        selected = item.IsEmpty ? default : new(item.ItemType, item.Stack, item.Prefix, item.ItemFlags);
        return selected.IsCanonical;
    }
}
