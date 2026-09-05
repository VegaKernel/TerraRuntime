using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Optimized;

internal static partial class UndergroundMorphology
{
    private static double FractalNoise2D(
        ulong seed,
        double x,
        double y,
        double baseScale,
        int octaves)
    {
        double value = 0d;
        double amplitude = 1d;
        double total = 0d;
        double scale = baseScale;
        for (int octave = 0; octave < octaves; octave++)
        {
            value += ValueNoise2D(seed + unchecked((ulong)octave * 0x9E3779B97F4A7C15UL), x / scale, y / scale) * amplitude;
            total += amplitude;
            amplitude *= 0.5d;
            scale *= 0.5d;
        }
        return total <= 0d ? 0d : value / total;
    }

    private static double ValueNoise2D(ulong seed, double x, double y)
    {
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        int x1 = x0 + 1;
        int y1 = y0 + 1;
        double tx = SmoothStep(x - x0);
        double ty = SmoothStep(y - y0);
        double a = HashSigned(seed, x0, y0);
        double b = HashSigned(seed, x1, y0);
        double c = HashSigned(seed, x0, y1);
        double d = HashSigned(seed, x1, y1);
        double top = a + (b - a) * tx;
        double bottom = c + (d - c) * tx;
        return top + (bottom - top) * ty;
    }

    private static double HashSigned(ulong seed, int x, int y) => Hash01(seed, x, y) * 2d - 1d;

    private static double Hash01(ulong seed, int coordinate) =>
        Hash01(seed, coordinate, unchecked(coordinate * 31 + 17));

    private static double Hash01(ulong seed, int x, int y)
    {
        ulong value = seed;
        value ^= unchecked((ulong)(long)x) * 0x9E3779B97F4A7C15UL;
        value ^= unchecked((ulong)(long)y) * 0xD1B54A32D192ED03UL;
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (value >> 11) * (1d / (1UL << 53));
    }

    private static double SmoothStep(double value)
    {
        value = Math.Clamp(value, 0d, 1d);
        return value * value * (3d - 2d * value);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0d, 1d);

    private static void Normalize(ref double x, ref double y, double fallbackX, double fallbackY)
    {
        double length = Math.Sqrt(x * x + y * y);
        if (length <= 0.000001d)
        {
            x = fallbackX;
            y = fallbackY;
            length = Math.Sqrt(x * x + y * y);
            if (length <= 0.000001d)
            {
                x = 1d;
                y = 0d;
                return;
            }
        }
        x /= length;
        y /= length;
    }

    private static void MarkPlanCoverage(
        double x,
        double y,
        int width,
        int minimumY,
        int maximumY,
        ref ulong horizontalMask,
        ref byte verticalMask)
    {
        int horizontal = Math.Clamp((int)Math.Floor(x / Math.Max(1d, width) * 12d), 0, 11);
        int vertical = Math.Clamp((int)Math.Floor((y - minimumY) / Math.Max(1d, maximumY - minimumY + 1d) * 4d), 0, 3);
        horizontalMask |= 1UL << horizontal;
        verticalMask |= checked((byte)(1 << vertical));
    }

    private static int PopCount(ulong value)
    {
        int count = 0;
        while (value != 0)
        {
            value &= value - 1;
            count++;
        }
        return count;
    }

    private static int PopCount(byte value) => PopCount((ulong)value);

    private static ulong FingerprintTunnel(ulong fingerprint, TunnelSpec feature)
    {
        fingerprint = Fingerprint(fingerprint, feature.Seed);
        fingerprint = Fingerprint(fingerprint, Quantize(feature.X));
        fingerprint = Fingerprint(fingerprint, Quantize(feature.Y));
        fingerprint = Fingerprint(fingerprint, Quantize(feature.DirectionX));
        fingerprint = Fingerprint(fingerprint, Quantize(feature.DirectionY));
        fingerprint = Fingerprint(fingerprint, feature.Steps);
        fingerprint = Fingerprint(fingerprint, Quantize(feature.Radius));
        return fingerprint;
    }

    private static ulong Fingerprint(ulong current, int value) => Fingerprint(current, unchecked((ulong)(uint)value));

    private static ulong Fingerprint(ulong current, ulong value)
    {
        current ^= value;
        current *= 1099511628211UL;
        return current;
    }

    private static int Quantize(double value) => checked((int)Math.Round(value * 1024d));

    private sealed class CarveAccumulator(int width, int minimumY, int maximumY, int rockLayer)
    {
        private ulong touchedSectors;

        public int CarvedTiles { get; private set; }
        public int UpperBandCarvedTiles { get; private set; }
        public int DeepBandCarvedTiles { get; private set; }
        public int TouchedSectorCount => PopCount(touchedSectors);

        public void Record(int x, int y)
        {
            CarvedTiles++;
            if (y < rockLayer)
                UpperBandCarvedTiles++;
            else
                DeepBandCarvedTiles++;

            int sectorX = Math.Clamp((int)((long)x * 12L / Math.Max(1, width)), 0, 11);
            int sectorY = Math.Clamp(
                (int)((long)(y - minimumY) * 4L / Math.Max(1, maximumY - minimumY + 1)),
                0,
                3);
            touchedSectors |= 1UL << (sectorY * 12 + sectorX);
        }
    }
}
