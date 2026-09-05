using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Per-connection packet-10 visibility state. Terraria joins with only a bounded section window, so a playing
/// connection must continue receiving unseen tile sections as its authoritative movement crosses network-section
/// boundaries. This tracker owns only which immutable sections have already been transferred to one client.
/// </summary>
internal sealed class PlayerSectionStreamingState
{
    private const float TileSizePixels = 16f;
    private const int HorizontalRadiusSections = 2;
    private const int VerticalRadiusSections = 1;

    public const int MaximumWindowSectionCount =
        (HorizontalRadiusSections * 2 + 1) * (VerticalRadiusSections * 2 + 1);

    private readonly WorldDimensions dimensions;
    private readonly bool[] sent;
    private int sentCount;

    public PlayerSectionStreamingState(WorldDimensions dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        this.dimensions = dimensions;
        sent = new bool[dimensions.SectionCount];
    }

    internal int SentSectionCount => sentCount;

    public void ObserveBootstrap(
        ReadOnlySpan<WorldSectionId> baseSections,
        int requestedTileX,
        int requestedTileY)
    {
        MarkSent(baseSections);

        Span<WorldSectionId> requested =
            stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumRequestedSectionCount];
        int requestedCount = InitialSectionBootstrapPlanner.PlanRequestedSections(
            dimensions,
            requestedTileX,
            requestedTileY,
            requested);
        MarkSent(requested[..requestedCount]);
    }

    /// <summary>
    /// Plans the unseen 5x3 section window around a player position. The caller marks each section only after the
    /// corresponding packet-10 frame has actually entered the outbound queue, so backpressure never creates false
    /// visibility state. Invalid/non-finite positions simply request no world data.
    /// </summary>
    public int PlanUnsent(
        float positionX,
        float positionY,
        Span<WorldSectionId> destination)
    {
        if (destination.Length < MaximumWindowSectionCount)
        {
            throw new ArgumentException(
                $"Destination must hold at least {MaximumWindowSectionCount} sections.",
                nameof(destination));
        }

        if (!float.IsFinite(positionX) || !float.IsFinite(positionY))
            return 0;

        int tileX = (int)MathF.Floor(positionX / TileSizePixels);
        int tileY = (int)MathF.Floor(positionY / TileSizePixels);
        if ((uint)tileX >= (uint)dimensions.WidthTiles ||
            (uint)tileY >= (uint)dimensions.HeightTiles)
        {
            return 0;
        }

        WorldSectionId center = TerrariaSectionGeometry.FromTile(dimensions, tileX, tileY);
        int minX = Math.Max(0, center.X - HorizontalRadiusSections);
        int maxX = Math.Min(dimensions.SectionColumns - 1, center.X + HorizontalRadiusSections);
        int minY = Math.Max(0, center.Y - VerticalRadiusSections);
        int maxY = Math.Min(dimensions.SectionRows - 1, center.Y + VerticalRadiusSections);

        int count = 0;
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                var section = new WorldSectionId(x, y);
                int index = TerrariaSectionGeometry.ToLinearIndex(dimensions, section);
                if (!sent[index])
                    destination[count++] = section;
            }
        }

        return count;
    }

    public void MarkSent(WorldSectionId section)
    {
        TerrariaSectionGeometry.ValidateSection(dimensions, section);
        int index = TerrariaSectionGeometry.ToLinearIndex(dimensions, section);
        if (sent[index])
            return;

        sent[index] = true;
        sentCount++;
    }

    private void MarkSent(ReadOnlySpan<WorldSectionId> sections)
    {
        for (int i = 0; i < sections.Length; i++)
            MarkSent(sections[i]);
    }
}
