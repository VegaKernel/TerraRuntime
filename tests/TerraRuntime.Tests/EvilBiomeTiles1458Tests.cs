using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class EvilBiomeTiles1458Tests
{
    [Fact]
    public void All_known_material_wall_activity_pairs_match_official_binary_capture()
    {
        // Direct CanEvilReplace calls on pinned official TerrariaServer 1.4.5.8:
        // inactive then active, type-major, wall-minor; one byte (0/1) per result.
        var evidence = new byte[2 * 754 * 367];
        int index = 0;
        foreach (var flags in new[] { WorldTileFlags.None, WorldTileFlags.Active })
        for (ushort type = 0; type < 754; type++)
        for (ushort wall = 0; wall < 367; wall++)
            evidence[index++] = (byte)(EvilBiomeTiles1458.CanReplace(
                new WorldTile { Type = type, Wall = wall, Flags = flags }) ? 1 : 0);
        Assert.Equal("222570950638D41A5999DE23EA5E0C8E0C921613D57FB9A2FB501414710EB177",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(evidence)));
    }

    [Theory]
    [InlineData(41)] [InlineData(43)] [InlineData(44)]
    [InlineData(677)] [InlineData(678)] [InlineData(679)]
    [InlineData(481)] [InlineData(482)] [InlineData(483)]
    public void Dungeon_and_cracked_bricks_are_protected_only_when_active(ushort type)
    {
        var tile = new WorldTile { Type = type, Flags = WorldTileFlags.Active };
        Assert.False(EvilBiomeTiles1458.CanReplace(tile));
        var before = tile; EvilBiomeTiles1458.Excavate(ref tile); Assert.Equal(before, tile);
        tile.Flags &= ~WorldTileFlags.Active;
        Assert.True(EvilBiomeTiles1458.CanReplace(tile));
    }

    [Theory]
    [InlineData(7)] [InlineData(8)] [InlineData(9)]
    [InlineData(94)] [InlineData(95)] [InlineData(96)]
    [InlineData(97)] [InlineData(98)] [InlineData(99)]
    public void Dungeon_wall_protects_air_and_ordinary_material(ushort wall)
    {
        foreach (var flags in new[] { WorldTileFlags.None, WorldTileFlags.Active })
        {
            var tile = new WorldTile { Type = 0, Wall = wall, Flags = flags };
            Assert.False(EvilBiomeTiles1458.CanReplace(tile));
            var before = tile; EvilBiomeTiles1458.Excavate(ref tile); Assert.Equal(before, tile);
        }
    }

    [Theory]
    [InlineData(31)] [InlineData(22)] [InlineData(204)]
    public void Orb_and_evil_ore_are_replaceable_but_not_excavated(ushort type)
    {
        var tile = new WorldTile { Type = type, Flags = WorldTileFlags.Active };
        Assert.True(EvilBiomeTiles1458.CanReplace(tile));
        var before = tile; EvilBiomeTiles1458.Excavate(ref tile); Assert.Equal(before, tile);
    }

    [Theory]
    [InlineData(31)] [InlineData(22)] [InlineData(204)]
    public void Crimson_does_not_inherit_corruption_ore_or_orb_exclusions(ushort type)
    {
        var tile = new WorldTile { Type = type, Flags = WorldTileFlags.Active, FrameX = 36 };
        var expected = tile; expected.Flags &= ~WorldTileFlags.Active;
        EvilBiomeTiles1458.Excavate(ref tile, crimson: true);
        Assert.Equal(expected, tile);
    }

    [Fact]
    public void Excavation_changes_only_activity()
    {
        var tile = new WorldTile { Type = 1, Wall = 2, FrameX = 18, FrameY = 36, Shape = 3,
            TileColor = 7, WallColor = 9, LiquidAmount = 153, LiquidKind = WorldLiquidKind.Lava,
            Flags = WorldTileFlags.Active | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock };
        var expected = tile; expected.Flags &= ~WorldTileFlags.Active;
        EvilBiomeTiles1458.Excavate(ref tile);
        Assert.Equal(expected, tile);
        EvilBiomeTiles1458.Excavate(ref tile);
        Assert.Equal(expected, tile);
    }

    [Fact]
    public void Unknown_active_material_or_wall_is_not_replaceable()
    {
        Assert.False(EvilBiomeTiles1458.CanReplace(new WorldTile { Type = ushort.MaxValue, Flags = WorldTileFlags.Active }));
        Assert.False(EvilBiomeTiles1458.CanReplace(new WorldTile { Wall = ushort.MaxValue }));
    }
}
