using System.Runtime.InteropServices;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

[Flags]
public enum WorldTileFlags : ushort
{
    None = 0,
    Active = 1 << 0,
    WireRed = 1 << 1,
    WireBlue = 1 << 2,
    WireGreen = 1 << 3,
    WireYellow = 1 << 4,
    Actuator = 1 << 5,
    Inactive = 1 << 6,
    InvisibleBlock = 1 << 7,
    InvisibleWall = 1 << 8,
    FullbrightBlock = 1 << 9,
    FullbrightWall = 1 << 10
}

/// <summary>
/// Named runtime groups over the packed <see cref="WorldTileFlags"/> snapshot field. These are TerraRuntime
/// state masks, not Terraria packet or .wld bit layouts; wire/file codecs own conversion to their raw masks.
/// </summary>
public static class WorldTileFlagMasks
{
    public const WorldTileFlags Wires =
        WorldTileFlags.WireRed |
        WorldTileFlags.WireBlue |
        WorldTileFlags.WireGreen |
        WorldTileFlags.WireYellow;

    public const WorldTileFlags Actuation =
        WorldTileFlags.Actuator |
        WorldTileFlags.Inactive;

    public const WorldTileFlags Visibility =
        WorldTileFlags.InvisibleBlock |
        WorldTileFlags.InvisibleWall;

    public const WorldTileFlags Fullbright =
        WorldTileFlags.FullbrightBlock |
        WorldTileFlags.FullbrightWall;

    public const WorldTileFlags Known =
        WorldTileFlags.Active |
        Wires |
        Actuation |
        Visibility |
        Fullbright;
}

public enum WorldLiquidKind : byte
{
    Water = 0,
    Lava = 1,
    Honey = 2,
    Shimmer = 3
}

/// <summary>
/// Normalized runtime tile state. This is deliberately independent from the variable-length .wld encoding.
/// Shape is 0 for full/ordinary, 1 for half-brick, and 2..5 for the four vanilla slope values plus one.
/// The explicit 16-byte sequential layout is the TerraRuntime snapshot ABI; the final byte is reserved and
/// must remain zero so verified snapshot shards can be copied directly into the runtime tile array.
/// Raw ushort fields remain the packed storage ABI; gameplay should prefer the typed content accessors.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2, Size = 16)]
public struct WorldTile
{
    public ushort Type;
    public ushort Wall;
    public short FrameX;
    public short FrameY;
    public WorldTileFlags Flags;
    public byte LiquidAmount;
    public byte TileColor;
    public byte WallColor;
    public byte Shape;
    public WorldLiquidKind LiquidKind;
    internal byte Reserved;

    public readonly bool IsActive => (Flags & WorldTileFlags.Active) != 0;

    /// <summary>True when at least one named vanilla wire channel is present on the tile.</summary>
    public readonly bool HasAnyWire => (Flags & WorldTileFlagMasks.Wires) != 0;

    /// <summary>True when the tile carries an actuator.</summary>
    public readonly bool HasActuator => (Flags & WorldTileFlags.Actuator) != 0;

    /// <summary>
    /// True when the runtime tile is in Terraria's actuated/inactive state. This is distinct from
    /// <see cref="IsActive"/>: an active tile may be temporarily actuated out of collision/visibility semantics.
    /// </summary>
    public readonly bool IsActuated => (Flags & WorldTileFlags.Inactive) != 0;

    public readonly bool IsBlockInvisible => (Flags & WorldTileFlags.InvisibleBlock) != 0;

    public readonly bool IsWallInvisible => (Flags & WorldTileFlags.InvisibleWall) != 0;

    public readonly bool IsBlockFullbright => (Flags & WorldTileFlags.FullbrightBlock) != 0;

    public readonly bool IsWallFullbright => (Flags & WorldTileFlags.FullbrightWall) != 0;

    /// <summary>True when the packed snapshot field contains no undefined TerraRuntime flag bits.</summary>
    public readonly bool HasOnlyKnownFlags => (Flags & ~WorldTileFlagMasks.Known) == 0;

    /// <summary>
    /// Enables or disables one or more named runtime flags while preserving unrelated tile state. Unknown bits
    /// are rejected so gameplay code cannot silently extend the persisted snapshot ABI with an ad-hoc mask.
    /// </summary>
    public bool TrySetFlags(WorldTileFlags flags, bool enabled)
    {
        if ((flags & ~WorldTileFlagMasks.Known) != 0)
            return false;

        Flags = enabled
            ? Flags | flags
            : Flags & ~flags;
        return true;
    }

    /// <summary>Typed gameplay identity over the packed tile-type storage field.</summary>
    public readonly TileTypeId TileType => new(Type);

    /// <summary>Typed gameplay identity over the packed wall-type storage field.</summary>
    public readonly WallTypeId WallType => new(Wall);

    /// <summary>
    /// Stores a typed tile identity when it fits the current 16-bit runtime snapshot ABI.
    /// This is a storage-width check, not Terraria-version catalog validation.
    /// </summary>
    public bool TrySetTileType(TileTypeId type)
    {
        if ((uint)type.Value > ushort.MaxValue)
            return false;

        Type = (ushort)type.Value;
        return true;
    }

    /// <summary>
    /// Stores a typed wall identity when it fits the current 16-bit runtime snapshot ABI.
    /// This is a storage-width check, not Terraria-version catalog validation.
    /// </summary>
    public bool TrySetWallType(WallTypeId type)
    {
        if ((uint)type.Value > ushort.MaxValue)
            return false;

        Wall = (ushort)type.Value;
        return true;
    }
}
