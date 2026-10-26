using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal ToolWorldReadPreparation? CreateToolReadPreparation() =>
        tiles is null ? null : new(tiles);

    // Owns the pre-action terrain witness for local tool targets and HitTile.Prune.
    // The 501 saved hits can be scattered across the world; a target-only ROI is
    // insufficient. Source-after-write reads must instead use the world plan's
    // virtual tiles, while recording their original section through this owner.
    internal sealed class ToolWorldReadPreparation(WorldTileStore owner)
    {
        // One complete HitTile cache plus 18 source shovel PickTile invocations,
        // each with the 25-cell power/anchor stencil. This bounds this witness,
        // not the independently bounded tree/framing transaction's read set.
        private const int MaximumSections = ToolHitState1458.SlotCount +
            18 * VanillaToolDamage1458.TargetWindowCount;
        private readonly List<(WorldSectionId Section, long Version)> sections = [];
        private bool invalid;

        internal bool IsCurrent
        {
            get
            {
                if (invalid) return false;
                foreach (var read in sections)
                    if (owner.GetSectionVersion(read.Section) != read.Version) return false;
                return true;
            }
        }

        internal bool TryReadBefore(int x, int y, out WorldTile tile)
        {
            tile = default;
            if (invalid || (uint)x >= (uint)owner.Dimensions.WidthTiles ||
                (uint)y >= (uint)owner.Dimensions.HeightTiles) return Reject();
            var section = TerrariaSectionGeometry.FromTile(owner.Dimensions, x, y);
            long version = owner.GetSectionVersion(section);
            if ((version & 1L) != 0) return Reject();
            bool recorded = false;
            foreach (var read in sections)
                if (read.Section == section)
                {
                    if (read.Version != version) return Reject();
                    recorded = true;
                    break;
                }
            if (!recorded)
            {
                if (sections.Count == MaximumSections) return Reject();
                sections.Add((section, version));
            }
            tile = owner.Get(x, y);
            if (owner.GetSectionVersion(section) != version) return Reject();
            return true;
        }

        internal bool TryCaptureTargetBefore(int x, int y, Span<ToolTargetTile1458> window)
            => TryCaptureTarget(x, y, null, window);

        internal bool TryCaptureTarget(int x, int y, ToolWorldTileOverlay1458? overlay,
            Span<ToolTargetTile1458> window)
        {
            if (overlay is not null &&
                (!ReferenceEquals(overlay.Tiles, owner) || !overlay.IsCurrentOwned)) return Reject();
            if (window.Length != VanillaToolDamage1458.TargetWindowCount || x < 2 || y < 2 ||
                x >= owner.Dimensions.WidthTiles - 2 || y >= owner.Dimensions.HeightTiles - 2)
                return Reject();
            int index = 0;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    if (!TryReadBefore(x + dx, y + dy, out var tile)) return false;
                    if (overlay is not null && overlay.TryGetStagedAfterimage(x + dx, y + dy, out var after))
                        tile = after;
                    window[index++] = new(tile.TileType, tile.FrameX, tile.FrameY,
                        (tile.Flags & WorldTileFlags.Active) != 0, tile.Wall);
                }
            return overlay is null || overlay.IsCurrentOwned || Reject();
        }

        internal bool TryCapturePruneBefore(ToolHitState1458 hits, Span<bool> presence)
            => TryCapturePrune(hits, null, presence);

        internal bool TryCapturePrune(ToolHitState1458 hits, ToolWorldTileOverlay1458? overlay,
            Span<bool> presence)
        {
            ArgumentNullException.ThrowIfNull(hits);
            if (overlay is not null &&
                (!ReferenceEquals(overlay.Tiles, owner) || !overlay.IsCurrentOwned)) return Reject();
            if (presence.Length != ToolHitState1458.SlotCount) return Reject();
            presence.Clear();
            for (int index = 0; index < presence.Length; index++)
            {
                var hit = hits.GetEntry(index);
                if (hit.Kind == 0) continue;
                if (hit.Kind is not (1 or 2) || !TryReadBefore(hit.X, hit.Y, out var tile)) return Reject();
                if (overlay is not null && overlay.TryGetStagedAfterimage(hit.X, hit.Y, out var after)) tile = after;
                presence[index] = hit.Kind == 1
                    ? (tile.Flags & WorldTileFlags.Active) != 0
                    : tile.Wall != 0;
            }
            return overlay is null || overlay.IsCurrentOwned || Reject();
        }

        private bool Reject()
        {
            invalid = true;
            return false;
        }
    }
}
