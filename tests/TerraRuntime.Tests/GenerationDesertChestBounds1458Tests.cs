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

public sealed class GenerationDesertChestBounds1458Tests
{
    private static readonly Row[] Expected = Read();
    public static IEnumerable<object[]> OriginalRows() => Expected.SelectMany(r => new[] { new object[] { r.Floor, r.Seed, 0 }, new object[] { r.Floor, r.Seed, 1 } });
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Both_production_chest_contexts_read_inclusive_density_bounds_at_the_strict_loot_split(int floor, int seed, int creator)
    {
        //Actual original AddBuriedChest, floors370/371/372 around (200*3+500*4)/7.
        //Both real context factories are exercised; the unrelated outer desert rectangle is deliberately different.
        Row expected = Expected.Single(r => r.Floor == floor && r.Seed == seed);
        var w = new Workspace(80, 600); w.TrySetLayers(120, 250); w.SetVanillaLiquidLines(500, 500);
        w.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458 { CopperOre = 7, IronOre = 6, SilverOre = 9, GoldOre = 8, CopperBar = 20, IronBar = 22, SilverBar = 21, GoldBar = 19, HellChestItems = [274, 220, 112, 218, 3019], ExtraBastStatueCountMax = 2, TreeX = [], TreeStyle = [], CaveBackX = [], CaveBackStyle = [], ForestBackgroundStyles = [] });
        w.SetVanillaDesertGenerationState(new(new(10, 170, 60, 300), new(10, 200, 60, 301), new(10, 30, 60, 500)));
        w.SetVanillaUndergroundDesertRegion(10, 100, 60, 100);
        for (int x = 0; x < 80; x++) for (int y = 0; y < 600; y++) w.TileStore.Set(x, y, new WorldTile { Type = (ushort)(y >= floor ? 396 : 0), Wall = 187, Flags = y >= floor ? WorldTileFlags.Active : WorldTileFlags.None });
        var rng = new RandomAdapter(seed); var context = new Context(w, rng); var state = new ChestPlacementState1458();
        if (creator == 1) { var ds = new DungeonState1458(); ds.EnsureInitialized(context, w); var pass = new DungeonPass1458(DungeonStage1458.Pyramids, ds); typeof(DungeonPass1458).GetMethod("CreatePyramidChestContext", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pass, [w]); }
        state.EnsureInitialized(context, w); var placer = state.CreatePlacer(w, rng); bool accepted = placer.TryAdd(40, floor - 4, out _, out _); string physical = rng.PhysicalState(); int next = rng.Next();
        var text = new StringBuilder(); for (int x = 0; x < 80; x++) for (int y = 0; y < 600; y++) { var t = w.TileStore.Get(x, y); text.Append(t.IsActive ? 1 : 0).Append(',').Append(t.Type).Append(',').Append(t.Wall).Append(',').Append(t.FrameX).Append(',').Append(t.FrameY).Append(',').Append(t.LiquidAmount).Append(',').Append((byte)t.LiquidKind).Append(',').Append(t.IsHalfBrick ? 1 : 0).Append(',').Append(t.SourceSlope).Append(',').Append(t.TileColor).Append(',').Append(t.WallColor); foreach (var flag in new[] { WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen, WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive, WorldTileFlags.InvisibleBlock, WorldTileFlags.FullbrightBlock, WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall }) text.Append(',').Append((t.Flags & flag) != 0 ? 1 : 0); text.Append(';'); }
        Assert.Equal(expected.Accepted, accepted);
        Assert.Equal(expected.Grid, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))));
        Assert.Equal(expected.Cursor + ":" + string.Join(',', expected.Slots), physical);
        Assert.Equal(expected.Next, next); Assert.Equal(200, state.ChestContext.DesertHiveHigh); Assert.Equal(500, state.ChestContext.DesertHiveLow);
        WorldChestItem[] items = w.CaptureGeneratedChests().Single().Items; Assert.Equal(expected.Items.Length, items.Length);
        for (int i = 0; i < items.Length; i++) Assert.Equal(expected.Items[i], new[] { items[i].ItemType, items[i].Stack, (int)items[i].Prefix });

    }
    private static Row[] Read()
    {
        using Stream resource = typeof(GenerationDesertChestBounds1458Tests).Assembly.GetManifestResourceStream("GenerationDesertChestBounds1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<Row[]>(gzip, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    private sealed record Row
    {
        public int Floor { get; init; }
        public int Seed { get; init; }
        public bool Accepted { get; init; }
        public string Grid { get; init; } = "";
        public int Cursor { get; init; }
        public int[] Slots { get; init; } = [];
        public int Next { get; init; }
        public int[][] Items { get; init; } = [];
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
