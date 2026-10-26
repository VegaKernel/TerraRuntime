using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        // WorldGen.KillTile 64310..64371: inspect each neighbor after the prior
        // recursive Kill returns. Active unrelated neighbors still consume the
        // chance draw; the current tile is deactivated before recursion.
        private bool CrackedNeighbor(in SourceStep step)
        {
            if (server?.SourceProjectiles is not { } store) return false;
            if (step.Index == 8)
            {
                int type = step.SourceTile.Type - 481 + 736;
                if (!VanillaDefinitionCatalog.TryGet(new(type), out var definition)) return false;
                var born = new ProjectileStateUpdate(new(type), byte.MaxValue,
                    step.X * 16 + 8 - definition.Width * .5f, step.Y * 16 + 8 - definition.Height * .5f,
                    0, .41f, default, 0, 20, 0, 0);
                return server.TryStageSourceProjectile(store, in born, false, out _) &&
                    Push(step with { Kind = StepKind.KillAfterSpecial });
            }
            (int dx, int dy) = step.Index switch
            {
                0 => (-1, 0), 1 => (1, 0), 2 => (0, -1), 3 => (0, 1),
                4 => (-1, -1), 5 => (1, -1), 6 => (-1, 1), _ => (1, 1)
            };
            int x = step.X + dx, y = step.Y + dy;
            if (!Overlay.TryReadSourceSafely(x, y, out var neighbor)) return false;
            if (neighbor.IsActive && random.Next(step.Index == 2 ? 3 : 6) == 0 && neighbor.Type is >= 481 and <= 483)
            {
                if (!Overlay.TryReadSourceSafely(step.X, step.Y, out var current)) return false;
                var inactive = current;
                inactive.Flags &= ~WorldTileFlags.Active;
                if (!Stage(WorldTileMutationKind.ClearTile, step.X, step.Y, in current, in inactive)) return false;
                return Push(step with { Kind = StepKind.CrackedAfterNeighbor }) &&
                    Push(new(StepKind.Kill, x, y, NoItem: true));
            }
            return Push(step with { Index = step.Index + 1 });
        }

        private bool CrackedAfterNeighbor(in SourceStep step)
        {
            (int dx, int dy) = step.Index switch
            {
                0 => (-1, 0), 1 => (1, 0), 2 => (0, -1), 3 => (0, 1),
                4 => (-1, -1), 5 => (1, -1), 6 => (-1, 1), _ => (1, 1)
            };
            return server is not null && server.TryStageSourceTileManipulation(step.X + dx, step.Y + dy, 20) &&
                Push(step with { Kind = StepKind.CrackedNeighbor, Index = step.Index + 1 });
        }
    }
}
