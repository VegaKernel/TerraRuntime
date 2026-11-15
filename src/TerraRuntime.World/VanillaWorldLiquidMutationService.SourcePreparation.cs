namespace TerraRuntime.World;

public sealed partial class VanillaWorldLiquidMutationService
{
    internal SourceWaterPreparation1458 CreateSourceWaterPreparation(VanillaWorldLiquidSimulator1458 simulator)
    {
        if (!simulator.OwnsTiles(_tiles)) throw new ArgumentException("Liquid preparation requires the same world.", nameof(simulator));
        return new(_tiles, simulator);
    }

    // Detached AddWater journal, not a second queue. The caller owns the staged
    // tile image and its version witnesses. Queue dependencies are bounded cell
    // memberships/skip flags and counts, not a copy of the entire live queue.
    internal sealed class SourceWaterPreparation1458(WorldTileStore tiles, VanillaWorldLiquidSimulator1458 simulator)
    {
        private const int MaximumOffers = 4096;
        private readonly int capacity = simulator.CurrentMaximumLiquid1458;
        private readonly int activeBefore = tiles.LiquidUpdates.ActiveCount;
        private readonly int bufferedBefore = tiles.LiquidUpdates.BufferedCount;
        private readonly Dictionary<(int X, int Y), (bool Active, bool Buffered, bool Skip)> witnesses = [];
        private readonly HashSet<(int X, int Y)> planned = [];
        private readonly List<(int X, int Y)> active = [];
        private readonly List<(int X, int Y)> buffered = [];
        private int offers;
        private bool failed;
        private bool adopted;

        internal bool IsCurrent
        {
            get
            {
                var queue = tiles.LiquidUpdates;
                if (failed || adopted || simulator.CurrentMaximumLiquid1458 != capacity ||
                    queue.ActiveCount != activeBefore || queue.BufferedCount != bufferedBefore) return false;
                foreach (var (cell, before) in witnesses)
                    if (queue.IsQueued(cell.X, cell.Y) != before.Active || queue.IsBuffered(cell.X, cell.Y) != before.Buffered ||
                        queue.IsSkipNextUpdate(cell.X, cell.Y) != before.Skip) return false;
                return true;
            }
        }

        internal ReadOnlySpan<(int X, int Y)> NetworkChanges => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(active);

        internal bool TryAddWater(int x, int y, in WorldTile virtualTile, out bool destroy)
        {
            destroy = false;
            if (!IsCurrent || offers++ == MaximumOffers) return Refuse();
            var dimensions = tiles.Dimensions;
            if (x < 5 || y < 5 || x >= dimensions.WidthTiles - 5 || y >= dimensions.HeightTiles - 5 || virtualTile.LiquidAmount == 0) return true;
            var cell = (x, y);
            var queue = tiles.LiquidUpdates;
            if (!witnesses.TryGetValue(cell, out var before))
            {
                before = (queue.IsQueued(x, y), queue.IsBuffered(x, y), queue.IsSkipNextUpdate(x, y));
                witnesses.Add(cell, before);
            }
            if (before.Active || before.Buffered || planned.Contains(cell)) return true;
            // AddWater uses nactive and the explicit tile546 exception. It does
            // not use SolidTile's slope/half-brick exclusions.
            if (virtualTile.IsActive && !virtualTile.IsActuated && virtualTile.Type != 546 &&
                VanillaTileCollisionCatalog.IsSolid(virtualTile.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(virtualTile.TileType)) return true;
            if (activeBefore + active.Count >= capacity - 1)
            {
                if (bufferedBefore + buffered.Count < VanillaWorldLiquidSimulator1458.LoadingBufferedLiquidCapacity1458)
                { planned.Add(cell); buffered.Add(cell); }
                return true;
            }
            if (virtualTile.IsActive)
            {
                if (!VanillaTileObjectLiquidDeath1458.TryGet(virtualTile, out bool waterDeath, out bool lavaDeath)) return Refuse();
                destroy = virtualTile.LiquidKind == WorldLiquidKind.Lava ? lavaDeath : waterDeath;
            }
            planned.Add(cell); active.Add(cell);
            return true;
        }

        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            adopted = true;
            var queue = tiles.LiquidUpdates;
            foreach (var cell in active)
            {
                if (!queue.TryEnqueue(cell.X, cell.Y, 0, 0)) throw new InvalidOperationException("Prepared liquid append lost its queue witness.");
                queue.ClearSkipNextUpdate(cell.X, cell.Y);
            }
            foreach (var cell in buffered)
                if (!queue.TryBuffer(cell.X, cell.Y)) throw new InvalidOperationException("Prepared liquid buffer append lost its queue witness.");
            return true;
        }

        private bool Refuse() { failed = true; return false; }
    }
}
