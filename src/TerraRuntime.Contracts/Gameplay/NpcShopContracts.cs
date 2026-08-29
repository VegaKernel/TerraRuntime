namespace TerraRuntime.Contracts.Gameplay;

/// <summary>Stable server-defined identity for one runtime shop.</summary>
public readonly record struct ShopId : IComparable<ShopId>
{
    public const int MaxLength = 128;
    private readonly string? value;

    public ShopId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, MaxLength);
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
                throw new ArgumentException("Shop IDs cannot contain whitespace or control characters.", nameof(value));
        }

        this.value = value;
    }

    public string Value => value ?? string.Empty;
    public bool IsAssigned => value is not null;
    public int CompareTo(ShopId other) => StringComparer.Ordinal.Compare(Value, other.Value);
    public override string ToString() => Value;
}

/// <summary>
/// Stable identity for an offer inside a shop. Catalog order is presentation only and must never become purchase
/// identity, because providers may reorder or conditionally omit offers between published catalog revisions.
/// </summary>
public readonly record struct ShopOfferId : IComparable<ShopOfferId>
{
    public const int MaxLength = 128;
    private readonly string? value;

    public ShopOfferId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, MaxLength);
        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
                throw new ArgumentException("Shop offer IDs cannot contain whitespace or control characters.", nameof(value));
        }

        this.value = value;
    }

    public string Value => value ?? string.Empty;
    public bool IsAssigned => value is not null;
    public int CompareTo(ShopOfferId other) => StringComparer.Ordinal.Compare(Value, other.Value);
    public override string ToString() => Value;
}

public enum ShopCurrencyKind : byte
{
    VanillaCoins = 0
}

/// <summary>
/// One protocol-valid vanilla item offered by a runtime shop. UnitPrice is denominated in the selected currency's
/// smallest runtime unit. The initial currency surface intentionally exposes only vanilla coins; custom provider
/// currencies require a later prepare/commit/rollback contract rather than an unsafe debit callback.
/// </summary>
public readonly record struct ShopOffer(
    ShopOfferId Id,
    ItemTypeId ItemType,
    short Stack,
    long UnitPrice,
    ShopCurrencyKind Currency = ShopCurrencyKind.VanillaCoins)
{
    public bool IsValid =>
        Id.IsAssigned &&
        !ItemType.IsNone &&
        VanillaItemIds.TryCreate(ItemType.Value, out _) &&
        Stack > 0 &&
        UnitPrice >= 0 &&
        Enum.IsDefined(Currency);
}

/// <summary>
/// A shop is attached to a server-defined NPC archetype rather than a raw client-visible NPC type, allowing two
/// NPCs with the same vanilla presentation to expose different commerce policy.
/// </summary>
public sealed class NpcShopCatalog
{
    private readonly ShopOffer[] offers;

    public NpcShopCatalog(
        ShopId id,
        GameplayArchetypeId npcArchetypeId,
        IEnumerable<ShopOffer> offers)
    {
        if (!id.IsAssigned)
            throw new ArgumentException("Shop ID must be assigned.", nameof(id));
        if (!npcArchetypeId.IsAssigned)
            throw new ArgumentException("NPC archetype ID must be assigned.", nameof(npcArchetypeId));
        ArgumentNullException.ThrowIfNull(offers);

        ShopOffer[] copy = offers.ToArray();
        if (copy.Length > 255)
            throw new ArgumentOutOfRangeException(nameof(offers), "A shop catalog cannot contain more than 255 offers.");

        var seen = new HashSet<ShopOfferId>();
        foreach (ShopOffer offer in copy)
        {
            if (!offer.IsValid)
                throw new ArgumentException("Shop catalog contains an invalid offer.", nameof(offers));
            if (!seen.Add(offer.Id))
                throw new ArgumentException($"Duplicate shop offer ID '{offer.Id}'.", nameof(offers));
        }

        Id = id;
        NpcArchetypeId = npcArchetypeId;
        this.offers = copy;
    }

    public ShopId Id { get; }
    public GameplayArchetypeId NpcArchetypeId { get; }
    public ReadOnlyMemory<ShopOffer> Offers => offers;

    public bool TryGetOffer(ShopOfferId offerId, out ShopOffer offer)
    {
        if (!offerId.IsAssigned)
        {
            offer = default;
            return false;
        }

        for (int index = 0; index < offers.Length; index++)
        {
            if (offers[index].Id != offerId)
                continue;

            offer = offers[index];
            return true;
        }

        offer = default;
        return false;
    }
}
