namespace TerraRuntime.Gameplay.Items;

public readonly record struct WorldItemPickupSlot1458(int Type, int Stack, int Prefix, bool Favorite);
public readonly record struct WorldItemOwnerPlayer1458(byte Slot, float X, float Y, int Width, int Height,
    bool Dead, bool CanPull, bool ManaMagnet, bool LifeMagnet, int GrabRange);

/// <summary>Player.ItemSpace/CanPullItem and WorldItem.FindOwner, without inventory mutation or RNG.</summary>
public static class VanillaWorldItemOwner1458
{
    public const int SearchRange = 1920; // NPC.sWidth.
    public const int DefaultGrabRange = 42;
    public const int PhysicalWidth = 16;
    public const int PhysicalHeight = 16;
    public const int ManaMagnetRange = 300;
    public const int LifeMagnetRange = 250;
    public const int CoinMagnetRange = 350;
    public const int TreasureMagnetRange = 150;

    public static bool TryCanPull(int type, int prefix, ReadOnlySpan<WorldItemPickupSlot1458> inventory,
        ReadOnlySpan<WorldItemPickupSlot1458> voidInventory, bool voidEnabled, bool preventPickups, out bool canPull)
    {
        canPull = false;
        if (inventory.Length < VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount ||
            !VanillaWorldItemPickupCatalog1458.TryGet(type, out var facts)) return false;
        bool space = facts.Pickup;
        if (!space)
        {
            if (facts.UniqueStack)
                foreach (var slot in inventory[..VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount])
                    if (slot.Type == type && slot.Stack > 0) return true;
            int ordinaryEnd = type is >= 71 and <= 74
                ? VanillaPlayerItemSlotCatalog.CoinSlotEndExclusive : VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive;
            for (int i = 0; i < ordinaryEnd; i++)
                if (Accept(inventory[i], type, prefix)) { space = true; break; }
            if (!space && facts.Ammo && !facts.NotAmmo)
                for (int i = VanillaPlayerItemSlotCatalog.AmmoSlotStart; i < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive; i++)
                    if ((inventory[i].Type != 0 || facts.EmptyAmmo) && Accept(inventory[i], type, prefix)) { space = true; break; }
            if (!space)
                for (int i = VanillaPlayerItemSlotCatalog.AmmoSlotStart; i < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive; i++)
                    if (inventory[i].Type > 0 && Accept(inventory[i], type, prefix, honorFavorite: false)) { space = true; break; }
            if (!space && voidEnabled && !facts.Quest && type != 3822)
            {
                if (voidInventory.Length != 40) return false; // Player.bank4.item.
                foreach (var slot in voidInventory)
                    if (Accept(slot, type, prefix)) { space = true; break; }
            }
        }
        canPull = space && (!preventPickups || facts.IgnoresEncumbering);
        return true;
    }

    private static bool Accept(in WorldItemPickupSlot1458 slot, int type, int prefix, bool honorFavorite = true)
    {
        if (slot.Type == 0) return true;
        if (!VanillaWorldItemAllocationCatalog1458.TryGet(slot.Type, out var allocation) ||
            !VanillaWorldItemPickupCatalog1458.TryGet(slot.Type, out var pickup)) return false;
        if (honorFavorite && slot.Favorite && pickup.OnlyNeedOne) return false;
        return slot.Stack < allocation.MaximumStack && slot.Type == type && slot.Prefix == prefix;
    }

    public static byte Select(int type, float x, float y, int grabDelay, byte grabPlayer,
        ReadOnlySpan<WorldItemOwnerPlayer1458> players)
    {
        float best = SearchRange;
        byte selected = byte.MaxValue;
        foreach (var player in players)
        {
            if (player.Dead || !player.CanPull || (grabDelay > 0 && (grabPlayer == player.Slot || grabPlayer == byte.MaxValue))) continue;
            float distance = Math.Abs(player.X + player.Width / 2 - x - PhysicalWidth / 2) +
                Math.Abs(player.Y + player.Height / 2 - y - PhysicalHeight);
            if ((player.ManaMagnet && type is 184 or 1735 or 1868) || type == 4143) distance -= ManaMagnetRange;
            if (player.LifeMagnet && type is 58 or 1734 or 1867) distance -= LifeMagnetRange;
            if (distance < best || (distance == best && selected != byte.MaxValue && player.Slot < selected))
            { best = distance; selected = player.Slot; }
        }
        return selected;
    }

    public static bool InGrabRange(in WorldItemOwnerPlayer1458 player, float x, float y) =>
        (int)player.X - player.GrabRange < (int)x + PhysicalWidth &&
        (int)player.X + player.Width + player.GrabRange > (int)x &&
        (int)player.Y - player.GrabRange < (int)y + PhysicalHeight &&
        (int)player.Y + player.Height + player.GrabRange > (int)y;
}
