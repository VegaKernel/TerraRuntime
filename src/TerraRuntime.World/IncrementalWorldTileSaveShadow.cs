namespace TerraRuntime.World;

/// <summary>
/// Incremental shadow of authoritative world tiles for persistence.
/// The game thread replaces immutable section snapshots as newer revisions arrive; capturing a save image clones
/// only the small section-reference table, never the complete tile array.
/// </summary>
public sealed class IncrementalWorldTileSaveShadow
{
    private readonly WorldSectionTileSnapshot?[] sections;
    private int initializedSectionCount;

    public IncrementalWorldTileSaveShadow(WorldDimensions dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        Dimensions = dimensions;
        sections = new WorldSectionTileSnapshot?[dimensions.SectionCount];
    }

    public WorldDimensions Dimensions { get; }

    public int InitializedSectionCount => initializedSectionCount;

    public bool IsComplete => initializedSectionCount == Dimensions.SectionCount;

    /// <summary>
    /// Applies one immutable section image. Equal or older revisions are ignored so delayed work cannot roll the
    /// persistence shadow backwards. The snapshot bounds must exactly match this world's section geometry.
    /// </summary>
    public bool TryApply(WorldSectionTileSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        int sectionIndex = TerrariaSectionGeometry.ToLinearIndex(Dimensions, snapshot.Section);
        WorldTileBounds expectedBounds = TerrariaSectionGeometry.GetBounds(Dimensions, snapshot.Section);
        if (snapshot.Bounds != expectedBounds)
        {
            throw new ArgumentException(
                "Section snapshot bounds do not match the save shadow world geometry.",
                nameof(snapshot));
        }

        WorldSectionTileSnapshot? current = sections[sectionIndex];
        if (current is not null && snapshot.Revision <= current.Revision)
            return false;

        sections[sectionIndex] = snapshot;
        if (current is null)
            initializedSectionCount++;

        return true;
    }

    public int Apply(ReadOnlySpan<WorldSectionTileSnapshot?> snapshots)
    {
        int applied = 0;
        foreach (WorldSectionTileSnapshot? snapshot in snapshots)
        {
            if (snapshot is not null && TryApply(snapshot))
                applied++;
        }

        return applied;
    }

    /// <summary>
    /// Captures a detached immutable image by copying only section references. Each referenced section already owns
    /// an immutable tile array, so later shadow updates replace references instead of mutating published save data.
    /// </summary>
    public bool TryCaptureImage(out WorldTileSaveImage? image)
    {
        if (!IsComplete)
        {
            image = null;
            return false;
        }

        var capturedSections = new WorldSectionTileSnapshot[sections.Length];
        for (int i = 0; i < sections.Length; i++)
        {
            capturedSections[i] = sections[i]
                ?? throw new InvalidOperationException("Complete save shadow contains an uninitialized section.");
        }

        image = new WorldTileSaveImage(Dimensions, capturedSections);
        return true;
    }
}

/// <summary>
/// Detached tile image composed from immutable section snapshots. It is safe for asynchronous serialization while
/// the authoritative world and its save shadow continue to advance independently.
/// </summary>
public sealed class WorldTileSaveImage
{
    private readonly WorldSectionTileSnapshot[] sections;

    internal WorldTileSaveImage(WorldDimensions dimensions, WorldSectionTileSnapshot[] sections)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(sections);
        if (sections.Length != dimensions.SectionCount)
            throw new ArgumentException("Save image section count does not match its dimensions.", nameof(sections));

        Dimensions = dimensions;
        this.sections = sections;
    }

    public WorldDimensions Dimensions { get; }

    public int Count => checked(Dimensions.WidthTiles * Dimensions.HeightTiles);

    public int SectionCount => sections.Length;

    public WorldSectionTileSnapshot GetSection(WorldSectionId section) =>
        sections[TerrariaSectionGeometry.ToLinearIndex(Dimensions, section)];

    public WorldTile Get(int x, int y)
    {
        if ((uint)x >= (uint)Dimensions.WidthTiles)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Dimensions.HeightTiles)
            throw new ArgumentOutOfRangeException(nameof(y));

        WorldSectionId section = TerrariaSectionGeometry.FromTile(Dimensions, x, y);
        return sections[TerrariaSectionGeometry.ToLinearIndex(Dimensions, section)].Get(x, y);
    }

    /// <summary>
    /// Reconstructs one vanilla .wld traversal column into caller-provided storage without touching live world state.
    /// The destination must be at least the world height; no allocation is performed by this method.
    /// </summary>
    public void CopyColumnTo(int x, Span<WorldTile> destination)
    {
        if ((uint)x >= (uint)Dimensions.WidthTiles)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (destination.Length < Dimensions.HeightTiles)
            throw new ArgumentException("Destination is shorter than the world height.", nameof(destination));

        int sectionX = x / TerrariaSectionGeometry.WidthTiles;
        for (int sectionY = 0; sectionY < Dimensions.SectionRows; sectionY++)
        {
            WorldSectionTileSnapshot snapshot = sections[(sectionY * Dimensions.SectionColumns) + sectionX];
            WorldTileBounds bounds = snapshot.Bounds;
            for (int y = bounds.Y; y < bounds.ExclusiveBottom; y++)
                destination[y] = snapshot.GetUnchecked(x, y);
        }
    }
}
