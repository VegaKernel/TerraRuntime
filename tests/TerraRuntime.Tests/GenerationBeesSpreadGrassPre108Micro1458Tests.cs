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

public sealed class GenerationBeesSpreadGrassPre108Micro1458Tests
{
    // Direct TerrariaServer.exe 1.4.5.8 WorldGen.SpreadGrass(50,50,59,60)
    // on an 11x11 rehost of genuine NotTheBees direct-first terrain around
    // source (25,466). It is a bounded natural-shape micro, not the exact
    // in-pass pre-FinishNotTheBees checkpoint or its shared RNG phase.
    [Fact]
    public void First_exposed_pre108_mud_offer_matches_official_full_grid_and_rng()
    {
        Workspace world = Create(out RuntimeProjectileStore projectiles);
        const string pre = "112ce97ff087ea6bf482f183cd07717b90e3c66d4ced4f894447496e69546852";
        const string post = "b85cb6341d613826d9ffd4f5f8d2da92a1248abc91c40d7af7f673e38a524580";
        Assert.Equal(pre, HashSeventeen(world));
        var random = new RandomAdapter(1458);
        var framing = new GenerationTileFraming1458(world.TileStore, random, world,
            useWorldGenerationSolidity: true);
        var spread = new GenerationGrass1458(world.TileStore, random,
            TestContext.Current.CancellationToken, worldSurface: 40,
            frameSquare: (x, y) => framing.SquareTileFrame(x, y), workspace: world);
        spread.Apply(50, 50, 59, 60);
        Assert.Equal(9, spread.Converted);
        Assert.Equal((ushort)60, world.TileStore.Get(50, 50).Type);
        Assert.Equal(post, HashSeventeen(world));
        Assert.Equal(0u, random.Index);
        Assert.Equal("c0aba3351d35e3a74291e7c4e60370ebeae7daa2d7832a2272844e32123ed23f",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',',
                random.SeedArray.Select(static value => value.ToString(CultureInfo.InvariantCulture)))))));
        Assert.Equal(906992634, random.Next());
        Assert.Equal(0, projectiles.ActiveCount);
    }

    [Fact]
    public void Omitting_exposed_mud_offer_fails_official_grid()
    {
        Workspace world = Create(out _);
        Assert.NotEqual("b85cb6341d613826d9ffd4f5f8d2da92a1248abc91c40d7af7f673e38a524580",
            HashSeventeen(world));
    }

    private static Workspace Create(out RuntimeProjectileStore projectiles)
    {
        var world = new Workspace(100, 100);
        world.InitializeVanillaGenerationSolidity();
        foreach (ushort id in new ushort[] { 481, 482, 483, 484, 659 })
            world.SetVanillaGenerationSolid(new(id), false);
        world.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.NotTheBees,
            VanillaSecretWorldSeed1458.None));
        projectiles = new RuntimeProjectileStore();
        world.AttachEntityEffects(new WorldGenerationEntityEffects1458(
            new RuntimeNpcStore(), projectiles, sourceServer: true), sourceMyPlayer: 0);
        WorldTile stone = new() { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < 100; x++)
            for (int y = 0; y < 100; y++)
                world.TileStore.Set(x, y, in stone);
        string[] rows =
        [
            "AABBBBBBAAA", "AAAABBBBAAA", "AAAABBBBBAA", "AAAAABBBBED",
            "AAAAAABBBDF", "AAAAABHCCCC", "AAAAABCCCCC", "AAAAABCCDCC",
            "AAAGDCCCDDC", "AEDDCCCDDDD", "DDDDCCCDDDD"
        ];
        for (int ry = 0; ry < rows.Length; ry++)
            for (int rx = 0; rx < rows[ry].Length; rx++)
            {
                char code = rows[ry][rx];
                WorldTile tile = new()
                {
                    Type = code switch
                    {
                        'A' or 'E' => 225,
                        'B' or 'C' or 'H' => 59,
                        'D' => 1,
                        'F' => 0,
                        'G' => 165,
                        _ => throw new InvalidOperationException("Unknown source tile code.")
                    },
                    Flags = code is 'A' or 'B' or 'G' or 'H'
                        ? WorldTileFlags.Active : WorldTileFlags.None,
                    FrameX = code == 'G' ? (short)198 : (short)-1,
                    FrameY = code == 'G' ? (short)72 : (short)-1
                };
                Assert.True(tile.TrySetSourceShape(false, code == 'H' ? (byte)3 : (byte)0));
                world.TileStore.Set(45 + rx, 45 + ry, in tile);
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
