using TerraRuntime.Contracts.Gameplay;
namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class EarlyPass1458
{
    private void ApplyWavyCaves(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IRandom random)
    {
        if (!HasSpecial(TerraRuntime.World.VanillaSpecialWorldSeed1458.TheConstant)) return;
        double scale = grid.Width / 4200d;
        int count = (int)(35d * (scale * scale));
        if (HasSpecial(TerraRuntime.World.VanillaSpecialWorldSeed1458.Remix)) count /= 3;
        var framing = new GenerationTileFraming1458(workspace.TileStore, context.VanillaRandom!, workspace, useWorldGenerationSolidity: true);
        int previous = 0;
        for (int offer = 0; offer < count; offer++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int y = random.Next((int)state.MainWorldSurface + 100, grid.Height - 300);
            int retries = 0;
            while (Math.Abs(y - previous) < 80)
            {
                if (++retries > 100) break;
                y = random.Next((int)state.MainWorldSurface + 100, grid.Height - 300);
            }
            previous = y;
            double progress = (double)offer / (count - 1);
            int x = 80 + (int)((grid.Width - 160) * progress);
            double height = 12 + random.Next(3, 6);
            double period = 0.25d + random.NextDouble();
            int steps = random.Next(300, 500);
            CarveWavyStroke(framing, grid, random, x, y, height, period, steps);
        }
    }

    private static void CarveWavyStroke(GenerationTileFraming1458 framing, RuntimeGrid grid, IRandom random,
        int startX, int startY, double height, double period, int steps)
    {
        double x = startX;
        int direction = startX > grid.Width / 2 ? -1 : 1;
        int narrow = 2 + random.Next(2), wide = 15 + random.Next(11), change = 1 + random.Next(2);
        int taper = (int)Math.Ceiling((double)wide / change);
        double amplitude = 1d, frequency = 1d;
        int drift = (int)(-1d + random.NextDouble() * 3d), size = narrow, waveTick = 0;
        for (int step = 0; step < steps; step++)
        {
            bool entrance = step < taper, exit = step >= steps - taper;
            x += direction;
            if (!entrance && !exit)
            {
                waveTick++;
                amplitude = Math.Min(2d, Math.Max(0.5d, amplitude + (-0.5d + random.NextDouble()) * 0.25d));
                frequency = Math.Min(1.1d, Math.Max(0.9d, frequency + (-0.5d + random.NextDouble()) * 0.02d));
            }
            double y = startY + Math.Sin(waveTick * 0.1d * frequency * period) * amplitude * height + waveTick * drift;
            int previousSize = size;
            if (entrance) size = Math.Min(wide, size + change);
            else if (exit) size = Math.Max(narrow, size - change);
            y -= (previousSize + size) / 4;
            for (int offset = 0; offset < size; offset++)
            {
                int column = (int)x, row = (int)y + offset;
                if (column >= 20 && column < grid.Width - 20 && row >= 20 && row < grid.Height - 20)
                    framing.KillTile(column, row);
            }
        }
    }
}
