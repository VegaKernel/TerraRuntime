using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class GenerationHouseOfferBoundaries1458Tests
{
    private static readonly ExpectedRow[] Expected = Read();
    public static IEnumerable<object[]> OriginalRows() => Expected.Select((row, index) => new object[] { index, row.Input.Name });
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void House_offer_matches_original_geometry_rejections_tools_chests_and_physical_rng(int rowIndex, string name)
    {
        //143 untouched CaveHouseBiome.Place offers,4 explicit rolling-cactus phase pairs, and4 legal
        //registered BuriedChests delegates with quota2 prove the failure budget counts rejected attempts.
        //The inherited text projection includes all grid fields varied here and the entire physical RNG;
        //actuator/coating flags remain false in these fixtures. The natural prefix adds a full21field projection.
        ExpectedRow expected = Expected[rowIndex]; Assert.Equal(name, expected.Input.Name);
        OfferCase item = expected.Input; int kind = expected.Kind;
        var workspace = new Workspace(600, 600); workspace.TrySetLayers(120, 250);
        workspace.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458 { CopperOre = 7, IronOre = 6, SilverOre = 9, GoldOre = 8, CopperBar = 20, IronBar = 22, SilverBar = 21, GoldBar = 19, HellChestItems = [274, 220, 112, 218, 3019], ExtraBastStatueCountMax = 2, TreeX = [], TreeStyle = [], CaveBackX = [], CaveBackStyle = [], ForestBackgroundStyles = [] });
        Populate(item, kind, (x, y, type, active) => { var tile = new WorldTile { Type = type, Flags = active ? WorldTileFlags.Active : WorldTileFlags.None }; workspace.TileStore.Set(x, y, in tile); });
        foreach (var m in item.Mutations) { var tile = new WorldTile { Type = (ushort)m.Type, FrameX = (short)m.FrameX, FrameY = (short)m.FrameY, Flags = m.Active ? WorldTileFlags.Active : WorldTileFlags.None, LiquidAmount = (byte)m.LiquidAmount, LiquidKind = (WorldLiquidKind)m.LiquidKind }; WorldTileFlags[] wires = [WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen, WorldTileFlags.WireYellow]; for (int c = 0; c < 4; c++) if ((m.Wires & (1 << c)) != 0) tile.Flags |= wires[c]; workspace.TileStore.Set(m.X, m.Y, in tile); }
        if (kind == 1) { workspace.InitializeVanillaGenerationSolidity(); workspace.SetVanillaGenerationSolid(new TileTypeId(659), true); workspace.SetVanillaGenerationSolid(new TileTypeId(484), item.CactusSolid); }
        var random = new RandomAdapter(item.Seed); var state = new ChestPlacementState1458(); state.EnsureInitialized(new Context(workspace, random), workspace);
        if (item.Rectangle.Length == 4) { var r = item.Rectangle; var region = new WorldTileRegion(r[0], r[1], r[2], r[3]); switch (item.Owner) { case "house": state.ProtectedHouseRooms.Add(new(r[0], r[1], r[2], r[3])); break; case "shimmer": workspace.SetVanillaShimmerStructure(region); break; case "hive": workspace.SetVanillaHiveProtectedAreas([region]); break; case "shrine": workspace.SetVanillaJungleShrineProtectedAreas([region]); break; } }

        // The original fixture initializes StructureMap reservations. Legacy geometry projections
        // remain in the digest, while the canonical owner now supplies every placement decision.
        if (item.Rectangle.Length == 4 && item.Owner is "house" or "hive" or "shrine")
        {
            var r = item.Rectangle;
            workspace.VanillaStructureMap.AddProtectedStructure(new(r[0], r[1], r[2], r[3]));
        }

        bool accepted;
        if (kind == 2)
        {
            if (item.Index >= 2) { workspace.SetVanillaUndergroundDesertRegion(100, 150, 40, 30); workspace.SetVanillaDesertGenerationState(new(new(180, 260, 40, 30), new(180, 260, 40, 30), new(180, 260, 40, 30))); }
            for (int draw = 0; draw < 4; draw++) random.Next(); // The registered caller's four legal quota-range draws.
            var pass = new ChestPlacementPass1458(ChestPlacementStage1458.BuriedChests, state);
            typeof(ChestPlacementPass1458).GetMethod("PlaceHouseOffers", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pass, [new Context(workspace, random), workspace, random, item.Index >= 2 ? 0 : 2, item.Index >= 2 ? 2 : 0]);
            accepted = state.ProtectedHouseRooms.Count > 0;
        }
        else accepted = CaveHousePlacement1458.TryPlace(workspace, state, random, item.OriginX, item.OriginY, out _);
        Assert.Equal(expected.Accepted, accepted);
        var text = new StringBuilder(); text.Append("ACCEPTED:").Append(accepted ? '1' : '0').Append(';');
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 600; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                text.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.Wall)
                    .Append(',').Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append((tile.Flags & WorldTileFlags.WireRed) != 0 ? '1' : '0').Append(',')
                    .Append((tile.Flags & WorldTileFlags.WireBlue) != 0 ? '1' : '0').Append(',').Append((tile.Flags & WorldTileFlags.WireGreen) != 0 ? '1' : '0').Append(',').Append((tile.Flags & WorldTileFlags.WireYellow) != 0 ? '1' : '0').Append(',').Append(tile.IsHalfBrick ? '1' : '0').Append(',').Append(tile.SourceSlope).Append(',')
                    .Append(tile.IsActuated ? '1' : '0').Append(',').Append(tile.TileColor).Append(',')
                    .Append(tile.WallColor).Append(',').Append(tile.LiquidAmount).Append(',').Append((byte)tile.LiquidKind).Append(';');
            }
        text.Append("PROTECTED:");
        if (item.Rectangle.Length == 4 && item.Owner != "house" && item.Owner != "unprotected") text.Append(string.Join(',', item.Rectangle)).Append(';');
        foreach (CaveHouseRoom1458 room in state.ProtectedHouseRooms)
            text.Append(room.X).Append(',').Append(room.Y).Append(',').Append(room.Width).Append(',').Append(room.Height).Append(';');
        if (kind != 2) text.Append("TOOLS:").Append(state.CaveHouseSharpenerCount).Append(',')
            .Append(state.CaveHouseExtractinatorCount).Append(',').Append(state.CaveHouseBastStatueCount).Append(';');
        text.Append("CHESTS:");
        foreach (WorldChest chest in workspace.CaptureGeneratedChests())
        {
            text.Append(chest.SlotId).Append(',').Append(chest.X).Append(',').Append(chest.Y).Append(',').Append(chest.Name).Append(':');
            foreach (WorldChestItem loot in chest.Items)
                text.Append(loot.ItemType).Append(',').Append(loot.Stack).Append(',').Append(loot.Prefix).Append(';');
            text.Append('|');
        }
        text.Append("CHESTSTATE:").Append(state.ChestContext.HellChest).Append(',')
            .Append(state.ChestContext.GeneratedShadowKey ? '1' : '0').Append(',')
            .Append(state.ChestContext.GeneratedRamRune ? '1' : '0').Append(',')
            .Append(state.ChestContext.GennedLivingMahoganyWands ? '1' : '0').Append(';');
        text.Append("RNG:").Append(random.PhysicalState());

        Assert.Equal(expected.State, random.PhysicalState());
        Assert.Equal(expected.Hash, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))));

    }
    private static ExpectedRow[] Read()
    {
        using Stream resource = typeof(GenerationHouseOfferBoundaries1458Tests).Assembly.GetManifestResourceStream("GenerationHouseOfferBoundaries1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<ExpectedRow[]>(gzip, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    private sealed record ExpectedRow
    {
        public OfferCase Input { get; init; } = new();
        public int Kind { get; init; }
        public string Hash { get; init; } = "";
        public string State { get; init; } = "";
        public bool Accepted { get; init; }
    }
    private sealed record Mutation
    {
        public int X { get; init; }
        public int Y { get; init; }
        public int Type { get; init; }
        public bool Active { get; init; }
        public int Wires { get; init; }
        public int LiquidAmount { get; init; }
        public int LiquidKind { get; init; }
        public int FrameX { get; init; }
        public int FrameY { get; init; }
    }

    private sealed record OfferCase
    {
        public int Index { get; init; }
        public bool CactusSolid { get; init; }
        public string Name { get; init; } = "";
        public int Biome { get; init; }
        public int Seed { get; init; }
        public int Geometry { get; init; }
        public int OriginX { get; init; }
        public int OriginY { get; init; }
        public int FloorY { get; init; }
        public Mutation[] Mutations { get; init; } = [];
        public string Owner { get; init; } = "";
        public int[] Rectangle { get; init; } = [];
    }

    private static void Populate(OfferCase item, int kind, Action<int, int, ushort, bool> put)
    {
        ushort[] materials = [1, 147, 396, 60, 70, 368, 367];
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 600; y++)
            {
                bool active = item.Geometry == 2 ? y >= item.FloorY : y >= 120;
                if (item.Geometry == 0 && x >= 180 && x < 220 && y >= (kind == 2 ? 260 : 150) && y < (kind == 2 ? 290 : 180) ||
                    item.Geometry == 1 && x >= 170 && x < 230 && y >= 145 && y < 180)
                    active = false;
                ushort material = item.Biome < 7 ? materials[item.Biome] : item.Biome switch
                {
                    7 => x % 2 == 0 ? (ushort)1 : (ushort)147,
                    8 => x % 2 == 0 ? (ushort)60 : (ushort)70,
                    9 => x % 2 == 0 ? (ushort)368 : (ushort)367,
                    _ => x % 2 == 0 ? (ushort)59 : (ushort)1
                };
                put(x, y, active ? material : (ushort)0, active);
            }
    }

    private sealed class Context(Workspace workspace, IWorldGenerationVanillaRandom random) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => new(Provider1458.GeneratorId, "ManualHouseBuilder", 1458, 600, 600);
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
