using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GenerationGates1458Tests
{
    [Fact]
    public void Final_cleanup_doors_trapdoors_and_tall_gates_match_official_pass()
    {
        // Unmodified registered TerrariaServer1.4.5.8 FinalCleanup. Shared fixture input;
        // independent original output, rather than an encoder/decoder round trip.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, TerraRuntime.World.VanillaSpecialWorldSeed1458.None));
        PopulateFixture((x, y, type, fx, fy, active, shape, actuated) =>
        {
            var tile = new WorldTile
            {
                Type = type,
                FrameX = fx,
                FrameY = fy,
                Shape = shape,
                Flags = (active ? WorldTileFlags.Active : WorldTileFlags.None) |
                    (actuated ? WorldTileFlags.Inactive : WorldTileFlags.None)
            };
            workspace.TileStore.Set(x, y, in tile);
        });
        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "GatesFixture", 1458, 1000, 500), workspace, random));
        Assert.Equal("27AD998040A4E635B5E939826E4743E82FFE50DE4214D7085693402D071F78C3", GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal("23EA178B7446B1A87F45002C6A727BF1BED999E96A5FDB2359570C39670C07E2", GenerationNaturalPrefix49Tests.RandomHash(random, includeReport: false));
        Assert.Equal(20, workspace.VanillaAnglerQuest!.Quest);
        Assert.Equal("9827875DE943B87ABDB4DEDD01DC0E246B2B46FCF51DA0923D47DE651D9FA1CB", Hash(workspace));
        // The independent mode1 capture omits only AnglerQuestSwap. Mode0 has the
        // same grid and next261937618 after the unrelated quest epilogue.
        Assert.Equal(261937618, random.Next());
        for (int index = 0; index < 120; index++)
        {
            int family = index / 20;
            int scenario = index % 10;
            bool survives = scenario == 0 || family < 2 && scenario is 6 or 8 ||
                family >= 4 && scenario is 1 or 7;
            Assert.Equal(survives, workspace.TileStore.Get(32 + index % 14 * 68, 40 + index / 14 * 52).IsActive);
        }
    }

    // Author-written predecessor plan, also supplied to the original-only cache probe.
    internal static void PopulateFixture(Action<int, int, ushort, short, short, bool, byte, bool> put)
    {
        void Put(int x, int y, ushort type, int fx = 0, int fy = 0, bool active = true, byte shape = 0, bool actuated = false) =>
            put(x, y, type, (short)fx, (short)fy, active, shape, actuated);
        ushort[] identities = [10, 11, 386, 387, 388, 389];
        int index = 0;
        foreach (ushort type in identities)
            for (int style = 0; style < 2; style++)
                for (int scenario = 0; scenario < 10; scenario++, index++)
                {
                    int left = 32 + index % 14 * 68;
                    int top = 40 + index / 14 * 52;
                    bool tall = type is 388 or 389;
                    int objectWidth = type is 10 or 388 or 389 ? 1 : 2;
                    int objectHeight = tall ? 5 : type is 10 or 11 ? 3 : type == 386 ? 2 : 1;
                    int baseX = type == 10 ? style * 54 : type == 11 ? style * 108 : style * objectWidth * 18;
                    int baseY = style * (tall ? 94 : objectHeight * 18);
                    for (int column = 0; column < objectWidth; column++)
                        for (int row = 0; row < objectHeight; row++)
                        {
                            bool last = column == objectWidth - 1 && row == objectHeight - 1;
                            int fy = baseY + (tall ? row == 0 ? 0 : 20 + (row - 1) * 18 : row * 18);
                            if (last && scenario == 6) fy += tall ? 94 : objectHeight * 18;
                            Put(left + column, top + row, last && scenario == 7 ? (ushort)1 : type,
                                baseX + column * 18, fy, !(last && scenario == 1));
                        }
                    int hinge = left + (type == 11 && style == 1 ? 1 : 0);
                    int anchorX;
                    int anchorY;
                    if (type is 10 or 11 || tall)
                    {
                        Put(hinge, top - 1, 1);
                        Put(hinge, top + objectHeight, 1);
                        anchorX = hinge;
                        anchorY = top - 1;
                    }
                    else
                    {
                        int supportY = top + (type == 386 && style == 0 ? 1 : 0);
                        Put(left - 1, supportY, 1);
                        Put(left + objectWidth, supportY, 1);
                        anchorX = left - 1;
                        anchorY = supportY;
                    }
                    if (scenario is 2 or 3 or 4 or 5)
                        Put(anchorX, anchorY, 1, active: scenario != 2,
                            shape: scenario == 4 ? (byte)2 : scenario == 5 ? (byte)1 : (byte)0, actuated: scenario == 3);
                    if (scenario == 8)
                    {
                        if (type is 386 or 387)
                        {
                            // A whole supported TallGate, not an invalid one-cell stand-in, supplies a
                            // solid-but-noAttach side anchor. The independent pass keeps that gate intact.
                            int gateX = left - 1;
                            int gateTop = top - 2;
                            for (int row = 0; row < 5; row++)
                                Put(gateX, gateTop + row, 388, fy: row == 0 ? 0 : 20 + (row - 1) * 18);
                            Put(gateX, gateTop - 1, 1);
                            Put(gateX, gateTop + 5, 1);
                        }
                        else
                        {
                            // Doors accept a solid closed trapdoor despite noAttach. Tall gates refuse it.
                            int trapY = tall ? top + objectHeight : top - 1;
                            Put(hinge, trapY, 387);
                            Put(hinge + 1, trapY, 387, fx: 18);
                            Put(hinge - 1, trapY, 1);
                            Put(hinge + 2, trapY, 1);
                        }
                    }
                    if (scenario == 9)
                    {
                        // Only one side of a trapdoor, and only the top of a vertical gate, is insufficient.
                        if (type is 386 or 387) Put(left + objectWidth, anchorY, 1, active: false);
                        else Put(hinge, top + objectHeight, 1, active: false);
                    }
                }
    }

    private static string Hash(Workspace workspace)
    {
        var text = new StringBuilder();
        for (int x = 0; x < 1000; x++)
            for (int y = 0; y < 500; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                text.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.Wall)
                    .Append(',').Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(';');
            }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private sealed class Context(WorldGenerationRequest request, Workspace workspace,
        IWorldGenerationVanillaRandom random) : IWorldGenerationContext
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
        public int Next(int maximum) => random.Next(maximum);
        public int Next(int minimum, int maximum) => random.Next(minimum, maximum);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
