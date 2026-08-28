namespace TerraRuntime.World;

/// <summary>
/// Wall-clock breakdown for one canonical .wld load. The profile is diagnostic only and never
/// participates in validation or changes loader behavior.
/// </summary>
public readonly record struct WorldFileLoadProfile(
    TimeSpan EnvelopeAndHeader,
    TimeSpan TileAllocation,
    TimeSpan TileDecode,
    TimeSpan NonTileSections,
    TimeSpan Total);
