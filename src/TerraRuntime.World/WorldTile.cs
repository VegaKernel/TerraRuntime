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
