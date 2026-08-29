namespace TerraRuntime.World;

/// <summary>
/// Creates the identity/dimension prefix used by a fresh Terraria 1.4.5.x world. Identity values remain explicit so
/// deterministic world generation is not quietly coupled to process-global randomness or wall-clock state.
/// </summary>
public static class VanillaFreshWorldHeader326
{
    // Terraria 1.4.5 CreateMetadata uses 0x014500000001. The official 1.4.5.8 fixture contract verifies this value
    // before fresh-world persistence consumes this factory.
    public const ulong WorldGeneratorVersion = 0x014500000001UL;
    public const int TileSizePixels = 16;

    public static WorldFileHeader Create(
        string name,
        string seedText,
        int widthTiles,
        int heightTiles,
        Guid uniqueId,
        int worldId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(seedText);
        ArgumentOutOfRangeException.ThrowIfLessThan(widthTiles, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(heightTiles, 1);
        if (uniqueId == Guid.Empty)
            throw new ArgumentException("Fresh worlds require a non-empty unique ID.", nameof(uniqueId));

        int rightWorld = checked(widthTiles * TileSizePixels);
        int bottomWorld = checked(heightTiles * TileSizePixels);

        return new WorldFileHeader(
            name,
            seedText,
            WorldGeneratorVersion,
            uniqueId,
            worldId,
            LeftWorld: 0,
            RightWorld: rightWorld,
            TopWorld: 0,
            BottomWorld: bottomWorld,
            new WorldDimensions(widthTiles, heightTiles));
    }
}
