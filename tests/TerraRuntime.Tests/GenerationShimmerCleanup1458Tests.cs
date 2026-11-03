using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationShimmerCleanup1458Tests
{
    [Theory]
    [InlineData(0, 1, "6054E4BCF0F9C667C4879F5C58F416B5234878B9FA409C33037DAC9C94ACFDB2", "053DD2C8B3A4B8705E16C896635DEEFBAFACCB1F902709509BA154917F3CE060", 258252096)]
    [InlineData(0, 42, "39E9AECE09B32C5E1585956037DF5128C2026FEF152F636C91011A14AC4BA192", "E34DD92E641454C95178B9A7CDD2F19378271EE5B6B4DBB156376224762B3BBD", 869525530)]
    [InlineData(0, 1458, "90D3501B87E0C4E4C2FC44F70F6EAEF4D747315D68F44F37235C3DBC17C71268", "6E2202FE0B7FE99080CA88ADEA984F405AB781939DFC51CABD6E9602158B8FB0", 1545511899)]
    [InlineData(1, 1, "849BE6BA667DA97E4167F9DD3ACE8CB082FDF6EC7DC255F8D9A1E5AB5EF032C3", "ABEEFF97CB0B6246242DF7CD622170A5FA3CBFA8FEF1B1CA68991FBCEA1AB678", 1961455256)]
    [InlineData(1, 42, "915668492C51AB8A914378801364CFC0EA63F82814638E0122AAFC8733D6F0E5", "6B7E5CD5E1E18147577D4D7B6EE931FC2FEF988D2FEF17CD5D48BE57BEA2544D", 546624604)]
    [InlineData(1, 1458, "16D755748A3CAD5F5542F33791006D91C99CDE5E6629F00B3948C7B6116A3D69", "BBB7B170B6FA878BC5CAF1285681EAD2293FE570573477BC1A09480C441ACCC1", 287193327)]
    [InlineData(2, 1, "870A94388FDCA9230399ED607517FBBCA60C9B3E7F9B55C92DF39C7CF505CD8B", "5D7D82B143E9B10758D611A749826C8AF2C5FFB242C5477A131A0DFF25D967FC", 534011718)]
    [InlineData(2, 42, "870A94388FDCA9230399ED607517FBBCA60C9B3E7F9B55C92DF39C7CF505CD8B", "FA244F107DFF1CAAED5F36CB9F3249BD46439F31C309C40C53F3590C213928BA", 1434747710)]
    [InlineData(2, 1458, "870A94388FDCA9230399ED607517FBBCA60C9B3E7F9B55C92DF39C7CF505CD8B", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634)]
    public void Shimmer_cleanup_matches_actual_original_all_cells_and_full_random(
        int profile, int seed, string expectedGrid, string expectedRandom, int expectedNext)
    {
        var workspace = new Workspace(500, 500);
        Assert.True(workspace.TrySetLayers(profile == 1 ? 250 : 170, 300));
        workspace.VanillaShimmerPosition = profile == 2 ? null : new WorldGenerationPoint(250, 250);
        PopulateShimmer((x, y, c) => { WorldTile tile = ConvertCell(c); workspace.TileStore.Set(x, y, in tile); }, profile);
        var random = new RandomAdapter(seed);
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);
        Assert.Equal(profile != 2, FinalPass1458.CleanupShimmer(workspace, random, framing, CancellationToken.None));
        Assert.Equal(profile != 2, framing.UsesPostShimmerSolidity);
        Assert.Equal(expectedGrid, Hash(workspace));
        Assert.Equal(expectedRandom, random.CapturePhysicalRandomHash());
        Assert.Equal(expectedNext, random.Next());
    }

    [Theory]
    [InlineData(1, "70560C15FACF98249E95C7BFC605F08BB710A50F4096F6A09923295FFC939FC8")]
    [InlineData(42, "1346ABB7602390E85F72F2B4876C58DAC85FBFC609EDA6E3E6C104D71A4D54B3")]
    [InlineData(1458, "9807F605E83665224991DEB1655BCBA0F9115C5DF2773752A7BCBA4BFBD3D95A")]
    public void Purification_brush_matches_actual_original_every_tile_wall_torch_and_tree_profile(int seed, string expected)
    {
        var observations = new StringBuilder();
        for (int profile = 0; profile < 1160; profile++)
        {
            var workspace = new Workspace(80, 80);
            PopulatePure((x, y, c) => { WorldTile tile = ConvertCell(c); workspace.TileStore.Set(x, y, in tile); }, profile);
            var random = new RandomAdapter(seed);
            new GenerationTileFraming1458(workspace.TileStore, random, workspace, useWorldGenerationSolidity: true).Purify(30, 30, 30, 50);
            observations.Append(profile).Append(':').Append(Hash(workspace)).Append(':')
                .Append(random.CapturePhysicalRandomHash()).Append(':').Append(random.Next()).Append(';');
        }
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(observations.ToString()))));
    }

    private static void PopulatePure(Action<int, int, Cell> write, int profile)
    {
        Cell center = new(1, Active: true, Wires: true, Actuator: true, TileColor: 5, WallColor: 7);
        if (profile < 754) center = center with { Type = (ushort)profile };
        else if (profile < 1121) center = center with { Wall = (ushort)(profile - 754) };
        else if (profile < 1147) center = center with { Type = 4, Fy = (short)((profile - 1121) * 22), Wall = 1 };
        else
        {
            int treeProfile = profile - 1147;
            ushort[] trees = [5, 323, 583, 584, 585, 586, 587, 588, 589, 596, 616, 634, 72];
            center = center with { Type = treeProfile % 2 == 0 ? (ushort)112 : (ushort)199 };
            write(30, 29, new Cell(trees[treeProfile], Fx: 0, Fy: 132, Active: true));
            write(30, 28, new Cell(trees[treeProfile], Fx: 0, Fy: 0, Active: true));
            write(30, 27, new Cell(trees[treeProfile], Fx: 110, Fy: 0, Active: true));
        }
        write(30, 30, center);
        for (int x = 24; x <= 36; x++) write(x, 70, new Cell(1, Active: true));
    }

    private static void PopulateShimmer(Action<int, int, Cell> write, int profile)
    {
        for (int x = 0; x < 500; x++)
        for (int y = 0; y < 500; y++)
        {
            bool cavity = Math.Abs(x - 250) <= 35 && y >= 240 && y <= 265;
            write(x, y, new Cell(1, (ushort)(profile == 1 ? 80 : 1), Active: !cavity,
                Liquid: (byte)(cavity ? y >= 250 ? y == 250 ? 127 : 255 : 20 : 0),
                Kind: (byte)(cavity && y >= 250 ? 3 : 2), TileColor: 5, WallColor: 7, Wires: true));
        }
        ushort[] identities = [22, 204, 51, 56, 229, 230, 659, 184, 162, 182, 180, 179, 381, 183, 181, 534, 536, 539, 625, 627,
            25, 117, 203, 23, 199, 492, 661, 662, 70, 112, 116, 234, 398, 399, 402, 400, 401, 403, 163, 164, 200, 32, 352];
        for (int n = 0; n < identities.Length; n++)
        {
            int x = 180 + n % 11 * 12, y = 195 + n / 11 * 12;
            write(x, y, new Cell(identities[n], (ushort)(n % 2 == 0 ? 69 : 80), 17, 23, true,
                3, true, true, true, true, 11, 13, 37, (byte)(n % 4), true));
        }
        // A distant tree5 carrying shimmer exercises the whole-map final destruction scan.
        write(60, 60, new Cell(5, Fx: 0, Fy: 22, Active: true, Liquid: 50, Kind: 3));
        write(60, 61, new Cell(2, Active: true));
    }

    private static WorldTile ConvertCell(Cell c) => new()
    {
        Type = c.Type, Wall = c.Wall, FrameX = c.Fx, FrameY = c.Fy,
        Shape = c.Shape, TileColor = c.TileColor, WallColor = c.WallColor,
        LiquidAmount = c.Liquid, LiquidKind = (WorldLiquidKind)c.Kind,
        Flags = (c.Active ? WorldTileFlags.Active : WorldTileFlags.None) |
            (c.Actuated ? WorldTileFlags.Inactive : WorldTileFlags.None) |
            (c.Actuator ? WorldTileFlags.Actuator : WorldTileFlags.None) |
            (c.Wires ? WorldTileFlagMasks.Wires : WorldTileFlags.None) |
            (c.Coatings ? WorldTileFlagMasks.Visibility | WorldTileFlagMasks.Fullbright : WorldTileFlags.None) |
            (c.HalfSlope ? WorldTileFlags.HalfBrickWithSlope : WorldTileFlags.None)
    };

    private readonly record struct Cell(ushort Type = 0, ushort Wall = 0, short Fx = 0, short Fy = 0,
        bool Active = false, byte Shape = 0, bool Actuated = false, bool Actuator = false,
        bool Wires = false, bool Coatings = false, byte TileColor = 0, byte WallColor = 0,
        byte Liquid = 0, byte Kind = 0, bool HalfSlope = false);

    private static string Hash(Workspace workspace)
    {
        var text = new StringBuilder();
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            WorldTile t = workspace.TileStore.Get(x, y);
            text.Append(t.IsActive ? 1 : 0).Append(',').Append(t.Type).Append(',').Append(t.Wall).Append(',')
                .Append(t.FrameX).Append(',').Append(t.FrameY).Append(',').Append(t.LiquidAmount).Append(',')
                .Append((byte)t.LiquidKind).Append(',').Append(t.TileColor).Append(',').Append(t.WallColor).Append(',')
                .Append(t.IsHalfBrick ? 1 : 0).Append(',').Append(t.Shape >= 2 ? t.Shape - 1 : 0).Append(',')
                .Append((t.Flags & WorldTileFlags.WireRed) != 0 ? 1 : 0).Append(',')
                .Append((t.Flags & WorldTileFlags.WireBlue) != 0 ? 1 : 0).Append(',')
                .Append((t.Flags & WorldTileFlags.WireGreen) != 0 ? 1 : 0).Append(',')
                .Append((t.Flags & WorldTileFlags.WireYellow) != 0 ? 1 : 0).Append(',')
                .Append((t.Flags & WorldTileFlags.Actuator) != 0 ? 1 : 0).Append(',').Append(t.IsActuated ? 1 : 0).Append(',')
                .Append((t.Flags & WorldTileFlags.InvisibleBlock) != 0 ? 1 : 0).Append(',')
                .Append((t.Flags & WorldTileFlags.InvisibleWall) != 0 ? 1 : 0).Append(',')
                .Append((t.Flags & WorldTileFlags.FullbrightBlock) != 0 ? 1 : 0).Append(',')
                .Append((t.Flags & WorldTileFlags.FullbrightWall) != 0 ? 1 : 0).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
        public string CapturePhysicalRandomHash()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            using var bytes = new MemoryStream();
            using var writer = new BinaryWriter(bytes);
            writer.Write((uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!);
            foreach (int value in (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!) writer.Write(value);
            return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
        }
    }
}
