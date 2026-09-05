using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Optimized;

internal static partial class UndergroundMorphology
{
    private static void CarveCheese(
        IWorldGenerationWorkspace workspace,
        CheeseSpec feature,
        int minimumY,
        int maximumY,
        Func<int, int, bool> isProtected,
        CarveAccumulator accumulator)
    {
        int radiusX = (int)Math.Ceiling(feature.RadiusX + 4d);
        int radiusY = (int)Math.Ceiling(feature.RadiusY + 4d);
        double cos = Math.Cos(feature.Rotation);
        double sin = Math.Sin(feature.Rotation);

        for (int x = Math.Max(1, (int)Math.Floor(feature.X) - radiusX);
             x <= Math.Min(workspace.WidthTiles - 2, (int)Math.Ceiling(feature.X) + radiusX);
             x++)
        {
            for (int y = Math.Max(minimumY, (int)Math.Floor(feature.Y) - radiusY);
                 y <= Math.Min(maximumY, (int)Math.Ceiling(feature.Y) + radiusY);
                 y++)
            {
                double dx = x - feature.X;
                double dy = y - feature.Y;
                double rx = dx * cos + dy * sin;
                double ry = -dx * sin + dy * cos;
                double nx = rx / feature.RadiusX;
                double ny = ry / feature.RadiusY;
                double radial = nx * nx + ny * ny;
                if (radial > 1.45d)
                    continue;

                double warp = FractalNoise2D(feature.Seed ^ WarpSeed, x, y, 17d, 2) * 0.18d;
                double scallop = Math.Sin((nx * 3.1d + ny * 2.4d) * Math.PI + feature.Rotation) * 0.055d;
                if (radial + warp + scallop <= 1d)
                    TryCarve(workspace, x, y, minimumY, maximumY, isProtected, accumulator);
            }
        }
    }

    private static void CarveConnector(
        IWorldGenerationWorkspace workspace,
        ulong seed,
        CheeseSpec a,
        CheeseSpec b,
        int minimumY,
        int maximumY,
        Func<int, int, bool> isProtected,
        CarveAccumulator accumulator)
    {
        double midX = (a.X + b.X) * 0.5d;
        double midY = (a.Y + b.Y) * 0.5d;
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        double normalX = distance <= 0.001d ? 0d : -dy / distance;
        double normalY = distance <= 0.001d ? 1d : dx / distance;
        double bend = (Hash01(seed, 1) - 0.5d) * Math.Min(70d, distance * 0.34d);
        double controlX = midX + normalX * bend;
        double controlY = Math.Clamp(midY + normalY * bend * 0.55d, minimumY + 4d, maximumY - 4d);
        int steps = Math.Clamp((int)Math.Ceiling(distance / 1.65d), 12, 520);

        for (int step = 0; step <= steps; step++)
        {
            double t = step / (double)steps;
            double oneMinus = 1d - t;
            double x = oneMinus * oneMinus * a.X + 2d * oneMinus * t * controlX + t * t * b.X;
            double y = oneMinus * oneMinus * a.Y + 2d * oneMinus * t * controlY + t * t * b.Y;
            double radius = 2.2d + (FractalNoise2D(seed ^ WarpSeed, x, y, 24d, 2) + 1d) * 0.55d;
            CarveCircle(workspace, x, y, radius, minimumY, maximumY, isProtected, accumulator);
        }
    }

    private static void CarveSpaghetti(
        IWorldGenerationWorkspace workspace,
        TunnelSpec tunnel,
        int minimumY,
        int maximumY,
        Func<int, int, bool> isProtected,
        CarveAccumulator accumulator)
    {
        double x = tunnel.X;
        double y = tunnel.Y;
        double driftX = tunnel.DirectionX;
        double driftY = tunnel.DirectionY * 0.72d;
        double scale = 78d + Hash01(tunnel.Seed, 17) * 58d;

        for (int step = 0; step < tunnel.Steps; step++)
        {
            if (x < 3d || x >= workspace.WidthTiles - 3d || y < minimumY + 2d || y > maximumY - 2d)
                break;

            double breathing = 0.72d + 0.28d * Math.Sin(tunnel.Phase + step * 0.093d);
            double localRadius = Math.Max(1.8d, tunnel.Radius * breathing);
            CarveCircle(workspace, x, y, localRadius, minimumY, maximumY, isProtected, accumulator);

            (double curlX, double curlY) = CurlField(tunnel.Seed, x, y, scale);
            double vx = curlX * 0.92d + driftX * 0.68d;
            double vy = curlY * 0.76d + driftY * 0.68d;
            Normalize(ref vx, ref vy, driftX, driftY);
            x += vx * 1.75d;
            y += vy * 1.35d;
        }
    }

    private static void CarveNoodle(
        IWorldGenerationWorkspace workspace,
        TunnelSpec tunnel,
        int minimumY,
        int maximumY,
        Func<int, int, bool> isProtected,
        CarveAccumulator accumulator)
    {
        double x = tunnel.X;
        double y = tunnel.Y;
        double angle = Math.Atan2(tunnel.DirectionY, tunnel.DirectionX);

        for (int step = 0; step < tunnel.Steps; step++)
        {
            if (x < 3d || x >= workspace.WidthTiles - 3d || y < minimumY + 2d || y > maximumY - 2d)
                break;

            double pulse = 0.82d + 0.18d * Math.Sin(tunnel.Phase + step * 0.17d);
            CarveCircle(
                workspace,
                x,
                y,
                Math.Max(1.1d, tunnel.Radius * pulse),
                minimumY,
                maximumY,
                isProtected,
                accumulator);

            double steering = FractalNoise2D(tunnel.Seed ^ WarpSeed, x, y, 38d, 2) * 0.22d;
            steering += Math.Sin(tunnel.Phase + step * 0.071d) * 0.035d;
            angle += steering;
            x += Math.Cos(angle) * 1.52d;
            y += Math.Sin(angle) * 1.12d;
        }
    }

    private static (double X, double Y) CurlField(ulong seed, double x, double y, double scale)
    {
        const double epsilon = 2.5d;
        double dY = FractalNoise2D(seed, x, y + epsilon, scale, 3) -
                    FractalNoise2D(seed, x, y - epsilon, scale, 3);
        double dX = FractalNoise2D(seed, x + epsilon, y, scale, 3) -
                    FractalNoise2D(seed, x - epsilon, y, scale, 3);

        double x2 = FractalNoise2D(seed ^ 0x9E3779B97F4A7C15UL, x, y + epsilon, scale * 0.53d, 2) -
                    FractalNoise2D(seed ^ 0x9E3779B97F4A7C15UL, x, y - epsilon, scale * 0.53d, 2);
        double y2 = FractalNoise2D(seed ^ 0x9E3779B97F4A7C15UL, x + epsilon, y, scale * 0.53d, 2) -
                    FractalNoise2D(seed ^ 0x9E3779B97F4A7C15UL, x - epsilon, y, scale * 0.53d, 2);

        double vx = dY + x2 * 0.48d;
        double vy = -dX - y2 * 0.48d;
        Normalize(ref vx, ref vy, 1d, 0d);
        return (vx, vy);
    }

    private static void CarveCircle(
        IWorldGenerationWorkspace workspace,
        double centerX,
        double centerY,
        double radius,
        int minimumY,
        int maximumY,
        Func<int, int, bool> isProtected,
        CarveAccumulator accumulator)
    {
        int r = Math.Max(2, (int)Math.Ceiling(radius));
        double rr = radius * radius;
        int cx = (int)Math.Round(centerX);
        int cy = (int)Math.Round(centerY);
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                if (dx * dx + dy * dy > rr)
                    continue;
                TryCarve(workspace, cx + dx, cy + dy, minimumY, maximumY, isProtected, accumulator);
            }
        }
    }

    private static void TryCarve(
        IWorldGenerationWorkspace workspace,
        int x,
        int y,
        int minimumY,
        int maximumY,
        Func<int, int, bool> isProtected,
        CarveAccumulator accumulator)
    {
        if ((uint)x >= (uint)workspace.WidthTiles || y < minimumY || y > maximumY ||
            (uint)y >= (uint)workspace.HeightTiles || isProtected(x, y))
        {
            return;
        }
        if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
            (tile.Flags & WorldGenerationTileFlags.Active) == 0 ||
            !IsNaturalTerrain(tile.Type))
        {
            return;
        }

        var air = new WorldGenerationTile(
            Type: 0,
            Wall: 0,
            FrameX: 0,
            FrameY: 0,
            Flags: WorldGenerationTileFlags.None,
            LiquidAmount: 0,
            TileColor: 0,
            WallColor: 0,
            Shape: 0,
            LiquidKind: WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in air))
            throw new InvalidOperationException($"Optimized underground morphology could not carve tile ({x}, {y}).");
        accumulator.Record(x, y);
    }

    private static bool IsNaturalTerrain(ushort type) =>
        type is Dirt or Stone or Grass or CorruptGrass or Ebonstone or Sand or Mud or JungleGrass or MushroomGrass or
            Snow or Ice or CrimsonGrass or Crimstone;
}
