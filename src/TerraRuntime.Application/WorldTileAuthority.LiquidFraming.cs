using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        private delegate bool DeathWaterOffer(int x, int y, in WorldTile tile, out bool destroy);
        internal Func<WorldItemDropStateUpdate, bool>? deathDrop;
        private readonly List<(int X, int Y)> deathKills = [];
        internal ReadOnlySpan<(int X, int Y)> DeathKills => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(deathKills);
        private DeathWaterOffer? deathWater;
        private bool wiringPumpFrame;
        private bool IsLavaDeathFraming => deathWater is not null && !wiringPumpFrame;

        // WorldGen.GetItemDrop_Platforms maps canonical Tile19 frameY / 18.
        // The liquid-death owner independently decides which styles survive.
        private static readonly ushort[] LiquidPlatformDropItems =
        [
            94, 631, 632, 633, 634, 913, 1384, 1385, 1386, 1387,
            1388, 1389, 1418, 1457, 1702, 1796, 1818, 2518, 2549, 2566,
            2581, 2627, 2628, 2629, 2630, 2744, 2822, 3144, 3146, 3145,
            3903, 3904, 3905, 3906, 3907, 3908, 3945, 3957, 4159, 4180,
            4201, 4222, 4311, 4416, 4580, 5162, 5183, 5204, 5292, 5544,
            5562, 5615, 5703, 5726, 5751, 5770, 5791, 5812, 5833, 5852,
            5872, 5912, 5946, 5989, 6012, 6035, 6058, 6081, 6103, 6125
        ];

        // WorldGen.KillTile_GetItemDrops selects ordinary Tile4 by frameY / 22.
        private static readonly ushort[] LiquidTorchDropItems =
        [
            8, 427, 428, 429, 430, 431, 432, 433, 523, 974, 1245, 1333,
            2274, 3004, 3045, 3114, 4383, 4384, 4385, 4386, 4387, 4388, 5293, 5353
        ];

        internal bool TryFrameRetainedPumpSquare(int x, int y,
            VanillaWorldLiquidMutationService.SourceWaterPreparation1458 water)
        {
            wiringPumpFrame = true;
            try { return TryFrameRetainedLiquidSquare(x, y, water); }
            finally { wiringPumpFrame = false; }
        }

        internal bool TryVerifyWiringPump(int x, int y, ushort expectedType)
        {
            if (!Overlay.TryRead(x, y, out var tile) || tile.Type != expectedType ||
                !tile.IsActive || tile.HasActuator || tile.IsActuated || tile.FrameX < 0 || tile.FrameX > 54 ||
                tile.FrameY < 0 || tile.FrameY > 18 || tile.FrameX % 18 != 0 || tile.FrameY % 18 != 0) return false;
            int left = x - tile.FrameX % 36 / 18, top = y - tile.FrameY / 18, style = tile.FrameX / 36;
            for (int dx = 0; dx < 2; dx++)
            {
                for (int dy = 0; dy < 2; dy++)
                {
                    if (!Overlay.TryRead(left + dx, top + dy, out var cell) || !cell.IsActive || cell.Type != expectedType ||
                        cell.HasActuator || cell.IsActuated || cell.FrameX != style * 36 + dx * 18 || cell.FrameY != dy * 18) return false;
                }
                if (!Overlay.TryRead(left + dx, top + 2, out var floor) || !floor.IsActive || floor.IsHalfBrick ||
                    !VanillaTileCollisionCatalog.IsSolid(floor.TileType) && !VanillaToolTileCatalog1458.IsTable(floor.TileType)) return false;
            }
            return true;
        }

        internal bool TryFrameRetainedLiquidSquare(int x, int y,
            VanillaWorldLiquidMutationService.SourceWaterPreparation1458 water) =>
            TryFrameRetainedLiquidSquareCore(x, y, water.TryAddWater);

        private bool TryFrameRetainedLiquidSquareCore(int x, int y, DeathWaterOffer offer)
        {
            if (!clientRole || server is not null || work.Count != 0 || deathWater is not null || !IsCurrentOwned) return Reject();
            deathWater = offer;
            try { return PushSquare(x, y) && Drain(); }
            finally { deathWater = null; }
        }

        private bool TryPrepareLiquidFrame(in SourceStep step, in WorldTile tile, out bool deferred)
        {
            deferred = false;
            if (deathWater is null) return true;
            if (tile.IsActive && !CanFrameRetainedLiquidTile(in tile)) return false;
            if (tile.LiquidAmount == 0) return true;
            if (!deathWater(step.X, step.Y, in tile, out bool destroy)) return false;
            if (!destroy) return true;
            // AddWater delegates the clear and all rest-of-object framing to
            // KillTile before its packet 17. Record only this explicit notice.
            if (deathDrop is null || wiringPumpFrame) return false;
            deathKills.Add((step.X, step.Y));
            deferred = true;
            return Push(step) && Push(new(StepKind.Kill, step.X, step.Y));
        }

        private bool CanFrameRetainedLiquidTile(in WorldTile tile)
        {
            if (!VanillaTileDefinitionCatalog.TryGet(tile.TileType, out var definition) ||
                IsSourceFallingTile(tile.Type) || IsSourcePlant(tile.Type) || IsFramedTree(tile.Type) ||
                tile.Type == 80) return false;
            if (wiringPumpFrame)
                return !definition.IsFrameImportant || tile.Type is 19 or 137 or 142 or 143;

            return tile.Type switch
            {
                4 => IsCanonicalLiquidStyle(tile.FrameY, 22, LiquidTorchDropItems.Length),
                19 => IsCanonicalLiquidStyle(tile.FrameY, 18, LiquidPlatformDropItems.Length),
                49 or 136 or 137 or 442 => true,
                135 => IsCanonicalLiquidStyle(tile.FrameY, 18, 8),
                14 or 15 or 16 or 18 => TryGetOrdinaryFurnitureFrame(in tile, true, out _),
                _ => !definition.IsFrameImportant
            };
        }

        private static bool IsCanonicalLiquidStyle(short frame, int pitch, int count) =>
            frame >= 0 && frame % pitch == 0 && frame / pitch < count;

        private bool TryStageDeathDrop(int x, int y, in WorldTile tile)
        {
            if (deathDrop is null || !CanFrameRetainedLiquidTile(in tile)) return false;
            // These furniture items drop only from Check3x2/Check1x2/Check2x1
            // after the recursive clears, not from the initiating cell kill.
            if (IsOrdinaryPlacementFurniture(tile.Type)) return true;
            if (tile.Type == 135 && tile.FrameY == 7 * 18) return true;
            ushort item = tile.Type switch
            {
                4 => LiquidTorchDropItems[tile.FrameY / 22],
                19 => LiquidPlatformDropItems[tile.FrameY / 18],
                49 => 148,
                135 => GetWiringTriggerDropItem(tile.Type, tile.FrameY),
                136 => 538,
                213 => 965,
                442 => 3707,
                _ => 0
            };
            if (item == 0) return false;
            var drop = VanillaSimpleTileBreakResolver1458.MaterializeItemState(new ItemTypeId(item), 1, x, y,
                new SystemWorldItemSpawnRandom(random));
            return deathDrop(drop);
        }
    }
}
