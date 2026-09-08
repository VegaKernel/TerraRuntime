using System.Buffers.Binary;
using System.Security.Cryptography;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaTileObjectLiquidDeath1458Tests
{
    [Fact]
    public void All_types_match_independent_official_server_liquid_flag_matrix()
    {
        // Golden computed by invoking the REAL Check*(Tile) on official Linux TerrariaServer 1.4.5.8
        // SHA256 4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034 after Main.Initialize*
        // and TileObjectData.Initialize. No TerraRuntime codec/catalog contributed to the expected bytes.
        short[] frames = [0, 1, 16, 17, 18, 19, 20, 21, 22, 23, 35, 36, 37, 38, 39, 40, 41,
            53, 54, 55, 71, 72, 73, 89, 90, 91, 107, 108, 109, 143, 144, 145, 255, 256, 257,
            539, 540, 541, 639, 640, 641, 899, 900, 901, 1241, 1242, 1243, 1727, 1728, 1729, 32767];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> wire = stackalloc byte[7];
        for (ushort type = 0; type < 754; type++)
        foreach (short frameX in frames)
        foreach (short frameY in frames)
        {
            var tile = new WorldTile { Type = type, FrameX = frameX, FrameY = frameY };
            Assert.True(VanillaTileObjectLiquidDeath1458.TryGet(in tile, out bool water, out bool lava));
            BinaryPrimitives.WriteUInt16LittleEndian(wire, type);
            BinaryPrimitives.WriteInt16LittleEndian(wire[2..], frameX);
            BinaryPrimitives.WriteInt16LittleEndian(wire[4..], frameY);
            wire[6] = (byte)((water ? 1 : 0) | (lava ? 2 : 0));
            hash.AppendData(wire);
        }
        Assert.Equal("12DB9C63AC0CFF9128E5891FB71F56BCDBC63C05C1297A198C23AA28C37E7909",
            Convert.ToHexString(hash.GetHashAndReset()));
    }

    // Independent frame/flag facts from official TileObjectData.Initialize and GetTileData(Tile),
    // including alternate selection. These are NOT the results of CheckLavaDeath(type, style).
    [Theory]
    [InlineData(14, 702, 0, false, false)]
    [InlineData(15, 0, 640, false, false)]
    [InlineData(15, 18, 658, false, false)]
    [InlineData(18, 504, 0, false, false)]
    [InlineData(19, 0, 234, false, false)]
    [InlineData(19, 468, 234, false, false)]
    [InlineData(19, 0, 0, false, true)]
    [InlineData(33, 0, 550, false, false)]
    [InlineData(79, 0, 288, false, false)]
    [InlineData(79, 126, 306, false, false)]
    [InlineData(87, 810, 0, false, false)]
    [InlineData(88, 0, 0, false, false)]
    [InlineData(89, 540, 0, false, false)]
    [InlineData(90, 72, 900, false, false)]
    [InlineData(93, 0, 1242, true, false)]
    [InlineData(93, 18, 1278, true, false)]
    [InlineData(93, 0, 2160, false, true)]
    [InlineData(100, 0, 900, false, false)]
    [InlineData(101, 216, 0, false, false)]
    [InlineData(104, 612, 0, false, false)]
    [InlineData(105, 1764, 0, false, false)]
    [InlineData(34, 0, 1728, false, false)]
    [InlineData(34, 54, 1728, false, false)]
    [InlineData(42, 0, 1152, false, true)] // inherited alternate 0 overrides lava-proof subtile 32
    [InlineData(4, 0, 154, true, true)]
    [InlineData(4, 44, 176, false, false)]
    [InlineData(4, 88, 176, false, false)]
    [InlineData(82, 90, 0, false, false)]
    [InlineData(82, 0, 0, false, true)]
    [InlineData(215, 0, 0, true, true)]
    [InlineData(215, 54, 0, false, true)]
    [InlineData(215, 54, 36, false, true)]
    [InlineData(215, 108, 36, true, true)]
    [InlineData(372, 0, 0, false, true)]
    [InlineData(405, 0, 0, false, true)]
    [InlineData(646, 0, 0, false, true)]
    [InlineData(149, 0, 0, false, false)]
    [InlineData(12, 0, 0, false, true)]
    [InlineData(28, 0, 0, false, true)]
    [InlineData(61, 0, 0, false, true)]
    [InlineData(637, 0, 0, false, false)] // natural objects use the Main override, not local LavaDeath=true
    [InlineData(51, 0, 0, true, true)] // no TileObjectData entry
    [InlineData(240, 1458, 0, false, true)]
    [InlineData(242, 0, 1008, false, true)]
    [InlineData(91, 288, 0, false, true)]
    public void Tile_overload_resolves_effective_flags(int type, int frameX, int frameY, bool water, bool lava)
    {
        var tile = new WorldTile { Type = (ushort)type, FrameX = (short)frameX, FrameY = (short)frameY };
        Assert.True(VanillaTileObjectLiquidDeath1458.TryGet(in tile, out bool actualWater, out bool actualLava));
        Assert.Equal(water, actualWater);
        Assert.Equal(lava, actualLava);
    }

    [Theory]
    [InlineData(754, 0, 0)]
    [InlineData(65535, 0, 0)]
    [InlineData(4, -1, 0)]
    [InlineData(19, 0, -1)]
    public void Unknown_types_or_unresolved_style_frames_are_not_admitted(int type, int x, int y)
    {
        var tile = new WorldTile { Type = (ushort)type, FrameX = (short)x, FrameY = (short)y };
        Assert.False(VanillaTileObjectLiquidDeath1458.TryGet(in tile, out _, out _));
    }
}
