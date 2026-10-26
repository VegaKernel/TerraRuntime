using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        private sealed class ContainerContext
        {
            internal readonly VanillaMultiTileObjectMutationDescriptor Descriptor;
            internal readonly ItemTypeId Item;
            internal ContainerContext(in VanillaMultiTileObjectMutationDescriptor descriptor, ItemTypeId item)
            { Descriptor = descriptor; Item = item; }
            internal bool Destroying;
            internal int DropX;
            internal int DropY;
        }
        private readonly List<RuntimeChestStore.SourceContainerPermission> containerPermissions = [];
        private bool AreSourceContainerPermissionsCurrent()
        {
            foreach (var permission in containerPermissions) if (!permission.IsCurrentOwned) return false;
            return true;
        }
        private bool TryRetainContainerPermission(int x, int y, out RuntimeChestStore.SourceContainerPermission? permission)
        {
            foreach (var captured in containerPermissions)
                if (captured.AnchorX == x && captured.AnchorY == y) { permission = captured; return captured.IsCurrentOwned; }
            permission = null;
            if (containerPermissions.Count == ToolWorldTileOverlay1458.MaximumChangedCells ||
                !owner.TryCaptureSourceContainerPermission(x, y, out permission) || permission is null) return false;
            containerPermissions.Add(permission);
            return permission.IsCurrentOwned;
        }
        private ContainerContext? container;
        private bool AllowsContainerKill(ushort type) => container is not null && container.Descriptor.Definition.TileType.Value == type;

        /// <summary>SERVER primitive only after the retained input matched source packet34.</summary>
        internal bool TryBreakContainer(int x, int y, byte authenticatedPlayerSlot, out ToolWorldSourceResult1458 result)
        {
            result = default;
            if (clientRole || server is null || container is not null || work.Count != 0 || !IsCurrentOwned ||
                owner.objectMutations is not { } service ||
                service.TryResolveObjectAt(x, y, out var descriptor) != VanillaMultiTileObjectMutationStatus.Applied ||
                descriptor.Definition.TileType.Value is not (21 or 467 or 88) ||
                descriptor.MetadataKind != VanillaTileObjectMetadataKind.Chest) return Reject();
            bool actorKnown = false;
            foreach (var actor in actors) if (actor.Active && actor.Slot == authenticatedPlayerSlot) { actorKnown = true; break; }
            if (!actorKnown || authenticatedPlayerSlot == byte.MaxValue) return Reject();
            var bounds = descriptor.Bounds;
            if (!TryRetainContainerPermission(bounds.X, bounds.Y, out var permission) || permission is null) return Reject();
            if (!permission.CanRemoveClosed)
            {
                result = new(false, true, false);
                return permission.IsCurrentOwned && IsCurrentOwned;
            }
            for (int dx = 0; dx < bounds.Width; dx++)
                for (int dy = 0; dy < bounds.Height; dy++)
                    if (!Overlay.TryRead(bounds.X + dx, bounds.Y + dy, out var cell) ||
                        !cell.Equals(Overlay.Tiles.Get(bounds.X + dx, bounds.Y + dy))) return Reject();
            if (!Overlay.TryRead(bounds.X, bounds.Y, out var first) || first.FrameX < 0 ||
                first.FrameX % (bounds.Width * 18) != 0 ||
                !VanillaItemObjectPlacementCatalog.TryGet(descriptor.Definition.TileType,
                    checked((short)(first.FrameX / (bounds.Width * 18))), 0, out var item) ||
                !server.TryStageSourceContainerMetadataRemoval(bounds.X, bounds.Y)) return Reject();
            if (!server.TryBeginSourceDefaultItemOwner(authenticatedPlayerSlot)) return Reject();
            container = new(in descriptor, item.ItemType);
            int changed = mutationOrdinal;
            try
            {
                if (!Push(new(StepKind.Kill, bounds.X, bounds.Y)) || !Drain() || container.Destroying) return Reject();
                byte responseAction = descriptor.Definition.TileType.Value == 88 ? (byte)3 :
                    descriptor.Definition.TileType.Value == 467 ? (byte)5 : (byte)1;
                if (!server.TryStageSourceContainerResponse(bounds.X, bounds.Y, responseAction, permission.ChestId)) return Reject();
                result = new(mutationOrdinal != changed, false, false);
                return permission.IsCurrentOwned && IsCurrentOwned;
            }
            finally { container = null; server.EndSourceDefaultItemOwner(); }
        }

        private bool FrameContainer(int x, int y, in WorldTile tile)
        {
            if (IsSourceDestroyingObject) return true;
            if (container is not { } context) return TryVerifyIntactContainer(x, y, in tile);
            if (context.Destroying) return true; // original global destroyObject reentry guard
            var bounds = context.Descriptor.Bounds;
            if (!AllowsContainerKill(tile.Type) || x < bounds.X || y < bounds.Y ||
                x >= bounds.ExclusiveRight || y >= bounds.ExclusiveBottom) return false;
            bool incomplete = false;
            for (int dx = 0; dx < bounds.Width; dx++)
                for (int dy = 0; dy < bounds.Height; dy++)
                {
                    if (!Overlay.TryRead(bounds.X + dx, bounds.Y + dy, out var cell)) return false;
                    incomplete |= !cell.IsActive || cell.Type != tile.Type || cell.FrameX / 18 % bounds.Width != dx || cell.FrameY / 18 != dy;
                }
            if (!incomplete) return true;
            context.Destroying = true;
            context.DropX = x; context.DropY = y;
            return Push(new(StepKind.ContainerNextCell, x, y) { Index = 0 });
        }

        private bool TryVerifyIntactContainer(int x, int y, in WorldTile tile)
        {
            int width = tile.Type == 88 ? 3 : 2;
            if (tile.FrameX < 0 || tile.FrameY < 0 || tile.FrameY > 18) return false;
            int left = x - tile.FrameX / 18 % width, top = y - tile.FrameY / 18;
            int styleFrame = tile.FrameX / (width * 18) * width * 18;
            if (!TryRetainContainerPermission(left, top, out var permission) || permission is null) return false;
            for (int dx = 0; dx < width; dx++)
            {
                for (int dy = 0; dy < 2; dy++)
                {
                    if (!Overlay.TryReadSourceSafely(left + dx, top + dy, out var cell) || !cell.IsActive ||
                        cell.Type != tile.Type || cell.FrameX != styleFrame + dx * 18 || cell.FrameY != dy * 18) return false;
                }
                if (!Overlay.TryReadSourceSafely(left + dx, top + 2, out var support) ||
                    !TerraRuntime.Gameplay.Tiles.VanillaToolTileCatalog1458.TryGet(support.TileType, out var facts)) return false;
                if (tile.Type != 88) { if ((!support.IsActive || !facts.IsSolid) && permission.CanDestroy) return false; }
                else
                {
                    int platformFrame = support.FrameX / 18;
                    bool properPlatform = facts.IsPlatform && (platformFrame is >= 0 and <= 7 or >= 12 and <= 16 or >= 25 and <= 26);
                    if (!support.IsActive || (!facts.IsSolid && !facts.IsSolidTop) || support.IsHalfBrick || support.IsActuated ||
                        (support.SourceSlope is 1 or 2 && !properPlatform)) return false;
                }
            }
            return true;
        }

        private bool ContainerNextCell(int index)
        {
            if (container is not { Destroying: true } context || server is null) return false;
            var bounds = context.Descriptor.Bounds;
            int scanHeight = bounds.Width == 2 ? 3 : 2; // CheckChest scans one extra row; Check3x2 does not.
            if (index == bounds.Width * scanHeight)
            {
                context.Destroying = false;
                return server.TryStageSourceItemRectangle(context.Item, 1,
                    context.DropX * 16, context.DropY * 16, 32, 32);
            }
            int x = bounds.X + index / scanHeight, y = bounds.Y + index % scanHeight;
            if (!Overlay.TryReadSourceSafely(x, y, out var cell) ||
                !Push(new(StepKind.ContainerNextCell, x, y) { Index = index + 1 })) return false;
            return !cell.IsActive || !AllowsContainerKill(cell.Type) || Push(new(StepKind.Kill, x, y));
        }
    }
}
