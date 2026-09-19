using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the registered TerrariaServer 1.4.5.8 <c>GenPassNameID.Flowers</c> and
/// <c>GenPassNameID.Mushrooms</c> passes.
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified registered delegates through <c>GenPass.Apply</c> on the same
/// synthetic ground inside the pinned dedicated server. The substrate is varied one fixture at a time because
/// Flowers converts stone, sandstone and ore into grass before it plants, so each of those is its own path. The
/// pre-planted fixture gives Mushrooms something to restamp without depending on Flowers, and the final fixture
/// runs both in order, which is how a real world reaches them.
///
/// The probe clears <c>GenVars.logX</c> between fixtures. The source relocates the Flowers pass's first patch
/// onto the fallen log when that field is set, and it survives between runs, so the very first case captured
/// without the reset carried a relocated patch that no later case did. The runtime does not model a fallen log
/// here, so an unreset probe would have pinned an expectation the port could never meet.
/// </remarks>
public sealed class FlowerAndMushroomPatchPass1458Tests
{
    private const int Width = 1600;
    private const int Height = 500;
    private const double WorldSurface = 400.0;
    private const double RockLayer = 300.0;
    private const int GroundRow = 150;

    [Theory]
    // fixture, seed, nextDraw, worldHash
    [InlineData("flowers-grass", 42, 44392, "d0e92fb2ae69431f5959fe45a497df35b297355ad89092dd0e3e1e49ee994976")]
    [InlineData("flowers-grass", 1458, 899039, "e71b582dff1b1502ea83c3c200af5c6ee18bdc761335b116ae0f5a4f9f85fb00")]
    [InlineData("flowers-stone", 42, 44392, "c9b309ba6e6f43785de4add824db26bee37cc5de47a4d5c114575e06f8d97a34")]
    [InlineData("flowers-stone", 1458, 899039, "76c72f40874e791de4ef8e609317b632e46d055811130ccf73559855136684de")]
    [InlineData("flowers-sandstone", 42, 44392, "6ced54c550c50df6f7b5058290a6b4b402b609809af1e2a75b0fbbe9daf05030")]
    [InlineData("flowers-sandstone", 1458, 899039, "0d0914cd667f4d08b357eb83bf78da1fb75547933fc629390de36ca2a855740b")]
    [InlineData("flowers-ore", 42, 44392, "9c9e460c35145c7822ed887e4d00fa19551d58eb64bbd7d4061717f4e5e46987")]
    [InlineData("flowers-ore", 1458, 899039, "9e50904b24f6a99b4d60c1f4bfd95ff24ed153ebf98b87f143ace4c9157c7ac7")]
    [InlineData("flowers-preplanted", 42, 381465, "804eaf3b2c4fc91d8e44b2e21c6e7a16613bd3574785cda45f1c95dfb31dfbf5")]
    [InlineData("flowers-preplanted", 1458, 121771, "f536e5f7394636b6e27f19025ff415ecf19d2bddaa20b06806ffc25fcb5dd482")]
    [InlineData("mushrooms-preplanted", 42, 761250, "b5388bdc9a305b65601403f116a8a7e93746a5939565714a12ad017d4553f527")]
    [InlineData("mushrooms-preplanted", 1458, 912804, "9417b03f4b1c7ccce50f31e74b5599b055881f7ec488b20cb9dcec73a88eaa0e")]
    [InlineData("flowers-then-mushrooms", 42, 77517, "d0e92fb2ae69431f5959fe45a497df35b297355ad89092dd0e3e1e49ee994976")]
    [InlineData("flowers-then-mushrooms", 1458, 272301, "e71b582dff1b1502ea83c3c200af5c6ee18bdc761335b116ae0f5a4f9f85fb00")]
    // Alternating Plants and Flowers: Mushrooms must restamp the first and leave the second alone.
    [InlineData("mushrooms-preflowered", 42, 761250, "d9bd1ec87f14edbb22d49764550470c86b63532596182238ac971f3dab6848ba")]
    [InlineData("mushrooms-preflowered", 1458, 912804, "babe5fca694c21f6b33ee02bcd1edbb88a8764bff3a84a10984a37c43729b120")]
    // JunglePlantsPart2 shares this fixture shape: a jungle-grass band, and a crowded variant whose existing
    // plants the pass must treat as clearable rather than blocking.
    [InlineData("jungleplants2-grass", 42, 300560, "59b8721bc4c45d586a5c87c6b0ac450128e313ce4637cdda2388d0c81bcc4bc6")]
    [InlineData("jungleplants2-grass", 1458, 795682, "8708fd47e7765ca89db32cd8820ad34b1d0710d7e72b04b1be56fad52e976f94")]
    [InlineData("jungleplants2-crowded", 42, 653585, "cc20daa8eaf5ca635a8ab82d9cfc77d60b17eb4e7fc6a062f1fe79a9470b1318")]
    [InlineData("jungleplants2-crowded", 1458, 589153, "c4bd172fefb397955384ae561f904f2d5f8cc243e39757ef4756c79eeabdc66c")]
    // A flower patch reaching a multi-cell plant. The right half of the world admits any active cell, so the
    // patch kills one cell of a plant detritus and the framing takes the rest of the object with it; the same
    // plant on the left half is refused and survives untouched.
    [InlineData("flowers-detritus", 42, 752586, "e94f026a7fadd67e43c60d3040db316efc1ce3c385ca1394d2459b68b7ab9049")]
    [InlineData("flowers-detritus", 1458, 379064, "e08ceefd70996126cdcd1d95d5e331c8da3a0df37f1e029e88a9ba6eeed5637c")]
    // Corrupt plants standing on ordinary grass. Every placement frames the square around it, and the framing
    // retypes a plant whose support does not match its identity rather than destroying it.
    [InlineData("flowers-mismatched", 42, 40471, "eb9246b08be509824b44b4929756b14bf7b721017558ff114d9be0009d0c034b")]
    [InlineData("flowers-mismatched", 1458, 121771, "fb5a0c1521dda89d85975e00e0ac13189d6c34967487c4d8a66e4911a6593104")]
    // The same plants, confined to the half of the world the patch pass refuses to plant over, so the only
    // thing that can convert them is the framing.
    [InlineData("flowers-mismatched-left", 42, 841207, "9a0d716c42b5439a791f5d7858291c75089658f1fb7e8b44e8b15bf8a9d55370")]
    [InlineData("flowers-mismatched-left", 1458, 548400, "17e40899c6fb5167a0be6e108db8b08a8b2dcde1e60dde8ebdebac50b97401ce")]
    public void Passes_match_official(string fixture, int seed, int nextDraw, string worldHash)
    {
        (ushort substrate, bool prePlanted, int mode) = Fixture(fixture);
        WorldTileStore store = CreateStore(
            substrate,
            prePlanted,
            fixture == "mushrooms-preflowered",
            fixture == "flowers-detritus",
            fixture.StartsWith("flowers-mismatched", StringComparison.Ordinal) ? (ushort)24 : (ushort)0,
            fixture == "flowers-mismatched-left");
        var random = new RandomAdapter(seed);

        var pass = new FlowerAndMushroomPatchPass1458(
            store, random, WorldSurface, TestContext.Current.CancellationToken);

        switch (mode)
        {
            case 1:
                pass.ApplyMushrooms();
                break;
            case 2:
                pass.ApplyFlowers();
                pass.ApplyMushrooms();
                break;
            case 3:
                new JunglePlantPart2Pass1458(
                        store, random, dungeonOnLeft: false, TestContext.Current.CancellationToken)
                    .Apply();
                break;
            default:
                pass.ApplyFlowers();
                break;
        }

        string expected = $"{nextDraw}|{worldHash}";
        string actual = $"{random.Next(1000000)}|{Hash(store)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual} histogram={Histogram(store)}");
    }

    private static (ushort Substrate, bool PrePlanted, int Mode) Fixture(string name) => name switch
    {
        "flowers-grass" => (2, false, 0),
        "flowers-stone" => (1, false, 0),
        "flowers-sandstone" => (40, false, 0),
        "flowers-ore" => (7, false, 0),
        "flowers-preplanted" => (2, true, 0),
        "mushrooms-preplanted" => (2, true, 1),
        "flowers-then-mushrooms" => (2, false, 2),
        "mushrooms-preflowered" => (2, true, 1),
        "jungleplants2-grass" => (60, false, 3),
        "jungleplants2-crowded" => (60, true, 3),
        "flowers-detritus" => (2, false, 0),
        "flowers-mismatched" => (2, true, 0),
        "flowers-mismatched-left" => (2, true, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(
        ushort substrate,
        bool prePlanted,
        bool flowered = false,
        bool detritus = false,
        ushort prePlantType = 0,
        bool leftHalfOnly = false)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            bool active = y >= GroundRow;
            ushort type = 0;
            if (active)
                type = y == GroundRow ? substrate : (ushort)0;
            if (active && y > GroundRow + 40)
                type = 1;

            store.Set(x, y, new WorldTile
            {
                Type = type,
                FrameX = active ? (short)0 : (short)-1,
                FrameY = active ? (short)0 : (short)-1,
                Flags = active ? WorldTileFlags.Active : WorldTileFlags.None
            });
        }

        if (detritus)
        {
            for (int x = 200; x <= 1400; x += 6)
            {
                int style = x / 6 % 8;
                for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 2; dy++)
                {
                    store.Set(x + dx, GroundRow - 2 + dy, new WorldTile
                    {
                        Type = 233,
                        FrameX = (short)(style * 54 + dx * 18),
                        FrameY = (short)(dy * 18),
                        Flags = WorldTileFlags.Active
                    });
                }
            }

            return store;
        }

        if (!prePlanted)
            return store;

        int last = leftHalfOnly ? Width / 2 : Width;
        for (int x = 0; x < last; x += 3)
        {
            store.Set(x, GroundRow - 1, new WorldTile
            {
                Type = prePlantType != 0
                    ? prePlantType
                    : (ushort)(flowered && (x / 3) % 2 == 1 ? 73 : 3),
                FrameX = 0,
                FrameY = 0,
                Flags = WorldTileFlags.Active
            });
        }

        return store;
    }

    private static string Histogram(WorldTileStore store)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (!tile.IsActive)
                continue;
            if (tile.Type is not (3 or 73 or 24 or 201 or 2 or 233))
                continue;

            string key = $"{tile.Type}:{tile.FrameX}";
            counts.TryGetValue(key, out int n);
            counts[key] = n + 1;
        }

        var sb = new StringBuilder();
        foreach (KeyValuePair<string, int> entry in counts)
        {
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(entry.Key).Append('=').Append(entry.Value);
        }

        return sb.Length == 0 ? "none" : sb.ToString();
    }

    private static string Hash(WorldTileStore store)
    {
        var sb = new StringBuilder();
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile tile = store.Get(x, y);
            int slope = tile.Shape >= 2 ? tile.Shape - 1 : 0;
            sb.Append(tile.IsActive ? '1' : '0').Append(',')
              .Append(tile.Type).Append(',')
              .Append(tile.Wall).Append(',')
              .Append(tile.FrameX).Append(',')
              .Append(tile.FrameY).Append(',')
              .Append(tile.LiquidAmount).Append(',')
              .Append(slope).Append(',')
              .Append(tile.Shape == 1 ? '1' : '0').Append(';');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
