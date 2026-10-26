using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed class PlacementSteps1458(WorldTileAuthority owner,
        ToolWorldSourcePass1458 local, ToolWorldSourcePass1458 server,
        ToolWorldMutationPlan world, PlayerAuthority.ToolTreeActorsPreparation actors,
        LocalPlacementDefinition1458 definition, int beforeStack)
    {
        private readonly List<TerrariaTileManipulationState> expected = [];
        private readonly List<ToolWorldPublicationOffset1458> boundaries = [];
        private bool failed;
        internal ReadOnlySpan<TerrariaTileManipulationState> Expected =>
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(expected);
        internal ReadOnlySpan<ToolWorldPublicationOffset1458> Boundaries =>
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(boundaries);
        internal int AdditionalWalls { get; private set; }
        internal bool IsCurrent => !failed && actors.IsCurrent && local.IsCurrentOwned && server.IsCurrentOwned;

        internal bool TryPlaceAt(int x, int y)
        {
            if (!IsCurrent || expected.Count != 0 || beforeStack < 1) return false;
            if (definition.IsWall)
            {
                if (!HasWallAnchor(x, y, out bool anchor) || !anchor ||
                    !TryPlaceWall(x, y, true)) return Reject();
                if (beforeStack > 1)
                    for (int side = 0; side < 4; side++)
                    {
                        int tx = x + (side == 0 ? -1 : side == 1 ? 1 : 0);
                        int ty = y + (side == 2 ? -1 : side == 3 ? 1 : 0);
                        if (!local.Overlay.TryReadSourceSafely(tx, ty, out var cell)) return Reject();
                        if (cell.Wall != 0) continue;
                        bool enclosed = true;
                        for (int leg = 0; leg < 4; leg++)
                        {
                            int nx = tx + (leg == 0 ? -1 : leg == 1 ? 1 : 0);
                            int ny = ty + (leg == 2 ? -1 : leg == 3 ? 1 : 0);
                            if (!local.Overlay.TryReadSourceSafely(nx, ny, out var neighbor)) return Reject();
                            enclosed &= neighbor.Wall == definition.CreateWall;
                        }
                        if (enclosed && !TryPlaceWall(tx, ty, false)) return Reject();
                    }
                return IsCurrent;
            }
            if (definition.PlaceStyle != 0 || definition.CreateTile is not (0 or 1 or 30 or 38 or 40) ||
                !HasSimpleTileAnchor(x, y, out bool anchored) || !anchored ||
                !actors.TryIsPlacementTileEmpty(x, y, out bool empty) ||
                !TryIsNpcPlacementEmpty(x, y, out bool npcEmpty) ||
                !local.TryPlaceSimpleTile(x, y, new(definition.CreateTile), empty && npcEmpty, out bool placed) || !placed)
                return Reject();
            // The server executes the observed primary before any later client
            // unslope/grass tail; the two process-role streams stay independent.
            if (!server.TryPlaceSimpleTile(x, y, new(definition.CreateTile), empty && npcEmpty, out bool serverPlaced) ||
                !serverPlaced || !Append(1, x, y, definition.CreateTile)) return Reject();
            return TrySolidPlacementTail(x, y) && IsCurrent;
        }

        private bool HasWallAnchor(int x, int y, out bool found)
        {
            found = false;
            for (int side = 0; side < 4; side++)
            {
                int tx = x + (side == 0 ? -1 : side == 1 ? 1 : 0);
                int ty = y + (side == 2 ? -1 : side == 3 ? 1 : 0);
                if (!local.Overlay.TryReadSourceSafely(tx, ty, out var tile)) return false;
                found |= tile.IsActive || tile.Wall > 0;
            }
            return true;
        }

        private bool HasSimpleTileAnchor(int x, int y, out bool found)
        {
            found = false;
            if (!local.Overlay.TryReadSourceSafely(x, y, out var center)) return false;
            found = center.Wall > 0;
            for (int side = 0; side < 4; side++)
            {
                int tx = x + (side == 0 ? -1 : side == 1 ? 1 : 0);
                int ty = y + (side == 2 ? -1 : side == 3 ? 1 : 0);
                if (!local.Overlay.TryReadSourceSafely(tx, ty, out var tile) ||
                    !VanillaToolTileCatalog1458.TryGet(tile.TileType, out var role)) return false;
                bool beam = tile.Type is 124 or 561 or 574 or 575 or 576 or 577 or 578;
                found |= tile.Wall > 0 || tile.IsActive && (role.IsSolid || role.IsRope || beam || tile.Type == 314);
            }
            return true;
        }

        private bool TryPlaceWall(int x, int y, bool primary)
        {
            var wall = new WallTypeId(definition.CreateWall);
            if (!local.TryPlaceSourceWall(x, y, wall, out bool placed) || !placed ||
                !server.TryPlaceSourceWall(x, y, wall, out bool serverPlaced) || !serverPlaced ||
                !Append(3, x, y, definition.CreateWall)) return false;
            if (!primary) AdditionalWalls++;
            return true;
        }

        private bool TrySolidPlacementTail(int x, int y)
        {
            for (int side = 0; side < 2; side++)
            {
                int ty = y + (side == 0 ? 1 : -1);
                if (!local.Overlay.TryReadSourceSafely(x, ty, out var tile) ||
                    !VanillaToolTileCatalog1458.TryGet(tile.TileType, out var role)) return Reject();
                bool unslope = !role.IsPlatform && (side == 0
                    ? tile.SourceSlope is 1 or 2 || tile.IsHalfBrick : tile.SourceSlope is 3 or 4);
                if (unslope && (!local.TrySlopeTile(x, ty, 0, out _) ||
                    !server.TrySlopeTile(x, ty, 0, out _) || !Append(14, x, ty, 0))) return Reject();
            }
            for (int tx = x - 1; tx <= x + 1; tx++)
            for (int ty = y - 1; ty <= y + 1; ty++)
            {
                if (!local.Overlay.TryReadSourceSafely(tx, ty, out var tile)) return Reject();
                if (!tile.IsActive || tile.Type == definition.CreateTile ||
                    tile.Type is not (2 or 23 or 60 or 70 or 109 or 199 or 477 or 492 or 633)) continue;
                bool enclosed = true;
                for (int nx = tx - 1; nx <= tx + 1; nx++)
                for (int ny = ty - 1; ny <= ty + 1; ny++)
                {
                    if (!local.Overlay.TryReadSourceSafely(nx, ny, out var cell) ||
                        !VanillaToolTileCatalog1458.TryGet(cell.TileType, out var role)) return Reject();
                    enclosed &= cell.IsActive && !cell.IsActuated && role.IsSolid && !role.IsSolidTop &&
                        !cell.IsHalfBrick && cell.SourceSlope == 0;
                }
                if (enclosed && (!local.TryKillTile(tx, ty, true, false, false, out _) ||
                    !server.TryKillTile(tx, ty, true, false, false, out _) || !Append(0, tx, ty, 1))) return Reject();
            }
            return true;
        }

        private bool TryIsNpcPlacementEmpty(int x, int y, out bool empty)
        {
            empty = false;
            if (!IsCurrent) return false;
            for (int slot = 0; slot < Math.Min(200, owner.npcs.Capacity); slot++)
            {
                if (!owner.npcs.TryGetActive((byte)slot, out var npc)) continue;
                if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition) ||
                    !definition.TryResolveHitbox(npc.Simulation, out var size) ||
                    !float.IsFinite(npc.PositionX) || !float.IsFinite(npc.PositionY)) return false;
                if (x * 16 < (int)npc.PositionX + size.Width && x * 16 + 16 > (int)npc.PositionX &&
                    y * 16 < (int)npc.PositionY + size.Height && y * 16 + 16 > (int)npc.PositionY) return true;
            }
            empty = true;
            return IsCurrent;
        }

        private bool Append(byte action, int x, int y, int data)
        {
            if (expected.Count == RuntimePlayerToolInput1458.MaximumObservations || data > short.MaxValue) return Reject();
            expected.Add(new(action, checked((short)x), checked((short)y), (short)data, 0));
            boundaries.Add(world.CapturePublicationOffset());
            return true;
        }
        private bool Reject() { failed = true; return false; }
    }
}
