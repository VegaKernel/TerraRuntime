using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class CaveHouseFurniture1458Tests
{
    [Theory]
    [InlineData((ushort)377, "5B708BC7FD6317BA6DD0D2F71337A813704B941F483F766371FEE1014C69E726")]
    [InlineData((ushort)506, "4428F96BE85702937ED1DE00610739D82DB84B6195A92E71D30C006CA4E51B96")]
    [InlineData((ushort)219, "DB08063BDC9762BE02849C0EFFDF2F23657C2DB850DA3C77D112556CBBD35850")]
    public void Priority_tool_placement_matches_original_support_refusal_and_rng(ushort type, string expected)
    {
        // Original metadata explicitly includes tileFrameImportant[19] = true;
        // suppressing platform framing changes both support and final frames.
        var results = new StringBuilder();
        for (int scenario = 0; scenario < 7; scenario++)
        {
            var workspace = new Workspace(80, 80);
            int left = type == 506 ? 40 : 39, columns = type == 506 ? 2 : 3;
            for (int x = left; x < left + columns; x++)
            {
                var floor = new WorldTile { Type = scenario is 3 or 5 ? (ushort)19 : (ushort)1,
                    Flags = (scenario == 1 ? WorldTileFlags.None : WorldTileFlags.Active) |
                        (scenario == 5 ? WorldTileFlags.Inactive : WorldTileFlags.None) };
                Assert.True(floor.TrySetSourceShape(false, (byte)(scenario == 3 ? 1 : scenario == 4 ? 3 : 0)));
                workspace.TileStore.Set(x, 41, in floor);
            }
            if (scenario is 2 or 6)
            {
                var occupied = new WorldTile { Type = scenario == 2 ? (ushort)1 : (ushort)178,
                    Flags = WorldTileFlags.Active, Shape = scenario == 2 ? (byte)1 : (byte)0 };
                workspace.TileStore.Set(40, 40, in occupied);
            }
            var random = new RandomAdapter(1458);
            CaveHousePlacement1458.PlaceHouseObject(workspace, random, 40, 40, type, forced: true);
            var text = new StringBuilder();
            for (int x = 35; x < 47; x++)
            for (int y = 35; y < 45; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                text.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append(tile.IsHalfBrick ? '1' : '0').Append(',').Append(tile.SourceSlope).Append(';');
            }
            text.Append(random.PhysicalState());
            results.Append(Digest(text)).Append('|');
        }
        Assert.Equal(expected, Digest(results));
    }

    [Theory]
    [InlineData(1, 1002897798, "CD6EC9EEFACC2CCDAAEA5782A60AA9D8F00148AD3A05140AFFC170F3E4E3869A")]
    [InlineData(1458, 1916656655, "EF6185E9B46F4D296E85CE86B6D7EE6C25D5C6FD66FF3D736272B53D7697F55F")]
    public void Shared_three_by_two_refusal_keeps_original_common_tail_reset_rng(int seed, int next, string physicalState)
    {
        var workspace = new Workspace(80, 80);
        var shard = new WorldTile { Type = 178, Flags = WorldTileFlags.Active };
        var floor = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(40, 40, in shard);
        workspace.TileStore.Set(40, 41, in floor);
        var random = new RandomAdapter(seed);
        Assert.False(GenerationDecorationPlacement1458.TryPlaceTile3x2(workspace.TileStore, random, 40, 40, 186, 22));
        Assert.Equal((ushort)178, workspace.TileStore.Get(40, 40).Type);
        Assert.True(workspace.TileStore.Get(40, 40).IsActive);
        Assert.Equal(physicalState, Digest(new StringBuilder(random.PhysicalState())));
        Assert.Equal(next, random.Next());
    }

    [Theory]
    [InlineData(0, "F7FCCC47D316CFF0868BC5788E5516B1C955F74577F0D92465A104950837ED27")]
    [InlineData(1, "3C17B65B1A4AEB86B796A7B2130EFDE170C18E97AB5A7D4F62C7B109C71012CA")]
    [InlineData(2, "FDB09C87951E38B1700961395C78F76598C54E378EED9392644083477E139040")]
    [InlineData(3, "1C99314795CE4345238B0422417D2F91C4706D7F4F4EFB8A408A4966A08DE298")]
    [InlineData(4, "B93B86613B9F30AB1727EDB05984A0D6B0D7D0CE6636B44F7FC36E58731A82BE")]
    [InlineData(5, "05434135804554A489A16592F37C620E978A2BD42EABE312A36A93B1EF6BD478")]
    [InlineData(6, "66AB3113EFDC9D023209A82238057ACF0C491ED43DED504A5B331014013ADF12")]
    public void Seven_biome_room_filling_matches_private_original_tiles_and_full_rng_state(int biome, string expected)
    {
        var results = new StringBuilder();
        foreach (int seed in new[] { 1, 7, 31, 100, 777, 1458, 1701, 2026, 4271, 6301, 10007, 14581, 31415, 57191, 86753, 8675309 })
        for (int scenario = 0; scenario < 3; scenario++)
        {
            var workspace = new Workspace(400, 300);
            Populate((x, y, type, wall, fx, fy, active, half, slope, actuated, wire, color) =>
            {
                var tile = new WorldTile { Type = type, Wall = wall, FrameX = fx, FrameY = fy, TileColor = color,
                    Flags = (active ? WorldTileFlags.Active : WorldTileFlags.None) |
                        (actuated ? WorldTileFlags.Inactive : WorldTileFlags.None) |
                        (wire ? WorldTileFlags.WireRed : WorldTileFlags.None) };
                Assert.True(tile.TrySetSourceShape(half, slope));
                workspace.TileStore.Set(x, y, in tile);
            }, biome, scenario);
            var random = new RandomAdapter(seed);
            // The original probe seeds before constructing its house. These four
            // constructors call PotentiallyConvertToSeedHouse, whose ordinary arm
            // spends one NextFloat even when no special palette is available.
            if (biome < 4)
                random.NextDouble();
            CaveHousePlacement1458.FillRooms(workspace, random, Rooms(), (CaveHouseType1458)biome);
            var text = new StringBuilder();
            for (int x = 40; x < 250; x++)
            for (int y = 40; y < 180; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                text.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.Wall)
                    .Append(',').Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append((tile.Flags & WorldTileFlags.WireRed) != 0 ? '1' : '0').Append(',')
                    .Append(tile.IsHalfBrick ? '1' : '0').Append(',').Append(tile.SourceSlope).Append(',')
                    .Append(tile.IsActuated ? '1' : '0').Append(',').Append(tile.TileColor).Append(';');
            }
            text.Append(random.PhysicalState());
            results.Append(seed).Append('/').Append(scenario).Append(':').Append(Digest(text)).Append('|');
        }
        Assert.Equal(expected, Digest(results));
    }

    internal static CaveHouseRoom1458[] Rooms() => [new(60, 60, 15, 8), new(125, 100, 23, 10), new(200, 145, 30, 12)];

    internal static void Populate(Action<int, int, ushort, ushort, short, short, bool, bool, byte, bool, bool, byte> put,
        int biome, int scenario)
    {
        ushort[] blocks = [30, 321, 396, 158, 190, 369, 357];
        ushort[] walls = [27, 149, 187, 42, 74, 181, 179];
        foreach (CaveHouseRoom1458 room in Rooms())
        {
            for (int x = room.Left; x < room.Right; x++)
            for (int y = room.Top; y < room.Bottom; y++)
            {
                bool shell = x == room.Left || x == room.Right - 1 || y == room.Top || y == room.Bottom - 1;
                bool missing = scenario == 1 && y == room.Top && x > room.Left + 3 && x < room.Right - 3;
                bool occupied = scenario == 2 && y == room.Bottom - 2 && x % 3 == 0;
                bool active = shell && !missing || occupied;
                put(x, y, active ? blocks[biome] : (ushort)0,
                    scenario == 1 && !shell && x % 4 == 0 ? (ushort)0 : walls[biome],
                    (short)(occupied ? 72 : 0), 0, active, occupied, 0, false, false, (byte)(occupied ? 3 : 0));
            }
        }
    }

    private static string Digest(StringBuilder text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        // Test-only observation of the source-pinned physical state; production
        // keeps its existing RNG owner and adds no reflection dependency.
        public string PhysicalState() => typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random) + ":" +
            string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!);
        public int Next() => random.Next();
        public int Next(int maximum) => random.Next(maximum);
        public int Next(int minimum, int maximum) => random.Next(minimum, maximum);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
