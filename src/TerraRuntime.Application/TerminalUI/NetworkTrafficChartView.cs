using System.Globalization;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;
using TuiColor = Terminal.Gui.Drawing.Color;

namespace TerraRuntime.Application.TerminalUI;

internal readonly record struct NetworkTrafficSample(
    double InboundKiBPerSecond,
    double OutboundKiBPerSecond);

/// <summary>
/// Overlayed inbound/outbound network throughput bars with independent vertical scales.
/// The left scale belongs to inbound traffic and the right scale belongs to outbound traffic, so a quiet direction
/// remains visible even when the opposite direction is orders of magnitude larger.
/// </summary>
internal sealed class NetworkTrafficChartView : View
{
    private const int AxisWidth = 8;
    private static readonly TuiAttribute InboundAttribute = new(TuiColor.BrightCyan, TuiColor.Black);
    private static readonly TuiAttribute OutboundAttribute = new(TuiColor.BrightYellow, TuiColor.Black);
    private static readonly TuiAttribute OverlapAttribute = new(TuiColor.BrightYellow, TuiColor.BrightCyan);
    private static readonly TuiAttribute GridAttribute = new(TuiColor.DarkGray, TuiColor.Black);

    private NetworkTrafficSample[] samples = [];
    private double inboundScaleMaximum = 1d;
    private double outboundScaleMaximum = 1d;

    internal double InboundScaleMaximumForSmoke => inboundScaleMaximum;
    internal double OutboundScaleMaximumForSmoke => outboundScaleMaximum;
    internal int PlotWidthForSmoke => Math.Max(0, Viewport.Width - AxisWidth * 2);

    internal void SetSamples(NetworkTrafficSample[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        samples = value;
        inboundScaleMaximum = CalculateScaleMaximum(value, static sample => sample.InboundKiBPerSecond);
        outboundScaleMaximum = CalculateScaleMaximum(value, static sample => sample.OutboundKiBPerSecond);
        SetNeedsDraw();
    }

    internal (int InboundHeight, int OutboundHeight) GetBarHeightsForSmoke(int sampleIndex, int plotHeight)
    {
        if ((uint)sampleIndex >= (uint)samples.Length)
            throw new ArgumentOutOfRangeException(nameof(sampleIndex));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plotHeight);
        NetworkTrafficSample sample = samples[sampleIndex];
        return (
            ScaleHeight(sample.InboundKiBPerSecond, inboundScaleMaximum, plotHeight),
            ScaleHeight(sample.OutboundKiBPerSecond, outboundScaleMaximum, plotHeight));
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        base.OnDrawingContent(context);

        int width = Viewport.Width;
        int height = Viewport.Height;
        if (width <= AxisWidth * 2 || height <= 0)
            return true;

        int plotX = AxisWidth;
        int plotWidth = width - AxisWidth * 2;
        int rightAxisX = plotX + plotWidth;

        DrawAxisLabels(height, rightAxisX);
        if (samples.Length == 0 || plotWidth <= 0)
            return true;

        for (int column = 0; column < plotWidth; column++)
        {
            int sampleIndex = MapSampleIndex(column, plotWidth, samples.Length);
            NetworkTrafficSample sample = samples[sampleIndex];
            int inboundHeight = ScaleHeight(sample.InboundKiBPerSecond, inboundScaleMaximum, height);
            int outboundHeight = ScaleHeight(sample.OutboundKiBPerSecond, outboundScaleMaximum, height);

            for (int row = 0; row < height; row++)
            {
                int level = height - row;
                bool inbound = inboundHeight >= level;
                bool outbound = outboundHeight >= level;
                if (!inbound && !outbound)
                    continue;

                if (inbound && outbound)
                {
                    SetAttribute(OverlapAttribute);
                    AddRune(plotX + column, row, new Rune('▒'));
                }
                else if (inbound)
                {
                    SetAttribute(InboundAttribute);
                    AddRune(plotX + column, row, new Rune('█'));
                }
                else
                {
                    SetAttribute(OutboundAttribute);
                    AddRune(plotX + column, row, new Rune('▓'));
                }
            }
        }

        return true;
    }

    private void DrawAxisLabels(int height, int rightAxisX)
    {
        int middleRow = (height - 1) / 2;
        int bottomRow = height - 1;

        DrawLeftAxisRow(0, FormatScale(inboundScaleMaximum), "IN");
        if (middleRow > 0 && middleRow < bottomRow)
            DrawLeftAxisRow(middleRow, FormatScale(inboundScaleMaximum * 0.5d), null);
        if (bottomRow > 0)
            DrawLeftAxisRow(bottomRow, "0", null);

        DrawRightAxisRow(rightAxisX, 0, FormatScale(outboundScaleMaximum), "OUT");
        if (middleRow > 0 && middleRow < bottomRow)
            DrawRightAxisRow(rightAxisX, middleRow, FormatScale(outboundScaleMaximum * 0.5d), null);
        if (bottomRow > 0)
            DrawRightAxisRow(rightAxisX, bottomRow, "0", null);
    }

    private void DrawLeftAxisRow(int row, string value, string? title)
    {
        SetAttribute(InboundAttribute);
        string label = title is null ? value : $"{title} {value}";
        if (label.Length > AxisWidth - 1)
            label = label[^Math.Min(label.Length, AxisWidth - 1)..];
        AddStr(0, row, label.PadLeft(AxisWidth - 1));
        SetAttribute(GridAttribute);
        AddRune(AxisWidth - 1, row, new Rune('│'));
    }

    private void DrawRightAxisRow(int x, int row, string value, string? title)
    {
        SetAttribute(GridAttribute);
        AddRune(x, row, new Rune('│'));
        SetAttribute(OutboundAttribute);
        string label = title is null ? value : $"{value} {title}";
        if (label.Length > AxisWidth - 1)
            label = label[..Math.Min(label.Length, AxisWidth - 1)];
        AddStr(x + 1, row, label.PadRight(AxisWidth - 1));
    }

    private static int MapSampleIndex(int column, int plotWidth, int sampleCount)
    {
        if (sampleCount <= 1 || plotWidth <= 1)
            return 0;
        return Math.Clamp(
            (int)Math.Round(column * (sampleCount - 1d) / (plotWidth - 1d), MidpointRounding.AwayFromZero),
            0,
            sampleCount - 1);
    }

    private static int ScaleHeight(double value, double maximum, int height)
    {
        if (!double.IsFinite(value) || value <= 0d || height <= 0)
            return 0;
        double normalized = Math.Clamp(value / maximum, 0d, 1d);
        return Math.Clamp((int)Math.Ceiling(normalized * height), 1, height);
    }

    private static double CalculateScaleMaximum(
        IReadOnlyList<NetworkTrafficSample> values,
        Func<NetworkTrafficSample, double> selector)
    {
        double maximum = 0d;
        for (int i = 0; i < values.Count; i++)
        {
            double value = selector(values[i]);
            if (double.IsFinite(value) && value > maximum)
                maximum = value;
        }

        if (maximum <= 0d)
            return 1d;

        double raw = maximum * 1.10d;
        double power = Math.Pow(10d, Math.Floor(Math.Log10(raw)));
        double normalized = raw / power;
        double nice = normalized <= 1d ? 1d : normalized <= 2d ? 2d : normalized <= 5d ? 5d : 10d;
        return Math.Max(1d, nice * power);
    }

    private static string FormatScale(double kibPerSecond)
    {
        if (!double.IsFinite(kibPerSecond) || kibPerSecond <= 0d)
            return "0";
        if (kibPerSecond >= 1024d)
            return string.Create(CultureInfo.InvariantCulture, $"{kibPerSecond / 1024d:0.#}M");
        if (kibPerSecond >= 100d)
            return string.Create(CultureInfo.InvariantCulture, $"{kibPerSecond:0}K");
        if (kibPerSecond >= 10d)
            return string.Create(CultureInfo.InvariantCulture, $"{kibPerSecond:0.#}K");
        return string.Create(CultureInfo.InvariantCulture, $"{kibPerSecond:0.##}K");
    }
}
