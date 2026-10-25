using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedPostSettle1458Tests
{
    [Theory]
    [InlineData(600, 500, "7FD75A49E0099D3800CC9F51AE5B9365FF70DF444983FF617B26C1531792BC24")]
    [InlineData(4200, 1200, "AE27ADEC1AF031C1207E873B6C12CFABA9605B973310FCCCEBD31029A7AFBB20")]
    public void Ice_matches_official_registered_ordinary_pass(int width, int height, string digest)
    {
        // TerrariaServer 1.4.5.8 FragileIceOverIceBiomeWater registered PassLegacy,
        // seed1458: five snow/ice floors, shallow water, Honey/Shimmer,
        // ordinary-stone refusal and lava refusal.
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(height == 1200 ? 300 : 140, height == 1200 ? 500 : 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        int firstX = height == 1200 ? 1000 : 100;
        int top = height == 1200 ? 420 : 190;
        ushort[] floors = [147, 161, 200, 163, 164, 1, 147, 147, 147, 147];
        for (int group = 0; group < floors.Length; group++)
        {
            int left = firstX + group * 45;
            for (int x = left; x < left + 9; x++)
            {
                var floor = new WorldTile { Type = floors[group], Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(x, top + 6, in floor);
                for (int y = top; y < top + 6; y++)
                {
                    var water = new WorldTile
                    {
                        LiquidAmount = group == 7 ? (byte)50 : byte.MaxValue,
                        LiquidKind = group switch
                        {
                            6 => WorldLiquidKind.Lava,
                            8 => WorldLiquidKind.Honey,
                            9 => WorldLiquidKind.Shimmer,
                            _ => WorldLiquidKind.Water
                        }
                    };
                    workspace.TileStore.Set(x, y, in water);
                }
            }
        }
        var paintedWater = new WorldTile
        {
            LiquidAmount = byte.MaxValue,
            TileColor = 5,
            Flags = WorldTileFlags.WireRed
        };
        workspace.TileStore.Set(firstX, top, in paintedWater);
        var overhead = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(firstX + 4, top - 1, in overhead);

        var random = new RandomAdapter(1458);
        new PostSettlePass1458(PostSettleStage1458.Ice, new PostSettleState1458())
            .Execute(new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Ice", 1458, width, height),
                workspace, random));

        int thinIce = 0;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            if (workspace.TileStore.Get(x, y) is { IsActive: true, Type: 162 }) thinIce++;
        Assert.Equal(71, thinIce);
        Assert.True(workspace.VanillaPostIceNonSolidTiles);
        Assert.Equal(digest, HashFixture(workspace));
        Assert.Equal(906992634, random.Next());
    }

    [Theory]
    [InlineData(1458, 600, 500, "72345AD32F51D98300A1274DCBBCC0A8430D47530DF591F3669C30AB98D1D607", 157, 80, 444, 80, 163, 402866635)]
    [InlineData(1458, 4200, 1200, "6502B8649A5096D8D7A56B6B873E270B75F42B03CD773BB89A77EC19680F5CAF", 997, 230, 3204, 230, 163, 402866635)]
    [InlineData(1, 600, 500, "FEB4B449F9CF69F84F308FEDB1CF9EFFD79C030ECDF37840CFD475152DCAFF1E", 153, 80, 440, 130, 85, 1029774417)]
    [InlineData(1, 4200, 1200, "AECDF32415BB89E7CF03FB6F72A4BB99BB5ED2706FFADB9ACA434EC4ED1E367D", 993, 230, 3200, 280, 85, 1029774417)]
    [InlineData(42, 600, 500, "0CCE92D1033891BF7A1A2833913375B307325824F3693E4AFEA028E7B6250A25", 163, 130, 440, 80, 99, 514872570)]
    [InlineData(42, 4200, 1200, "43F9CC5575055F1B2541E918D8942AB3AAF9EB292DEE5F51954A0EE91710C9F7", 1003, 280, 3200, 230, 99, 514872570)]
    [InlineData(4, 600, 500, "936BF1918F54272E68817DAC0B1F487A010DE03FD74B82BD13B880E77B80F9FB", 160, 130, 440, 130, 0, 1211126341)]
    [InlineData(4, 4200, 1200, "5F0015F9C799F1200E049301B11229468A75BF9BE91332A697F329D20E3ED430", 1000, 280, 3200, 280, 0, 1211126341)]
    public void Shell_piles_match_official_registered_ordinary_pass_and_publish_anchors(
        int seed, int width, int height, string digest, int leftXAfter, int leftYAfter,
        int rightXAfter, int rightYAfter, int expectedShells, int nextRandom)
    {
        // Independent TerrariaServer 1.4.5.8 registered PassLegacy, seed1458.
        // Dry substrate patches accept both outward offers without introducing a
        // second water-adjacent edge in the source's anchor search rectangle.
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(height == 1200 ? 300 : 140, height == 1200 ? 500 : 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        int leftX = height == 1200 ? 1000 : 160;
        int rightX = height == 1200 ? 3200 : 440;
        int floorY = height == 1200 ? 280 : 130;
        int bandWidth = height == 1200 ? 200 : 120;
        var sand = new WorldTile { Type = 53, Flags = WorldTileFlags.Active };
        void SandBand(int firstX, int lastX)
        {
            for (int x = firstX; x <= lastX; x++)
            for (int y = floorY; y < floorY + 50; y++)
                workspace.TileStore.Set(x, y, in sand);
        }
        SandBand(leftX, leftX + bandWidth - 1);
        SandBand(rightX - bandWidth + 1, rightX);
        SandBand(leftX - 100, leftX - 2);
        SandBand(rightX + 2, rightX + 100);
        var water = new WorldTile { LiquidAmount = 100 };
        workspace.TileStore.Set(leftX - 1, floorY, in water);
        workspace.TileStore.Set(rightX + 1, floorY, in water);
        workspace.SetVanillaShellAnchors(new(leftX, floorY), new(rightX, floorY));

        var random = new RandomAdapter(seed);
        new PostSettlePass1458(PostSettleStage1458.ShellPiles, new PostSettleState1458())
            .Execute(new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Shells", checked((ulong)seed), width, height),
                workspace, random));

        Assert.Equal(new VanillaShellAnchor1458(leftXAfter, leftYAfter), workspace.VanillaLeftShellAnchor);
        Assert.Equal(new VanillaShellAnchor1458(rightXAfter, rightYAfter), workspace.VanillaRightShellAnchor);
        int shells = 0;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            if (workspace.TileStore.Get(x, y) is { IsActive: true, Type: 495 }) shells++;
        Assert.Equal(expectedShells, shells);
        Assert.Equal(digest, HashFixture(workspace));
        Assert.Equal(nextRandom, random.Next());
    }

    [Theory]
    [InlineData(1458, 600, 500, "F418CBBCBE9D86668D9C5DC01CA051DBBEE6C3A3401A52C9BBDBC3D38473A31E", 1162070998)]
    [InlineData(1458, 4200, 1200, "E58255F2CA721C9615E7C8A22DA42105C91D2AF87001265CCCC0C78DCA60FD82", 663238162)]
    [InlineData(42, 600, 500, "F418CBBCBE9D86668D9C5DC01CA051DBBEE6C3A3401A52C9BBDBC3D38473A31E", 818126015)]
    [InlineData(42, 4200, 1200, "E58255F2CA721C9615E7C8A22DA42105C91D2AF87001265CCCC0C78DCA60FD82", 320230768)]
    [InlineData(8675309, 600, 500, "F418CBBCBE9D86668D9C5DC01CA051DBBEE6C3A3401A52C9BBDBC3D38473A31E", 447973439)]
    [InlineData(8675309, 4200, 1200, "E58255F2CA721C9615E7C8A22DA42105C91D2AF87001265CCCC0C78DCA60FD82", 1122986386)]
    [InlineData(4, 600, 500, "2B90551D225A1AB53054028E9695238CFC8CC69AA32A169A601F27399D4DF6B5", 1679415354)]
    [InlineData(4, 4200, 1200, "EF7D4D0D83D47BE4F675EC32AA7CBC517E1186134B13D1225B1BD0B4C4DFC596", 875195200)]
    public void Waterfalls_matches_official_registered_full_pass(int seed, int width, int height, string digest, int nextRandom)
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy Waterfalls, seed1458. The fixture
        // exercises both scans, snow-type rarity, halfbrick veto, protected supports,
        // type-191 temporary non-solidity and a twice-pounded lip.
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(height == 1200 ? 300 : 140, height == 1200 ? 500 : 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        void Put(int x, int y, ushort type = 0, bool active = false, byte liquid = 0,
            ushort wall = 0, bool half = false)
        {
            var tile = new WorldTile
            {
                Type = type, Flags = active ? WorldTileFlags.Active : WorldTileFlags.None,
                LiquidAmount = liquid, Wall = wall, Shape = half ? (byte)1 : (byte)0
            };
            workspace.TileStore.Set(x, y, in tile);
        }

        int row = height == 1200 ? 500 : 120;
        foreach (int baseX in height == 1200 ? new[] { 800, 1800, 2800 } : new[] { 80 })
        {
            foreach ((int offset, ushort type) in new[] { (0, (ushort)1), (45, (ushort)75),
                         (90, (ushort)76), (135, (ushort)1), (180, (ushort)1),
                         (225, (ushort)1), (475, (ushort)191) })
            {
                int x = baseX + offset;
                Put(x, row, type, active: true, wall: offset == 180 ? (ushort)350 : (ushort)0);
                Put(x, row + 1, 1, active: true);
                Put(x - 1, row, liquid: 100);
            }
            Put(baseX + 135, row - 5, 1, active: true, half: true);
            Put(baseX + 225, row - 1, 21, active: true);
            foreach ((int offset, bool left, bool right, ushort type) in new[] {
                         (270, true, false, (ushort)1), (315, false, true, (ushort)1),
                         (360, true, true, (ushort)1), (405, true, false, (ushort)48),
                         (440, true, false, (ushort)232) })
            {
                int x = baseX + offset;
                Put(x, row, type, active: true);
                Put(x, row + 1, 1, active: true);
                if (left) { Put(x - 1, row, 1, active: true, half: true); Put(x - 2, row, liquid: 100); }
                if (right) { Put(x + 1, row, 1, active: true, half: true); Put(x + 2, row, liquid: 100); }
            }
        }

        var random = new RandomAdapter(seed);
        new PostSettlePass1458(PostSettleStage1458.Waterfalls, new PostSettleState1458())
            .Execute(new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", checked((ulong)seed), width, height),
                workspace, random));

        Assert.Equal(digest, HashFixture(workspace));
        Assert.Equal(nextRandom, random.Next());
    }

    [Fact]
    public void Canonical_ordinary_world_extends_source_order_from_remove_water_through_statues()
    {
        var provider = new SourceBackedPostSettle1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "PostSettle",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(Provider1458.GeneratorId, provider.Id);
        Assert.Equal(67, builder.Entries.Count);

        string[] expectedStage =
        [
            "terraria:1.4.5.8/RemoveWaterFromSand",
            "terraria:1.4.5.8/Oasis",
            "terraria:1.4.5.8/ShellPiles",
            "terraria:1.4.5.8/SmoothWorld",
            "terraria:1.4.5.8/Waterfalls",
            "terraria:1.4.5.8/Ice",
            "terraria:1.4.5.8/WallVariety",
            "terraria:1.4.5.8/LifeCrystals",
            "terraria:1.4.5.8/Statues"
        ];

        int settle = builder.Entries.FindIndex(static entry =>
            entry.Descriptor.Id == SourceBackedJungleStructures1458.SettleLiquidsId);
        Assert.True(settle >= 0);
        Assert.Equal(
            expectedStage,
            builder.Entries.Skip(settle + 1).Take(expectedStage.Length).Select(static entry => entry.Descriptor.Id.Value));

        foreach (string passId in expectedStage)
            Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, passId).Descriptor.RngMode);

        CaptureEntry secrets = Find(builder, "terraria:1.4.5.8/SecretSeeds");
        Assert.Equal(WorldGenerationRngMode.IsolatedDeterministic, secrets.Descriptor.RngMode);
        Assert.IsType<OrdinarySecretSeedCompatibilityBarrier1458>(secrets.Pass);
        Assert.Contains(SourceBackedPostSettle1458.StatuesId, secrets.Descriptor.RequiredAfter.ToArray());

        Assert.IsType<SourceBackedBiomesCompatibilityBarrier1458>(Find(builder, "terraria:1.4.5.8/Biomes").Pass);
        Assert.IsType<SourceBackedCavesCompatibilityBarrier1458>(Find(builder, "terraria:1.4.5.8/Caves").Pass);
        Assert.IsType<SourceBackedOreCompatibilityBarrier1458>(Find(builder, "terraria:1.4.5.8/Ores").Pass);
    }

    [Fact]
    public void Pinned_catalog_segment_matches_source_order_from_first_settle_through_statues()
    {
        string[] expected =
        [
            "Remove Water From Sand",
            "Oasis",
            "Shell Piles",
            "Smooth World",
            "Waterfalls",
            "Ice",
            "Wall Variety",
            "Life Crystals",
            "Statues"
        ];

        string[] catalog = PassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        int settle = Array.IndexOf(catalog, "Settle Liquids");

        Assert.True(settle >= 0);
        Assert.Equal(expected, catalog.Skip(settle + 1).Take(expected.Length));
    }

    [Fact]
    public void Noncanonical_world_keeps_existing_compatibility_plan_unchanged()
    {
        var provider = new SourceBackedPostSettle1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "Synthetic",
            Seed: 1458,
            WidthTiles: 192,
            HeightTiles: 128);
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(8, builder.Entries.Count);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedPostSettle1458.OasisId);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedPostSettle1458.StatuesId);
    }

    private static CaptureEntry Find(CaptureBuilder builder, string id) =>
        Assert.Single(builder.Entries, entry => entry.Descriptor.Id.Value == id);

    private readonly record struct CaptureEntry(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private static string HashFixture(Workspace workspace)
    {
        var snapshot = new StringBuilder(workspace.WidthTiles * workspace.HeightTiles * 20);
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            snapshot.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(',')
                .Append(tile.Shape).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private sealed class Context(
        WorldGenerationRequest request, Workspace workspace, IWorldGenerationVanillaRandom random) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => request;
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
        public int Next() => random.Next();
        public int Next(int maxValue) => random.Next(maxValue);
        public int Next(int minValue, int maxValue) => random.Next(minValue, maxValue);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CaptureEntry> Entries { get; } = [];

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            Entries.Add(new CaptureEntry(descriptor, pass));
    }
}
