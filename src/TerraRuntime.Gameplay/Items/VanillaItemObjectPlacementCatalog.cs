using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// One source-pinned authorization mapping from a held vanilla item to an object-placement identity. This is
/// deliberately narrower than Item.createTile: only combinations whose object transaction is implemented are admitted.
/// </summary>
public readonly record struct VanillaItemObjectPlacementDefinition(
    ItemTypeId ItemType,
    TileTypeId TileType,
    short Style,
    byte Alternate)
{
    public bool IsValid =>
        !ItemType.IsNone &&
        Style >= 0 &&
        VanillaItemIds.TryCreate(ItemType.Value, out ItemTypeId canonicalItem) &&
        canonicalItem == ItemType &&
        VanillaTileIds.TryCreate(TileType.Value, out TileTypeId canonicalTile) &&
        canonicalTile == TileType;

    public bool Matches(TileTypeId tileType, short style, byte alternate) =>
        IsValid &&
        tileType == TileType &&
        style == Style &&
        alternate == Alternate;
}

/// <summary>
/// Sparse authoritative item-to-object catalog for TerrariaServer 1.4.5.8. The first admitted mapping is the
/// ordinary Chest item (48) to Containers tile (21), base style/alternate zero. Other chest variants remain
/// unsupported until their item identities and style/alternate semantics are independently pinned.
/// </summary>
public static class VanillaItemObjectPlacementCatalog
{
    private static readonly VanillaItemObjectPlacementDefinition[] Definitions =
    [
        new(
            VanillaItemIds.Chest,
            VanillaTileIds.Containers,
            Style: 0,
            Alternate: 0)
    ];

    public static ReadOnlySpan<VanillaItemObjectPlacementDefinition> All => Definitions;

    public static bool TryGet(
        ItemTypeId itemType,
        out VanillaItemObjectPlacementDefinition definition)
    {
        foreach (VanillaItemObjectPlacementDefinition candidate in Definitions)
        {
            if (candidate.ItemType == itemType)
            {
                definition = candidate;
                return true;
            }
        }

        definition = default;
        return false;
    }

    /// <summary>
    /// Reverse lookup used by authoritative object breaking. Only exact source-pinned placement identities can
    /// materialize an item drop; unknown styles/alternates remain fail-closed instead of guessing an item id.
    /// </summary>
    public static bool TryGet(
        TileTypeId tileType,
        short style,
        byte alternate,
        out VanillaItemObjectPlacementDefinition definition)
    {
        foreach (VanillaItemObjectPlacementDefinition candidate in Definitions)
        {
            if (candidate.Matches(tileType, style, alternate))
            {
                definition = candidate;
                return true;
            }
        }

        definition = default;
        return false;
    }
}
