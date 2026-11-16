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
            if (tile.IsActive && (!VanillaTileDefinitionCatalog.TryGet(tile.TileType, out var definition) ||
                definition.IsFrameImportant && tile.Type is not (19 or 137) && !(wiringPumpFrame && tile.Type is 142 or 143) || IsSourceFallingTile(tile.Type) || IsSourcePlant(tile.Type) ||
                IsFramedTree(tile.Type) || tile.Type == 80)) return false;
            if (tile.LiquidAmount == 0) return true;
            if (!deathWater(step.X, step.Y, in tile, out bool destroy)) return false;
            if (!destroy) return true;
            // The admitted wooden platform uses the existing KillTile dust,
            // clear, recursive SquareTileFrame and NewItem materializer. Other
            // styles/objects need their own verified complete participants.
            if (deathDrop is null || tile.Type != 19 || tile.FrameY != 0) return false;
            deathKills.Add((step.X, step.Y));
            deferred = true;
            return Push(step) && Push(new(StepKind.Kill, step.X, step.Y));
        }

        private bool TryStageDeathDrop(int x, int y, in WorldTile tile)
        {
            if (tile.Type != 19 || tile.FrameY != 0 || deathDrop is null) return false;
            var drop = VanillaSimpleTileBreakResolver1458.MaterializeItemState(new(94), 1, x, y,
                new SystemWorldItemSpawnRandom(random));
            return deathDrop(drop);
        }
    }
}
