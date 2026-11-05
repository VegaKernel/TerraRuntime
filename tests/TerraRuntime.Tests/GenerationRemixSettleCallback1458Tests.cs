using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationRemixSettleCallback1458Tests
{
    [Fact]
    public void Registered_original_remix_settle_turns_isolated_falling_water_into_lava()
    {
        // Untouched TerrariaServer.exe 1.4.5.8 registered "Settle Liquids
        // Again" delegate, seed1458, 1000x500. The source liquid at
        // (500,200) falls vertically to (500,259), strictly between lavaLine
        // 180 and rockLayer-80=320, outside the 380-tile ocean margin.
        var workspace = new Workspace(1000, 500);
        workspace.InitializeVanillaGenerationSolidity();
        Assert.True(workspace.TrySetLayers(140, 400));
        workspace.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.Remix,
            VanillaSecretWorldSeed1458.None));
        workspace.SetVanillaLiquidLines(400, 180);
        for (int y = 190; y <= 260; y++)
        {
            Put(499, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            Put(501, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        }
        Put(500, 260, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Put(500, 200, new WorldTile { LiquidAmount = 200, LiquidKind = WorldLiquidKind.Water });

        var random = new SourceRandom(1458);
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace,
            useWorldGenerationSolidity: true);
        VanillaWaterCheckDiagnostic1458 result = framing.SettleLiquidsWithContext(
            400, workspace.IsVanillaGenerationSolid, workspace.SetVanillaGenerationSolid,
            (_, _, _) => false, () => { }, () => { },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsApplied, result.ToString());
        Assert.Equal((byte)0, workspace.TileStore.Get(500, 200).LiquidAmount);
        Assert.Equal((byte)200, workspace.TileStore.Get(500, 259).LiquidAmount);
        Assert.Equal(WorldLiquidKind.Lava, workspace.TileStore.Get(500, 259).LiquidKind);
        Assert.Equal("33F52178F2CC1F8B8C436BE347060FB61420EB9D93E65344D41C7B20F504648C", FullGridHash());
        Assert.Equal(906992634, random.Next());

        void Put(int x, int y, WorldTile tile) => workspace.TileStore.Set(x, y, in tile);

        string FullGridHash()
        {
            var cells = new StringBuilder(1000 * 500 * 18);
            for (int x = 0; x < 1000; x++)
            for (int y = 0; y < 500; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                cells.Append(tile.IsActive ? '1' : '0').Append(',')
                    .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append(tile.LiquidAmount).Append(',').Append((byte)tile.LiquidKind)
                    .Append(';');
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cells.ToString())));
        }
    }

    private sealed class SourceRandom(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 source = new(seed);
        public int Next() => source.Next();
        public int Next(int max) => source.Next(max);
        public int Next(int min, int max) => source.Next(min, max);
        public double NextDouble() => source.NextDouble();
        public void NextBytes(byte[] bytes) => source.NextBytes(bytes);
    }
}
