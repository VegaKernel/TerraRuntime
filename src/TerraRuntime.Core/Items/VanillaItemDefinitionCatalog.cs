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

    private static readonly VanillaItemDefinition KingSlimeBossBagDefinition = new(
        Type: VanillaKingSlimeItemIds.KingSlimeBossBag,
        RuntimeDefaults: new VanillaItemRuntimeDefaults(Width: 24, Height: 24, MaximumStack: CommonMaximumStack),
        UseTiming: null,
        Placement: null,
        PickTool: null,
        WorldDrop: new VanillaItemWorldDropDefinition(
            Width: 24,
            Height: 24,
            NoGravity: false,
            PrefixFamily: VanillaItemPrefixFamily.None));

    private static readonly VanillaItemDefinition KingSlimePetItemDefinition = new(
        Type: VanillaKingSlimeItemIds.KingSlimePetItem,
        RuntimeDefaults: new VanillaItemRuntimeDefaults(Width: 16, Height: 30, MaximumStack: CommonMaximumStack),
        UseTiming: null,
        Placement: null,
        PickTool: null,
        WorldDrop: new VanillaItemWorldDropDefinition(
            Width: 16,
            Height: 30,
            NoGravity: false,
            PrefixFamily: VanillaItemPrefixFamily.None));

    private static readonly VanillaItemDefinition KingSlimeMasterTrophyDefinition = new(
        Type: VanillaKingSlimeItemIds.KingSlimeMasterTrophy,
        RuntimeDefaults: new VanillaItemRuntimeDefaults(Width: 14, Height: 14, MaximumStack: CommonMaximumStack),
        UseTiming: null,
        Placement: null,
        PickTool: null,
        WorldDrop: new VanillaItemWorldDropDefinition(
            Width: 14,
            Height: 14,
            NoGravity: false,
            PrefixFamily: VanillaItemPrefixFamily.None));

    private static readonly VanillaItemDefinition EaterDemoniteOreDefinition =
        EaterWorldDrop(VanillaEaterOfWorldsItemIds.DemoniteOre, 12, 12);
    private static readonly VanillaItemDefinition EaterShadowScaleDefinition =
        EaterWorldDrop(VanillaEaterOfWorldsItemIds.ShadowScale, 14, 18);
    private static readonly VanillaItemDefinition EatersBoneDefinition =
        EaterWorldDrop(VanillaEaterOfWorldsItemIds.EatersBone, 16, 30);
    private static readonly VanillaItemDefinition EaterTrophyDefinition =
        EaterWorldDrop(VanillaEaterOfWorldsItemIds.EaterOfWorldsTrophy, 30, 30);
    private static readonly VanillaItemDefinition EaterMaskDefinition =
        EaterWorldDrop(VanillaEaterOfWorldsItemIds.EaterMask, 28, 20);
    private static readonly VanillaItemDefinition EaterBossBagDefinition =
        EaterWorldDrop(VanillaEaterOfWorldsItemIds.EaterOfWorldsBossBag, 24, 24);
    private static readonly VanillaItemDefinition EaterPetItemDefinition =
        EaterWorldDrop(VanillaEaterOfWorldsItemIds.EaterOfWorldsPetItem, 16, 30);
    private static readonly VanillaItemDefinition EaterMasterTrophyDefinition =
        EaterWorldDrop(VanillaEaterOfWorldsItemIds.EaterOfWorldsMasterTrophy, 14, 14);

    private static readonly VanillaItemDefinition BrainCrimtaneOreDefinition =
        BrainWorldDrop(VanillaBrainOfCthulhuItemIds.CrimtaneOre, 12, 12);
    private static readonly VanillaItemDefinition BrainTissueSampleDefinition =
        BrainWorldDrop(VanillaBrainOfCthulhuItemIds.TissueSample, 14, 18);
    private static readonly VanillaItemDefinition BrainTrophyDefinition =
        BrainWorldDrop(VanillaBrainOfCthulhuItemIds.BrainOfCthulhuTrophy, 30, 30);
    private static readonly VanillaItemDefinition BrainMaskDefinition =
        BrainWorldDrop(VanillaBrainOfCthulhuItemIds.BrainMask, 28, 20);
    private static readonly VanillaItemDefinition BrainBoneRattleDefinition =
        BrainWorldDrop(VanillaBrainOfCthulhuItemIds.BoneRattle, 16, 30);
    private static readonly VanillaItemDefinition BrainBossBagDefinition =
        BrainWorldDrop(VanillaBrainOfCthulhuItemIds.BrainOfCthulhuBossBag, 24, 24);
    private static readonly VanillaItemDefinition BrainPetItemDefinition =
        BrainWorldDrop(VanillaBrainOfCthulhuItemIds.BrainOfCthulhuPetItem, 16, 30);
    private static readonly VanillaItemDefinition BrainMasterTrophyDefinition =
        BrainWorldDrop(VanillaBrainOfCthulhuItemIds.BrainOfCthulhuMasterTrophy, 14, 14);

    private static readonly VanillaItemDefinition SkeletronHandDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.SkeletronHand, 30, 10);
    private static readonly VanillaItemDefinition SkeletronMaskDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.SkeletronMask, 28, 20);
    private static readonly VanillaItemDefinition BookOfSkullsDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.BookOfSkulls, 24, 28);
    private static readonly VanillaItemDefinition SkeletronTrophyDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.SkeletronTrophy, 30, 30);
    private static readonly VanillaItemDefinition SkeletronBossBagDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.SkeletronBossBag, 24, 24);
    private static readonly VanillaItemDefinition SkeletronPetItemDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.SkeletronPetItem, 16, 30);
    private static readonly VanillaItemDefinition SkeletronMasterTrophyDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.SkeletronMasterTrophy, 14, 14);
    private static readonly VanillaItemDefinition ChippysCouchDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.ChippysCouch, 20, 20);
    private static readonly VanillaItemDefinition ChippysHeadDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.ChippysHead, 18, 14);
    private static readonly VanillaItemDefinition ChippysBodyDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.ChippysBody, 18, 14);
    private static readonly VanillaItemDefinition ChippysLegsDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.ChippysLegs, 18, 14);
    private static readonly VanillaItemDefinition ChippysHeadbandDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.ChippysHeadband, 26, 30);
    private static readonly VanillaItemDefinition ChippysWingsInactiveDefinition = SkeletronWorldDrop(VanillaSkeletronItemIds.ChippysWingsInactive, 24, 8);

    private static VanillaItemDefinition BrainWorldDrop(ItemTypeId type, int width, int height) =>
        new(
            Type: type,
            RuntimeDefaults: new VanillaItemRuntimeDefaults(width, height, CommonMaximumStack),
            UseTiming: null,
            Placement: null,
            PickTool: null,
            WorldDrop: new VanillaItemWorldDropDefinition(
                width,
                height,
                NoGravity: false,
                PrefixFamily: VanillaItemPrefixFamily.None));

    private static VanillaItemDefinition SkeletronWorldDrop(ItemTypeId type, int width, int height) =>
        new(
            Type: type,
            RuntimeDefaults: new VanillaItemRuntimeDefaults(width, height, CommonMaximumStack),
            UseTiming: null,
            Placement: null,
            PickTool: null,
            WorldDrop: new VanillaItemWorldDropDefinition(
                width,
                height,
                NoGravity: false,
                PrefixFamily: VanillaItemPrefixFamily.None));

    private static VanillaItemDefinition EaterWorldDrop(ItemTypeId type, int width, int height) =>
        new(
            Type: type,
            RuntimeDefaults: new VanillaItemRuntimeDefaults(width, height, CommonMaximumStack),
            UseTiming: null,
            Placement: null,
            PickTool: null,
            WorldDrop: new VanillaItemWorldDropDefinition(
                width,
                height,
                NoGravity: false,
                PrefixFamily: VanillaItemPrefixFamily.None));

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

        if (type == VanillaKingSlimeItemIds.KingSlimeBossBag)
        {
            definition = KingSlimeBossBagDefinition;
            return true;
        }

        if (type == VanillaKingSlimeItemIds.KingSlimePetItem)
        {
            definition = KingSlimePetItemDefinition;
            return true;
        }

        if (type == VanillaKingSlimeItemIds.KingSlimeMasterTrophy)
        {
            definition = KingSlimeMasterTrophyDefinition;
            return true;
        }

        if (type == VanillaEaterOfWorldsItemIds.DemoniteOre)
        {
            definition = EaterDemoniteOreDefinition;
            return true;
        }
        if (type == VanillaEaterOfWorldsItemIds.ShadowScale)
        {
            definition = EaterShadowScaleDefinition;
            return true;
        }
        if (type == VanillaEaterOfWorldsItemIds.EatersBone)
        {
            definition = EatersBoneDefinition;
            return true;
        }
        if (type == VanillaEaterOfWorldsItemIds.EaterOfWorldsTrophy)
        {
            definition = EaterTrophyDefinition;
            return true;
        }
        if (type == VanillaEaterOfWorldsItemIds.EaterMask)
        {
            definition = EaterMaskDefinition;
            return true;
        }
        if (type == VanillaEaterOfWorldsItemIds.EaterOfWorldsBossBag)
        {
            definition = EaterBossBagDefinition;
            return true;
        }
        if (type == VanillaEaterOfWorldsItemIds.EaterOfWorldsPetItem)
        {
            definition = EaterPetItemDefinition;
            return true;
        }
        if (type == VanillaEaterOfWorldsItemIds.EaterOfWorldsMasterTrophy)
        {
            definition = EaterMasterTrophyDefinition;
            return true;
        }

        if (type == VanillaBrainOfCthulhuItemIds.CrimtaneOre)
        {
            definition = BrainCrimtaneOreDefinition;
            return true;
        }
        if (type == VanillaBrainOfCthulhuItemIds.TissueSample)
        {
            definition = BrainTissueSampleDefinition;
            return true;
        }
        if (type == VanillaBrainOfCthulhuItemIds.BrainOfCthulhuTrophy)
        {
            definition = BrainTrophyDefinition;
            return true;
        }
        if (type == VanillaBrainOfCthulhuItemIds.BrainMask)
        {
            definition = BrainMaskDefinition;
            return true;
        }
        if (type == VanillaBrainOfCthulhuItemIds.BoneRattle)
        {
            definition = BrainBoneRattleDefinition;
            return true;
        }
        if (type == VanillaBrainOfCthulhuItemIds.BrainOfCthulhuBossBag)
        {
            definition = BrainBossBagDefinition;
            return true;
        }
        if (type == VanillaBrainOfCthulhuItemIds.BrainOfCthulhuPetItem)
        {
            definition = BrainPetItemDefinition;
            return true;
        }
        if (type == VanillaBrainOfCthulhuItemIds.BrainOfCthulhuMasterTrophy)
        {
            definition = BrainMasterTrophyDefinition;
            return true;
        }

        if (type == VanillaSkeletronItemIds.SkeletronHand) { definition = SkeletronHandDefinition; return true; }
        if (type == VanillaSkeletronItemIds.SkeletronMask) { definition = SkeletronMaskDefinition; return true; }
        if (type == VanillaSkeletronItemIds.BookOfSkulls) { definition = BookOfSkullsDefinition; return true; }
        if (type == VanillaSkeletronItemIds.SkeletronTrophy) { definition = SkeletronTrophyDefinition; return true; }
        if (type == VanillaSkeletronItemIds.SkeletronBossBag) { definition = SkeletronBossBagDefinition; return true; }
        if (type == VanillaSkeletronItemIds.SkeletronPetItem) { definition = SkeletronPetItemDefinition; return true; }
        if (type == VanillaSkeletronItemIds.SkeletronMasterTrophy) { definition = SkeletronMasterTrophyDefinition; return true; }
        if (type == VanillaSkeletronItemIds.ChippysCouch) { definition = ChippysCouchDefinition; return true; }
        if (type == VanillaSkeletronItemIds.ChippysHead) { definition = ChippysHeadDefinition; return true; }
        if (type == VanillaSkeletronItemIds.ChippysBody) { definition = ChippysBodyDefinition; return true; }
        if (type == VanillaSkeletronItemIds.ChippysLegs) { definition = ChippysLegsDefinition; return true; }
        if (type == VanillaSkeletronItemIds.ChippysHeadband) { definition = ChippysHeadbandDefinition; return true; }
        if (type == VanillaSkeletronItemIds.ChippysWingsInactive) { definition = ChippysWingsInactiveDefinition; return true; }

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
