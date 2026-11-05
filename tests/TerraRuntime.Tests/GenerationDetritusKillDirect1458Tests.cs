using System.Buffers.Binary;
using System.Globalization;
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

public sealed class GenerationDetritusKillDirect1458Tests
{
    // Independent direct TerrariaServer.exe 1.4.5.8 WorldGen.KillTile(50,50)
    // with source generation flags and the genuine pre108 tileSolid catalog.
    // The footprint is the source-reachable 2x2 detritus233 family in Bees'
    // type25 neighbourhood; this does not qualify FinishNotTheBees scan order.
    [Fact]
    public void Dry_detritus_kill_matches_official_full_grid_rng_and_no_projectile() => Check(
        wet: false,
        pre: "1b6ba205b68e5383ae642a0b72aa1f6c75df2034ec1ebba7d824ddd240cd6d17",
        post: "d6eb0b2308cc2bbfe7b5ebd42504999c39c1e5e6c6c830aa405b2a96763db111");

    [Fact]
    public void Wet_detritus_kill_matches_official_full_grid_rng_and_no_projectile() => Check(
        wet: true,
        pre: "cb118024ca098497fbf6bb9a00fa4dbec29b27bca6bcf0951ce9111dcb7c0a37",
        post: "cdc72bcd7862226024668dad48969fc184b6fd93db2f9cecd91dcfcb60b6a528");

    [Fact]
    public void Omitting_detritus_kill_fails_both_official_post_grids()
    {
        foreach (bool wet in new[] { false, true })
        {
            Workspace world = Create(wet, out RuntimeProjectileStore projectiles);
            Assert.NotEqual(wet
                ? "cdc72bcd7862226024668dad48969fc184b6fd93db2f9cecd91dcfcb60b6a528"
                : "d6eb0b2308cc2bbfe7b5ebd42504999c39c1e5e6c6c830aa405b2a96763db111",
                HashSeventeen(world));
            Assert.Equal(0, projectiles.ActiveCount);
        }
    }

    private static void Check(bool wet, string pre, string post)
    {
        Workspace world = Create(wet, out RuntimeProjectileStore projectiles);
        Assert.Equal(pre, HashSeventeen(world));
        var random = new RandomAdapter(1458);
        var framing = new GenerationTileFraming1458(world.TileStore, random, world,
            useWorldGenerationSolidity: true);
        framing.KillTile(50, 50);
        Assert.Equal(post, HashSeventeen(world));
        Assert.Equal(0u, random.Index);
        Assert.Equal("c0aba3351d35e3a74291e7c4e60370ebeae7daa2d7832a2272844e32123ed23f",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',',
                random.SeedArray.Select(static value => value.ToString(CultureInfo.InvariantCulture)))))));
        Assert.Equal(906992634, random.Next());
        Assert.Equal(0, projectiles.ActiveCount);
    }

    private static Workspace Create(bool wet, out RuntimeProjectileStore projectiles)
    {
        var world = new Workspace(100, 100);
        world.InitializeVanillaGenerationSolidity();
        foreach (ushort identity in new ushort[] { 481, 482, 483, 484, 659 })
            world.SetVanillaGenerationSolid(new(identity), false);
        world.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.NotTheBees,
            VanillaSecretWorldSeed1458.None));
        projectiles = new RuntimeProjectileStore();
        world.AttachEntityEffects(new WorldGenerationEntityEffects1458(
            new RuntimeNpcStore(), projectiles, sourceServer: true), sourceMyPlayer: 0);
        for (int x = 0; x < 100; x++)
            for (int y = 0; y < 100; y++)
            {
                WorldTile stone = new() { Type = 1, Flags = WorldTileFlags.Active };
                world.TileStore.Set(x, y, in stone);
            }
        for (int dx = 0; dx < 2; dx++)
        {
            WorldTile support = new() { Type = 60, Wall = 15, Flags = WorldTileFlags.Active };
            world.TileStore.Set(50 + dx, 52, in support);
            for (int dy = 0; dy < 2; dy++)
            {
                WorldTile member = new()
                {
                    Type = 233, Wall = 15, Flags = WorldTileFlags.Active,
                    FrameX = (short)(288 + dx * 18),
                    FrameY = (short)(36 + dy * 18),
                    LiquidAmount = wet && dx == 1 && dy == 1 ? byte.MaxValue : (byte)0,
                    LiquidKind = wet && dx == 1 && dy == 1
                        ? WorldLiquidKind.Honey : WorldLiquidKind.Water
                };
                world.TileStore.Set(50 + dx, 50 + dy, in member);
            }
        }
        return world;
    }

    private static string HashSeventeen(Workspace world)
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
        public uint Index => (uint)typeof(VanillaUnifiedRandom1458)
            .GetField("inext", System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)!.GetValue(random)!;
        public int[] SeedArray => (int[])typeof(VanillaUnifiedRandom1458)
            .GetField("seedArray", System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance)!.GetValue(random)!;
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
