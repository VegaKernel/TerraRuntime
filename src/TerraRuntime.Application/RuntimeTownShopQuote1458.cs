using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed record RuntimeTownShopQuoteSnapshot1458(ulong Revision, NpcHandle VendorHandle,
    float PriceAdjustment, byte DiscountMask, bool WindowsItemPrefixArithmetic,
    ShopItem1458[] Stock40, ShopMemo1458[] Memos)
{
    internal VanillaItemPrefixWorld1458? World { get; init; }
    internal byte PriceArithmeticMask { get; init; }
    internal byte CapturedPriceArithmeticMask => PriceArithmeticMask == 0
        ? (byte)(WindowsItemPrefixArithmetic ? 2 : 1) : PriceArithmeticMask;
    internal byte PriceContextMask { get; init; }
    internal byte CapturedPriceContextMask => PriceContextMask == 0
        ? ComposePriceContexts(DiscountMask, CapturedPriceArithmeticMask) : PriceContextMask;
    internal static byte ComposePriceContexts(byte discount, byte arithmetic) =>
        (byte)(((arithmetic & 1) != 0 ? discount : 0) | ((arithmetic & 2) != 0 ? discount << 2 : 0));
    internal static byte ContextDiscounts(byte contexts) =>
        (byte)(((contexts & 5) != 0 ? 1 : 0) | ((contexts & 10) != 0 ? 2 : 0));
    internal static byte ContextArithmetics(byte contexts) =>
        (byte)(((contexts & 3) != 0 ? 1 : 0) | ((contexts & 12) != 0 ? 2 : 0));
}

// A talk session owns its bounded stock/memo lifetime. Packet40 does not expose Main.OpenShop;
// before a conserved economic witness the two possible discount latches remain explicit.
internal sealed class RuntimeTownShopQuote1458
{
    private readonly RuntimeTownShopSession1458 session;
    private readonly RuntimePlayerMembership membership;
    private readonly RuntimePlayerMember member;
    private readonly RuntimeNpcStore npcs;
    private readonly ConnectionHandle connection;
    private readonly VanillaTownShopEntry1458[] sourceOffers;
    private readonly NpcHandle vendorHandle;
    private readonly bool windowsArithmetic;
    private readonly VanillaItemPrefixWorld1458? world;
    private ShopItem1458[] stock;
    private ShopMemo1458[] memos = [];
    private ulong revision = 1;
    private byte discountMask;
    private byte deferredPriceContextMask;
    private byte priceArithmeticMask;
    private byte priceContextMask;
    private bool opened;
    private bool retired;
    private PlayerAuthority.PendingShopJournal? retiredJournal;

    private RuntimeTownShopQuote1458(RuntimeTownShopSession1458 session, RuntimePlayerMembership membership,
        RuntimePlayerMember member, RuntimeNpcStore npcs, ConnectionHandle connection, NpcHandle vendorHandle,
        ShopItem1458[] stock, byte discountMask, bool windowsArithmetic, VanillaItemPrefixWorld1458? world,
        byte priceArithmeticMask, byte priceContextMask)
    {
        this.session = session; this.membership = membership; this.member = member; this.npcs = npcs;
        this.connection = connection; this.vendorHandle = vendorHandle;
        this.stock = stock.ToArray(); this.discountMask = discountMask;
        this.windowsArithmetic = windowsArithmetic; this.world = world; sourceOffers = session.Offers.ToArray();
        this.priceArithmeticMask = priceArithmeticMask == 0 ? (byte)(windowsArithmetic ? 2 : 1) : priceArithmeticMask;
        this.priceContextMask = priceContextMask == 0
            ? RuntimeTownShopQuoteSnapshot1458.ComposePriceContexts(discountMask, this.priceArithmeticMask) : priceContextMask;
    }

    internal static bool TryCreate(RuntimeTownShopSession1458 session, RuntimePlayerMembership membership,
        RuntimeNpcStore npcs, ConnectionHandle connection, NpcHandle vendorHandle,
        ShopItem1458[] stock, byte discountMask, bool windowsArithmetic,
        out RuntimeTownShopQuote1458 quote, VanillaItemPrefixWorld1458? world = null,
        byte priceArithmeticMask = 0, byte priceContextMask = 0)
    {
        quote = default!;
        if (!connection.IsAssigned || !membership.TryGet(connection, out var member) ||
            discountMask is < 1 or > 3 || priceArithmeticMask > 3 || priceContextMask > 15 || stock.Length != 40 ||
            session.Kind is not (RuntimeTownShopSessionKind1458.OrdinaryShop or RuntimeTownShopSessionKind1458.SpecialShop) ||
            session.Offers.Length > 40 || session.MissingFacts != RuntimeTownCommerceMissingFacts1458.None ||
            !float.IsFinite(session.PriceAdjustment) || session.PriceAdjustment <= 0 ||
            !npcs.TryGet(vendorHandle, out var vendor) || vendor.TypeIdentity != session.NpcType ||
            vendorHandle.Slot != session.NpcSlot || vendor.Simulation.Life <= 0) return false;
        byte arithmetic = priceArithmeticMask == 0 ? (byte)(windowsArithmetic ? 2 : 1) : priceArithmeticMask;
        if (priceContextMask != 0 && (RuntimeTownShopQuoteSnapshot1458.ContextDiscounts(priceContextMask) != discountMask ||
            RuntimeTownShopQuoteSnapshot1458.ContextArithmetics(priceContextMask) != arithmetic)) return false;
        quote = new(session, membership, member, npcs, connection, vendorHandle, stock, discountMask, windowsArithmetic, world, priceArithmeticMask, priceContextMask);
        return true;
    }

    internal bool TryObserveDiscount(bool available)
        => TryObserveDiscount((byte)(available ? 2 : 1));

    internal bool TryObserveDiscount(byte availableMask)
    {
        if (availableMask is < 1 or > 3) return false;
        byte possible = RuntimeTownShopQuoteSnapshot1458.ComposePriceContexts(availableMask, priceArithmeticMask);
        // A partial legacy observation cannot reconstruct missing platform/discount pairs.
        if (priceContextMask != RuntimeTownShopQuoteSnapshot1458.ComposePriceContexts(discountMask, priceArithmeticMask))
        {
            possible &= priceContextMask;
            if (RuntimeTownShopQuoteSnapshot1458.ContextDiscounts(possible) != availableMask) return false;
        }
        return TryObservePriceContexts(possible);
    }

    internal bool TryObservePriceContexts(byte possibleContexts)
    {
        if (possibleContexts is < 1 or > 15 || !IsSourceCurrent() || revision == ulong.MaxValue ||
            (RuntimeTownShopQuoteSnapshot1458.ContextArithmetics(possibleContexts) & priceArithmeticMask) !=
                RuntimeTownShopQuoteSnapshot1458.ContextArithmetics(possibleContexts)) return false;
        if (opened) return true;
        // The first inventory image captures the completed client operation's price history.
        // A later owned equipment phase must not replace it while source138 is outstanding.
        if (HasCapturedJournal)
        { deferredPriceContextMask |= possibleContexts; return true; }
        UpdatePriceContexts((byte)(priceContextMask | deferredPriceContextMask | possibleContexts));
        deferredPriceContextMask = 0;
        return true;
    }

    private void UpdatePriceContexts(byte contexts)
    {
        if (contexts == priceContextMask) return;
        priceContextMask = contexts;
        discountMask = RuntimeTownShopQuoteSnapshot1458.ContextDiscounts(contexts);
        priceArithmeticMask = RuntimeTownShopQuoteSnapshot1458.ContextArithmetics(contexts);
        revision++;
    }

    private bool HasCapturedJournal => member.CursorMove?.Shop is { } pending &&
        ReferenceEquals(pending.Quote, this) && pending.Economic?.Revision == revision;

    internal bool TryCapturePossibleDiscountMask(out byte mask)
    {
        mask = 0;
        if (!IsSourceCurrent()) return false;
        mask = RuntimeTownShopQuoteSnapshot1458.ContextDiscounts((byte)(priceContextMask | deferredPriceContextMask));
        return true;
    }

    internal bool TryCapturePossiblePriceContexts(out byte mask)
    {
        mask = 0;
        if (!IsSourceCurrent()) return false;
        mask = (byte)(priceContextMask | deferredPriceContextMask);
        return true;
    }

    internal bool TryCapture(out RuntimeTownShopQuoteSnapshot1458 snapshot)
    {
        snapshot = default!;
        if (revision == ulong.MaxValue || !IsSourceCurrent()) return false;
        // A rejected/non-economic journal may finish without adopting this quote.
        // Retain its observed history for the next capture rather than discarding it.
        if (!opened && !HasCapturedJournal && deferredPriceContextMask != 0)
        {
            UpdatePriceContexts((byte)(priceContextMask | deferredPriceContextMask));
            deferredPriceContextMask = 0;
            if (revision == ulong.MaxValue) return false;
        }
        snapshot = new(revision, vendorHandle, session.PriceAdjustment, discountMask,
            windowsArithmetic, stock.ToArray(), memos.ToArray())
            { World = world, PriceArithmeticMask = priceArithmeticMask, PriceContextMask = priceContextMask };
        return true;
    }

    internal bool TryPrepare(RuntimeTownShopQuoteSnapshot1458 snapshot, ShopTransactionPlan1458 transaction,
        bool discount, out RuntimeTownShopEconomicPlan1458 token) =>
        TryPrepare(snapshot, transaction, (byte)(discount ? 2 : 1), out token);

    internal bool TryPrepare(RuntimeTownShopQuoteSnapshot1458 snapshot, ShopTransactionPlan1458 transaction,
        byte resolvedDiscountMask, out RuntimeTownShopEconomicPlan1458 token)
        => TryPrepare(snapshot, transaction, resolvedDiscountMask, null, out token);

    internal bool TryPrepare(RuntimeTownShopQuoteSnapshot1458 snapshot, ShopTransactionPlan1458 transaction,
        byte resolvedDiscountMask, PlayerAuthority.PendingShopJournal? journal,
        out RuntimeTownShopEconomicPlan1458 token)
        => TryPrepare(snapshot, transaction, resolvedDiscountMask, snapshot.CapturedPriceArithmeticMask, journal, out token);

    internal bool TryPrepare(RuntimeTownShopQuoteSnapshot1458 snapshot, ShopTransactionPlan1458 transaction,
        byte resolvedDiscountMask, byte resolvedArithmeticMask, PlayerAuthority.PendingShopJournal? journal,
        out RuntimeTownShopEconomicPlan1458 token)
        => TryPrepare(snapshot, transaction, resolvedDiscountMask, resolvedArithmeticMask,
            (byte)(snapshot.CapturedPriceContextMask &
                RuntimeTownShopQuoteSnapshot1458.ComposePriceContexts(resolvedDiscountMask, resolvedArithmeticMask)), journal, out token);

    internal bool TryPrepare(RuntimeTownShopQuoteSnapshot1458 snapshot, ShopTransactionPlan1458 transaction,
        byte resolvedDiscountMask, byte resolvedArithmeticMask, byte resolvedContexts,
        PlayerAuthority.PendingShopJournal? journal, out RuntimeTownShopEconomicPlan1458 token)
    {
        token = default!;
        if (resolvedDiscountMask is < 1 or > 3 || (resolvedDiscountMask & snapshot.DiscountMask) != resolvedDiscountMask ||
            resolvedArithmeticMask is < 1 or > 3 || (resolvedArithmeticMask & snapshot.CapturedPriceArithmeticMask) != resolvedArithmeticMask ||
            resolvedContexts is < 1 or > 15 || (resolvedContexts & snapshot.CapturedPriceContextMask) != resolvedContexts ||
            RuntimeTownShopQuoteSnapshot1458.ContextDiscounts(resolvedContexts) != resolvedDiscountMask ||
            RuntimeTownShopQuoteSnapshot1458.ContextArithmetics(resolvedContexts) != resolvedArithmeticMask ||
            !IsEconomicCurrent(snapshot, journal) || transaction.Stock.Length != 40 || transaction.Memos.Length > 40 ||
            !npcs.TryGet(vendorHandle, out var vendor)) return false;
        // Own detached output before the final pure guards; no provider or observer runs during adoption.
        token = new(this, snapshot, vendor, transaction.Stock.ToArray(), transaction.Memos.ToArray(), resolvedDiscountMask, resolvedArithmeticMask, resolvedContexts, journal);
        return token.IsCurrent;
    }

    // Official CloseNPCChatOrSign may emit NoNpc40 between its inventory5 images and138.
    // Retain only the already captured receipt; this is not an inactive-session quote grant.
    internal bool TryRetirePending(PlayerAuthority.PendingShopJournal journal)
    {
        if (retired || journal.Invalid || journal.IsRetired ||
            !ReferenceEquals(member.CursorMove?.Shop, journal) || !ReferenceEquals(journal.Quote, this) ||
            journal.Economic is not { } before || !journal.Capture.IsCurrent || !IsCurrent(before) ||
            !journal.TryRetireReports()) return false;
        retiredJournal = journal;
        retired = true;
        return true;
    }

    internal void Retire() => retired = true;

    private bool IsIdentityCurrent() =>
        membership.TryGet(connection, out var currentMember) && ReferenceEquals(currentMember, member) &&
        ReferenceEquals(session.Quote, this) && sourceOffers.AsSpan().SequenceEqual(session.Offers) &&
        npcs.TryGet(vendorHandle, out var vendor) && vendor.TypeIdentity == session.NpcType &&
        vendor.Simulation.Life > 0;

    private bool IsSourceCurrent()
    {
        if (retired || !IsIdentityCurrent() ||
            !membership.TryGetTownShopSession(connection.Player, out var current) || !ReferenceEquals(current, session) ||
            !ReferenceEquals(session.Quote, this)) return false;
        return true;
    }

    // Cooperative receipt work may check its captured economy while the existing
    // cursor owner still retains it. This pure query grants no economic token;
    // retired adoption continues to require release of that exact cursor receipt.
    internal bool IsRetainedJournalCurrent(PlayerAuthority.PendingShopJournal journal) =>
        !journal.Invalid && ReferenceEquals(member.CursorMove?.Shop, journal) &&
        ReferenceEquals(journal.Quote, this) && journal.Economic is { } snapshot &&
        journal.Capture.IsCurrent &&
        (retired ? IsRetiredJournalCurrent(snapshot, journal) : IsCurrent(snapshot));

    private bool IsEconomicCurrent(RuntimeTownShopQuoteSnapshot1458 snapshot, PlayerAuthority.PendingShopJournal? journal) =>
        !retired ? IsCurrent(snapshot) : member.CursorMove is null && IsRetiredJournalCurrent(snapshot, journal);

    private bool IsRetiredJournalCurrent(RuntimeTownShopQuoteSnapshot1458 snapshot, PlayerAuthority.PendingShopJournal? journal) =>
        journal is not null && ReferenceEquals(retiredJournal, journal) &&
        ReferenceEquals(journal.Economic, snapshot) && journal.IsRetired && !journal.Invalid &&
        IsIdentityCurrent() && journal.Capture.IsCurrent &&
        !membership.TryGetTownShopSession(connection.Player, out _) &&
        membership.TryGetTalkNpc(connection.Player, out short talk) &&
        talk == TerraRuntime.Protocol.Multiplicity.TerrariaNpcTalkCodec.NoNpc && IsSnapshotCurrent(snapshot);

    private bool IsCurrent(RuntimeTownShopQuoteSnapshot1458 snapshot) => IsSourceCurrent() && IsSnapshotCurrent(snapshot);

    private bool IsSnapshotCurrent(RuntimeTownShopQuoteSnapshot1458 snapshot) =>
        revision == snapshot.Revision && vendorHandle == snapshot.VendorHandle &&
        session.PriceAdjustment == snapshot.PriceAdjustment && discountMask == snapshot.DiscountMask &&
        windowsArithmetic == snapshot.WindowsItemPrefixArithmetic && world == snapshot.World &&
        priceArithmeticMask == snapshot.CapturedPriceArithmeticMask &&
        priceContextMask == snapshot.CapturedPriceContextMask &&
        stock.AsSpan().SequenceEqual(snapshot.Stock40) && memos.AsSpan().SequenceEqual(snapshot.Memos);

    internal sealed class RuntimeTownShopEconomicPlan1458(RuntimeTownShopQuote1458 owner,
        RuntimeTownShopQuoteSnapshot1458 before, NpcSnapshot vendor,
        ShopItem1458[] afterStock, ShopMemo1458[] afterMemos, byte afterMask, byte afterArithmeticMask, byte afterContexts,
        PlayerAuthority.PendingShopJournal? journal = null)
    {
        private bool adopted;
        internal bool IsCurrent => !adopted && owner.IsEconomicCurrent(before, journal) &&
            owner.npcs.TryGet(vendor.Handle, out var current) && current == vendor;
        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent || owner.revision == ulong.MaxValue) return false;
            owner.stock = afterStock; owner.memos = afterMemos; owner.discountMask = afterMask;
            owner.priceArithmeticMask = afterArithmeticMask;
            owner.priceContextMask = afterContexts;
            owner.deferredPriceContextMask = 0;
            owner.opened = true; owner.revision++; adopted = true;
            return true;
        }
        internal bool IsAcceptedCurrent => adopted && owner.IsSourceCurrent() &&
            owner.revision == before.Revision + 1 && owner.discountMask == afterMask &&
            owner.priceArithmeticMask == afterArithmeticMask && owner.priceContextMask == afterContexts;
    }
}
