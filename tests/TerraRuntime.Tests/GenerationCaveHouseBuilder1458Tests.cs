using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationCaveHouseBuilder1458Tests
{
    internal static IWorldGenerationVanillaRandom CreateRandomForTesting(int seed) => new RandomAdapter(seed);

    [Theory]
    [InlineData(1, false, "D86D874C6B7F048527682D9210FB5D114F7EEF136854CC44171A4D65D5EAB7B5")]
    [InlineData(1, true, "A9B61FA2499D9B78E1BD85994B40DE6BD20002CD35E18FCB9EAC8DEBB873A82B")]
    [InlineData(2, false, "2F2603DE9FD7770683F418C1ACC265153116AAE75B45750AB28116178ABD6286")]
    [InlineData(2, true, "8A7C2AB78A62FDA8690C2E1C51528755D8B1C28400648B462A297F96D46F2805")]
    public void Beam_minimum_length_and_search_support_match_original_inactive_platform_and_landing(int gap,
        bool inactivePlatform, string expected)
    {
        var workspace = new Workspace(600, 600);
        var upper = new WorldTile { Type = inactivePlatform ? (ushort)19 : (ushort)30,
            Flags = inactivePlatform ? WorldTileFlags.None : WorldTileFlags.Active };
        workspace.TileStore.Set(40, 41, in upper);
        var landing = new WorldTile { Type = 1, Flags = WorldTileFlags.Active | WorldTileFlags.Inactive };
        Assert.True(landing.TrySetSourceShape(true, 4));
        workspace.TileStore.Set(40, 42 + gap, in landing);
        var random = new RandomAdapter(1458);
        string before = random.PhysicalState();
        CaveHousePlacement1458.PlaceSupportBeamsForTesting(workspace, [new(40, 40, 1, 2)], 124, random);
        var text = new StringBuilder();
        for (int x = 35; x < 46; x++)
        for (int y = 35; y < 51; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            text.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.FrameX)
                .Append(',').Append(tile.FrameY).Append(',').Append(tile.IsHalfBrick ? '1' : '0').Append(',')
                .Append(tile.SourceSlope).Append(',').Append(tile.IsActuated ? '1' : '0').Append(';');
        }
        Assert.Equal(before, random.PhysicalState());
        text.Append(random.PhysicalState());
        Assert.Equal(expected, Digest(text));
        bool placed = gap == 2 && !inactivePlatform;
        Assert.Equal(placed, workspace.TileStore.Get(40, 42).IsActive);
        WorldTile after = workspace.TileStore.Get(40, 42 + gap);
        Assert.Equal(!placed, after.IsHalfBrick);
        Assert.Equal(placed ? (byte)0 : (byte)4, after.SourceSlope);
        Assert.True(after.IsActuated);
    }

    [Theory]
    [InlineData(0, "A7E6B71BB2ACA2E2C83FB910E5ADB749498B7D80CB49C9AE2DE3829B44CA4889")]
    [InlineData(1, "9A3257E3C5489DC3B90F2061EA84C94C524AEC1149D9CCC9516B7A3321CC463C")]
    [InlineData(2, "7E4D4CD90CE09FC88561C565987F8531C0357AAD68400E69257F538EEC498724")]
    [InlineData(3, "95A45884F082BDA5DF2E3BDF22B7FD03DD1B98C7FACF0C005FD3FA478EB95460")]
    [InlineData(4, "0CFEBC870745F9B4D24B0A736E5BB0493CD46F4AF161E9B00E49627B9767C213")]
    [InlineData(5, "EC9C9F53B58FB8F37D10E4B17BD6F5843B3E1E35E32CFC3AD2FB92D0CC25C1DD")]
    [InlineData(6, "1ECBF7C28C1B956E10B0E8DA625187107356C6032CA492DCC38D6525301B0CD7")]
    public void Complete_ordinary_house_builder_matches_original_tiles_chests_structures_tools_and_rng(int biome, string expected)
    {
        var aggregate = new StringBuilder();
        foreach (int seed in new[] { 1, 1458, 8675309 })
        for (int geometry = 0; geometry < 2; geometry++)
        {
            var workspace = new Workspace(600, 600);
            Assert.True(workspace.TrySetLayers(120, 250));
            workspace.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458
            {
                CopperOre = 7, IronOre = 6, SilverOre = 9, GoldOre = 8,
                CopperBar = 20, IronBar = 22, SilverBar = 21, GoldBar = 19,
                HellChestItems = [274, 220, 112, 218, 3019], ExtraBastStatueCountMax = 2,
                TreeX = [], TreeStyle = [], CaveBackX = [], CaveBackStyle = [], ForestBackgroundStyles = []
            });
            Populate((x, y, type, active) =>
            {
                var tile = new WorldTile { Type = type, Flags = active ? WorldTileFlags.Active : WorldTileFlags.None };
                workspace.TileStore.Set(x, y, in tile);
            }, geometry);
            var random = new RandomAdapter(seed);
            var state = new ChestPlacementState1458();
            var context = new Context(workspace, random);
            state.EnsureInitialized(context, workspace);
            CaveHousePlacement1458.PlaceRooms(workspace, state, random, Rooms(geometry), (CaveHouseType1458)biome);
            var text = new StringBuilder();
            for (int x = 0; x < 600; x++)
            for (int y = 0; y < 600; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                text.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.Wall)
                    .Append(',').Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append((tile.Flags & WorldTileFlags.WireRed) != 0 ? '1' : '0').Append(',')
                    .Append(tile.IsHalfBrick ? '1' : '0').Append(',').Append(tile.SourceSlope).Append(',')
                    .Append(tile.IsActuated ? '1' : '0').Append(',').Append(tile.TileColor).Append(',')
                    .Append(tile.WallColor).Append(',').Append(tile.LiquidAmount).Append(',').Append((byte)tile.LiquidKind).Append(';');
            }
            text.Append("PROTECTED:");
            foreach (CaveHouseRoom1458 room in state.ProtectedHouseRooms)
                text.Append(room.X).Append(',').Append(room.Y).Append(',').Append(room.Width).Append(',').Append(room.Height).Append(';');
            text.Append("TOOLS:").Append(state.CaveHouseSharpenerCount).Append(',')
                .Append(state.CaveHouseExtractinatorCount).Append(',').Append(state.CaveHouseBastStatueCount).Append(';');
            text.Append("CHESTS:");
            foreach (WorldChest chest in workspace.CaptureGeneratedChests())
            {
                text.Append(chest.SlotId).Append(',').Append(chest.X).Append(',').Append(chest.Y).Append(',').Append(chest.Name).Append(':');
                foreach (WorldChestItem item in chest.Items)
                    text.Append(item.ItemType).Append(',').Append(item.Stack).Append(',').Append(item.Prefix).Append(';');
                text.Append('|');
            }
            text.Append("CHESTSTATE:").Append(state.ChestContext.HellChest).Append(',')
                .Append(state.ChestContext.GeneratedShadowKey ? '1' : '0').Append(',')
                .Append(state.ChestContext.GeneratedRamRune ? '1' : '0').Append(',')
                .Append(state.ChestContext.GennedLivingMahoganyWands ? '1' : '0').Append(';');
            text.Append("RNG:").Append(random.PhysicalState());
            aggregate.Append(seed).Append('/').Append(geometry).Append(':').Append(Digest(text)).Append('|');
        }
        Assert.Equal(expected, Digest(aggregate));
    }

    internal static CaveHouseRoom1458[] Rooms(int geometry) => geometry == 0
        ? [new(100, 180, 23, 10)]
        : [new(160, 170, 24, 10), new(154, 179, 26, 12)];

    internal static void Populate(Action<int, int, ushort, bool> put, int geometry)
    {
        for (int x = 0; x < 600; x++)
        for (int y = 0; y < 600; y++)
        {
            bool active = y >= 120 && !(geometry == 1 && x >= 140 && x < 205 && y >= 155 && y < 215);
            put(x, y, active ? (ushort)1 : (ushort)0, active);
        }
    }

    private static string Digest(StringBuilder text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));

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
