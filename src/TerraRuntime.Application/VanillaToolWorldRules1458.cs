using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal enum ToolWorldBreakability1458 : byte
{
    Allowed,
    FailedEffect,
    NoEffect
}

internal readonly record struct ToolWorldRemoval1458(
    bool CanKill, bool BlockDamaged, ToolWorldBreakability1458 Breakability,
    bool RequiresChestPermission, int ChestX, int ChestY)
{
    public ToolWorldReadCapture1458? Reads { get; init; }
    public bool CanPound { get; init; }
}

/// <summary>
/// Position-dependent Terraria 1.4.5.8 WorldGen.CanKillTile/CheckTileBreakability.
/// Immutable type facts come from the one source-extracted Game tile catalog.
/// Chest permission is returned as a requirement for the concrete captured owner;
/// this pure read boundary never consults a client flag or an external callback.
/// </summary>
internal static class VanillaToolWorldRules1458
{
    private const ushort ProtectedWall = 350;
    private const ushort ClosedDoor = 10;
    private const ushort Chest = 21;
    private const ushort Spikes = 48;
    private const ushort Traps = 137;
    private const ushort WoodenSpikes = 232;
    private const ushort TempleAltar = 237;
    private const ushort PlanterBox = 380;
    private const ushort TrapdoorClosed = 387;
    private const ushort TallGateClosed = 388;
    private const ushort FakeChest = 441;
    private const ushort SecondFakeChest = 468;
    private const ushort GolfHole = 476;
    private const ushort RollingCactus = 484;
    private const ushort Pylon = 597;
    private const ushort Altar = 26;
    private const ushort MushroomTree = 72;
    private const ushort Hellforge = 77;
    private const ushort Cactus = 80;
    private const ushort Dresser = 88;
    private const ushort Teleporter = 235;
    private const ushort PalmTree = 323;
    private const ushort SecondChest = 467;
    private const ushort DisplayDoll = 470;
    private const ushort HatRack = 475;
    private const ushort FallenLog = 488;
    private const int ObjectFrameCell = 18;
    private const int LockedDoorFirstFrameY = 594;
    private const int LockedDoorLastFrameY = 646;
    private const int ClosedDoorFramePeriod = 54;

    public static bool TryEvaluate(WorldTileStore tiles, int x, int y, bool hardMode,
        out ToolWorldRemoval1458 result) => TryEvaluate(tiles, null, x, y, hardMode, out result);

    public static bool TryEvaluate(WorldTileStore tiles, ToolWorldTileOverlay1458? overlay,
        int x, int y, bool hardMode, out ToolWorldRemoval1458 result)
    {
        result = default;
        if (overlay is not null && !ReferenceEquals(tiles, overlay.Tiles)) return false;
        var reads = new ToolWorldReadCapture1458(tiles, overlay);
        if (!reads.TryRead(x, y, out var target) || !target.IsActive ||
            !VanillaToolTileCatalog1458.TryGet(target.TileType, out var targetFacts))
            return false;
        WorldTile above = default;
        if (y > 0 && !reads.TryRead(x, y - 1, out above)) return false;
        bool canKill = target.Wall != ProtectedWall;
        bool blockDamaged = false;
        if (above.IsActive)
        {
            if (!VanillaToolTileCatalog1458.TryGet(above.TileType, out var aboveFacts)) return false;
            if (aboveFacts.IsTreeTrunk && above.Type != target.Type && !IsUnanchoredTreePart(in above))
                canKill = false;
            if (above.Type != target.Type && (above.Type is Chest or Altar or MushroomTree or Hellforge or Dresser or SecondChest or FallenLog ||
                (above.Type == PalmTree && above.FrameX is 66 or 220) ||
                (above.Type == Cactus && IsCactusBase(in above))))
                canKill = false;
        }

        if (targetFacts.IsBoulder)
        {
            if (!TryBoulderProtected(reads, x, y, in target, hardMode, out bool protectedBoulder)) return false;
            if (protectedBoulder) { canKill = false; blockDamaged = true; }
        }
        if (IsLockedDoor(in target)) { canKill = false; blockDamaged = true; }
        if (target.Type == Teleporter)
        {
            if (!TryTeleporterProtected(reads, x, y, in target, hardMode, out bool protectedTeleporter)) return false;
            if (protectedTeleporter) { canKill = false; blockDamaged = true; }
        }

        bool needsChest = target.Type is Chest or SecondChest or Dresser;
        int chestX = 0, chestY = 0;
        if (needsChest)
        {
            if (target.FrameX < 0 || target.FrameY < 0) return false;
            int width = target.Type == Dresser ? 3 : 2;
            chestX = x - target.FrameX / ObjectFrameCell % width;
            chestY = y - target.FrameY / ObjectFrameCell;
            if (!Contains(tiles, chestX, chestY)) return false;
        }
        if (!TryBreakability(reads, x, y, in target, in targetFacts, in above, hardMode, out var breakability))
            return false;
        if (!reads.IsCurrent) return false;
        // This owner runs only after world loading/generation has finished.
        // The source's generation-only MushroomBlock/WoodBlock exclusions do not apply.
        bool canPound = canKill && !targetFacts.IsBoulder &&
            target.Type is not (ClosedDoor or Spikes or Traps or WoodenSpikes or PlanterBox or
                TrapdoorClosed or TallGateClosed or GolfHole or RollingCactus) &&
            (!above.IsActive || !ForbidsSloping(in above));
        result = new(canKill, blockDamaged, breakability, needsChest, chestX, chestY)
        { Reads = reads, CanPound = canPound };
        return true;
    }

    private static bool TryBreakability(ToolWorldReadCapture1458 reads, int x, int y,
        in WorldTile target, in ToolTileDefinition1458 targetFacts, in WorldTile above,
        bool hardMode, out ToolWorldBreakability1458 result)
    {
        result = ToolWorldBreakability1458.Allowed;
        if (y < 1 || y >= reads.Tiles.Dimensions.HeightTiles - 1) return true;
        if (!reads.TryRead(x, y + 1, out var below)) return false;
        if (below.IsActive && IsLockedDoor(in below))
        {
            result = ToolWorldBreakability1458.NoEffect;
            return true;
        }
        if (!targetFacts.IsSolid && !targetFacts.IsSolidTop) return true;
        if (above.IsActive)
        {
            if (!TryPreventsSupportRemoval(in above, target.Type, !IsContainer(in target, in targetFacts), hardMode, out bool prevents))
                return false;
            if (prevents)
            {
                if (!VanillaToolTileCatalog1458.TryGet(above.TileType, out var aboveFacts)) return false;
                bool exemptTree = aboveFacts.IsTreeTrunk && IsUnanchoredTreePart(in above);
                bool exemptPalm = above.Type == PalmTree && above.FrameX is not 66 and not 220;
                result = exemptTree || exemptPalm ? ToolWorldBreakability1458.Allowed : ToolWorldBreakability1458.NoEffect;
                return true;
            }
            if (above.Type == Cactus && above.Type != target.Type && IsCactusBase(in above))
            {
                result = ToolWorldBreakability1458.NoEffect;
                return true;
            }
            if (IsLockedDoor(in target))
            {
                result = ToolWorldBreakability1458.FailedEffect;
                return true;
            }
            if (targetFacts.IsBoulder)
            {
                if (!TryBoulderProtected(reads, x, y, in target, hardMode, out bool protectedBoulder)) return false;
                result = protectedBoulder ? ToolWorldBreakability1458.FailedEffect : ToolWorldBreakability1458.Allowed;
                return true;
            }
        }
        if (target.Type == Teleporter)
        {
            if (!TryTeleporterProtected(reads, x, y, in target, hardMode, out bool protectedTeleporter)) return false;
            if (protectedTeleporter) result = ToolWorldBreakability1458.NoEffect;
        }
        return true;
    }

    private static bool TryBoulderProtected(ToolWorldReadCapture1458 reads, int x, int y,
        in WorldTile target, bool hardMode, out bool protectedBoulder)
    {
        protectedBoulder = false;
        if (target.FrameX < 0 || target.FrameY < 0) return false;
        int offset = -(target.FrameX / ObjectFrameCell);
        if (offset < -1) offset += 2;
        int left = x + offset;
        int top = y - target.FrameY % (ObjectFrameCell * 2) / ObjectFrameCell;
        for (int column = 0; column < 2; column++)
        {
            if (!reads.TryRead(left + column, top - 1, out var support)) return false;
            if (!TryPreventsSupportRemoval(in support, target.Type, true, hardMode, out bool prevents)) return false;
            if (prevents) { protectedBoulder = true; return true; }
        }
        return true;
    }

    private static bool TryTeleporterProtected(ToolWorldReadCapture1458 reads, int x, int y,
        in WorldTile target, bool hardMode, out bool protectedTeleporter)
    {
        protectedTeleporter = false;
        if (target.FrameX < 0) return false;
        int left = x - target.FrameX % (ObjectFrameCell * 3) / ObjectFrameCell;
        for (int column = 0; column < 3; column++)
        {
            if (!reads.TryRead(left + column, y - 1, out var support)) return false;
            if (!support.IsActive) continue;
            if (!TryPreventsSupportRemoval(in support, target.Type, true, hardMode, out bool prevents)) return false;
            if (prevents) { protectedTeleporter = true; return true; }
        }
        return true;
    }

    private static bool TryPreventsSupportRemoval(in WorldTile above, ushort ignoredType,
        bool scanContainers, bool hardMode, out bool prevents)
    {
        prevents = false;
        if (!VanillaToolTileCatalog1458.TryGet(above.TileType, out var facts)) return false;
        prevents = (above.Type != ignoredType && ((above.Type == Hellforge && !hardMode) || facts.PreventsTileRemovalAbove)) ||
            IsLockedDoor(in above) || (scanContainers && IsContainer(in above, in facts));
        return true;
    }

    private static bool IsContainer(in WorldTile tile, in ToolTileDefinition1458 facts) =>
        facts.IsBasicDresser || facts.IsBasicChest || facts.IsFakeBasicChest || tile.Type is DisplayDoll or HatRack;

    private static bool ForbidsSloping(in WorldTile tile) => tile.Type is
        Chest or Altar or Hellforge or Dresser or Teleporter or TempleAltar or FakeChest or
        SecondChest or SecondFakeChest or DisplayDoll or HatRack or FallenLog or Pylon;

    private static bool IsUnanchoredTreePart(in WorldTile tile) =>
        (tile.FrameX == 66 && tile.FrameY is >= 0 and <= 44) ||
        (tile.FrameX == 88 && tile.FrameY is >= 66 and <= 110) || tile.FrameY >= 198;

    private static bool IsCactusBase(in WorldTile tile) => tile.FrameX / ObjectFrameCell is 0 or 1 or 4 or 5;

    private static bool IsLockedDoor(in WorldTile tile) => tile.Type == ClosedDoor &&
        tile.FrameY is >= LockedDoorFirstFrameY and <= LockedDoorLastFrameY && tile.FrameX < ClosedDoorFramePeriod;

    private static bool Contains(WorldTileStore tiles, int x, int y) =>
        (uint)x < (uint)tiles.Dimensions.WidthTiles && (uint)y < (uint)tiles.Dimensions.HeightTiles;

}

/// <summary>Exact bounded source queries for one removal decision, not a mutable hit/funding owner.</summary>
internal sealed class ToolWorldReadCapture1458(WorldTileStore tiles, ToolWorldTileOverlay1458? overlay)
{
    private const int MaximumReadSections = 8;
    private readonly (WorldSectionId Section, long Version)[] sections = new (WorldSectionId, long)[MaximumReadSections];
    private int count;
    internal WorldTileStore Tiles { get; } = tiles;

    public bool IsCurrent
    {
        get
        {
            if (overlay is not null && !overlay.IsCurrentOwned) return false;
            for (int index = 0; index < count; index++)
            {
                var captured = sections[index];
                if ((captured.Version & 1L) != 0 || Tiles.GetSectionVersion(captured.Section) != captured.Version)
                    return false;
            }
            return true;
        }
    }

    internal bool TryRead(int x, int y, out WorldTile tile)
    {
        tile = default;
        if ((uint)x >= (uint)Tiles.Dimensions.WidthTiles || (uint)y >= (uint)Tiles.Dimensions.HeightTiles)
            return false;
        var section = TerrariaSectionGeometry.FromTile(Tiles.Dimensions, x, y);
        bool found = false;
        for (int index = 0; index < count; index++)
            if (sections[index].Section == section) { found = true; break; }
        if (!found)
        {
            if (count == MaximumReadSections) return false;
            sections[count++] = (section, Tiles.GetSectionVersion(section));
        }
        if (overlay is not null)
        {
            if (!overlay.TryRead(x, y, out tile)) return false;
        }
        else tile = Tiles.Get(x, y);
        return IsCurrent;
    }
}
