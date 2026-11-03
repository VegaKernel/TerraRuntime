using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class GenerationDecorationPhaseHelpers1458Tests
{
    private static readonly Row[] Expected = Read();
    public static IEnumerable<object[]> OriginalRows() => Expected.Select(r => new object[] { r.Index, r.Function, r.FloorType, r.FloorSolid, r.Shape });
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Decoration_and_buried_chest_helpers_match_original_live_solidity_and_independent_shape_bits(int index, int function, int floorType, bool floorSolid, int shape)
    {
        //Actual unchanged official helpers, complete21field grid and full physical RNG. All actors inactive.
        //212 profiles include sourceSmallPile sizes0/1, pots, PlaceTile187, AddBuriedChest+trySlope,
        //four independent half/slope combinations and all ten TileID.Sets.Boulders identities.
        Row expected = Expected.Single(r => r.Index == index);
        var w = new Workspace(80, 600); w.TrySetLayers(120, 250); w.SetVanillaLiquidLines(500, 500);
        w.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458 { CopperOre = 7, IronOre = 6, SilverOre = 9, GoldOre = 8, CopperBar = 20, IronBar = 22, SilverBar = 21, GoldBar = 19, HellChestItems = [274, 220, 112, 218, 3019], ExtraBastStatueCountMax = 2, TreeX = [], TreeStyle = [], CaveBackX = [], CaveBackStyle = [], ForestBackgroundStyles = [] });
        w.InitializeVanillaGenerationSolidity(); foreach (int id in new[] { 481, 482, 483, 484, 659 }) w.SetVanillaGenerationSolid(new TileTypeId(id), false);
        w.SetVanillaGenerationSolid(new TileTypeId(floorType), floorSolid);
        for (int x = 0; x < 80; x++) for (int y = 0; y < 600; y++) w.TileStore.Set(x, y, new WorldTile { Type = (ushort)(y >= 280 ? 1 : 0), Flags = y >= 280 ? WorldTileFlags.Active : WorldTileFlags.None });
        for (int x = 39; x <= 41; x++) { var tile = new WorldTile { Type = (ushort)floorType, Flags = WorldTileFlags.Active }; tile.TrySetSourceShape(shape is 1 or 3, (byte)(shape >= 2 ? 1 : 0)); w.TileStore.Set(x, 271, tile); }
        var rng = new RandomAdapter(1458); var context = new Context(w, rng); var state = new ChestPlacementState1458(); state.EnsureInitialized(context, w);
        var placer = state.CreatePlacer(w, rng);
        bool accepted = function switch
        {
            0 or 1 => GenerationDecorationPlacement1458.TryPlaceSmallPile(w.TileStore, 40, 270, 34, function, workspace: w),
            2 => GenerationDecorationPlacement1458.TryPlacePot(w.TileStore, rng, 40, 270, 19, w),
            3 => GenerationDecorationPlacement1458.TryPlaceTile3x2(w.TileStore, rng, 40, 270, 187, 9, workspace: w),
            _ => placer.TryAdd(40, 267, out _, out _, 939, false, 15, function == 5, (ushort)0)
        };
        string physical = rng.PhysicalState(); int next = rng.Next();
        var text = new StringBuilder(); for (int x = 0; x < 80; x++) for (int y = 0; y < 600; y++) { var t = w.TileStore.Get(x, y); text.Append(t.IsActive ? 1 : 0).Append(',').Append(t.Type).Append(',').Append(t.Wall).Append(',').Append(t.FrameX).Append(',').Append(t.FrameY).Append(',').Append(t.LiquidAmount).Append(',').Append((byte)t.LiquidKind).Append(',').Append(t.IsHalfBrick ? 1 : 0).Append(',').Append(t.SourceSlope).Append(',').Append(t.TileColor).Append(',').Append(t.WallColor); foreach (var flag in new[] { WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen, WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive, WorldTileFlags.InvisibleBlock, WorldTileFlags.FullbrightBlock, WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall }) text.Append(',').Append((t.Flags & flag) != 0 ? 1 : 0); text.Append(';'); }
        Assert.Equal(expected.Accepted, accepted); Assert.Equal(expected.Grid, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))));
        Assert.Equal(expected.Cursor + ":" + string.Join(',', expected.Slots), physical); Assert.Equal(expected.Next, next);
        for (int x = 0; x < 80; x++) for (int y = 0; y < 600; y++) Assert.True(w.TileStore.Get(x, y).HasValidSourceShape);

    }
    private static Row[] Read() { using Stream stream = typeof(GenerationDecorationPhaseHelpers1458Tests).Assembly.GetManifestResourceStream("GenerationDecorationPhaseHelpers1458")!; using var gzip = new GZipStream(stream, CompressionMode.Decompress); return JsonSerializer.Deserialize<Row[]>(gzip, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!; }
    private sealed record Row
    {
        public int Index { get; init; }
        public int Function { get; init; }
        public int FloorType { get; init; }
        public bool FloorSolid { get; init; }
        public int Shape { get; init; }
        public bool Accepted { get; init; }
        public uint Cursor { get; init; }
        public int[] Slots { get; init; } = []; public int Next { get; init; }
        public string Grid { get; init; } = "";
    }
    private sealed class Context(Workspace workspace, IWorldGenerationVanillaRandom random) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => new(Provider1458.GeneratorId, "ManualHouseBuilder", 1458, 80, 600);
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportProgress(double fraction, string? message = null) { }
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public string PhysicalState() => typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random) + ":" +
            string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!);
        public int Next() => random.Next();
        public int Next(int maximum) => random.Next(maximum);
        public int Next(int minimum, int maximum) => random.Next(minimum, maximum);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }

}
