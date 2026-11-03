using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class GenerationDirectionalAnchorPredicates1458Tests
{
    public static IEnumerable<object[]> OriginalRows()
    {
        using Stream resource = typeof(GenerationDirectionalAnchorPredicates1458Tests).Assembly.GetManifestResourceStream("DirectionalAnchorPredicates1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        var rows = JsonSerializer.Deserialize<Row[]>(gzip, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        foreach (Row r in rows) yield return new object[] { r.Index, r.Type, r.On, r.Actuated, r.Half, (int)r.Slope, r.Direction, r.IsSolid, r.Accepted };
    }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Directional_anchor_matches_original_CheckAndAdjust_singleton_context(int index, int type, bool on, bool actuated, bool half, int slope, int direction, bool solid, bool expected)
    {
        //Actual Main data1+2, one cardinal anchor, actual unchanged CheckAndAdjustMultiDirectionalTile.
        //This read-only predicate matrix complements the independent full TileFrame/grid/RNG profiles.
        Assert.InRange(index, 0, 1919);
        var workspace = new Workspace(80, 600); workspace.InitializeVanillaGenerationSolidity();
        workspace.SetVanillaGenerationSolid(new TileTypeId(type), solid);
        (int X, int Y)[] offsets = [(0, -1), (0, 1), (-1, 0), (1, 0)];
        int x = 40 + offsets[direction].X, y = 270 + offsets[direction].Y;
        var anchor = new WorldTile { Type = (ushort)type, Flags = (on ? WorldTileFlags.Active : WorldTileFlags.None) | (actuated ? WorldTileFlags.Inactive : WorldTileFlags.None) };
        Assert.True(anchor.TrySetSourceShape(half, (byte)slope)); workspace.TileStore.Set(x, y, anchor);
        var random = new RandomAdapter();
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);
        bool actual = (bool)typeof(GenerationTileFraming1458).GetMethod("ValidMultiDirectionalAnchor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(framing, [x, y, direction])!;
        Assert.Equal(expected, actual); Assert.Equal(0, random.Calls);
    }
    private sealed record Row
    {
        public int Index { get; init; }
        public int Type { get; init; }
        public bool On { get; init; }
        public bool Actuated { get; init; }
        public bool Half { get; init; }
        public byte Slope { get; init; }
        public int Direction { get; init; }
        public bool IsSolid { get; init; }
        public bool Accepted { get; init; }
    }
    private sealed class RandomAdapter : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(1458); public int Calls { get; private set; }
        public int Next() { Calls++; return random.Next(); }
        public int Next(int max) { Calls++; return random.Next(max); }
        public int Next(int min, int max) { Calls++; return random.Next(min, max); }
        public double NextDouble() { Calls++; return random.NextDouble(); }
        public void NextBytes(byte[] bytes) { Calls++; random.NextBytes(bytes); }
    }
}
