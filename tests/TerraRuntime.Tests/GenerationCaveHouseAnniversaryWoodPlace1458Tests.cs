using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationCaveHouseAnniversaryWoodPlace1458Tests
{
    // Untouched TerrariaServer 1.4.5.8 WoodHouseBuilder.Place on the same
    // 600x600 two-room stone/cavity image; corrected original JSON SHA ED943EE69CC66C88...
    [Theory]
    [InlineData(1, "88cff6e412ddb6c0cd752d8bb31853bcc5b710d088d79ddf0c72a5a889eaa6a4",
        13, "d823328fb6bae4228f8cfcd4715f2856859573000c665206fab2c72aa29178cb", 21968909,
        178, 177, "975x1p80;997x1p0;965x85p0;40x30p0;2350x3p0;8x13p0;72x50p0;5508x1p0")]
    [InlineData(42, "2be8b4cf336408fcc67b4649e00d1058333ff61d157126c2c1cc781252bd2e24",
        27, "f71fd5d252b66f90aa78ae1000e6b0f607ca8ef7006ae6f6689d377d5f5dea6e", 2004133994,
        162, 177, "50x1p0;965x66p0;22x13p0;290x1p0;2350x2p0;72x85p0;5508x1p0")]
    [InlineData(1458, "4a120c5cd6ff57022ff52061bd00596de2292151ac32ff88d4b4508b77775e93",
        45, "3d6774dabe66205517aa857937413e8ec1d002afc115b19156cf3caee9e3e592", 2146166448,
        177, 188, "49x1p0;997x1p0;166x11p0;22x7p0;40x49p0;298x2p0;72x70p0")]
    public void Direct_original_wood_place_matches_grid_chest_protection_and_rng(
        int seed, string expectedGrid, uint expectedIndex, string expectedSeeds, int expectedNext,
        int expectedChestX, int expectedChestY, string expectedInventory)
    {
        var world = new Workspace(600, 600);
        Assert.True(world.TrySetLayers(120, 250));
        world.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.CelebrationMk10,
            VanillaSecretWorldSeed1458.None));
        world.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458
        {
            CopperOre = 7, IronOre = 6, SilverOre = 9, GoldOre = 8,
            CopperBar = 20, IronBar = 22, SilverBar = 21, GoldBar = 19,
            HellChestItems = [274, 220, 112, 218, 3019], ExtraBastStatueCountMax = 2,
            TreeX = [], TreeStyle = [], CaveBackX = [], CaveBackStyle = [], ForestBackgroundStyles = []
        });
        GenerationCaveHouseBuilder1458Tests.Populate((x, y, type, active) =>
        {
            var tile = new WorldTile { Type = type,
                Flags = active ? WorldTileFlags.Active : WorldTileFlags.None };
            world.TileStore.Set(x, y, in tile);
        }, geometry: 1);
        var random = new RandomAdapter(seed);
        var state = new ChestPlacementState1458();
        state.EnsureInitialized(new Context(world, random), world);
        CaveHousePlacement1458.PlaceRooms(world, state, random,
            GenerationCaveHouseBuilder1458Tests.Rooms(1), CaveHouseType1458.Wood);
        string actualGrid = Hash17(world);
        WorldChest[] chests = world.CaptureGeneratedChests();
        string actualInventory = chests.Length == 0 ? "none" : string.Join(';',
            chests[0].Items.Where(static item => !item.IsEmpty)
                .Select(static item => $"{item.ItemType}x{item.Stack}p{item.Prefix}"));
        string actualSeeds = random.SeedArrayHash();
        uint actualIndex = random.Index;
        int actualNext = random.Next();
        Assert.Equal([new WorldTileRegion(152, 162, 40, 26), new WorldTileRegion(146, 171, 42, 28)],
            world.VanillaStructureMap.CaptureProtectedStructures());
        bool matches = actualGrid == expectedGrid && actualIndex == expectedIndex &&
            actualSeeds == expectedSeeds && actualNext == expectedNext &&
            chests.Length == 1 && chests[0].X == expectedChestX &&
            chests[0].Y == expectedChestY && actualInventory == expectedInventory;
        Assert.True(matches, $"seed{seed}: grid {actualGrid}/{expectedGrid}, RNG index " +
            $"{actualIndex}/{expectedIndex}, seeds {actualSeeds}/{expectedSeeds}, " +
            $"next {actualNext}/{expectedNext}, chest " +
            $"{(chests.Length == 0 ? "none" : $"{chests[0].X},{chests[0].Y}")}, " +
            $"items {actualInventory}/{expectedInventory}");
    }

    private static readonly WorldTileFlags[] Masks =
    [
        WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen,
        WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive,
        WorldTileFlags.InvisibleBlock, WorldTileFlags.FullbrightBlock,
        WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall
    ];

    private static string Hash17(Workspace world)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> cell = stackalloc byte[17];
        foreach (WorldTile tile in world.TileStore.Tiles)
        {
            cell[0] = tile.IsActive ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16LittleEndian(cell[1..], tile.Type);
            BinaryPrimitives.WriteUInt16LittleEndian(cell[3..], tile.Wall);
            BinaryPrimitives.WriteInt16LittleEndian(cell[5..], tile.FrameX);
            BinaryPrimitives.WriteInt16LittleEndian(cell[7..], tile.FrameY);
            cell[9] = tile.LiquidAmount; cell[10] = (byte)tile.LiquidKind;
            cell[11] = tile.IsHalfBrick ? (byte)1 : (byte)0;
            cell[12] = tile.SourceSlope;
            cell[13] = tile.TileColor; cell[14] = tile.WallColor;
            ushort flags = 0;
            for (int bit = 0; bit < Masks.Length; bit++)
                if ((tile.Flags & Masks[bit]) != 0) flags |= (ushort)(1 << bit);
            BinaryPrimitives.WriteUInt16LittleEndian(cell[15..], flags);
            hash.AppendData(cell);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private sealed class Context(Workspace world, IWorldGenerationVanillaRandom random)
        : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => new(Provider1458.GeneratorId,
            "DirectWoodPlace", 1458, 600, 600);
        public IWorldGenerationWorkspace Workspace => world;
        public IWorldGenerationMetadataWorkspace Metadata => world;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportProgress(double fraction, string? message = null) { }
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        public uint Index => (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", Private)!
            .GetValue(random)!;
        public string SeedArrayHash()
        {
            int[] values = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", Private)!
                .GetValue(random)!;
            byte[] bytes = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        public int Next() => random.Next();
        public int Next(int maximum) => random.Next(maximum);
        public int Next(int minimum, int maximum) => random.Next(minimum, maximum);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
