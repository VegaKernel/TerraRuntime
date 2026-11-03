using System.Buffers.Binary;
using System.Security.Cryptography;
using Xunit;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationDoublePileRewards1458Tests
{
    private static readonly JsonDocument Golden = ReadGolden();
    public static IEnumerable<object[]> OriginalCases => Enumerable.Range(0, Golden.RootElement.GetProperty("cases").GetArrayLength()).Select(i => new object[] { i });

    [Theory, MemberData(nameof(OriginalCases))]
    public void Two_cell_pile_callbacks_match_original_grid_and_reward_random_arguments(int index)
    {
        JsonElement row = Golden.RootElement.GetProperty("cases")[index];
        var workspace = new Workspace(80, 80);
        Assert.True(workspace.TrySetLayers(20, 50));
        workspace.InitializeVanillaGenerationSolidity();
        int identity = 0;
        foreach (JsonElement solid in Golden.RootElement.GetProperty("phase754").EnumerateArray())
            workspace.SetVanillaGenerationSolid(new TileTypeId((ushort)identity++), solid.GetBoolean());
        var random = new RandomAdapter(row.GetProperty("seed").GetInt32());
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);
        for (int x = 0; x < 80; x++) for (int y = 0; y < 80; y++) Set(x,y,new WorldTile { FrameX = -1, FrameY = -1 });
        int frame = row.GetProperty("frame").GetInt32(), trigger = row.GetProperty("trigger").GetInt32(), broken = row.GetProperty("broken").GetInt32();
        for (int x = 39; x <= 40; x++)
        {
            Set(x, 40, new WorldTile { Type = 185, FrameX = (short)(frame + (x - 39) * 18), FrameY = 18,
                Wall = 64, TileColor = 17, WallColor = 3, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed |
                WorldTileFlags.WireGreen | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightWall });
            Set(x, 41, new WorldTile { Type = 1, FrameX = -1, FrameY = -1, Flags = WorldTileFlags.Active });
        }
        if (broken != 0)
        {
            int x = broken == 1 ? 40 - trigger : 39, y = broken == 1 ? 40 : 41;
            WorldTile cell = workspace.TileStore.Get(x, y); cell.Flags &= ~WorldTileFlags.Active; Set(x, y, cell);
        }
        framing.TileFrame(39 + trigger, 40);
        Assert.Equal(row.GetProperty("grid").GetString(), GridHash(workspace));
        Assert.Equal(row.GetProperty("cursor").GetInt32(), random.Cursor);
        Assert.Equal(row.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()), random.Slots);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());

        void Set(int x, int y, WorldTile tile) => workspace.TileStore.Set(x, y, in tile);
    }

    private static string GridHash(Workspace workspace)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] column = new byte[workspace.HeightTiles * 17];
        WorldTileFlags[] masks = [WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen,
            WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive,
            WorldTileFlags.InvisibleBlock, WorldTileFlags.FullbrightBlock,
            WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall];
        int unknown = 0, invalid = 0, reserved = 0;
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile t = workspace.TileStore.Get(x, y);
                Span<byte> b = column.AsSpan(y * 17, 17);
                b[0] = (byte)(t.IsActive ? 1 : 0);
                BinaryPrimitives.WriteUInt16LittleEndian(b[1..], t.Type);
                BinaryPrimitives.WriteUInt16LittleEndian(b[3..], t.Wall);
                BinaryPrimitives.WriteInt16LittleEndian(b[5..], t.FrameX);
                BinaryPrimitives.WriteInt16LittleEndian(b[7..], t.FrameY);
                b[9] = t.LiquidAmount; b[10] = (byte)t.LiquidKind; b[11] = (byte)(t.IsHalfBrick ? 1 : 0);
                b[12] = t.SourceSlope; b[13] = t.TileColor; b[14] = t.WallColor;
                ushort flags = 0;
                for (int bit = 0; bit < masks.Length; bit++) if ((t.Flags & masks[bit]) != 0) flags |= (ushort)(1 << bit);
                BinaryPrimitives.WriteUInt16LittleEndian(b[15..], flags);
                if ((t.Flags & ~WorldTileFlagMasks.Known) != 0) unknown++;
                if (!t.HasValidSourceShape) invalid++;
                if (t.Reserved != 0) reserved++;
            }
            hash.AppendData(column);
        }
        Assert.Equal(0, unknown); Assert.Equal(0, invalid); Assert.Equal(0, reserved);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static JsonDocument ReadGolden()
    {
        using Stream stream = typeof(GenerationDoublePileRewards1458Tests).Assembly.GetManifestResourceStream("GenerationDoublePileRewards1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 source = new(seed);
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        internal int Cursor => Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", Hidden)!.GetValue(source));
        internal int[] Slots => ((int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", Hidden)!.GetValue(source)!).ToArray();
        public int Next() => source.Next();
        public int Next(int maximum) => source.Next(maximum);
        public int Next(int minimum, int maximum) => source.Next(minimum, maximum);
        public double NextDouble() => source.NextDouble();
        public void NextBytes(byte[] buffer) => source.NextBytes(buffer);
    }
}
