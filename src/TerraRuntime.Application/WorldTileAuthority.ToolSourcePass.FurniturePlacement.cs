using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct FurniturePlacementShape1458(ushort Tile, byte Width, byte Height,
    byte OriginX, byte OriginY, WorldTileEntityKind? Kind, bool NeedsWall, int Style)
{
    internal byte Alternate(int direction) => checked((byte)(Tile is 14 or 16 or 18 ? 0 : direction == 1 ? 1 : 0));
    internal short FrameBase(int direction) => checked((short)((Style + Alternate(direction)) * Width * 18));
    internal bool IsLogicSensor => Tile == 423;
    internal bool HasObjectReport => Tile is not (423 or 470 or 475);
    internal bool HasEntity => Kind is not null;
    internal static bool TryGet(in LocalPlacementDefinition1458 definition, out FurniturePlacementShape1458 shape)
    {
        shape = definition.Item.Value switch
        {
            3613 or 3614 or 3615 or 3726 or 3727 or 3728 or 3729
                when definition.CreateTile == 423 && definition.PlaceStyle is >= 0 and <= 6 =>
                    new(423, 1, 1, 0, 0, WorldTileEntityKind.LogicSensor, false, definition.PlaceStyle),
            32 when definition.CreateTile == 14 && definition.PlaceStyle == 0 => new(14, 3, 2, 1, 1, null, false, 0),
            34 when definition.CreateTile == 15 && definition.PlaceStyle == 0 => new(15, 1, 2, 0, 1, null, false, 0),
            35 when definition.CreateTile == 16 && definition.PlaceStyle == 0 => new(16, 2, 1, 0, 0, null, false, 0),
            36 when definition.CreateTile == 18 && definition.PlaceStyle == 0 => new(18, 2, 1, 0, 0, null, false, 0),
            498 when definition.CreateTile == 470 && definition.PlaceStyle == 0 => new(470, 2, 3, 0, 2, WorldTileEntityKind.DisplayDoll, false, 0),
            1989 when definition.CreateTile == 470 && definition.PlaceStyle == 2 => new(470, 2, 3, 0, 2, WorldTileEntityKind.DisplayDoll, false, 2),
            2699 when definition.CreateTile == 471 && definition.PlaceStyle == 0 => new(471, 3, 3, 1, 1, WorldTileEntityKind.WeaponsRack, true, 0),
            3977 when definition.CreateTile == 475 && definition.PlaceStyle == 0 => new(475, 3, 4, 1, 3, WorldTileEntityKind.HatRack, false, 0),
            _ => default
        };
        return shape.Tile != 0 && !definition.IsWall;
    }
}

internal sealed partial class WorldTileAuthority
{
    internal static bool SupportsFurniturePlacement(in LocalPlacementDefinition1458 definition) =>
        FurniturePlacementShape1458.TryGet(in definition, out _);

    internal sealed partial class ToolWorldSourcePass1458
    {
        // Original TileObject.CanPlace + Place: all footprint/anchor facts are
        // queried before writes. Player/NPC Collision.EmptyTile is supplied by
        // the retained complete censuses, never the packet's tile contents.
        internal bool TryPlaceFurniture(in FurniturePlacementShape1458 shape, int clickX, int clickY,
            int direction, bool sourceEmpty, out bool placed,
            out TerrariaPlaceObjectState objectReport, out WorldTile[] footprint)
        {
            placed = false; objectReport = default; footprint = [];
            if (!IsCurrentOwned || work.Count != 0 || direction is not (-1 or 1)) return Reject();
            int x = clickX - shape.OriginX, y = clickY - shape.OriginY;
            var size = Overlay.Tiles.Dimensions;
            if (x < 5 || y < 5 || x + shape.Width >= size.WidthTiles - 5 ||
                y + shape.Height >= size.HeightTiles - 5 || !sourceEmpty) return true;
            // The admitted definitions have no random style or place override,
            // and do not flatten their anchors.
            for (int dx = 0; dx < shape.Width; dx++)
            for (int dy = 0; dy < shape.Height; dy++)
            {
                if (!Overlay.TryReadSourceSafely(x + dx, y + dy, out var tile) ||
                    !VanillaToolTileCatalog1458.TryGet(tile.TileType, out var facts)) return Reject();
                if (tile.LiquidAmount > 0 && tile.LiquidKind == WorldLiquidKind.Lava ||
                    tile.IsActive && ((!facts.IsCuttable || tile.Type is 484 or 654) && !facts.BreakableWhenPlacing) ||
                    shape.NeedsWall && tile.Wall == 0) return true;
            }
            if (!shape.NeedsWall && !shape.IsLogicSensor)
                for (int dx = 0; dx < shape.Width; dx++)
                {
                    if (!Overlay.TryReadSourceSafely(x + dx, y + shape.Height, out var anchor) ||
                        !VanillaToolTileCatalog1458.TryGet(anchor.TileType, out var facts)) return Reject();
                    bool valid = anchor.IsActive && !anchor.IsActuated;
                    bool blocked = shape.HasEntity && anchor.Type is 127 or 138 or 664 or 665 or 484 or 711 or 712 or 713 or 714 or 715 or 716;
                    bool solid = facts.IsSolid && !facts.IsSolidTop && !facts.NoAttach && !blocked &&
                        ((!anchor.IsHalfBrick && anchor.SourceSlope == 0) || anchor.SourceSlope is 3 or 4);
                    bool top = facts.IsPlatform ? !anchor.IsHalfBrick && IsProperPlatform(anchor.FrameX) : facts.IsSolid && facts.IsSolidTop;
                    if (!valid || !(solid || top)) return true;
                }
            for (int dx = 0; dx < shape.Width; dx++)
            for (int dy = 0; dy < shape.Height; dy++)
            {
                int tx = x + dx, ty = y + dy;
                if (!Overlay.TryReadSourceSafely(tx, ty, out var before)) return Reject();
                if (before.IsActive && !TryKillTile(tx, ty, false, false, false, out _)) return Reject();
            }
            for (int dx = 0; dx < shape.Width; dx++)
            for (int dy = 0; dy < shape.Height; dy++)
            {
                int tx = x + dx, ty = y + dy;
                if (!Overlay.TryReadSourceSafely(tx, ty, out var before)) return Reject();
                if (before.IsActive) continue; // Original Place does not overwrite an unremoved cuttable.
                var after = before; after.Flags |= WorldTileFlags.Active; after.Type = shape.Tile;
                after.FrameX = shape.IsLogicSensor ? (short)0 : checked((short)(shape.FrameBase(direction) + dx * 18));
                after.FrameY = checked((short)((shape.IsLogicSensor ? shape.Style : dy) * 18));
                if (!Stage(WorldTileMutationKind.PlaceTile, tx, ty, in before, in after)) return Reject();
            }
            if (!PushSquare(clickX, clickY) || !Drain()) return Reject();
            footprint = new WorldTile[shape.Width * shape.Height];
            int index = 0;
            for (int dx = 0; dx < shape.Width; dx++)
            for (int dy = 0; dy < shape.Height; dy++)
                if (!Overlay.TryReadSourceSafely(x + dx, y + dy, out footprint[index++])) return Reject();
            objectReport = new(checked((short)clickX), checked((short)clickY), checked((short)shape.Tile),
                checked((short)shape.Style), shape.Alternate(direction), -1, direction == 1);
            placed = true; return IsCurrentOwned;
        }

        // Source SERVER MessageBuffer20 accepts the proven CLIENT image, then
        // RangeFrame; liquids omitted by CLIENT retain the live SERVER value.
        // No unproved raw packet can call this method through ingress.
        internal bool TryApplyFurnitureFootprint(int x, int y, byte width, byte height,
            ReadOnlySpan<WorldTile> source)
        {
            if (!IsCurrentOwned || work.Count != 0 || source.Length != width * height || source.Length > 12)
                return Reject();
            int index = 0;
            for (int dx = 0; dx < width; dx++)
            for (int dy = 0; dy < height; dy++)
            {
                if (!Overlay.TryReadSourceSafely(x + dx, y + dy, out var before)) return Reject();
                var after = source[index++]; after.LiquidAmount = before.LiquidAmount; after.LiquidKind = before.LiquidKind;
                if (!Stage(WorldTileMutationKind.PlaceTile, x + dx, y + dy, in before, in after)) return Reject();
            }
            for (int tx = x - 1; tx <= x + width + 1; tx++)
            for (int ty = y - 1; ty <= y + height + 1; ty++)
            {
                if (!Push(new(StepKind.Frame, tx, ty)) || !Drain() ||
                    !Overlay.TryReadSourceSafely(tx, ty, out var before)) return Reject();
                // Framing.WallFrame(..., resetFrame:false) has no reset draw.
                if (before.Wall == 0)
                {
                    var after = before; after.WallColor = 0;
                    after.Flags &= ~(WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall);
                    if (!before.Equals(after) && !Stage(WorldTileMutationKind.TransformTile, tx, ty, in before, in after)) return Reject();
                }
                else if (!Overlay.RecordRead(tx - 1, ty) || !Overlay.RecordRead(tx + 1, ty) ||
                    !Overlay.RecordRead(tx, ty - 1) || !Overlay.RecordRead(tx, ty + 1)) return Reject();
            }
            return IsCurrentOwned;
        }

        internal bool TryCreateFurnitureEntity(in FurniturePlacementShape1458 shape, int x, int y, out WorldTileEntity? row)
        {
            row = null;
            if (!IsCurrentOwned || shape.Kind is not { } kind || !TryFurnitureImage(out var image) || image is null ||
                !Overlay.TryReadSourceSafely(x, y, out var tile) || !tile.IsActive || tile.Type != shape.Tile) return Reject();
            if (shape.IsLogicSensor)
                return TryCreateLogicSensor(image, x, y, shape.Style, in tile, out row);
            if (tile.FrameY != 0 || tile.FrameX < 0 || tile.FrameX % (shape.Width * 18) != 0 ||
                !image.TryStageEmptyPlacement(x, y, kind, out int id) ||
                !image.TryGetAt(x, y, out row) || row is null || row.PersistedId != id) return Reject();
            return IsCurrentOwned;
        }

        private bool TryCreateLogicSensor(RuntimeWorldFurnitureStore1458.Preparation image,
            int x, int y, int style, in WorldTile tile, out WorldTileEntity? row)
        {
            row = null;
            if (clientRole || style is < 0 or > 6 || tile.FrameY != style * 18 || tile.FrameX is not (0 or 18))
                return Reject();
            byte check = checked((byte)(style + 1));
            var state = new WorldLogicSensorPayload(check, owner.IsLogicSensorOnAtPlacement(x, y, check));
            if (!image.TryStageLogicSensorPlacement(x, y, state, out int id) ||
                !image.TryGetAt(x, y, out row) || row is null || row.PersistedId != id) return Reject();
            var after = tile;
            after.FrameX = state.IsOn ? (short)18 : (short)0;
            return Stage(WorldTileMutationKind.TransformTile, x, y, in tile, in after) && IsCurrentOwned;
        }
    }
}
