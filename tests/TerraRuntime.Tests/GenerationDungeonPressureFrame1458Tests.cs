using System.Text.Json;
using System.IO.Compression;
using System.Reflection;
using TerraRuntime.Core;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;
namespace TerraRuntime.Tests;

// Official SquareTileFrame with both generation flags set, repeated twice on the captured native32 solidity.
// Includes inactive/actuated/shaped floors, all preserved wires/coatings and an adjacent pressure plate.
public sealed class GenerationDungeonPressureFrame1458Tests
{
    private static JsonDocument Read()
    {
        using var raw = typeof(GenerationDungeonPressureFrame1458Tests).Assembly.GetManifestResourceStream("GenerationDungeonPressureFrame1458")!;
        using var zip = new GZipStream(raw, CompressionMode.Decompress);
        return JsonDocument.Parse(zip);
    }
    public static IEnumerable<object[]> Rows()
    {
        using var data = Read();
        for (int i = 0; i < data.RootElement.GetProperty("rows").GetArrayLength(); i++)
            yield return new object[] { i };
    }
    [Theory]
    [MemberData(nameof(Rows))]
    public void Original_dungeon_square_frame_owns_detached_pressure_plate_and_adjacent_shapes(int id)
    {
        using var data = Read();
        var e = data.RootElement.GetProperty("rows")[id];
        const int W = 96, H = 180, x = 48, y = 80;
        var w = new Workspace(W, H);
        w.InitializeVanillaGenerationSolidity();
        Assert.True(w.TrySetLayers(90, 150));
        int type = 0;
        foreach (var solid in data.RootElement.GetProperty("phase").EnumerateArray())
        {
            if (w.IsVanillaGenerationSolid(new(type)) != solid.GetBoolean())
                w.SetVanillaGenerationSolid(new(type), solid.GetBoolean());
            type++;
        }
        var request = new WorldGenerationRequest(new("test:original-pressure-square"), "Direct", 0, W, H) { SeedText = "dontdigup" };
        w.SetVanillaSeedProfile(WorldSeedResolver1458.Resolve(in request));
        ref var plate = ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y)];
        plate.Flags = WorldTileFlags.Active | WorldTileFlags.WireRed | WorldTileFlags.WireBlue | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock;
        plate.Type = 135;
        plate.FrameX = 0;
        plate.FrameY = 36;
        plate.TileColor = 7;
        plate.WallColor = 5;
        ref var floor = ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y + 1)];
        int state = e.GetProperty("state").GetInt32();
        floor.Flags = state == 0 ? WorldTileFlags.None : WorldTileFlags.Active;
        if (state == 2)
            floor.Flags |= WorldTileFlags.Inactive;
        floor.Type = e.GetProperty("ground").GetUInt16();
        floor.Shape = checked((byte)e.GetProperty("shape").GetInt32());
        if (e.GetProperty("neighbour").GetBoolean())
        {
            ref var p = ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x - 1, y)];
            p.Flags = WorldTileFlags.Active;
            p.Type = 135;
            p.FrameY = 36;
            ref var support = ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x - 1, y + 1)];
            support.Flags = WorldTileFlags.Active;
            support.Type = 1;
        }
        var random = new Adapter(e.GetProperty("seed").GetInt32());
        new DungeonObjectPlacement1458(w.TileStore, random, w).FrameSquare(x, y);
        Assert.Equal(e.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(w));
        const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(e.GetProperty("cursor").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", f)!.GetValue(random.Value)!);
        Assert.Equal(e.GetProperty("slots").EnumerateArray().Select(v => v.GetInt32()), (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", f)!.GetValue(random.Value)!);
        Assert.Equal(e.GetProperty("next").GetInt32(), random.Next());
    }
    private sealed class Adapter(int seed) : IWorldGenerationVanillaRandom
    {
        internal readonly VanillaUnifiedRandom1458 Value = new(seed); public int Next() => Value.Next(); public int Next(int max) => Value.Next(max); public int Next(int min, int max) => Value.Next(min, max); public double NextDouble() => Value.NextDouble(); public void NextBytes(byte[] b) => Value.NextBytes(b);
    }
}
