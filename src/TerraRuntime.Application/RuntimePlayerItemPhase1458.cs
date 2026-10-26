using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

// Retains constructor-owned clocks alongside packet-updated mana, animation and respawn fields.
// Inventory reports cannot reconstruct missing clocks; an import without this phase stays unknown.
internal readonly record struct RuntimePlayerItemPhase1458(
    int BaseManaMaximum,
    PlayerManaRegenerationState1458 Mana,
    PlayerSelectedConsumableState1458 Selected,
    float ManaHeat)
{
    // Main.npcShop is a separate UI lifetime from SetTalkNPC. The accepted commerce
    // journal owns this explicit context; packet40 alone cannot set it.
    internal bool ShopOpen { get; init; }
    internal bool DiscountAvailable { get; init; }
    // Packet5/138 exposes price outcomes, not the client's UI arithmetic platform.
    // Zero starts unknown; conserved price witnesses may narrow Linux/Windows alternatives.
    internal byte ClientUiPriceArithmeticMask { get; init; }
    internal byte CapturedClientUiPriceArithmeticMask => ShopPriceContextStates != 0
        ? (byte)(((ShopPriceContextStates & 15) != 0 ? 1 : 0) |
            ((ShopPriceContextStates & 240) != 0 ? 2 : 0))
        : ClientUiPriceArithmeticMask == 0 ? (byte)3 : ClientUiPriceArithmeticMask;

    internal RuntimePlayerItemPhase1458 ApplyAcceptedClientUiPriceArithmetic(byte resolvedMask)
    {
        if (resolvedMask is < 1 or > 3 || ClientUiPriceArithmeticMask > 3 ||
            (resolvedMask & CapturedClientUiPriceArithmeticMask) != resolvedMask)
            throw new ArgumentOutOfRangeException(nameof(resolvedMask));
        if (ShopPriceContextStates == 0)
            return this with { ClientUiPriceArithmeticMask = resolvedMask };
        byte states = (byte)(ShopPriceContextStates &
            (((resolvedMask & 1) != 0 ? 15 : 0) | ((resolvedMask & 2) != 0 ? 240 : 0)));
        return WithShopPriceStates(states);
    }
    // Correlated closed/false, closed/true, open/false, open/true alternatives.
    // Zero retains the exact configured ShopOpen/DiscountAvailable component context.
    internal byte ShopDiscountStates { get; init; }
    // Each platform retains its own closed/open and unavailable/available alternatives.
    // Marginals alone would invent a price pairing absent from the accepted receipt.
    internal byte ShopPriceContextStates { get; init; }
    internal byte CapturedShopDiscountStates => ShopPriceContextStates != 0
        ? (byte)((ShopPriceContextStates & 15) | (ShopPriceContextStates >> 4))
        : ShopDiscountStates == 0
            ? (byte)(1 << ((ShopOpen ? 2 : 0) + (DiscountAvailable ? 1 : 0))) : ShopDiscountStates;
    internal byte CapturedShopPriceContextStates => ShopPriceContextStates != 0
        ? ShopPriceContextStates
        : (byte)(((CapturedClientUiPriceArithmeticMask & 1) != 0 ? CapturedShopDiscountStates : 0) |
            ((CapturedClientUiPriceArithmeticMask & 2) != 0 ? CapturedShopDiscountStates << 4 : 0));
    internal byte CapturedShopPriceContextMask => (byte)(
        ((CapturedShopPriceContextStates & 5) != 0 ? 1 : 0) |
        ((CapturedShopPriceContextStates & 10) != 0 ? 2 : 0) |
        ((CapturedShopPriceContextStates & 80) != 0 ? 4 : 0) |
        ((CapturedShopPriceContextStates & 160) != 0 ? 8 : 0));

    private RuntimePlayerItemPhase1458 WithShopPriceStates(byte states)
    {
        byte discountStates = (byte)((states & 15) | (states >> 4));
        return this with { ShopPriceContextStates = states, ShopDiscountStates = discountStates,
            ClientUiPriceArithmeticMask = (byte)(((states & 15) != 0 ? 1 : 0) | ((states & 240) != 0 ? 2 : 0)),
            ShopOpen = (discountStates & 12) != 0, DiscountAvailable = (discountStates & 10) != 0 };
    }

    internal RuntimePlayerItemPhase1458 ApplyAcceptedShopPriceContexts(byte contexts, bool retired = false)
    {
        byte arithmetic = RuntimeTownShopQuoteSnapshot1458.ContextArithmetics(contexts);
        if (contexts is < 1 or > 15 ||
            (arithmetic & CapturedClientUiPriceArithmeticMask) != arithmetic)
            throw new ArgumentOutOfRangeException(nameof(contexts));
        byte open = (byte)(((contexts & 3) << 2) | ((contexts & 12) << 4));
        return WithShopPriceStates(retired ? (byte)(open | (open >> 2)) : open);
    }

    internal RuntimePlayerItemPhase1458 CloseShopUiUnknownContexts(byte possibleContexts = 0)
    {
        if (possibleContexts > 15 ||
            (RuntimeTownShopQuoteSnapshot1458.ContextArithmetics(possibleContexts) &
                CapturedClientUiPriceArithmeticMask) != RuntimeTownShopQuoteSnapshot1458.ContextArithmetics(possibleContexts))
            throw new ArgumentOutOfRangeException(nameof(possibleContexts));
        byte states = CapturedShopPriceContextStates;
        states |= (byte)(((possibleContexts & 3) << 2) | ((possibleContexts & 12) << 4));
        byte nextStates = (byte)(states | ((states & 204) >> 2));
        // Closing without new alternatives preserves the exact retained owner,
        // including its unmaterialized default fields at transfer or NoNpc.
        return nextStates == CapturedShopPriceContextStates ? this : WithShopPriceStates(nextStates);
    }
    internal byte DiscountAvailableMask => (byte)(
        ((CapturedShopDiscountStates & 5) != 0 ? 1 : 0) |
        ((CapturedShopDiscountStates & 10) != 0 ? 2 : 0));

    internal RuntimePlayerItemPhase1458 CloseShopUiUnknown(byte validatedPossibleLatchMask = 0)
    {
        if ((validatedPossibleLatchMask & ~3) != 0)
            throw new ArgumentOutOfRangeException(nameof(validatedPossibleLatchMask));
        if (ShopPriceContextStates != 0)
            return CloseShopUiUnknownContexts((byte)(CapturedShopPriceContextMask &
                RuntimeTownShopQuoteSnapshot1458.ComposePriceContexts(
                    validatedPossibleLatchMask, CapturedClientUiPriceArithmeticMask)));
        byte states = CapturedShopDiscountStates;
        // A retained quote can witness an earlier latch even without a paid trade.
        states |= (byte)(((validatedPossibleLatchMask & 1) != 0 ? 4 : 0) |
            ((validatedPossibleLatchMask & 2) != 0 ? 8 : 0));
        // Conversation closes before a possible later UI draw closes Main.npcShop.
        return this with { ShopDiscountStates = (byte)(states | ((states & 12) >> 2)) };
    }

    internal RuntimePlayerItemPhase1458 ApplyAcceptedShopDiscount(byte resolvedMask, bool retired = false)
    {
        if (resolvedMask == 0 || (resolvedMask & ~3) != 0)
            throw new ArgumentOutOfRangeException(nameof(resolvedMask));
        if (ShopPriceContextStates != 0)
        {
            byte contexts = (byte)(CapturedShopPriceContextMask &
                RuntimeTownShopQuoteSnapshot1458.ComposePriceContexts(resolvedMask, CapturedClientUiPriceArithmeticMask));
            if (RuntimeTownShopQuoteSnapshot1458.ContextDiscounts(contexts) != resolvedMask)
                throw new ArgumentOutOfRangeException(nameof(resolvedMask));
            return ApplyAcceptedShopPriceContexts(contexts, retired);
        }
        byte allowed = (byte)(((resolvedMask & 1) != 0 ? 5 : 0) | ((resolvedMask & 2) != 0 ? 10 : 0));
        // The accepted quote carries the validated price history, including a hidden UI
        // opening before the current equipment phase or a close before source138.
        byte admitted = allowed;
        byte open = (byte)(((admitted & 3) << 2) | (admitted & 12));
        byte accepted = retired ? (byte)(open | (open >> 2)) : open;
        return this with { ShopOpen = !retired, DiscountAvailable = (accepted & 10) != 0,
            ShopDiscountStates = accepted };
    }

    internal RuntimePlayerItemPhase1458 AdvanceShopDiscount(bool equipped)
    {
        if (ShopPriceContextStates != 0)
        {
            byte correlated = (byte)(ShopPriceContextStates & 204);
            if ((ShopPriceContextStates & 3) != 0) correlated |= (byte)(equipped ? 2 : 1);
            if ((ShopPriceContextStates & 48) != 0) correlated |= (byte)(equipped ? 32 : 16);
            return WithShopPriceStates(correlated);
        }
        byte states = CapturedShopDiscountStates;
        byte next = (byte)(states & 12);
        if ((states & 3) != 0) next |= (byte)(equipped ? 2 : 1);
        return this with { DiscountAvailable = ShopDiscountStates == 0 && !ShopOpen
            ? equipped : DiscountAvailable, ShopDiscountStates = ShopDiscountStates == 0 ? (byte)0 : next };
    }

    internal RuntimePlayerManaCrystalIntent1458? ManaCrystalIntent { get; init; }
    internal RuntimePlayerSelectedPotionUse1458? SelectedPotionUse { get; init; }
    // Source ResetEffects/equipment/UpdatePermanentBoosters order, captured before
    // this phase's selected use. New permanent flags affect the following phase.
    internal byte? PermanentPhaseFlags { get; init; }
    internal PlayerPermanentBoosterState1458? PermanentBoosters { get; init; }
    internal PlayerPermanentSpeedState1458? PermanentSpeeds { get; init; }
    internal PlayerCraftingRegion1458? PermanentCraftingRegion { get; init; }
    // Latest multi-use type explanation only; custody stays in the existing journal.
    internal RuntimePlayerQuickBuffExplanation1458? QuickBuffExplanation { get; init; }
    internal int DeadTime { get; init; }
    internal int RespawnTimer { get; init; }
    internal int ManaPotionDelay { get; init; }
    internal bool PendingItemReuse { get; init; }
    internal bool Channel { get; init; }
    // Source StartChanneling(Item) provenance; current selection and raw Channel do not establish this cache.
    internal int? OwnedChannelExpectedType { get; init; }
    internal bool SlowMagicUse { get; init; }
    internal RuntimePlayerMagicPayment1458? MagicPayment { get; init; }
    // Constructor-owned persistent local selector state. Reports cannot seed it.
    // Null remains unknown after a retired/unrepresented source phase.
    internal AmmoCyclingState1458? AmmoCycling { get; init; }
    internal RuntimePlayerToolUse1458? ToolUse { get; init; }
    internal int? ToolTime { get; init; }
    internal int? AttackCD { get; init; }
    internal VanillaPlayerCombatSnapshot? DerivedCombat { get; init; }
    internal PlayerDerivedCritState1458? DerivedCrit => DerivedCombat is { } combat
        ? new(combat.MeleeCrit, combat.RangedCrit, combat.MagicCrit) : null;
    internal static RuntimePlayerItemPhase1458 Constructor => new(
        20, new(0, 0, 0f, 0, 0), new(0, 0, 0, 0, false, 0, 0), 0f)
    {
        AmmoCycling = new(0, 0),
        ToolTime = 0, ToolUse = RuntimePlayerToolUse1458.Constructor,
        AttackCD = 0, DerivedCombat = VanillaPlayerCombatSnapshot.Baseline
    };
}
