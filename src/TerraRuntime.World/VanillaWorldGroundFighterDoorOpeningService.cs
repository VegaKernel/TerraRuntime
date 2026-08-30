using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public interface IVanillaTallGateOccupancyProbe
{
    bool IsActorFree(int tileX, int tileY);
}

public enum VanillaGroundFighterDoorOpeningKind : byte
{
    Door = 1,
    TallGate = 2
}

public readonly record struct VanillaGroundFighterDoorOpeningMutation(
    VanillaGroundFighterDoorOpeningKind Kind,
    int PacketTileX,
    int PacketTileY,
    int DirectionX,
    int ChangedTiles);

/// <summary>
/// Object-specific TerrariaServer 1.4.5.8 WorldGen.OpenDoor / ShiftTallGate(opening) mutation.
/// Normal doors reproduce the source 1x3 -> 2x3 frame/style transform, locked-Dungeon-door rejection,
/// row block-paint/coating transfer and destination tile-cut rules. Tall gates reproduce the 1x5 388 -> 389
/// type shift while preserving frames; their source Collision.EmptyTile(ignoreTiles:true) actor check is an
/// explicit boundary and therefore fails closed when no occupancy probe is supplied.
/// </summary>
public sealed class VanillaWorldGroundFighterDoorOpeningService : IVanillaGroundFighterDoorOpeningSink
{
    private const ushort StalactiteTileType = 165;
    private const int TallGateHeight = 5;
    private const int TallGateCoordinateFullHeight = 90;
    private const int FrameUnit = 18;
    private const int ClosedDoorStyleHeight = 54;
    private const int ClosedDoorHorizontalStyleWidth = 54;
    private const int OpenDoorHorizontalStyleWidth = 72;
    private const int DoorStyleCountPerHorizontalBand = 36;
    private const int LockedDoorMinimumFrameY = 594;
    private const int LockedDoorMaximumFrameY = 646;
    private const int LockedDoorMaximumFrameXExclusive = 54;

    private static readonly ushort[] DripTileTypes = [373, 374, 375, 461, 709];
    private const WorldTileFlags BlockCoatingFlags =
        WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock;

    private readonly WorldTileStore tiles;
    private readonly IVanillaTallGateOccupancyProbe? tallGateOccupancy;

    public VanillaWorldGroundFighterDoorOpeningService(
        WorldTileStore tiles,
        IVanillaTallGateOccupancyProbe? tallGateOccupancy = null)
    {
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        this.tallGateOccupancy = tallGateOccupancy;
    }

    public bool TryOpen(in VanillaGroundFighterDoorOpeningIntent intent) =>
        TryOpen(in intent, out _);

    public bool TryOpen(
        in VanillaGroundFighterDoorOpeningIntent intent,
        out VanillaGroundFighterDoorOpeningMutation mutation)
    {
        mutation = default;
        if (!intent.IsValid || !Contains(intent.TileX, intent.TileY))
            return false;

        WorldTile touched = tiles.Get(intent.TileX, intent.TileY);
        if (!touched.IsActive || touched.TileType != intent.ClosedType)
            return false;

        if (intent.ClosedType == VanillaTileIds.ClosedDoor)
            return TryOpenDoor(in intent, in touched, out mutation);
        if (intent.ClosedType == VanillaTileIds.TallGateClosed)
            return TryOpenTallGate(in intent, in touched, out mutation);

        return false;
    }

    private bool TryOpenDoor(
        in VanillaGroundFighterDoorOpeningIntent intent,
        in WorldTile touched,
        out VanillaGroundFighterDoorOpeningMutation mutation)
    {
        mutation = default;
        if (IsLockedDoor(in touched) || touched.FrameX < 0 || touched.FrameY < 0)
            return false;

        int residualFrameY = touched.FrameY;
        int style = 0;
        while (residualFrameY >= ClosedDoorStyleHeight)
        {
            residualFrameY -= ClosedDoorStyleHeight;
            style++;
        }

        int openFrameX = 0;
        if (touched.FrameX >= ClosedDoorHorizontalStyleWidth)
        {
            int horizontalStyle = touched.FrameX / ClosedDoorHorizontalStyleWidth;
            style += DoorStyleCountPerHorizontalBand * horizontalStyle;
            openFrameX = OpenDoorHorizontalStyleWidth * horizontalStyle;
        }

        if (intent.DirectionX == -1)
            openFrameX += 36;
        if (openFrameX > short.MaxValue)
            return false;

        int topY = intent.TileY - residualFrameY / FrameUnit;
        int leftX = intent.DirectionX == -1 ? intent.TileX - 1 : intent.TileX;
        int cutX = intent.DirectionX == -1 ? intent.TileX - 1 : intent.TileX + 1;
        if (!Contains(leftX, topY) || !Contains(leftX + 1, topY + 2))
            return false;

        WorldTile[] sourceRows = new WorldTile[3];
        for (int row = 0; row < 3; row++)
        {
            WorldTile source = tiles.Get(intent.TileX, topY + row);
            if (!source.IsActive || source.TileType != VanillaTileIds.ClosedDoor)
                return false;
            sourceRows[row] = source;
        }

        for (int row = 0; row < 3; row++)
        {
            WorldTile destination = tiles.Get(cutX, topY + row);
            if (destination.IsActive && !CanDoorCut(in destination))
                return false;
        }

        for (int row = 0; row < 3; row++)
        {
            WorldTile destination = tiles.Get(cutX, topY + row);
            if (destination.IsActive)
            {
                ClearCutTile(ref destination);
                tiles.Set(cutX, topY + row, in destination);
            }
        }

        int openFrameY = (style % DoorStyleCountPerHorizontalBand) * ClosedDoorStyleHeight;
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 2; column++)
            {
                int x = leftX + column;
                int y = topY + row;
                WorldTile destination = tiles.Get(x, y);
                destination.Type = checked((ushort)VanillaTileIds.OpenDoor.Value);
                destination.Flags |= WorldTileFlags.Active;
                destination.FrameX = checked((short)(openFrameX + column * FrameUnit));
                destination.FrameY = checked((short)(openFrameY + row * FrameUnit));
                CopyBlockColorAndCoating(in sourceRows[row], ref destination);
                tiles.Set(x, y, in destination);
            }
        }

        mutation = new VanillaGroundFighterDoorOpeningMutation(
            VanillaGroundFighterDoorOpeningKind.Door,
            intent.TileX,
            intent.TileY,
            intent.DirectionX,
            ChangedTiles: 6);
        return true;
    }

    private bool TryOpenTallGate(
        in VanillaGroundFighterDoorOpeningIntent intent,
        in WorldTile touched,
        out VanillaGroundFighterDoorOpeningMutation mutation)
    {
        mutation = default;
        if (tallGateOccupancy is null || touched.FrameY < 0)
            return false;

        int frameWithinStyle = touched.FrameY % TallGateCoordinateFullHeight;
        if (frameWithinStyle % FrameUnit != 0)
            return false;

        int row = frameWithinStyle / FrameUnit;
        if ((uint)row >= TallGateHeight)
            return false;

        int topY = intent.TileY - row;
        if (!Contains(intent.TileX, topY) || !Contains(intent.TileX, topY + TallGateHeight - 1))
            return false;

        for (int offset = 0; offset < TallGateHeight; offset++)
        {
            WorldTile gate = tiles.Get(intent.TileX, topY + offset);
            if (!gate.IsActive || gate.TileType != VanillaTileIds.TallGateClosed)
                return false;
            if (!tallGateOccupancy.IsActorFree(intent.TileX, topY + offset))
                return false;
        }

        for (int offset = 0; offset < TallGateHeight; offset++)
        {
            WorldTile gate = tiles.Get(intent.TileX, topY + offset);
            gate.Type = checked((ushort)VanillaTileIds.TallGateOpen.Value);
            tiles.Set(intent.TileX, topY + offset, in gate);
        }

        mutation = new VanillaGroundFighterDoorOpeningMutation(
            VanillaGroundFighterDoorOpeningKind.TallGate,
            intent.TileX,
            intent.TileY,
            intent.DirectionX,
            ChangedTiles: TallGateHeight);
        return true;
    }

    private static bool IsLockedDoor(in WorldTile tile) =>
        tile.TileType == VanillaTileIds.ClosedDoor &&
        tile.FrameY is >= LockedDoorMinimumFrameY and <= LockedDoorMaximumFrameY &&
        tile.FrameX < LockedDoorMaximumFrameXExclusive;

    private static bool CanDoorCut(in WorldTile tile)
    {
        if (!tile.IsActive)
            return true;

        ushort type = tile.Type;
        return VanillaProjectileTileCutFacts.IsCuttable(tile.TileType) ||
               type == StalactiteTileType ||
               Array.BinarySearch(DripTileTypes, type) >= 0;
    }

    private static void ClearCutTile(ref WorldTile tile)
    {
        tile.Type = 0;
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.TileColor = 0;
        tile.Shape = 0;
        tile.Flags &= ~(
            WorldTileFlags.Active |
            WorldTileFlags.Inactive |
            WorldTileFlags.InvisibleBlock |
            WorldTileFlags.FullbrightBlock);
    }

    private static void CopyBlockColorAndCoating(in WorldTile source, ref WorldTile destination)
    {
        destination.TileColor = source.TileColor;
        destination.Flags =
            (destination.Flags & ~BlockCoatingFlags) |
            (source.Flags & BlockCoatingFlags);
    }

    private bool Contains(int x, int y) =>
        (uint)x < (uint)tiles.Dimensions.WidthTiles &&
        (uint)y < (uint)tiles.Dimensions.HeightTiles;
}
