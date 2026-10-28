using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class QuickCleanupIndependentShape1458Tests
{
    [Fact]
    public void Registered_cleanup_preserves_independent_half_bricks_and_slopes()
    {
        var store = new WorldTileStore(new WorldDimensions(800, 160));
        void Put(int x, int y, ushort type, bool half = false, byte slope = 0, bool active = true)
        {
            var cell = new WorldTile
            {
                Type = type, Flags = (active ? WorldTileFlags.Active : 0) | WorldTileFlags.WireBlue | WorldTileFlags.InvisibleWall,
                FrameX = 36, FrameY = -18, Wall = 2, TileColor = 7, WallColor = 9,
                LiquidAmount = 13, LiquidKind = WorldLiquidKind.Shimmer
            };
            Assert.True(cell.TrySetSourceShape(half, slope));
            store.Set(x, y, cell);
        }

        // Same explicit synthetic inputs as the original registered QuickCleanup
        // differential profile. Terraria1458 halfBrick and slope are independent.
        Put(530, 60, 1, true, 2, active: false);
        Put(534, 60, 3, true, 1);
        Put(538, 60, 225, true, 4);
        Put(542, 60, 1, true, 4); Put(542, 61, 1);
        Put(546, 60, 1, true, 4); Put(546, 59, 1);
        Put(550, 60, 19, true, 3);
        Put(554, 60, 397, true, 3);
        Put(75, 50, 53, true, 4);
        Put(79, 50, 53); Put(79, 51, 1, true, 4, active: false);

        new QuickCleanupPass1458(store, new RandomAdapter(1458), 70, 85, 380,
            TestContext.Current.CancellationToken).Apply();

        // Expected named fields observed from actual registered GenPass.Apply,
        // not a reconstruction of the runtime's old Shape-only predicates.
        (int X, int Y, bool Active, bool Half, byte Slope, ushort Type)[] expected =
        [
            (530, 60, false, false, 0, 1), (534, 60, true, false, 0, 3),
            (538, 60, true, true, 4, 225), (542, 60, true, true, 4, 1),
            (546, 60, false, true, 4, 1), (550, 60, true, true, 3, 19),
            (554, 60, true, true, 3, 397), (75, 50, true, true, 0, 53),
            (79, 51, true, false, 0, 0)
        ];
        var differences = new List<string>();
        foreach (var item in expected)
        {
            WorldTile cell = store.Get(item.X, item.Y);
            if (!cell.HasValidSourceShape || cell.IsActive != item.Active || cell.IsHalfBrick != item.Half ||
                cell.SourceSlope != item.Slope || cell.Type != item.Type)
                differences.Add($"({item.X},{item.Y}): active={cell.IsActive}, half={cell.IsHalfBrick}, slope={cell.SourceSlope}, type={cell.Type}, valid={cell.HasValidSourceShape}");
            Assert.Equal((short)36, cell.FrameX);
            Assert.Equal((short)-18, cell.FrameY);
            Assert.Equal((ushort)2, cell.Wall);
            Assert.Equal((byte)7, cell.TileColor);
            Assert.Equal((byte)9, cell.WallColor);
            Assert.True((cell.Flags & (WorldTileFlags.WireBlue | WorldTileFlags.InvisibleWall)) ==
                (WorldTileFlags.WireBlue | WorldTileFlags.InvisibleWall));
        }
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int maxValue) => random.Next(maxValue);
        public int Next(int minValue, int maxValue) => random.Next(minValue, maxValue);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
