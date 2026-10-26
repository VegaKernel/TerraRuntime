using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;
using TerraRuntime.Gameplay.Tiles;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class PlacementSteps1458
    {
        private ToolDefinition1458? replacementPick;
        private ToolDamageWorld1458 damageWorld;
        private ToolWorldReadPreparation? replacementReads;
        internal ToolHitState1458 Hits { get; private set; } = ToolHitState1458.Constructor;
        internal ToolHitState1458 ReplacementHits { get; private set; } = ToolHitState1458.Constructor;
        internal int? ReplacementPickUseTime { get; private set; }

        internal void ConfigureReplacement(ToolDefinition1458? pick, in ToolDamageWorld1458 facts,
            ToolWorldReadPreparation reads, ToolHitState1458 hits, ToolHitState1458 replacements)
        {
            replacementPick = pick; damageWorld = facts; replacementReads = reads;
            Hits = hits; ReplacementHits = replacements;
        }

        private bool TryReplaceTileAt(int x, int y)
        {
            if (replacementPick is not { } pick || replacementReads is null ||
                !local.TryGetOrdinaryReplacementDrop(x, y, new(definition.CreateTile), definition.Item,
                    pick.Pick, out bool eligible) || !eligible) return Reject();
            Span<ToolTargetTile1458> window = stackalloc ToolTargetTile1458[VanillaToolDamage1458.TargetWindowCount];
            if (!replacementReads.TryCaptureTargetBefore(x, y, window) ||
                !VanillaToolDamage1458.TryGetPickDamage(pick.Pick, x, y, window, damageWorld, out var preliminary) ||
                !ReplacementHits.TryHitObject(x, y, 1, out var hits, out int slot) ||
                !local.Overlay.TryReadSourceSafely(x, y, out var tile) ||
                !VanillaToolDamage1458.TryFinalizePickDamage(preliminary, tile.TileType, true,
                    damageWorld.GetGoodWorld, false, hits, slot, pick.Pick, -1, out int damage) || damage <= 0 ||
                !hits.TryAddDamage(slot, damage, true, out hits, out int sum, out int samples) || sum < 100 ||
                !local.TryAdvanceReplacementHit(samples)) return Reject();
            ReplacementHits = hits;
            // Source ClearMiningCacheAt clears/prunes replacement501 first,
            // ordinary501 second, BEFORE either process performs ReplaceTile.
            if (!TryClearReplacementCaches(x, y) ||
                !local.TryReplaceSourceTile(x, y, new(definition.CreateTile), out bool placed) || !placed ||
                !server.TryReplaceSourceTile(x, y, new(definition.CreateTile), out bool serverPlaced) || !serverPlaced ||
                !Append(21, x, y, definition.CreateTile)) return Reject();
            ReplacementPickUseTime = pick.UseTime;
            return IsCurrent;
        }

        private bool TryClearReplacementCaches(int x, int y)
        {
            Span<bool> presence = stackalloc bool[ToolHitState1458.SlotCount];
            int slot = ReplacementHits.Find(x, y, 1);
            if (slot >= 0)
            {
                var cleared = ReplacementHits.Clear(slot);
                if (replacementReads is null || !replacementReads.TryCapturePrune(cleared, local.Overlay, presence) ||
                    !cleared.TryPrune(presence, out var after)) return Reject();
                ReplacementHits = after;
            }
            slot = Hits.Find(x, y, 1);
            if (slot < 0) return true;
            var ordinary = Hits.Clear(slot);
            if (replacementReads is null || !replacementReads.TryCapturePrune(ordinary, local.Overlay, presence) ||
                !ordinary.TryPrune(presence, out var pruned)) return Reject();
            Hits = pruned;
            return true;
        }

        private bool TryReplaceWallAt(int x, int y)
        {
            if (!HasFriendlyReplacementWall(x, y, out bool friendly) || !friendly ||
                !local.TryReplaceSourceWall(x, y, new(definition.CreateWall), out bool placed) || !placed ||
                !server.TryReplaceSourceWall(x, y, new(definition.CreateWall), out bool serverPlaced) || !serverPlaced ||
                !Append(22, x, y, definition.CreateWall)) return Reject();
            // The successful replacement branch suppresses the normal fill loop.
            return IsCurrent;
        }

        private bool HasFriendlyReplacementWall(int x, int y, out bool friendly)
        {
            friendly = false;
            var size = local.Overlay.Tiles.Dimensions;
            if (x < 2 || y < 2 || x >= size.WidthTiles - 2 || y >= size.HeightTiles - 2)
            { friendly = true; return true; }
            for (int side = 0; side < 5; side++)
            {
                int tx = x + (side == 1 ? -1 : side == 2 ? 1 : 0);
                int ty = y + (side == 3 ? -1 : side == 4 ? 1 : 0);
                if (!local.Overlay.TryReadSourceSafely(tx, ty, out var tile)) return false;
                friendly |= tile.Wall == 0 || VanillaLocalPlacementTiles1458.IsHousingWall(tile.WallType);
            }
            return true;
        }
    }
}
