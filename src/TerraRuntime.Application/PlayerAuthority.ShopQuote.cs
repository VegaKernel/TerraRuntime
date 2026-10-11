using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    // CustomCurrencyManager.Initialize registers Defender Medals first in official1.4.5.8.
    private const int DefenderMedalsCurrency = 0;
    private void ObserveShopPricingPhase(RuntimePlayerMember member)
    {
        if (member.ItemPhase is not { } phase ||
            !membership.TryGetTownShopSession(member.Connection.Player, out var session) || session is null)
            return;
        if (session.Quote is null && session.VendorStore is { } npcs)
            TryBindTownShopQuote(member.Connection, session, npcs);
        session.Quote?.TryObservePriceContexts(phase.CapturedShopPriceContextMask);
    }
    internal bool TryBindTownShopQuote(ConnectionHandle connection, RuntimeTownShopSession1458 session,
        RuntimeNpcStore npcs)
    {
        if (!membership.TryGet(connection, out var member) || member.ItemPhase is not { } phase ||
            phase.DerivedCombat is not { } equipment || PickupWindowsArithmetic is not { } windows ||
            !membership.TryGetTownShopSession(connection.Player, out var current) || !ReferenceEquals(current, session) ||
            session.Quote is not null || session.Offers.Length > 40 || phase.ShopDiscountStates > 15 ||
            phase.ClientUiPriceArithmeticMask > 3 ||
            !npcs.TryGet(session.VendorHandle, out var vendor)) return false;
        var stock = new ShopItem1458[40];
        Span<bool> occupied = stackalloc bool[40];
        foreach (var offer in session.Offers)
        {
            if ((uint)offer.Slot >= stock.Length || occupied[offer.Slot] ||
                offer.Currency is not (VanillaTownShopCurrency1458.Coins or VanillaTownShopCurrency1458.DefenderMedals) ||
                !VanillaItemRetainedIdentity1458.TryResolve(offer.Item, out var retained) || retained == default ||
                !VanillaItemStoreValue1458.TryGet(offer.Item, VanillaPrefixIds.None, out int value, windows, PickupWorld)) return false;
            occupied[offer.Slot] = true;
            // Genuine Chest.SetupShop source captures retain aliases and use unprefixed stack1 shop offers.
            stock[offer.Slot] = new(new(retained.Value, 1, VanillaPrefixIds.NoneValue, false), value,
                false, true, offer.CustomPrice,
                offer.Currency == VanillaTownShopCurrency1458.Coins ? null : DefenderMedalsCurrency);
        }
        byte contexts = phase.CapturedShopPriceContextMask;
        if (equipment.DiscountEquipped)
        {
            // UpdateEquips replaces the latch only while npcShop is closed. An open
            // platform alternative cannot gain a new discount from current equipment.
            byte states = phase.CapturedShopPriceContextStates;
            contexts |= (byte)(((states & 3) != 0 ? 2 : 0) | ((states & 48) != 0 ? 8 : 0));
        }
        byte mask = RuntimeTownShopQuoteSnapshot1458.ContextDiscounts(contexts);
        if (!RuntimeTownShopQuote1458.TryCreate(session, membership, npcs, connection, vendor.Handle,
            stock, mask, windows, out var quote, PickupWorld, phase.CapturedClientUiPriceArithmeticMask, contexts) || !membership.TryGet(connection, out var stillMember) ||
            !ReferenceEquals(member, stillMember) || member.ItemPhase != phase ||
            !membership.TryGetTownShopSession(connection.Player, out current) || !ReferenceEquals(current, session))
            return false;
        session.Quote = quote;
        return true;
    }
}
