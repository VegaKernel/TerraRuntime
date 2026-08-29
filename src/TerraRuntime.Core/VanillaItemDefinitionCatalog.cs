using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>
/// Source-backed placement facts for one vanilla item. This is intentionally sparse: absence of a placement
/// definition means TerraRuntime has not verified that capability, not that the vanilla item necessarily cannot place.
/// </summary>
public readonly record struct VanillaItemPlacementDefinition(
    TileTypeId TileType,
    bool Consumable);

/// <summary>
/// Source-backed pick-tool facts for one vanilla item. Values are the exact TerrariaServer 1.4.5.8 defaults
/// currently consumed by authoritative tile-interaction policy.
/// </summary>
public readonly record struct VanillaItemPickToolDefinition(
    short PickPower,
    int TileBoost);

/// <summary>
/// Source-backed item defaults required to materialize a world-item spawn. PrefixFamily represents verified
/// Prefix(-1) capability; None means the source-backed item cannot receive a natural prefix in this path.
/// </summary>
public readonly record struct VanillaItemWorldDropDefinition(
    int Width,
    int Height,
    bool NoGravity,
    VanillaItemPrefixFamily PrefixFamily)
{
    public bool IsValid =>
        Width > 0 &&
        Height > 0 &&
        PrefixFamily is VanillaItemPrefixFamily.None or VanillaItemPrefixFamily.Summon;
}

/// <summary>
/// Sparse immutable vanilla item definition. Optional capability records distinguish verified facts from fields
/// TerraRuntime has not yet imported from TerrariaServer 1.4.5.8; an absent capability must never be read as a
/// guessed zero/default vanilla value.
/// </summary>
public readonly record struct VanillaItemDefinition(
    ItemTypeId Type,
    VanillaItemPlacementDefinition? Placement,
    VanillaItemPickToolDefinition? PickTool,
    VanillaItemWorldDropDefinition? WorldDrop);

/// <summary>
/// Initial source-verified TerrariaServer 1.4.5.8 item-definition catalog. The catalog grows only with facts
/// independently pinned by repository source-contract probes or equivalent official-source evidence.
/// </summary>
public static class VanillaItemDefinitionCatalog
{
    public const short CopperPickaxePickPower = 35;
    public const int CopperPickaxeTileBoost = -1;

    private static readonly VanillaItemDefinition DirtBlockDefinition = new(
        Type: VanillaItemIds.DirtBlock,
        Placement: new VanillaItemPlacementDefinition(
            TileType: VanillaTileIds.Dirt,
            Consumable: true),
        PickTool: null,
        WorldDrop: null);

    private static readonly VanillaItemDefinition CopperPickaxeDefinition = new(
        Type: VanillaItemIds.CopperPickaxe,
        Placement: null,
        PickTool: new VanillaItemPickToolDefinition(
            PickPower: CopperPickaxePickPower,
            TileBoost: CopperPickaxeTileBoost),
        WorldDrop: null);

    private static readonly VanillaItemDefinition GelDefinition = new(
        Type: VanillaItemIds.Gel,
        Placement: null,
        PickTool: null,
        WorldDrop: new VanillaItemWorldDropDefinition(
            Width: 10,
            Height: 12,
            NoGravity: false,
            PrefixFamily: VanillaItemPrefixFamily.None));

    private static readonly VanillaItemDefinition SlimeStaffDefinition = new(
        Type: VanillaItemIds.SlimeStaff,
        Placement: null,
        PickTool: null,
        WorldDrop: new VanillaItemWorldDropDefinition(
            Width: 26,
            Height: 28,
            NoGravity: false,
            PrefixFamily: VanillaItemPrefixFamily.Summon));

    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)
    {
        if (type == VanillaItemIds.DirtBlock)
        {
            definition = DirtBlockDefinition;
            return true;
        }

        if (type == VanillaItemIds.CopperPickaxe)
        {
            definition = CopperPickaxeDefinition;
            return true;
        }

        if (type == VanillaItemIds.Gel)
        {
            definition = GelDefinition;
            return true;
        }

        if (type == VanillaItemIds.SlimeStaff)
        {
            definition = SlimeStaffDefinition;
            return true;
        }

        definition = default;
        return false;
    }

    public static bool TryGetPlacement(
        ItemTypeId type,
        out VanillaItemPlacementDefinition placement)
    {
        if (TryGet(type, out VanillaItemDefinition definition) && definition.Placement is { } verified)
        {
            placement = verified;
            return true;
        }

        placement = default;
        return false;
    }

    public static bool TryGetPickTool(
        ItemTypeId type,
        out VanillaItemPickToolDefinition pickTool)
    {
        if (TryGet(type, out VanillaItemDefinition definition) && definition.PickTool is { } verified)
        {
            pickTool = verified;
            return true;
        }

        pickTool = default;
        return false;
    }

    public static bool TryGetWorldDrop(
        ItemTypeId type,
        out VanillaItemWorldDropDefinition worldDrop)
    {
        if (TryGet(type, out VanillaItemDefinition definition) &&
            definition.WorldDrop is { } verified &&
            verified.IsValid)
        {
            worldDrop = verified;
            return true;
        }

        worldDrop = default;
        return false;
    }
}
