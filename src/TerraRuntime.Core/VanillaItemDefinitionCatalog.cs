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

/// <summary>Source-backed common runtime defaults materialized by Item.SetDefaults.</summary>
public readonly record struct VanillaItemRuntimeDefaults(
    int Width,
    int Height,
    short MaximumStack)
{
    public bool IsValid => Width > 0 && Height > 0 && MaximumStack > 0;
}

/// <summary>Named TerrariaServer 1.4.5.8 item-use animation family.</summary>
public enum VanillaItemUseStyle : byte
{
    Swing = 1
}

/// <summary>
/// Source-backed timing/control defaults for one item use. Tick counts are authoritative gameplay values rather
/// than packet fields; downstream behavior can schedule use without recovering Item.SetDefaults data from an id.
/// </summary>
public readonly record struct VanillaItemUseTimingDefinition(
    VanillaItemUseStyle Style,
    int AnimationTicks,
    int UseTimeTicks,
    bool AutoReuse,
    bool UseTurn)
{
    public bool IsValid =>
        Enum.IsDefined(Style) &&
        AnimationTicks > 0 &&
        UseTimeTicks > 0;
}

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
    VanillaItemRuntimeDefaults RuntimeDefaults,
    VanillaItemUseTimingDefinition? UseTiming,
    VanillaItemPlacementDefinition? Placement,
    VanillaItemPickToolDefinition? PickTool,
    VanillaItemWorldDropDefinition? WorldDrop);

/// <summary>
/// Initial source-verified TerrariaServer 1.4.5.8 item-definition catalog. The catalog grows only with facts
/// independently pinned by repository source-contract probes or equivalent official-source evidence.
/// </summary>
public static class VanillaItemDefinitionCatalog
{
    public const short CommonMaximumStack = 9_999;
    public const short CopperPickaxePickPower = 35;
    public const int CopperPickaxeTileBoost = -1;

    private static readonly VanillaItemUseTimingDefinition DirtBlockUseTiming = new(
        Style: VanillaItemUseStyle.Swing,
        AnimationTicks: 15,
        UseTimeTicks: 10,
        AutoReuse: true,
        UseTurn: true);

    private static readonly VanillaItemUseTimingDefinition CopperPickaxeUseTiming = new(
        Style: VanillaItemUseStyle.Swing,
        AnimationTicks: 23,
        UseTimeTicks: 15,
        AutoReuse: true,
        UseTurn: true);

    private static readonly VanillaItemUseTimingDefinition SlimeStaffUseTiming = new(
        Style: VanillaItemUseStyle.Swing,
        AnimationTicks: 28,
        UseTimeTicks: 28,
        AutoReuse: true,
        UseTurn: false);

    private static readonly VanillaItemDefinition DirtBlockDefinition = new(
        Type: VanillaItemIds.DirtBlock,
        RuntimeDefaults: new VanillaItemRuntimeDefaults(Width: 12, Height: 12, MaximumStack: CommonMaximumStack),
        UseTiming: DirtBlockUseTiming,
        Placement: new VanillaItemPlacementDefinition(
            TileType: VanillaTileIds.Dirt,
            Consumable: true),
        PickTool: null,
        WorldDrop: null);

    private static readonly VanillaItemDefinition StoneBlockDefinition = new(
        Type: VanillaItemIds.StoneBlock,
        RuntimeDefaults: new VanillaItemRuntimeDefaults(Width: 12, Height: 12, MaximumStack: CommonMaximumStack),
        UseTiming: DirtBlockUseTiming,
        Placement: new VanillaItemPlacementDefinition(
            TileType: VanillaTileIds.Stone,
            Consumable: true),
        PickTool: null,
        WorldDrop: null);

    private static readonly VanillaItemDefinition SandBlockDefinition = new(
        Type: VanillaItemIds.SandBlock,
        RuntimeDefaults: new VanillaItemRuntimeDefaults(Width: 12, Height: 12, MaximumStack: CommonMaximumStack),
        UseTiming: DirtBlockUseTiming,
        Placement: new VanillaItemPlacementDefinition(
            TileType: VanillaTileIds.Sand,
            Consumable: true),
        PickTool: null,
        WorldDrop: null);

    private static readonly VanillaItemDefinition CopperPickaxeDefinition = new(
        Type: VanillaItemIds.CopperPickaxe,
        RuntimeDefaults: new VanillaItemRuntimeDefaults(Width: 24, Height: 28, MaximumStack: CommonMaximumStack),
        UseTiming: CopperPickaxeUseTiming,
        Placement: null,
        PickTool: new VanillaItemPickToolDefinition(
            PickPower: CopperPickaxePickPower,
            TileBoost: CopperPickaxeTileBoost),
        WorldDrop: null);

    private static readonly VanillaItemDefinition GelDefinition = new(
        Type: VanillaItemIds.Gel,
        RuntimeDefaults: new VanillaItemRuntimeDefaults(Width: 10, Height: 12, MaximumStack: CommonMaximumStack),
        UseTiming: null,
        Placement: null,
        PickTool: null,
        WorldDrop: new VanillaItemWorldDropDefinition(
            Width: 10,
            Height: 12,
            NoGravity: false,
            PrefixFamily: VanillaItemPrefixFamily.None));

    private static readonly VanillaItemDefinition SlimeStaffDefinition = new(
        Type: VanillaItemIds.SlimeStaff,
        RuntimeDefaults: new VanillaItemRuntimeDefaults(Width: 26, Height: 28, MaximumStack: CommonMaximumStack),
        UseTiming: SlimeStaffUseTiming,
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

        if (type == VanillaItemIds.StoneBlock)
        {
            definition = StoneBlockDefinition;
            return true;
        }

        if (type == VanillaItemIds.SandBlock)
        {
            definition = SandBlockDefinition;
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

    public static bool TryGetRuntimeDefaults(
        ItemTypeId type,
        out VanillaItemRuntimeDefaults runtimeDefaults)
    {
        if (TryGet(type, out VanillaItemDefinition definition) && definition.RuntimeDefaults.IsValid)
        {
            runtimeDefaults = definition.RuntimeDefaults;
            return true;
        }

        runtimeDefaults = default;
        return false;
    }

    /// <summary>
    /// Validates known source-backed maxima while preserving canonical inventory compatibility for item types whose
    /// complete defaults have not been imported into the deliberately sparse catalog yet.
    /// </summary>
    public static bool IsValidKnownStack(ItemTypeId type, short stack) =>
        stack > 0 &&
        (!TryGetRuntimeDefaults(type, out VanillaItemRuntimeDefaults defaults) ||
         stack <= defaults.MaximumStack);

    public static bool TryGetUseTiming(
        ItemTypeId type,
        out VanillaItemUseTimingDefinition useTiming)
    {
        if (TryGet(type, out VanillaItemDefinition definition) &&
            definition.UseTiming is { } verified &&
            verified.IsValid)
        {
            useTiming = verified;
            return true;
        }

        useTiming = default;
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
