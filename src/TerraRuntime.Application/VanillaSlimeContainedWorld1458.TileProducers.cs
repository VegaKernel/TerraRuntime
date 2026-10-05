using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class VanillaSlimeContainedWorld1458
{
    private sealed partial class CapturedWorld
    {
        private readonly List<(int X, int Y, WorldTile Tile)> producerTiles = [];
        private (int X, int Y, bool Checking)? producerLiquid;
        private (int X, int Y)? producerSquare;
        private const int TileSizePixels = 16;
        private const int IsolatedWebFrameX = 162;
        private const int IsolatedWebFrameY = 54;
        private const int FrameStepPixels = 18;

        private bool ProducerIsCurrent() => producerLiquid is not { } claim ||
            tiles.LiquidUpdates.IsCheckingLiquid(claim.X, claim.Y) == claim.Checking;

        public bool TryPlanTileProducer(int item, in NpcSnapshot parent,
            IVanillaNpcRandom random, out bool grew)
        {
            grew = false;
            if (!IsCurrent || !VanillaNpcDefinitionCatalog.TryGet(parent.TypeIdentity, parent.NetIdentity, out var definition) ||
                !definition.TryResolveHitbox(parent.Simulation, out var body)) return false;
            if (item == VanillaItemIds.DaybloomSeeds.Value)
            {
                // Dust.NewDust returns immediately on a dedicated server. Its actual Next60 offer remains.
                _ = random.NextInt32(0, 60);
                if (parent.VelocityY != 0f || parent.Simulation.LocalAi.Ai3 >= 5f ||
                    random.NextInt32(0, 180) != 0) return true;
                int x = (int)((parent.PositionX + body.Width * .5f) / TileSizePixels);
                int y = (int)((parent.PositionY + body.Height - 8f) / TileSizePixels);
                // Source WorldGen.InWorld has no default margin; the support read must also be representable.
                if (x < 0 || y < 0 || x >= tiles.Dimensions.WidthTiles || y >= tiles.Dimensions.HeightTiles)
                    return true;
                if (y + 1 >= tiles.Dimensions.HeightTiles) return false;
                WorldTile before = tiles.Get(x, y), support = tiles.Get(x, y + 1);
                if (VanillaHerbPlacement1458.TryPlan(x, tiles.Dimensions.WidthTiles, in before, in support, out var after))
                { producerTiles.Add((x, y, after)); producerSquare = (x, y); grew = true; }
                return IsCurrent;
            }
            if (item != VanillaItemIds.Cobweb.Value) return false;
            if (random.NextInt32(0, 120) != 0) return true;
            int webX = random.NextInt32((int)(parent.PositionX / TileSizePixels),
                (int)((parent.PositionX + body.Width) / TileSizePixels) + 1);
            int webY = random.NextInt32((int)(parent.PositionY / TileSizePixels),
                (int)((parent.PositionY + body.Height) / TileSizePixels) + 1);
            if (webX < 1 || webY < 1 || webX >= tiles.Dimensions.WidthTiles - 1 || webY >= tiles.Dimensions.HeightTiles - 1)
                return false;
            WorldTile web = tiles.Get(webX, webY);
            if (web.IsActive) return true;
            producerSquare = (webX, webY);
            bool checking = tiles.LiquidUpdates.IsCheckingLiquid(webX, webY);
            producerLiquid = (webX, webY, checking);
            // Source still sends20 after a failed dry-check, but performs no framing or tile mutation.
            if (web.LiquidAmount > 0 || checking) return IsCurrent;
            // General neighboring objects/solids have independent framing and break effects.
            // Admit this complete isolated frame arm, including all eight inactive-cell cleanups.
            if (webX <= 6 || webY <= 6 || webX >= tiles.Dimensions.WidthTiles - 6 || webY >= tiles.Dimensions.HeightTiles - 6)
                return false;
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                WorldTile adjacent = tiles.Get(webX + dx, webY + dy);
                if (adjacent.IsActive || adjacent.LiquidAmount > 0) return false;
                adjacent.Shape = 0; adjacent.TileColor = 0;
                adjacent.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
                producerTiles.Add((webX + dx, webY + dy, adjacent));
            }
            // PlaceTile clears Tile/TilePaint/Slope, then SquareTileFrame resets only the center bank.
            web.Type = (ushort)VanillaTileIds.Cobweb.Value; web.Shape = 0; web.TileColor = 0;
            web.Flags = (web.Flags & ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock)) | WorldTileFlags.Active;
            web.FrameX = (short)(IsolatedWebFrameX + random.NextInt32(0, 3) * FrameStepPixels);
            web.FrameY = IsolatedWebFrameY;
            producerTiles.Add((webX, webY, web));
            return IsCurrent;
        }

        public void CommitTileProducer()
        {
            // The actor/RNG adoption immediately before this call has no callbacks for these two producers.
            foreach (var cell in producerTiles) tiles.Set(cell.X, cell.Y, in cell.Tile);
            if (producerSquare is { } square)
                tileReplication?.TryPublishTileSquareToAll(tiles, square.X, square.Y, 1, 1,
                    VanillaTileChangeType1458.None, respectSectionRange: true);
        }
    }
}
