using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolActionSteps1458
    {
        private bool poundCall;

        internal bool TryPoundAt(int x, int y, int hitSlot, out bool performed, out bool consumedRelease)
        {
            performed = false;
            consumedRelease = false;
            if (!CanStage || poundCall || !client.Overlay.TryRead(x, y, out var tile)) return Reject();
            poundCall = true;
            if (!tile.IsActive || !VanillaToolDamage1458.IsTilePoundable(tile.TileType))
            { consumedRelease = true; return true; }
            // Stateful doll/track/trap/furniture frame changes require their
            // existing object/wiring participant, not a generic slope edit.

            if (!client.Overlay.TryRead(x, y - 1, out var above) ||
                !client.Overlay.TryRead(x, y + 1, out var below) ||
                !client.Overlay.TryRead(x - 1, y, out var left) ||
                !client.Overlay.TryRead(x + 1, y, out var right) ||
                !VanillaToolTileCatalog1458.TryGet(tile.TileType, out var roles)) return Reject();
            int damage = IsLockedDoor(in above) || IsLockedDoor(in below) ? 0 : 100;
            if (!Hits.TryAddDamage(hitSlot, damage, true, out var hit, out int accumulated,
                out int samples)) return Reject();
            Hits = hit;
            for (int index = 0; index < samples; index++) localRandom.NextDouble();
            performed = true;
            if (accumulated < 100)
                return client.TryKillTile(x, y, true, true, false, out _) || Reject();
            if (!TryClearMiningCache(x, y)) return false;

            if (tile.Type == 470)
            {
                if (!TryDollPoseAndReport(x, y)) return false;
            }
            else if (tile.Type == VanillaMinecartTrackFrame1458.TileType)
            {
                if (!TryPoundTrackAndReport(x, y)) return false;
            }
            else if (tile.Type is 137 or 424 or 442)
            {
                if (!TryRotateFurnitureAndReport(x, y)) return false;
            }
            else if (roles.IsPlatform)
            {
                if (tile.IsHalfBrick)
                {
                    if (!TryPoundAndReport(x, y)) return false;
                }
                else
                {
                    if (!client.Overlay.TryRead(x + 1, y - 1, out var upRight) ||
                        !client.Overlay.TryRead(x - 1, y + 1, out var downLeft) ||
                        !TryIsPlatform(in upRight, out bool platformRight) ||
                        !TryIsPlatform(in downLeft, out bool platformLeft) ||
                        !TryIsSolid(in right, out bool solidRight) ||
                        !TryIsSolid(in left, out bool solidLeft)) return Reject();
                    byte first = platformRight || platformLeft || solidRight && !solidLeft ? (byte)2 : (byte)1;
                    if (tile.SourceSlope == 0)
                    { if (!TrySlopeAndReport(x, y, first)) return false; }
                    else if (tile.SourceSlope == first)
                    { if (!TrySlopeAndReport(x, y, (byte)(3 - first))) return false; }
                    else
                    {
                        if (!VanillaToolWorldRules1458.TryEvaluate(client.Overlay.Tiles, client.Overlay,
                                x, y, hardMode, out var permission)) return Reject();
                        if (permission.CanPound &&
                            (!client.TryResetSlopeAndPound(x, y, out _) || !AppendTile(23, x, y, 0) ||
                             !server.TryResetSlopeAndPound(x, y, out _) || !RecordPublication(expectedCount))) return Reject();
                    }
                }
            }
            else if ((tile.IsHalfBrick || tile.SourceSlope != 0) && !roles.IsSolidTop)
            {
                if (!TryIsSolid(in right, out bool solidRight) || !TryIsSolid(in left, out bool solidLeft) ||
                    !TryIsSolid(in above, out bool solidAbove) || !TryIsSolid(in below, out bool solidBelow)) return Reject();
                int first = (solidRight || right.SourceSlope is 1 or 3) && !solidLeft ? 2 : 1;
                int second = 3 - first;
                int start = solidAbove && !solidBelow ? first + 2 : first;
                int next = solidAbove && !solidBelow ? second + 2 : second;
                int third = solidAbove && !solidBelow ? first : first + 2;
                int fourth = solidAbove && !solidBelow ? second : second + 2;
                int slope = tile.SourceSlope;
                byte requested = (byte)(slope == 0 ? start : slope == start ? next :
                    slope == next ? third : slope == third ? fourth : 0);
                if (!TrySlopeAndReport(x, y, requested)) return false;
            }
            else if (!TryPoundAndReport(x, y)) return false;
            consumedRelease = true;
            return true;
        }

        private bool TrySlopeAndReport(int x, int y, byte requested)
        {
            if (!client.TrySlopeTile(x, y, requested, out _) ||
                !client.Overlay.TryRead(x, y, out var result) ||
                !AppendTile(14, x, y, result.SourceSlope) ||
                !server.TrySlopeTile(x, y, result.SourceSlope, out _) || !RecordPublication(expectedCount)) return Reject();
            return true;
        }

        private bool TryPoundAndReport(int x, int y) =>
            client.TryPoundTile(x, y, out _) && AppendTile(7, x, y, 1) &&
            server.TryPoundTile(x, y, out _) && RecordPublication(expectedCount);

        private static bool IsLockedDoor(in WorldTile tile) =>
            tile.Type == 10 && tile.FrameY is >= 594 and <= 646 && tile.FrameX < 54;

        private static bool TryIsPlatform(in WorldTile tile, out bool platform)
        {
            platform = false;
            if (!VanillaToolTileCatalog1458.TryGet(tile.TileType, out var roles)) return false;
            platform = roles.IsPlatform;
            return true;
        }

        private static bool TryIsSolid(in WorldTile tile, out bool solid)
        {
            solid = false;
            if (!VanillaToolTileCatalog1458.TryGet(tile.TileType, out var roles)) return false;
            solid = tile.IsActive && (tile.Flags & WorldTileFlags.Inactive) == 0 &&
                roles.IsSolid && !roles.IsSolidTop && !tile.IsHalfBrick && tile.SourceSlope == 0;
            return true;
        }
    }
}
