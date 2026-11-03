using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationSettleCaller1458Tests
{
    [Theory]
    [InlineData(false, 1, "E1F77E6133D169767A0B30563FD1EEFB6C09F0251020F92AA4BFB3FC6B0AD132", "80CC01BA127EDD77FD9D57790793272673CD29192F6FF1179DB9A1567D45B8D8", 532638534)]
    [InlineData(false, 42, "E1F77E6133D169767A0B30563FD1EEFB6C09F0251020F92AA4BFB3FC6B0AD132", "EF45437E9DC20A3AAE2FCBA4A18CCD6BA3575F3823C0C875493DBEEB1B7BAB9E", 1111217775)]
    [InlineData(false, 1458, "E1F77E6133D169767A0B30563FD1EEFB6C09F0251020F92AA4BFB3FC6B0AD132", "2A9351445E69CD6E5581718381294CA2DBFAA2057BEBCD6402CD3BF72227F95F", 1162070998)]
    [InlineData(true, 1, "E1F77E6133D169767A0B30563FD1EEFB6C09F0251020F92AA4BFB3FC6B0AD132", "80CC01BA127EDD77FD9D57790793272673CD29192F6FF1179DB9A1567D45B8D8", 532638534)]
    [InlineData(true, 42, "E1F77E6133D169767A0B30563FD1EEFB6C09F0251020F92AA4BFB3FC6B0AD132", "EF45437E9DC20A3AAE2FCBA4A18CCD6BA3575F3823C0C875493DBEEB1B7BAB9E", 1111217775)]
    [InlineData(true, 1458, "E1F77E6133D169767A0B30563FD1EEFB6C09F0251020F92AA4BFB3FC6B0AD132", "2A9351445E69CD6E5581718381294CA2DBFAA2057BEBCD6402CD3BF72227F95F", 1162070998)]
    public void Registered_settle_callers_share_source_random_for_nearly_full_three_cell_basin(
        bool laterPass, int seed, string expectedGrid, string expectedRandom, int expectedNext)
    {
        // Untouched official AddPasses b__285_48/b__285_97, two exact repeated captures.
        // Source Liquid.Update averages 254,254,255 to254 and calls genRand.Next(30).
        var workspace = new Workspace(600, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        GenerationLiquidFixture1458.Configure(workspace, laterPass);
        void Place(int x, int y, ushort type)
        {
            var tile = new WorldTile { Type = type, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in tile);
        }
        for (int x = 299; x <= 303; x++) Place(x, 201, 1);
        Place(299, 200, 1);
        Place(303, 200, 1);
        for (int x = 300; x <= 302; x++)
        {
            var tile = new WorldTile { LiquidAmount = x == 302 ? (byte)255 : (byte)254 };
            workspace.TileStore.Set(x, 200, in tile);
        }
        var random = new RandomAdapter(seed);
        var context = new Context(workspace, random, seed);
        if (laterPass)
            new FinalPass1458(FinalStage1458.SettleLiquidsAgain, new FinalState1458()).Execute(context);
        else
            new JungleStructurePass1458(JungleStructureStage1458.SettleLiquids, new JungleStructureState1458()).Execute(context);
        Assert.Equal(expectedGrid, Hash(workspace));
        Assert.Equal(expectedRandom, random.CapturePhysicalRandomHash());
        Assert.Equal(expectedNext, random.Next());
        Assert.False(workspace.TileStore.LiquidUpdates.HasPendingWork);
    }

    private static string Hash(Workspace workspace)
    {
        var text = new StringBuilder();
        WorldTileFlags[] flags = [WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen,
            WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive,
            WorldTileFlags.InvisibleBlock, WorldTileFlags.FullbrightBlock,
            WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall];
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                text.Append(tile.IsActive ? 1 : 0).Append(',').Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(',')
                    .Append((byte)tile.LiquidKind).Append(',').Append(tile.IsHalfBrick ? 1 : 0).Append(',')
                    .Append(tile.SourceSlope).Append(',').Append(tile.TileColor).Append(',').Append(tile.WallColor);
                foreach (WorldTileFlags flag in flags) text.Append(',').Append((tile.Flags & flag) != 0 ? 1 : 0);
                text.Append(';');
            }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private sealed class Context(Workspace workspace, IWorldGenerationVanillaRandom random, int seed) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => new(Provider1458.GeneratorId, "Fixture", (ulong)seed, 600, 500);
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
