using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    private void BreakDungeonDebris(int i, int j)
    {
        if (SourceClient)
            return;
        var effects = workspace?.EntityEffects ?? throw new NotSupportedException(
            "Dungeon debris destruction requires the canonical generation projectile owner.");
        ReadOnlySpan<(int X, int Y)> neighbours = [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)];
        foreach (var offset in neighbours)
        {
            int x = i + offset.X, y = j + offset.Y;
            WorldTile neighbour = At(x, y);
            if (neighbour.IsActive && random.Next(offset is (0, -1) ? 3 : 6) == 0 &&
                neighbour.Type is >= 481 and <= 483)
            {
                // Source retires the parent before the child can walk back to it.
                At(i, j).Flags &= ~WorldTileFlags.Active;
                KillTile(x, y);
            }
        }
        ushort type = At(i, j).Type;
        effects.SpawnProjectile(new(type - 481 + 736), workspace!.GenerationMyPlayer,
            i * 16 + 3, j * 16 + 3, 0, .41f, 20, 0, default, SourceServer);
    }
}
