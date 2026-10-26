using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        // One source WorldGen.destroyObject value spans every represented object family.
        private bool IsSourceDestroyingObject => destroyingFurniture is not null || plantDestroying || saplingDestroying || larvaDestroying || container?.Destroying == true;
        private RuntimeWorldFurnitureStore1458.Preparation? furnitureImage;
        private bool furniturePrefixUsed;
        private bool? furnitureWindows;
        private VanillaItemPrefixWorld1458? furniturePrefixWorld;
        private bool IsFurnitureSourceCurrent => (furnitureImage is null || furnitureImage.IsCurrentOwned) &&
            (!furniturePrefixUsed || furnitureWindows == owner.players.PickupWindowsArithmetic && furniturePrefixWorld == owner.players.PickupWorld);
        private static bool IsExceptionalFurniture(ushort type) => type is 128 or 269 or 334 or 470 or 471 or 475;

        private bool TryFurnitureImage(out RuntimeWorldFurnitureStore1458.Preparation? image)
        {
            image = null;
            if (furnitureImage is null)
            {
                if (server is not null)
                {
                    if (!server.TryGetFurniture(out furnitureImage)) return false;
                }
                else if (Overlay.Tiles.RuntimeTileEntities is not { } metadata ||
                    !metadata.TryPrepare(ToolWorldTileOverlay1458.MaximumChangedCells, out furnitureImage)) return false;
            }
            image = furnitureImage;
            return image is not null && image.IsCurrentOwned;
        }

        private static bool CanonicalStock(WorldTileEntityItem? item) => item is not { Stack: > 0 } value ||
            value.TryGetItemType(out var type) && VanillaFurnitureItemFacts1458.IsCanonicalItemOrAir(type);
        private static bool HasCanonicalStoredIdentities(WorldTileEntityPayload payload) => payload switch
        {
            WorldDisplayDollPayload doll => doll.Equipment.All(CanonicalStock) && doll.Dyes.All(CanonicalStock) && CanonicalStock(doll.Misc),
            WorldHatRackPayload hats => hats.Items.All(CanonicalStock) && hats.Dyes.All(CanonicalStock),
            WorldItemTileEntityPayload rack => CanonicalStock(rack.Item),
            _ => false
        };

        private static bool HasItem(WorldTileEntityItem? item) => item is { Type: > 0, Stack: > 0 };
        private static bool ContainsItems(WorldTileEntityPayload payload) => payload switch
        {
            WorldDisplayDollPayload doll => doll.Equipment.Any(HasItem) || doll.Dyes.Any(HasItem) || HasItem(doll.Misc),
            WorldHatRackPayload hats => hats.Items.Any(HasItem) || hats.Dyes.Any(HasItem),
            _ => false
        };

        private static bool TryFurnitureAnchor(int x, int y, in WorldTile tile, out int anchorX, out int anchorY,
            out int width, out int height)
        {
            width = tile.Type is 128 or 269 or 470 ? 2 : 3;
            height = tile.Type == 475 ? 4 : 3;
            anchorX = x; anchorY = y;
            if (tile.FrameX < 0 || tile.FrameY < 0) return false;
            int frame = tile.FrameX;
            if (tile.Type is 128 or 269) frame = frame % 100 % 36;
            else if (tile.Type == 334) frame = frame < 5000 ? frame % 54 : (frame / 5000 - 1) * 18 % 54;
            else frame %= width * 18;
            anchorX -= frame / 18;
            anchorY -= tile.FrameY / 18 % height;
            return true;
        }

        private bool TryGetFurnitureAt(int x, int y, in WorldTile tile, out int ax, out int ay,
            out WorldTileEntity? row)
        {
            row = null; ax = x; ay = y;
            if (!TryFurnitureImage(out var image)) return false;
            if (tile.Type is 470 or 475)
            {
                // IsBreakable/TryChangePose use the LEFT cell's complete frameY.
                // Framing_CheckTile has a different modulo-height anchor below.
                ax = tile.Type == 470 ? x - (tile.FrameX % 36 != 0 ? 1 : 0) : x - tile.FrameX % 54 / 18;
                if (!Overlay.TryReadSourceSafely(ax, y, out var left)) return false;
                ay = y - left.FrameY / 18;
            }
            else if (!TryFurnitureAnchor(x, y, in tile, out ax, out ay, out _, out _)) return false;
            var dimensions = Overlay.Tiles.Dimensions;
            // Complete canonical import proves no entity exists outside its
            // validated coordinates; there is no terrain read at this anchor.
            if ((uint)ax >= (uint)dimensions.WidthTiles || (uint)ay >= (uint)dimensions.HeightTiles) return image!.IsCurrentOwned;
            if (!Overlay.RecordRead(ax, ay) || !image!.TryGetAt(ax, ay, out row)) return false;
            return row is null || HasCanonicalStoredIdentities(row.Payload);
        }

        internal bool TryChangeDisplayDollPose(int x, int y, out int id, out byte pose)
        {
            id = -1; pose = 0;
            if (!IsCurrentOwned || !Overlay.TryRead(x, y, out var tile) || !tile.IsActive || tile.Type != 470 ||
                !TryGetFurnitureAt(x, y, in tile, out int ax, out int ay, out var row)) return Reject();
            if (row is null) return true; // source TryChangePose returns false with no packet
            if (row.Kind != WorldTileEntityKind.DisplayDoll || row.Payload is not WorldDisplayDollPayload doll) return Reject();
            var misc = doll.Misc.GetValueOrDefault();
            if (!misc.TryGetItemType(out var item) ||
                !VanillaFurnitureItemFacts1458.TryGetNextDollPose(doll.Pose, item, misc.Stack, out pose) ||
                !furnitureImage!.TryStageDisplayDollPose(ax, ay, row.PersistedId, doll.Pose, pose)) return Reject();
            id = row.PersistedId;
            return IsCurrentOwned;
        }

        private bool TryFurnitureBeforeDust(in SourceStep step, ref WorldTile tile, out bool stop)
        {
            stop = false;
            if (tile.Type is 128 or 269)
            {
                int column = tile.FrameX % 100 % 36 / 18;
                int x = step.X - column;
                if (tile.FrameX < 0 || tile.FrameY < 0 || column is < 0 or > 1 || !Overlay.TryRead(x, step.Y, out var left)) return false;
                int armor = left.FrameX / 100;
                if (armor > 0)
                {
                    if (!VanillaFurnitureItemFacts1458.TryGetLegacyArmor(left.FrameY / 18, armor, out var item) ||
                        !TryFurnitureItem(item, 0, step.X * 16, step.Y * 16, 16, 16)) return false;
                    var after = left; after.FrameX = (short)(left.FrameX % 100);
                    if (!Stage(WorldTileMutationKind.TransformTile, x, step.Y, in left, in after) ||
                        !Overlay.TryRead(step.X, step.Y, out tile)) return false;
                }
            }
            else if (tile.Type == 334)
            {
                if (!TryFurnitureAnchor(step.X, step.Y, in tile, out int x, out _, out _, out _) ||
                    !Overlay.TryRead(x, step.Y, out var left) || !Overlay.TryRead(x + 1, step.Y, out var middle)) return false;
                if (left.FrameX >= 5000)
                {
                    int item = left.FrameX % 5000 - 100;
                    int prefix = middle.FrameX - (middle.FrameX < 25000 ? 10000 : 25000);
                    if (!VanillaItemIds.TryCreate(item, out var type) || (uint)prefix >= 98 ||
                        !clientRole && !TryFurnitureItem(type, (byte)prefix, step.X * 16, step.Y * 16, 16, 16)) return false;
                    var firstAfter = left; firstAfter.FrameX = (short)((left.FrameX / 5000 - 1) * 18);
                    var secondAfter = middle; secondAfter.FrameX = (short)(firstAfter.FrameX + 18);
                    if (!Stage(WorldTileMutationKind.TransformTile, x, step.Y, in left, in firstAfter) ||
                        !Stage(WorldTileMutationKind.TransformTile, x + 1, step.Y, in middle, in secondAfter) ||
                        !Overlay.TryRead(step.X, step.Y, out tile)) return false;
                }
            }
            else if (tile.Type == 471)
            {
                if (!TryGetFurnitureAt(step.X, step.Y, in tile, out int ax, out int ay, out var row)) return false;
                if (row is not null && (row.Kind != WorldTileEntityKind.WeaponsRack || row.Payload is not WorldItemTileEntityPayload)) return false;
                if (row?.Payload is WorldItemTileEntityPayload rack && rack.Item.Stack > 0)
                {
                    if (!TryDropRack(ax, ay, rack.Item)) return false;
                    stop = true;
                }
            }
            else if (tile.Type is 470 or 475)
            {
                if (!TryGetFurnitureAt(step.X, step.Y, in tile, out _, out _, out var row)) return false;
                if (row is not null && (tile.Type == 470 ? row.Payload is not WorldDisplayDollPayload : row.Payload is not WorldHatRackPayload)) return false;
                stop = step.Fail || !clientRole && row is not null && ContainsItems(row.Payload);
            }
            return IsCurrentOwned;
        }

        private bool TryDropRack(int x, int y, in WorldTileEntityItem item)
        {
            if (!item.TryGetItemType(out var type) ||
                !clientRole && !TryFurnitureItem(type, item.Prefix, x * 16, y * 16, 32, 32)) return false;
            return furnitureImage!.TryStageWeaponsRackItemClear(x, y, in item);
        }

        private bool TryFurnitureItem(ItemTypeId item, byte prefix, int x, int y, int width, int height)
        {
            if (item.IsNone || !VanillaFurnitureItemFacts1458.IsCanonicalItemOrAir(item)) return false; // corrupt/noncanonical Air payload is not an admitted physical item producer
            bool windows = false;
            if (prefix != 0)
            {
                if (owner.players.PickupWindowsArithmetic is not { } mode) return false;
                windows = mode;
                if (!furniturePrefixUsed)
                { furniturePrefixUsed = true; furnitureWindows = mode; furniturePrefixWorld = owner.players.PickupWorld; }
            }
            if (VanillaItemPrefixNormalization1458.Resolve(item, new(prefix), random.Next, windows,
                    out var normalized, furniturePrefixWorld) != VanillaItemPrefixNormalizationStatus1458.Resolved) return false;
            // CLIENT NewItem still materializes the sentinel400 and takes these
            // two draws; only TEWeaponsRack.DropItem suppresses that whole call.
            float vx = random.Next(-30, 31) * .1f;
            float vy = (VanillaFurnitureItemFacts1458.IsNoGravity(item) ? random.Next(-30, 31) : random.Next(-40, -15)) * .1f;
            if (clientRole) return true;
            var drop = new WorldItemDropStateUpdate(x + width / 2 - 8, y + height / 2 - 8, vx, vy,
                1, checked((byte)normalized.Prefix.Value), WorldItemOwnershipMode.None, checked((short)item.Value), false, 0, 0);
            return server is not null && server.TryStageDrop(in drop);
        }

        private static bool IsProperPlatform(short frameX)
        {
            int frame = frameX / 18;
            return frame is >= 0 and <= 7 or >= 12 and <= 16 or >= 25 and <= 26;
        }

        private sealed record FurnitureDestruction(int X, int Y, int Width, int Height, ushort Type,
            int DropX, int DropY, int DropWidth, int DropHeight, ushort Drop, bool DropAfter);
        private FurnitureDestruction? destroyingFurniture;

        private bool FrameExceptionalFurniture(int x, int y, in WorldTile tile)
        {
            if (IsSourceDestroyingObject) return true;
            if (!TryFurnitureAnchor(x, y, in tile, out int ax, out int ay, out int width, out int height)) return false;
            bool broken = false;
            for (int dx = 0; dx < width; dx++)
                for (int dy = 0; dy < height; dy++)
                {
                    if (!Overlay.TryRead(ax + dx, ay + dy, out var cell)) return false;
                    broken |= !cell.IsActive || cell.Type != tile.Type;
                    if (tile.Type is 334 or 471) broken |= cell.Wall == 0;
                    if (tile.Type is 128 or 269 or 334)
                    {
                        int fx = tile.Type == 334 ? cell.FrameX < 5000 ? cell.FrameX % 54 : (cell.FrameX / 5000 - 1) * 18 % 54 : cell.FrameX % 100 % 36;
                        broken |= cell.FrameY != dy * 18 || fx != dx * 18;
                    }
                }
            if (tile.Type is not (334 or 471))
                for (int dx = 0; dx < width; dx++)
                {
                    if (!Overlay.TryRead(ax + dx, ay + height, out var support) ||
                        !TerraRuntime.Gameplay.Tiles.VanillaToolTileCatalog1458.TryGet(support.TileType, out var role)) return false;
                    broken |= !support.IsActive || (support.Flags & WorldTileFlags.Inactive) != 0 || !(role.IsSolid || role.IsSolidTop) || support.IsHalfBrick ||
                        support.SourceSlope is 1 or 2 && !(role.IsPlatform && IsProperPlatform(support.FrameX));
                }
            if (!broken) return true;
            WorldTileEntityKind? kind = tile.Type switch { 470 => WorldTileEntityKind.DisplayDoll, 475 => WorldTileEntityKind.HatRack, 334 or 471 => WorldTileEntityKind.WeaponsRack, _ => null };
            if (kind is { } expected)
            {
                if (!TryFurnitureImage(out var image) || !image!.TryGetAt(ax, ay, out var row)) return false;
                if (row is not null && row.Kind != expected) return false;
                if (row?.Payload is WorldItemTileEntityPayload rack && rack.Item.Stack > 0 && !TryDropRack(ax, ay, rack.Item)) return false;
                if (!image.TryStageRemoval(ax, ay, expected)) return false;
            }
            ushort drop = tile.Type switch { 128 => 498, 269 => 1989, 470 => (ushort)(tile.FrameX / 72 == 1 ? 1989 : 498), 475 => 3977, _ => 2699 };
            bool legacy = tile.Type is 128 or 269 or 334;
            int dw = tile.Type is 128 or 269 ? 32 : width * 16;
            int dh = tile.Type is 128 or 269 ? 32 : height * 16;
            var destruction = new FurnitureDestruction(ax, ay, width, height, tile.Type,
                (legacy ? x : ax) * 16, (legacy ? y : ay) * 16, dw, dh, drop, tile.Type == 471);
            destroyingFurniture = destruction;
            if (!destruction.DropAfter && !TryFurnitureItem(new(drop), 0, destruction.DropX, destruction.DropY, dw, dh)) return false;
            return Push(new(StepKind.FurnitureNextCell, ax, ay) { Index = 0 });
        }

        private bool FurnitureNextCell(int index)
        {
            var context = destroyingFurniture;
            if (context is null) return false;
            if (index == context.Width * context.Height)
            {
                if (context.DropAfter && !TryFurnitureItem(new(context.Drop), 0, context.DropX, context.DropY, context.DropWidth, context.DropHeight)) return false;
                destroyingFurniture = null;
                if (context.Type == 14)
                    for (int dx = 3; dx >= -1; dx--)
                    for (int dy = 3; dy >= -1; dy--)
                        if (!Push(new(StepKind.Frame, context.X + dx, context.Y + dy))) return false;
                return true;
            }
            int x = context.X + index / context.Height, y = context.Y + index % context.Height;
            if (!Overlay.TryRead(x, y, out var tile) || !Push(new(StepKind.FurnitureNextCell, context.X, context.Y) { Index = index + 1 })) return false;
            return !tile.IsActive || tile.Type != context.Type || Push(new(StepKind.Kill, x, y));
        }
    }
}
