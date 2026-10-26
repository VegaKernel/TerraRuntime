using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        private bool fossilBreak;
        // WorldGen.AttemptFossilShattering: one global reentry flag around the
        // x-outer/y-inner nine-cell traversal, including source solidity reads.
        private bool FossilNeighbor(in SourceStep step)
        {
            if (step.Index == 9)
            {
                fossilBreak = false;
                return Push(step with { Kind = StepKind.KillAfterPreEffects });
            }
            int x = step.X - 1 + step.Index / 3, y = step.Y - 1 + step.Index % 3;
            if (!Overlay.TryReadSourceSafely(x, y + 1, out var below) ||
                !Overlay.TryReadSourceSafely(x, y, out var tile) ||
                !VanillaToolTileCatalog1458.TryGet(below.TileType, out var facts)) return false;
            bool solid = below.IsActive && !below.IsActuated && facts.IsSolid && !facts.IsSolidTop &&
                !below.IsHalfBrick && below.SourceSlope == 0;
            int chance = !solid || (x == step.X && y == step.Y - 1 && !step.Fail) ? 4 : 15;
            if ((x != step.X || y != step.Y) && tile.IsActive && tile.Type == 404 && random.Next(chance) == 0)
                return Push(step with { Kind = StepKind.FossilAfterNeighbor }) &&
                    Push(new(StepKind.Kill, x, y, NoItem: true));
            return Push(step with { Index = step.Index + 1 });
        }
        private bool FossilAfterNeighbor(in SourceStep step)
        {
            int x = step.X - 1 + step.Index / 3, y = step.Y - 1 + step.Index % 3;
            return server is not null && server.TryStageSourceTileManipulation(x, y, 0) &&
                Push(step with { Kind = StepKind.FossilNeighbor, Index = step.Index + 1 });
        }
    }
}
