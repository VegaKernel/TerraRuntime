using System.Runtime.InteropServices;

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
}
