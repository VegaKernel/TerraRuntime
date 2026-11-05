using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds.Generation;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationCorruptFlowerPlacement1458Tests
{
    // Direct TerrariaServer.exe 1.4.5.8 WorldGen.PlaceTile(50,50,24)
    // under genuine pre108 generation flags and 754-entry tileSolid catalog.
    // These are independent 100x100 official microfixtures, not a full Bees tail.
    [Fact]
    public void Dry_wall_zero_uses_source_flower_style_and_rng() => Check(
        wall: 0, liquid: 0, seed: 1458, expectedPlaced: true,
        pre: "0da6148f1944593914e20fa51f568806e9c1aa2836d80212edf12d6e4817688b",
        post: "1a8c05565b9b355d7c95288338db8019855ae0f7aed40f7aef1c1a13e7ff1a30",
        index: 5, rngArrayHash: "d08271d17dda06ff97973c57746f40b33c4be7b1a473f8fba68163e45299f97b",
        auditNext: 1507290721, type: 24, frameX: 36);

    [Fact]
    public void Dry_wall_207_reaches_source_thorn_branch_and_double_frame() => Check(
        wall: 207, liquid: 0, seed: 14, expectedPlaced: true,
        pre: "a1e65971eda45bfb71678cf1817397cd5ec42a2d947be5d1c2adbfb98e684a16",
        post: "c82e423e931dee0fc30d544b7e29b6de219ebde6a04cb9b68641838d3ed117dc",
        index: 1, rngArrayHash: "3a9875e93d7fc3f6dd203266cf773787454fedc53f63208a219cd9d7b05307bb",
        auditNext: 1986821818, type: 32, frameX: 0);

    [Fact]
    public void Liquid_refusal_preserves_source_preimage_and_rng() => Check(
        wall: 0, liquid: 255, seed: 1458, expectedPlaced: false,
        pre: "c4fcbb626d1dd875a3e4e90140b5779b27fadcb88f22da915aebceb0b0ad97e9",
        post: "c4fcbb626d1dd875a3e4e90140b5779b27fadcb88f22da915aebceb0b0ad97e9",
        index: 0, rngArrayHash: "c0aba3351d35e3a74291e7c4e60370ebeae7daa2d7832a2272844e32123ed23f",
        auditNext: 906992634, type: 61, frameX: -1);

    [Fact]
    public void Dry_wall_207_on_wall_207_support_clears_rejected_anchor_after_one_draw() => Check(
        wall: 207, liquid: 0, seed: 1458, expectedPlaced: false,
        pre: "bd063cbe99123c5b16d7db9cb90962c3c9a1a8972e0e4970e929f908ace4c218",
        post: "994e4b495a4ff6cff1e9b157e4c11d0dc0ff286970c58d33de97681f2bc4c9e3",
        index: 1, rngArrayHash: "25ba06ecb6f1dc0d13027d46c8c891e4370b0cfacafb88ed893393ee4eb0474d",
        auditNext: 1335025742, type: 0, frameX: 0, supportWall: 207);

    [Fact]
    public void Honey_wall_15_refusal_preserves_former_plant_and_rng() => Check(
        wall: 15, liquid: 255, seed: 1458, expectedPlaced: false,
        pre: "cdcd8d1a0914f27da3661f8814ff8f0e655bb94b3347e9d8133d5f80dcb485f9",
        post: "cdcd8d1a0914f27da3661f8814ff8f0e655bb94b3347e9d8133d5f80dcb485f9",
        index: 0, rngArrayHash: "c0aba3351d35e3a74291e7c4e60370ebeae7daa2d7832a2272844e32123ed23f",
        auditNext: 906992634, type: 61, frameX: -1,
        liquidKind: WorldLiquidKind.Honey);

    private static void Check(ushort wall, byte liquid, int seed, bool expectedPlaced,
        string pre, string post, uint index, string rngArrayHash, int auditNext,
        ushort type, short frameX, ushort supportWall = 0,
        WorldLiquidKind liquidKind = WorldLiquidKind.Water)
    {
        Workspace world = Create(wall, liquid, supportWall, liquidKind);
        Assert.Equal(pre, HashSeventeenFields(world));
        var random = new RandomAdapter(seed);
        var framing = new GenerationTileFraming1458(world.TileStore, random, world,
            useWorldGenerationSolidity: true);
        bool placed = framing.TryPlaceCorruptFlower24(50, 50);
        Assert.Equal(expectedPlaced, placed);
        Assert.Equal(post, HashSeventeenFields(world));
        WorldTile anchor = world.TileStore.Get(50, 50);
        Assert.Equal(type, anchor.Type);
        Assert.Equal(frameX, anchor.FrameX);
        Assert.Equal(expectedPlaced, anchor.IsActive);
        Assert.Equal(index, random.Index);
        Assert.Equal(rngArrayHash, HashSeedArray(random.SeedArray));
        Assert.Equal(auditNext, random.Next());
    }

    [Fact]
    public void Omission_of_dry_flower_is_detected_by_official_grid_and_rng()
    {
        Workspace world = Create(wall: 0, liquid: 0);
        var random = new RandomAdapter(1458);
        Assert.NotEqual("1a8c05565b9b355d7c95288338db8019855ae0f7aed40f7aef1c1a13e7ff1a30",
            HashSeventeenFields(world));
        Assert.NotEqual(1507290721, random.Next());
    }

    private static Workspace Create(ushort wall, byte liquid, ushort supportWall = 0,
        WorldLiquidKind liquidKind = WorldLiquidKind.Water)
    {
        var world = new Workspace(100, 100);
        world.InitializeVanillaGenerationSolidity();
        foreach (ushort identity in new ushort[] { 481, 482, 483, 484, 659 })
            world.SetVanillaGenerationSolid(new(identity), false);
        world.AttachEntityEffects(new WorldGenerationEntityEffects1458(
            new RuntimeNpcStore(), new RuntimeProjectileStore(), sourceServer: true), sourceMyPlayer: 0);
        for (int x = 0; x < 100; x++)
            for (int y = 0; y < 100; y++)
            {
                WorldTile stone = new() { Type = 1, Flags = WorldTileFlags.Active };
                world.TileStore.Set(x, y, in stone);
            }
        WorldTile flower = new()
        {
            Type = 61, Wall = wall, FrameX = -1, FrameY = -1,
            LiquidAmount = liquid, LiquidKind = liquidKind
        };
        world.TileStore.Set(50, 50, in flower);
        WorldTile support = new() { Type = 661, Wall = supportWall, Flags = WorldTileFlags.Active };
        world.TileStore.Set(50, 51, in support);
        return world;
    }

    private static string HashSeventeenFields(Workspace world)
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
            cell[9] = tile.LiquidAmount;
            cell[10] = (byte)tile.LiquidKind;
            cell[11] = tile.IsHalfBrick ? (byte)1 : (byte)0;
            cell[12] = tile.SourceSlope;
            cell[13] = tile.TileColor;
            cell[14] = tile.WallColor;
            ushort flags = 0;
            for (int bit = 0; bit < Masks.Length; bit++)
                if ((tile.Flags & Masks[bit]) != 0) flags |= (ushort)(1 << bit);
            BinaryPrimitives.WriteUInt16LittleEndian(cell[15..], flags);
            hash.AppendData(cell);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string HashSeedArray(int[] values) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',',
            values.Select(static value => value.ToString(CultureInfo.InvariantCulture))))));

    private static readonly WorldTileFlags[] Masks =
    [
        WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen,
        WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive,
        WorldTileFlags.InvisibleBlock, WorldTileFlags.FullbrightBlock,
        WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall
    ];

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        public uint Index => (uint)typeof(VanillaUnifiedRandom1458)
            .GetField("inext", Private)!.GetValue(random)!;
        public int[] SeedArray => (int[])typeof(VanillaUnifiedRandom1458)
            .GetField("seedArray", Private)!.GetValue(random)!;
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
