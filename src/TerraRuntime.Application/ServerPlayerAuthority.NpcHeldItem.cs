using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class ServerPlayerAuthority
{
    internal bool TryGetHeldItem(PlayerHandle player, int slot, out int itemType)
    {
        itemType = 0;
        if ((uint)slot >= VanillaPlayerItemSlotCatalog.InventoryCount ||
            !states.TryGetItem(player, checked((short)slot), out var item))
            return false;
        itemType = item.IsEmpty ? 0 : item.ItemType.Value;
        return true;
    }
}
