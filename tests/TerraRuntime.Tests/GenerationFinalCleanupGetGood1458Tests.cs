using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationFinalCleanupGetGood1458Tests
{
    [Fact]
    public void Worthy_tail_matches_original_registered_final_cleanup_paint_lava_and_rng()
    {
        // Direct original 1.4.5.8 registered PassLegacy("Final Cleanup") on the
        // same 1000x500 seed1458/netMode0 preimage. Unlike the older six-field
        // fixtures this digest also includes tile paint, wall paint and liquid kind.
        Workspace workspace = new(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.ForTheWorthy,
            VanillaSecretWorldSeed1458.None));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200,
            false, VanillaSpecialWorldSeed1458.ForTheWorthy));
        Put(workspace, 400, 80, 189);
        Put(workspace, 500, 70, 2);
        Put(workspace, 500, 69, 3);
        Put(workspace, 600, 200, 41);
        Put(workspace, 600, 201, 44);
        Put(workspace, 610, 200, 43);
        Put(workspace, 620, 200, 0, active: false, wall: 7);
        Put(workspace, 650, 200, 0, active: false, wall: 87);
        Put(workspace, 700, 200, 226);
        Put(workspace, 700, 199, 60);
        Put(workspace, 720, 200, 137, frameY: 36);
        for (int x = 750; x < 770; x++)
        {
            Put(workspace, x, 220, 57);
            Put(workspace, x, 219, 57);
        }

        RandomAdapter random = new(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId,
                "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("C62B6D8BE227F98AB8579B44E4B56242CDCDBD21F8CEA29BD5190B3AF2190587", Hash(workspace));
        Assert.Equal(392101808, random.Next());
        Assert.Equal((ushort)23, workspace.TileStore.Get(500, 70).Type);
        Assert.Equal((byte)25, workspace.TileStore.Get(650, 200).WallColor);
    }

    [Fact]
    public void Worthy_tail_matches_original_registered_chest_reward_slots_and_rng()
    {
        // Same original registered pass and terrain as above, plus twenty physical
        // source chest slots 0..19. Each has occupied item slot1. The independent
        // TerrariaServer.exe capture puts item678x1 in chest17's first free slot2.
        Workspace workspace = new(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.ForTheWorthy,
            VanillaSecretWorldSeed1458.None));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200,
            false, VanillaSpecialWorldSeed1458.ForTheWorthy));
        Put(workspace, 400, 80, 189);
        Put(workspace, 500, 70, 2);
        Put(workspace, 500, 69, 3);
        Put(workspace, 600, 200, 41);
        Put(workspace, 600, 201, 44);
        Put(workspace, 610, 200, 43);
        Put(workspace, 620, 200, 0, active: false, wall: 7);
        Put(workspace, 650, 200, 0, active: false, wall: 87);
        Put(workspace, 700, 200, 226);
        Put(workspace, 700, 199, 60);
        Put(workspace, 720, 200, 137, frameY: 36);
        for (int x = 750; x < 770; x++)
        {
            Put(workspace, x, 220, 57);
            Put(workspace, x, 219, 57);
        }
        for (int slot = 0; slot < 20; slot++)
        {
            int left = 800 + slot * 6;
            for (int dx = 0; dx < 2; dx++)
            {
                for (int dy = 0; dy < 2; dy++)
                {
                    WorldTile piece = new() { Type = 21, Flags = WorldTileFlags.Active,
                        FrameX = (short)(dx * 18), FrameY = (short)(dy * 18) };
                    workspace.TileStore.Set(left + dx, 100 + dy, piece);
                }
                Put(workspace, left + dx, 102, 1);
            }
            WorldGenerationChestItem[] items = new WorldGenerationChestItem[WorldGenerationChestRules.VanillaItemSlotCount];
            items[1] = new WorldGenerationChestItem(1, new ItemTypeId(1));
            Assert.True(workspace.TryAddChest(left, 100, string.Empty, items));
        }

        RandomAdapter random = new(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId,
                "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("67E8AB1AC47DDFEB8FE4BD9C66A23488CA977A53BDED709A364B1FAA9685147D", Hash(workspace));
        Assert.Equal(505583075, random.Next());
        WorldChest[] chests = workspace.CaptureGeneratedChests();
        Assert.Equal(20, chests.Length);
        for (int slot = 0; slot < chests.Length; slot++)
        {
            Assert.Equal(1, chests[slot].Items[1].ItemType);
            Assert.Equal(1, chests[slot].Items[1].Stack);
            Assert.Equal(slot == 17 ? 678 : 0, chests[slot].Items[2].ItemType);
            Assert.Equal(slot == 17 ? 1 : 0, chests[slot].Items[2].Stack);
        }
    }

    private static void Put(Workspace workspace, int x, int y, ushort type, bool active = true,
        ushort wall = 0, short frameY = 0)
    {
        WorldTile tile = new() { Type = type, Wall = wall, FrameY = frameY,
            Flags = active ? WorldTileFlags.Active : WorldTileFlags.None };
        workspace.TileStore.Set(x, y, tile);
    }

    private static string Hash(Workspace workspace)
    {
        StringBuilder snapshot = new(workspace.WidthTiles * workspace.HeightTiles * 25);
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                snapshot.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',')
                    .Append(tile.Wall).Append(',').Append(tile.FrameX).Append(',')
                    .Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(',')
                    .Append(tile.TileColor).Append(',').Append(tile.WallColor).Append(',')
                    .Append((byte)tile.LiquidKind).Append(';');
            }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
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
        public int Next(int maxValue) => random.Next(maxValue);
        public int Next(int minValue, int maxValue) => random.Next(minValue, maxValue);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
