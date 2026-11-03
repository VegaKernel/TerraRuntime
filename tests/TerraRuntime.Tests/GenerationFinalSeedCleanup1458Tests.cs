using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationFinalSeedCleanup1458Tests
{
    [Theory]
    [InlineData(false, 1, "D8D78DE59CBC655E86484EB51A7874C1DEF5428CB8618151DE04E95D2A968B97", "5D7D82B143E9B10758D611A749826C8AF2C5FFB242C5477A131A0DFF25D967FC", 534011718)]
    [InlineData(false, 42, "D8D78DE59CBC655E86484EB51A7874C1DEF5428CB8618151DE04E95D2A968B97", "FA244F107DFF1CAAED5F36CB9F3249BD46439F31C309C40C53F3590C213928BA", 1434747710)]
    [InlineData(false, 1458, "D8D78DE59CBC655E86484EB51A7874C1DEF5428CB8618151DE04E95D2A968B97", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634)]
    [InlineData(true, 1, "93BB038FB77CC8506E4C5DD8C91FAAF9B70B0DC4204DAE63D26AC140701307D2", "EE9D33337DA04344DB0680B34FCAEBCDD5658FFD05017C56C52B34A6F773889A", 1914957804)]
    [InlineData(true, 42, "93BB038FB77CC8506E4C5DD8C91FAAF9B70B0DC4204DAE63D26AC140701307D2", "A89ED323197F1A70CB408FF3C4EAE475DBC734327D4B8E2C932441B592659DCA", 2047190936)]
    [InlineData(true, 1458, "93BB038FB77CC8506E4C5DD8C91FAAF9B70B0DC4204DAE63D26AC140701307D2", "23EA178B7446B1A87F45002C6A727BF1BED999E96A5FDB2359570C39670C07E2", 261937618)]
    public void Ordinary_final_seed_cleanup_matches_actual_original_all_fallers_and_reset_state(
        bool wholePass, int seed, string expectedGrid, string expectedRandom, int expectedNext)
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, TerraRuntime.World.VanillaSpecialWorldSeed1458.None));
        Populate((x, y, c) =>
        {
            WorldTile tile = ConvertCell(c);
            workspace.TileStore.Set(x, y, in tile);
        });
        var random = new RandomAdapter(seed);
        if (wholePass)
            new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(new Context(workspace, random, seed));
        else
            new GenerationTileFraming1458(workspace.TileStore, random, workspace).FinalSeedCleanup();
        if (wholePass) Assert.Equal(seed switch { 1 => "8A9BEB2B1D4B49BAFCA96B51ADC062B0003E233174FC90EDE4296E9E490E6380", 42 => "8A9BEB2B1D4B49BAFCA96B51ADC062B0003E233174FC90EDE4296E9E490E6380", 1458 => "8A9BEB2B1D4B49BAFCA96B51ADC062B0003E233174FC90EDE4296E9E490E6380", _ => throw new InvalidOperationException() }, GenerationNaturalPrefix49Tests.GridHash(workspace));
        if (wholePass) Assert.Equal(seed switch { 1 => 40, 42 => 10, 1458 => 20, _ => throw new InvalidOperationException() }, workspace.VanillaAnglerQuest!.Quest);
        Assert.Equal(expectedGrid, Hash(workspace));
        Assert.Equal(expectedRandom, random.CapturePhysicalRandomHash());
        Assert.Equal(expectedNext, random.Next());
    }

    private static WorldTile ConvertCell(Cell c) => new()
    {
        Type = c.Type,
        Wall = c.Wall,
        FrameX = c.Fx,
        FrameY = c.Fy,
        Shape = c.Shape,
        TileColor = c.TileColor,
        WallColor = c.WallColor,
        LiquidAmount = c.Liquid,
        LiquidKind = (WorldLiquidKind)c.Kind,
        Flags = (c.Active ? WorldTileFlags.Active : WorldTileFlags.None) |
            (c.Actuated ? WorldTileFlags.Inactive : WorldTileFlags.None) |
            (c.Actuator ? WorldTileFlags.Actuator : WorldTileFlags.None) |
            (c.Wires ? WorldTileFlagMasks.Wires : WorldTileFlags.None) |
            (c.Coatings ? WorldTileFlagMasks.Visibility | WorldTileFlagMasks.Fullbright : WorldTileFlags.None) |
            (c.HalfSlope ? WorldTileFlags.HalfBrickWithSlope : WorldTileFlags.None)
    };

    private readonly record struct Cell(ushort Type = 0, ushort Wall = 0, short Fx = 0, short Fy = 0,
        bool Active = false, byte Shape = 0, bool Actuated = false, bool Actuator = false,
        bool Wires = false, bool Coatings = false, byte TileColor = 0, byte WallColor = 0,
        byte Liquid = 0, byte Kind = 0, bool HalfSlope = false);

    private static void Populate(Action<int, int, Cell> write)
    {
        ushort[] identities = [53, 234, 112, 116, 224, 123, 330, 331, 332, 333, 495];
        for (int n = 0; n < identities.Length; n++)
            for (int profile = 0; profile < 18; profile++)
            {
                int x = 80 + n * 80, y = profile == 17 ? 4 : 55 + profile * 20;
                Cell current = new(identities[n], (ushort)(profile == 15 ? 350 : 7), 17, 23, profile != 11,
                    3, false, true, true, true, 5, 7, 23, 2, true);
                Cell below = new(51, 7, 31, 37, false, 4, false, true, true, true, 11, 13, 29, 2, true);
                Cell above = new();
                if (profile is 1 or 2 or 3 or 4 or 5 or 7 or 12)
                    below = below with
                    {
                        Type = 1,
                        Active = true,
                        Shape = (byte)(profile == 2 ? 1 : profile is 3 or 4 ? 4 : 0),
                        Actuated = profile is 5 or 7 or 12,
                        Wall = (ushort)(profile is 7 or 12 ? 350 : 7),
                        HalfSlope = profile == 4
                    };
                if (profile is 6 or 14) below = below with { Type = 19, Active = true, Shape = 0, HalfSlope = false, Wall = (ushort)(profile == 14 ? 350 : 7) };
                if (profile is 8 or 9) below = below with { Type = 51, Active = true, Wall = 350 };
                if (profile == 9) above = new(1, 350, Active: true);
                if (profile == 10) below = below with { Wall = 350 };
                if (profile == 12) above = new(1, 350);
                if (profile == 13) above = new(1, 350, Active: true);
                if (profile == 16) below = below with { Type = 123, Active = true, Actuated = true, Wall = 350 };
                write(x, y, current);
                write(x, y + 1, below);
                write(x, y - 1, above);
            }
    }

    private static string Hash(Workspace workspace)
    {
        var text = new StringBuilder();
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile t = workspace.TileStore.Get(x, y);
                text.Append(t.IsActive ? 1 : 0).Append(',').Append(t.Type).Append(',').Append(t.Wall).Append(',')
                    .Append(t.FrameX).Append(',').Append(t.FrameY).Append(',').Append(t.LiquidAmount).Append(',')
                    .Append((byte)t.LiquidKind).Append(',').Append(t.TileColor).Append(',').Append(t.WallColor).Append(',')
                    .Append(t.IsHalfBrick ? 1 : 0).Append(',').Append(t.Shape >= 2 ? t.Shape - 1 : 0).Append(',')
                    .Append((t.Flags & WorldTileFlags.WireRed) != 0 ? 1 : 0).Append(',')
                    .Append((t.Flags & WorldTileFlags.WireBlue) != 0 ? 1 : 0).Append(',')
                    .Append((t.Flags & WorldTileFlags.WireGreen) != 0 ? 1 : 0).Append(',')
                    .Append((t.Flags & WorldTileFlags.WireYellow) != 0 ? 1 : 0).Append(',')
                    .Append((t.Flags & WorldTileFlags.Actuator) != 0 ? 1 : 0).Append(',').Append(t.IsActuated ? 1 : 0).Append(',')
                    .Append((t.Flags & WorldTileFlags.InvisibleBlock) != 0 ? 1 : 0).Append(',')
                    .Append((t.Flags & WorldTileFlags.InvisibleWall) != 0 ? 1 : 0).Append(',')
                    .Append((t.Flags & WorldTileFlags.FullbrightBlock) != 0 ? 1 : 0).Append(',')
                    .Append((t.Flags & WorldTileFlags.FullbrightWall) != 0 ? 1 : 0).Append(';');
            }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private sealed class Context(Workspace workspace, IWorldGenerationVanillaRandom random, int seed) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => new(Provider1458.GeneratorId, "Fixture", (ulong)seed, 1000, 500);
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
