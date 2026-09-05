namespace TerraRuntime.World;

/// <summary>
/// Immutable row-major tile image of one Terraria network section captured at a specific mutation revision.
/// The authoritative game thread creates snapshots; asynchronous encoders consume them without touching live tiles.
/// </summary>
public sealed class WorldSectionTileSnapshot
{
    private readonly WorldTile[] _tiles;

    internal WorldSectionTileSnapshot(
        WorldSectionId section,
        WorldTileRegion bounds,
        long revision,
        WorldTile[] tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (tiles.Length != checked(bounds.Width * bounds.Height))
            throw new ArgumentException("Section snapshot tile count does not match its bounds.", nameof(tiles));

        Section = section;
        Bounds = bounds;
        Revision = revision;
        _tiles = tiles;
    }

    public WorldSectionId Section { get; }

    public WorldTileRegion Bounds { get; }

    public long Revision { get; }

    public int Count => _tiles.Length;

    public ReadOnlyMemory<WorldTile> Tiles => _tiles;

    public WorldTile Get(int worldX, int worldY)
    {
        if ((uint)(worldX - Bounds.X) >= (uint)Bounds.Width)
            throw new ArgumentOutOfRangeException(nameof(worldX));
        if ((uint)(worldY - Bounds.Y) >= (uint)Bounds.Height)
            throw new ArgumentOutOfRangeException(nameof(worldY));

        return GetUnchecked(worldX, worldY);
    }

    internal WorldTile GetUnchecked(int worldX, int worldY) =>
        _tiles[((worldY - Bounds.Y) * Bounds.Width) + (worldX - Bounds.X)];
}
