using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        private bool TryApplyPlacementSeedPaint(int x, int y, ref WorldTile after)
        {
            if (after.Type is not (53 or 396 or 397)) return true;
            bool? rainbow = facts.OnlyShimmerOceanWorlds;
            if (!rainbow.HasValue && (!facts.DrunkWorld || !facts.TenthAnniversaryWorld ||
                facts.RemixWorld || facts.NotTheBeesWorld)) rainbow = false;
            if (!rainbow.HasValue) return false;
            if (rainbow.Value)
            {
                float angle = (y % 50) / 50f * ((float)Math.PI * 2f);
                int shiftedX = x + (int)(Math.Sin(angle) * 10d) - 5;
                after.TileColor = unchecked((byte)(13 + shiftedX % 44 % 11));
            }
            else if (facts.TenthAnniversaryWorld && !facts.RemixWorld &&
                !facts.NotTheBeesWorld && !facts.DontStarveWorld) after.TileColor = 7;
            return true;
        }

        internal bool TryGetOrdinaryReplacementDrop(int x, int y, TileTypeId target,
            ItemTypeId heldItem, int pickPower, out bool eligible)
        {
            eligible = false;
            if (!IsCurrentOwned || !Overlay.TryReadSourceSafely(x, y, out var before) ||
                !Overlay.TryReadSourceSafely(x, y - 1, out var above)) return Reject();
            if (!before.IsActive || !VanillaLocalPlacementTiles1458.CanReplaceSolid(before.TileType) ||
                !VanillaLocalPlacementTiles1458.IsOrdinarySolid(target) || before.TileType == target ||
                VanillaLocalPlacementTiles1458.PreventsReplacement(before.TileType) ||
                VanillaLocalPlacementTiles1458.ForbidsReplacementPlacement(target) || before.Wall == 350 ||
                before.Type == 230 && facts.GoodWorld || before.Type is 546 or 557 && before.LiquidAmount > 0)
                return true;
            if (IsSourceFallingTile(before.Type) && !IsSourceFallingTile(checked((ushort)target.Value)) &&
                pickPower < 110 && above.IsActive && IsSourceFallingTile(above.Type)) return true;
            if (above.IsActive)
            {
                if (above.Type is 80 or 488) return true;
                if (above.Type != before.Type && above.Type is 5 or 323 or 72 or >= 583 and <= 589 or 596 or 616 or 634)
                {
                    if (!VanillaToolTileCatalog1458.TryGet(above.TileType, out var role)) return Reject();
                    bool permits = role.IsTreeTrunk
                        ? above.FrameX == 66 && above.FrameY is >= 0 and <= 44 ||
                          above.FrameX == 88 && above.FrameY is >= 66 and <= 110 || above.FrameY >= 198
                        : above.Type == 323 && above.FrameX is not (66 or 220);
                    if (!permits) return true;
                }
            }
            if (!VanillaTileDefinitionCatalog.TryGet(before.TileType, out var definition) ||
                !definition.DropRule.IsDirectlyMaterializable) return Reject();
            // Source previews the primary drop before any HitTile draw. Direct
            // fixed/none rules have no preview entropy or contextual effects.
            if (target.Value is not (30 or 158) && definition.DropRule.PrimaryItem == heldItem) return true;
            eligible = true;
            return IsCurrentOwned;
        }

        internal bool TryAdvanceReplacementHit(int samples)
        {
            if (!IsCurrentOwned || samples is < 0 or > 1) return Reject();
            for (int i = 0; i < samples; i++) random.NextDouble();
            return IsCurrentOwned;
        }

        internal bool TryReplaceSourceTile(int x, int y, TileTypeId type, out bool replaced)
        {
            replaced = false;
            if (!IsCurrentOwned || work.Count != 0 || !Overlay.TryReadSourceSafely(x, y, out var before) ||
                !VanillaTileDefinitionCatalog.TryGet(before.TileType, out var definition) ||
                !definition.DropRule.IsDirectlyMaterializable ||
                !VanillaLocalPlacementTiles1458.IsOrdinarySolid(type)) return Reject();
            if (!AdvanceDust(before, false)) return Reject();
            var drops = definition.DropRule;
            if (clientRole)
            {
                if (!drops.PrimaryItem.IsNone) { random.Next(-30, 31); random.Next(-40, -15); }
                if (!drops.SecondaryItem.IsNone) { random.Next(-30, 31); random.Next(-40, -15); }
            }
            else if (server is null || !StageDrops(x, y, before)) return Reject();
            if (before.Type == 404 && !clientRole && !fossilBreak)
            {
                fossilBreak = true;
                if (!Push(new(StepKind.FossilNeighbor, x, y) { SourceTile = before, FossilOnly = true }) ||
                    !Drain()) return Reject();
            }
            for (int side = 0; side < 2; side++)
            {
                int ty = y + (side == 0 ? -1 : 1);
                if (!Overlay.TryReadSourceSafely(x, ty, out var extra)) return Reject();
                bool remove = side == 0
                    ? VanillaLocalPlacementTiles1458.BreaksAboveOnReplacement(extra.TileType) ||
                      extra.Type == 165 && extra.FrameY is 36 or 54 or 90
                    : VanillaLocalPlacementTiles1458.BreaksBelowOnReplacement(extra.TileType) ||
                      extra.Type == 165 && extra.FrameY is 0 or 18 or 72;
                if (extra.IsActive && remove && !TryKillTile(x, ty, false, false, false, out _)) return Reject();
            }
            // Side kills/frame cascades can change the original target too.
            if (!Overlay.TryReadSourceSafely(x, y, out var current) || !current.IsActive ||
                !VanillaToolTileCatalog1458.TryGet(current.TileType, out var oldRole) ||
                !VanillaToolTileCatalog1458.TryGet(type, out var newRole)) return Reject();
            var after = current; after.Type = checked((ushort)type.Value);
            after.FrameX = 0; after.FrameY = 0; after.TileColor = 0;
            after.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            if (!TryApplyPlacementSeedPaint(x, y, ref after) ||
                !Stage(WorldTileMutationKind.TransformTile, x, y, in current, in after) ||
                !VanillaToolWorldRules1458.TryEvaluate(Overlay.Tiles, Overlay, x, y, hardMode, out var permission))
                return Reject();
            if (!permission.CanPound)
            {
                current = after; after.ClearSourceShape();
                if (!Stage(WorldTileMutationKind.TransformTile, x, y, in current, in after)) return Reject();
            }
            if (oldRole.TruncatesWalls != newRole.TruncatesWalls && !FrameWalls(x, y) ||
                !PushSquare(x, y) || !Drain()) return Reject();
            replaced = true; return IsCurrentOwned;
        }

        internal bool TryReplaceSourceWall(int x, int y, WallTypeId wall, out bool replaced)
        {
            replaced = false;
            if (!IsCurrentOwned || work.Count != 0 || !Overlay.TryReadSourceSafely(x, y, out var before) ||
                !VanillaWallDefinitionCatalog.TryGet(wall, out var definition) || !definition.IsPresent)
                return Reject();
            if (before.Wall is 0 or 350 || before.WallType == wall ||
                !VanillaWallBreakRules1458.IsProgressionUnlocked(before.WallType, skeletronDowned, golemDowned))
                return true;
            for (int index = 0; index < 10; index++)
                if (!VanillaToolDust1458.TryAdvanceSuppressedWallParticle(before.WallType, random.Next)) return Reject();
            if (!VanillaWallDropCatalog1458.TryResolve(before.WallType, out var item)) return Reject();
            if (!item.IsNone)
            {
                // ReplaceWall calls Item.NewItem in BOTH process roles. A client
                // uses sentinel400, retaining the two launch draws but minting
                // no authoritative item or payment credit.
                if (clientRole) { random.Next(-30, 31); random.Next(-40, -15); }
                else if (server is null || !server.TryStageSourceItem(item, 1, x, y)) return Reject();
            }
            var after = before;
            after.Wall = checked((ushort)wall.Value); after.WallColor = 0;
            after.Flags &= ~(WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall);
            if (!Stage(WorldTileMutationKind.PlaceWall, x, y, in before, in after) || !FrameWalls(x, y)) return Reject();
            replaced = true;
            return IsCurrentOwned;
        }
    }
}
