using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Complete-delegate comparison for the registered TerrariaServer 1.4.5.8
/// <c>GenPassNameID.GlowingMushroomPlantsUndergroundAndJunglePlants</c> pass.
/// </summary>
/// <remarks>
/// Expectations come from running the unmodified registered delegate through <c>GenPass.Apply</c> on the same
/// synthetic input inside the pinned dedicated server. Each fixture is a uniform full-map ground band, so the
/// source's whole-map scan is exercised end to end rather than sampled, and each row checks the next shared-RNG
/// draw together with a hash over every cell in the world. This closes the pass on synthetic input; it is not a
/// claim about the real generated prefix.
/// </remarks>
public sealed class JunglePlantsPass1458Tests
{
    private const int Width = 600;
    private const int Height = 800;
    private const double WorldSurface = 200.0;
    private const double RockLayer = 300.0;

    [Theory]
    // fixture, seed, nextDraw, worldHash
    [InlineData(1, 42, 372175, "3562b9f7db5f6d0dc011180fecf1793676389efc07bdb21690338f6ef4e5e1c1")]
    [InlineData(1, 1458, 956224, "825b3642fb9f37d9568fac9be6ea56778cd53f04f7fc3276f5c94ec854ae1337")]
    [InlineData(2, 42, 27005, "cc77ee3dbe5988955687da8268cb8334d5ab84b4d6121ce56fa6ec8056a61b3a")]
    [InlineData(2, 1458, 636324, "07a5da58cfb2bd3068dcf018847e2ef12b9b5d853681990e22b73fc9cb954888")]
    [InlineData(3, 42, 100369, "0757f9574f890d4d5781774dd3248d837994444a9ed006e9243e67708cb3c6b7")]
    [InlineData(3, 1458, 37042, "4fd1dbece0a2324ccfbffdf544b4cf5e203d787782ac7adf92eb2fe514b5799c")]
    [InlineData(4, 42, 100500, "8122e09de6a09bd1f68a0cde16b46349af470c185abba5361c427af42442c87e")]
    [InlineData(4, 1458, 565875, "9b335caddf8e8c61a3ab4c2d682317fc7c8c0c7ac8d4d33cfd82e0c0d9ed38cb")]
    [InlineData(7, 42, 100369, "71794e4b59a11dd06823b8acb0aacfaa8fe9732b372e787c8b32390e19ad4ba8")]
    [InlineData(7, 1458, 37042, "760be2825a0b10bdffc5ca90c49fb43c2a7e03eb059880f0d483fd09d3d2b2bb")]
    [InlineData(8, 42, 20930, "93493d3f6df11b26bfb676e46ead13a9fc6d663abe2067dd487fbd86ff730b17")]
    [InlineData(8, 1458, 322100, "b1a2ed3aa5dd7381fe4b8a88f60bd53971c4fa744b7c6ef1081f5ee47da3cde7")]
    public void Jungle_half_matches_official(int fixture, int seed, int nextDraw, string worldHash) =>
        AssertAgainstOfficial(fixture, seed, nextDraw, worldHash);

    [Theory]
    // The mushroom half shares the same scan: dry ground grows mushroom trees and plants, while a flooded
    // column is offered to WorldGen.PlaceCatTail first and only falls back to a mushroom when it refuses.
    [InlineData(5, 42, 351481, "e7ba57f308e140d25ac87ca2e24a76ff0356239c0a0c2d99e9cdc312778265d1")]
    [InlineData(5, 1458, 625555, "1ef62eb54815410d0b3874f28e0abb6b4d323bcf043aab6522217b0c61b666a8")]
    [InlineData(6, 42, 190817, "215be104e76d11f7eaf06b714156b123199fcb12d2b1653eadbaf074f5122018")]
    [InlineData(6, 1458, 542286, "2b28097d6114051b465a428cf716fd7a2981f0da6d2ae810a00737ea02ff9df2")]
    public void Mushroom_half_matches_official(int fixture, int seed, int nextDraw, string worldHash) =>
        AssertAgainstOfficial(fixture, seed, nextDraw, worldHash);

    [Fact]
    public void Cancellation_precedes_cell_mutation_and_random_consumption()
    {
        Workspace workspace = CreateWorkspace(3);
        string before = HashWorld(workspace);
        var random = new RandomAdapter(42);

        Assert.Throws<OperationCanceledException>(() => Execute(workspace, random, new CancellationToken(true)));
        Assert.Equal(before, HashWorld(workspace));
        Assert.Equal(new RandomAdapter(42).Next(), random.Next());
    }

    private static void AssertAgainstOfficial(int fixture, int seed, int nextDraw, string worldHash)
    {
        Workspace workspace = CreateWorkspace(fixture);
        var random = new RandomAdapter(seed);
        Execute(workspace, random, TestContext.Current.CancellationToken);

        // One composed comparison so a divergence reports the shared RNG position and the world hash together
        // instead of hiding the second behind the first.
        string expected = $"{nextDraw}|{worldHash}";
        string actual = $"{random.Next(1000000)}|{HashWorld(workspace)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    private static void Execute(Workspace workspace, IWorldGenerationVanillaRandom random, CancellationToken cancellation)
    {
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "Jungle plants fixture", 42, Width, Height)
        {
            SeedText = "42"
        };
        new VegetationPass1458(VegetationStage1458.GlowingMushroomsAndJunglePlants, new VegetationState1458())
            .Execute(new Context(request, workspace, random, cancellation));
    }

    private static Workspace CreateWorkspace(int fixture)
    {
        var workspace = new Workspace(Width, Height);
        // This bounded pass fixture does not exercise Reset; it only needs the bootstrap and layer contracts.
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(42), 4200, false));
        Assert.True(workspace.TrySetLayers(WorldSurface, RockLayer));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            workspace.TileStore.Set(x, y, Fixture(fixture, x, y));
        return workspace;
    }

    private static string HashWorld(Workspace workspace)
    {
        var sb = new StringBuilder();
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            int slope = tile.Shape >= 2 ? tile.Shape - 1 : 0;
            bool halfBrick = tile.Shape == 1;
            sb.Append(tile.IsActive ? '1' : '0').Append(',')
              .Append(tile.Type).Append(',')
              .Append(tile.Wall).Append(',')
              .Append(tile.FrameX).Append(',')
              .Append(tile.FrameY).Append(',')
              .Append(tile.LiquidAmount).Append(',')
              .Append((int)tile.LiquidKind).Append(',')
              .Append(slope).Append(',')
              .Append(halfBrick ? '1' : '0').Append(';');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static int GroundRow(int fixture) => fixture switch
    {
        1 => 150,
        2 => 250,
        _ => 400
    };

    private static WorldTile Fixture(int fixture, int x, int y)
    {
        int ground = GroundRow(fixture);
        bool active = y >= ground;
        ushort type = 1;
        if (y == ground)
        {
            type = fixture switch
            {
                4 => (ushort)226,
                5 or 6 => (ushort)70,
                _ => (ushort)60
            };
        }
        else if (y > ground)
        {
            type = 59;
        }

        if (fixture == 7 && y == ground - 3)
        {
            // A ceiling three rows up. Nothing in the source consults it; the fixture exists to prove that.
            active = true;
            type = 1;
        }

        if (fixture == 8 && y == ground - 1 && x % 3 == 0)
        {
            // Every third column already carries a plant, which the scan must leave alone because it only
            // plants into cells whose neighbour above is inactive.
            active = true;
            type = 61;
        }

        byte liquid = 0;
        if (fixture == 6 && y >= ground - 6 && y < ground)
            liquid = byte.MaxValue;

        return new WorldTile
        {
            Type = type,
            LiquidAmount = liquid,
            LiquidKind = WorldLiquidKind.Water,
            FrameX = active ? (short)0 : (short)-1,
            FrameY = active ? (short)0 : (short)-1,
            Flags = active ? WorldTileFlags.Active : WorldTileFlags.None
        };
    }

    private sealed class Context(WorldGenerationRequest request, Workspace workspace,
        IWorldGenerationVanillaRandom random, CancellationToken cancellation) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => cancellation;
        public void ReportProgress(double fraction, string? message = null) { }
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
