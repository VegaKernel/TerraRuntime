using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Selects the authoritative mutation path for one vanilla tile identity. This is type semantics, not per-cell
/// state: millions of WorldTile instances share one immutable definition.
/// </summary>
public enum VanillaTileBreakPath : byte
{
    SimpleCell = 0,
    FrameImportant = 1,
    MultiTileObject = 2,
    Unbreakable = 3,
    FrameImportantSingleCell = 4
}

/// <summary>How TerrariaServer 1.4.5.8 resolves an item drop for a tile identity.</summary>
public enum VanillaTileDropRuleKind : byte
{
    None = 0,
    Fixed = 1,
    Contextual = 2,
    Object = 3
}

/// <summary>
/// Context-sensitive simple-cell drop family. This stays on the immutable tile definition so runtime authority does
/// not branch on raw TileID values. Frame-important and multi-tile identities use their own object/frame paths.
/// </summary>
public enum VanillaTileContextualDropKind : byte
{
    None = 0,
    CordageVine = 1,
    MushroomVine = 2,
    Hive = 3
}

/// <summary>
/// Pick-power family. Position/world-state gates stay in the mining policy while the tile definition only names
/// which source-backed family applies.
/// </summary>
public enum VanillaTileMiningProfile : byte
{
    Standard = 0,
    EvilStone = 1,
    Meteorite = 2,
    Obsidian = 3,
    Hellstone = 4,
    DemoniteCrimtaneDepthSensitive = 5,
    HellforgeDepthSensitive = 6,
    DungeonBrick = 7,
    CobaltTier = 8,
    MythrilTier = 9,
    AdamantiteTier = 10,
    Chlorophyte = 11,
    LihzahrdTemple = 12,
    Unbreakable = 13
}

/// <summary>
/// Immutable tile-drop rule. Fixed rules are complete and can be materialized directly. Contextual rules require
/// frame/style/random/world-state logic. Object rules are resolved by the multi-tile object path.
/// </summary>
public readonly record struct VanillaTileDropRule(
    VanillaTileDropRuleKind Kind,
    ItemTypeId PrimaryItem,
    ushort PrimaryStack,
    ItemTypeId SecondaryItem,
    ushort SecondaryStack,
    bool NoPrefix)
{
    public bool HasAnyDrop => Kind != VanillaTileDropRuleKind.None;
    public bool IsDirectlyMaterializable => Kind is VanillaTileDropRuleKind.None or VanillaTileDropRuleKind.Fixed;
}

/// <summary>
/// Flyweight TerrariaServer 1.4.5.8 tile definition. WorldTile deliberately stores only mutable cell state;
/// invariant vanilla semantics live once here instead of being duplicated for every cell in the world.
/// </summary>
public sealed record VanillaTileDefinition(
    TileTypeId Type,
    bool IsSolid,
    bool IsSolidTop,
    bool IsFrameImportant,
    bool CarriesContainerMetadata,
    bool CarriesSignMetadata,
    VanillaTileBreakPath BreakPath,
    VanillaTileMiningProfile MiningProfile,
    VanillaTileDropRule DropRule,
    VanillaTileContextualDropKind ContextualDropKind,
    TileTypeId? FailedPickTransformTarget)
{
    public bool IsBreakableByPick => BreakPath != VanillaTileBreakPath.Unbreakable;
    public bool HasAnyDrop => DropRule.HasAnyDrop;
    public bool TransformsOnFailedPick => FailedPickTransformTarget.HasValue;
}

/// <summary>
/// Typed flyweight definition table for every vanilla 1.4.5.8 tile identity. Runtime code consumes capabilities
/// from this table and does not maintain parallel allow-lists of raw tile IDs.
/// </summary>
public static class VanillaTileDefinitionCatalog
{
    public const int Count = VanillaTileIds.Count;

    private static readonly VanillaTileDefinition[] Definitions = BuildDefinitions();

    public static bool TryGet(TileTypeId type, out VanillaTileDefinition definition)
    {
        int value = type.Value;
        if ((uint)value >= (uint)Definitions.Length)
        {
            definition = null!;
            return false;
        }

        definition = Definitions[value];
        return true;
    }

    public static VanillaTileDefinition Get(TileTypeId type)
    {
        if (!TryGet(type, out VanillaTileDefinition definition))
            throw new ArgumentOutOfRangeException(nameof(type));
        return definition;
    }

    private static VanillaTileDefinition[] BuildDefinitions()
    {
        var definitions = new VanillaTileDefinition[Count];
        for (int raw = 0; raw < definitions.Length; raw++)
        {
            var type = new TileTypeId(raw);
            bool frameImportant = VanillaWorldFrameImportance326.IsFrameImportant(raw);
            bool multiTile = VanillaMultiTileObjectCatalog.TryGet(type, out _);
            VanillaTileMiningProfile miningProfile = GetMiningProfile(type);
            VanillaTileBreakPath breakPath = miningProfile == VanillaTileMiningProfile.Unbreakable
                ? VanillaTileBreakPath.Unbreakable
                : multiTile
                    ? VanillaTileBreakPath.MultiTileObject
                    : frameImportant && VanillaFrameImportantSingleCellCatalog1458.IsSupported(type)
                        ? VanillaTileBreakPath.FrameImportantSingleCell
                        : frameImportant
                            ? VanillaTileBreakPath.FrameImportant
                            : VanillaTileBreakPath.SimpleCell;

            VanillaTileDropRule dropRule = multiTile
                ? new VanillaTileDropRule(VanillaTileDropRuleKind.Object, default, 0, default, 0, false)
                : VanillaTileDropRuleData1458.Get(type);

            definitions[raw] = new VanillaTileDefinition(
                type,
                VanillaTileCollisionCatalog.IsSolid(type),
                VanillaTileCollisionCatalog.IsSolidTop(type),
                frameImportant,
                VanillaTileIds.IsChestAnchor(type),
                VanillaTileIds.CarriesSignText(type),
                breakPath,
                miningProfile,
                dropRule,
                GetContextualDropKind(type, dropRule),
                VanillaTileFailedPickTransformData1458.GetTarget(type));
        }

        return definitions;
    }


    private static VanillaTileContextualDropKind GetContextualDropKind(
        TileTypeId type,
        VanillaTileDropRule dropRule)
    {
        if (dropRule.Kind != VanillaTileDropRuleKind.Contextual)
            return VanillaTileContextualDropKind.None;

        if (type == VanillaTileIds.Vines ||
            type == VanillaTileIds.JungleVines ||
            type == VanillaTileIds.VineFlowers)
        {
            return VanillaTileContextualDropKind.CordageVine;
        }

        if (type == VanillaTileIds.MushroomVines)
            return VanillaTileContextualDropKind.MushroomVine;
        if (type == VanillaTileIds.Hive)
            return VanillaTileContextualDropKind.Hive;

        // Every remaining contextual identity in 1.4.5.8 is frame-important and is intentionally resolved by the
        // frame/object path. Keeping None here prevents a simple-cell fallback from silently approximating it.
        return VanillaTileContextualDropKind.None;
    }

    private static VanillaTileMiningProfile GetMiningProfile(TileTypeId type)
    {
        if (type == VanillaTileIds.MysticSnakeRope)
            return VanillaTileMiningProfile.Unbreakable;
        if (type == VanillaTileIds.Ebonstone || type == VanillaTileIds.Pearlstone || type == VanillaTileIds.Crimstone)
            return VanillaTileMiningProfile.EvilStone;
        if (type == VanillaTileIds.Meteorite)
            return VanillaTileMiningProfile.Meteorite;
        if (type == VanillaTileIds.Obsidian)
            return VanillaTileMiningProfile.Obsidian;
        if (type == VanillaTileIds.Hellstone)
            return VanillaTileMiningProfile.Hellstone;
        if (type == VanillaTileIds.Demonite || type == VanillaTileIds.Crimtane)
            return VanillaTileMiningProfile.DemoniteCrimtaneDepthSensitive;
        if (type == VanillaTileIds.Hellforge)
            return VanillaTileMiningProfile.HellforgeDepthSensitive;
        if (type == VanillaTileIds.BlueDungeonBrick ||
            type == VanillaTileIds.GreenDungeonBrick ||
            type == VanillaTileIds.PinkDungeonBrick ||
            type == VanillaTileIds.AncientBlueBrick ||
            type == VanillaTileIds.AncientGreenBrick ||
            type == VanillaTileIds.AncientPinkBrick)
        {
            return VanillaTileMiningProfile.DungeonBrick;
        }
        if (type == VanillaTileIds.Cobalt || type == VanillaTileIds.Palladium)
            return VanillaTileMiningProfile.CobaltTier;
        if (type == VanillaTileIds.Mythril || type == VanillaTileIds.Orichalcum)
            return VanillaTileMiningProfile.MythrilTier;
        if (type == VanillaTileIds.Adamantite || type == VanillaTileIds.Titanium)
            return VanillaTileMiningProfile.AdamantiteTier;
        if (type == VanillaTileIds.Chlorophyte)
            return VanillaTileMiningProfile.Chlorophyte;
        if (type == VanillaTileIds.LihzahrdBrick || type == VanillaTileIds.LihzahrdAltar)
            return VanillaTileMiningProfile.LihzahrdTemple;
        return VanillaTileMiningProfile.Standard;
    }
}
