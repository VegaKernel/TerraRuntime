using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal bool OwnsDeathTerrain(WorldTileStore expected, RuntimeNpcStore expectedNpcs, RuntimeWorldItemStore expectedItems) =>
        ReferenceEquals(tiles, expected) && ReferenceEquals(npcs, expectedNpcs) && ReferenceEquals(worldItems, expectedItems);
    internal bool TryPrepareLavaSlimeDeath(float x, float y, int width, int height, bool lowTiles,
        VanillaUnifiedRandom1458 random, out LavaSlimeDeathPreparation? prepared, Func<WorldItemDropStateUpdate, bool>? stageDrop = null)
    {
        prepared = null;
        if (tiles is null || liquidMutations is null || liquidSimulator is null ||
            !float.IsFinite(x) || !float.IsFinite(y) || width <= 0 || height <= 0) return false;
        float left = lowTiles ? x / 16f : (x + width * 0.5f) / 16f;
        float top = lowTiles ? y / 16f : (y + height * 0.5f) / 16f;
        float right = lowTiles ? (x + width) / 16f : left;
        float bottom = lowTiles ? (y + height) / 16f : top;
        if (left < 0 || top < 0 || right >= tiles.Dimensions.WidthTiles || bottom >= tiles.Dimensions.HeightTiles ||
            (long)((int)right - (int)left + 1) * ((int)bottom - (int)top + 1) > 64) return false;
        var overlay = new ToolWorldTileOverlay1458(tiles);
        var water = liquidMutations.CreateSourceWaterPreparation(liquidSimulator);
        // Share source framing and KillTile over this death's tile/RNG/item journal.
        // Objects requiring additional participants still refuse before adoption.
        var frame = new ToolWorldSourcePass1458(this, overlay, random, true,
            hardModeBaseline || progression.IsCompleted(VanillaWorldProgressionId.Hardmode), default, [], null);
        frame.deathDrop = stageDrop;
        for (int cellX = (int)left; cellX <= (int)right; cellX++)
            for (int cellY = (int)top; cellY <= (int)bottom; cellY++)
            {
                if (!overlay.TryRead(cellX, cellY, out var before)) return false;
                // NPC.HitEffect uses WorldGen.SolidTile, including its distinction
                // from AddWater's half-brick and slope treatment.
                bool solid = before.IsActive && !before.IsActuated && !before.IsHalfBrick && before.SourceSlope == 0 &&
                    VanillaTileCollisionCatalog.IsSolid(before.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(before.TileType);
                if (solid || before.LiquidAmount != 0) continue;
                var after = before;
                after.LiquidAmount = lowTiles ? byte.MaxValue : checked((byte)random.Next(50, 150));
                after.LiquidKind = WorldLiquidKind.Lava;
                if (!overlay.TryStage(cellX, cellY, after) ||
                    !frame.TryFrameRetainedNpcDeathSquare(cellX, cellY, water)) return false;
            }
        prepared = new(this, overlay, frame, water);
        return prepared.IsCurrent;
    }

    internal sealed class LavaSlimeDeathPreparation(WorldTileAuthority owner, ToolWorldTileOverlay1458 overlay,
        ToolWorldSourcePass1458 frame, VanillaWorldLiquidMutationService.SourceWaterPreparation1458 water)
    {
        private bool adopted;
        private bool published;
        internal bool IsCurrent => !adopted && overlay.IsCurrentOwned && frame.IsCurrentOwned && water.IsCurrent;
        internal bool TryPublishTileKills()
        {
            if (!adopted || published) return false;
            published = true;
            foreach (var cell in frame.DeathKills)
            {
                // Item-publication observers may have replaced the adopted terrain.
                // Never send a stale kill notice for that replacement.
                if (!overlay.IsAcceptedCurrent) break;
                var state = new TerrariaTileManipulationState((byte)TerrariaTileManipulationAction.KillTile,
                    checked((short)cell.X), checked((short)cell.Y), 0, 0);
                owner.replication?.TryPublishCommitted(default, in state);
            }
            return true;
        }
        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            if (!overlay.TryAdoptUnpublished() || !water.TryAdoptUnpublished())
                throw new InvalidOperationException("Admitted lava death lost callback-free terrain/liquid adoption.");
            adopted = true;
            // The existing replication registry coalesces liquid coordinates and
            // flushes with TickLiquids, like Liquid.NetSendLiquid's pending set.
            foreach (var cell in water.NetworkChanges)
            {
                var tile = overlay.Tiles.Get(cell.X, cell.Y);
                var state = new TerrariaLiquidState(checked((short)cell.X), checked((short)cell.Y), tile.LiquidAmount, (byte)tile.LiquidKind);
                owner.replication?.RetainSourceLiquid(in state);
            }
            return true;
        }
    }

    internal sealed partial class ToolWorldSourcePass1458
    {
        private delegate bool DeathWaterOffer(int x, int y, in WorldTile tile, out bool destroy);
        internal Func<WorldItemDropStateUpdate, bool>? deathDrop;
        private readonly List<(int X, int Y)> deathKills = [];
        internal ReadOnlySpan<(int X, int Y)> DeathKills => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(deathKills);
        private DeathWaterOffer? deathWater;

        internal bool TryFrameRetainedNpcDeathSquare(int x, int y,
            VanillaWorldLiquidMutationService.SourceWaterPreparation1458 water) =>
            TryFrameRetainedNpcDeathSquareCore(x, y, water.TryAddWater);

        private bool TryFrameRetainedNpcDeathSquareCore(int x, int y, DeathWaterOffer offer)
        {
            if (!clientRole || server is not null || work.Count != 0 || deathWater is not null || !IsCurrentOwned) return Reject();
            deathWater = offer;
            try { return PushSquare(x, y) && Drain(); }
            finally { deathWater = null; }
        }

        private bool TryPrepareDeathFrame(in SourceStep step, in WorldTile tile, out bool deferred)
        {
            deferred = false;
            if (deathWater is null) return true;
            if (tile.IsActive && (!VanillaTileDefinitionCatalog.TryGet(tile.TileType, out var definition) ||
                definition.IsFrameImportant && tile.Type is not (19 or 137) || IsSourceFallingTile(tile.Type) || IsSourcePlant(tile.Type) ||
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
