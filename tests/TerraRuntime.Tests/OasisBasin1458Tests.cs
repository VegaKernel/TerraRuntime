using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for TerrariaServer 1.4.5.8 <c>WorldGen.PlaceOasis</c>, the decision-and-shaping half
/// of the registered <c>GenPassNameID.Oasis</c> pass.
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified official method on the same synthetic input inside the pinned
/// dedicated server. Each row carries the source's own acceptance answer, its retained
/// <c>GenVars.oasisPosition/oasisWidth</c> entry, the next shared-RNG draw and a hash over every field of every
/// cell in the affected rectangle, so a divergence in shape, liquid, slope or scheduling all surface here.
/// This closes the method on synthetic input; it is not a claim about the real generated prefix.
/// </remarks>
public sealed class OasisBasin1458Tests
{
    private const int Width = 2000;
    private const int Height = 1200;
    private const int SurfaceRow = 250;
    private const int StoneRow = 520;
    private const int CandidateX = 1000;
    private const int CandidateY = 100;

    [Theory]
    // fixture, seed, placed, retainedCount, retained, nextDraw, cellHash
    [InlineData(1, 42, true, 1, "1000,250,55", 122004, "be50302c32a96d9146a5b6382a706b5e466147b51e845b5c61a4c597a03a9104")]
    [InlineData(1, 1458, true, 1, "1000,250,51", 471763, "d75c2171b3c1ebfea0d5557d10dd4dc002349f33bd1c5a8dfec307f7857c60ee")]
    [InlineData(2, 42, false, 0, "none", 668106, "b64832bdb9e565840b25cfee63f9ad720d8dc878ce422fc4a171111c20f9ebeb")]
    [InlineData(2, 1458, false, 0, "none", 422351, "b64832bdb9e565840b25cfee63f9ad720d8dc878ce422fc4a171111c20f9ebeb")]
    [InlineData(3, 42, false, 0, "none", 140907, "2dbfaf57c6846ef8b283175dbe32baba93470aa1d573816fc9bd029f0c109668")]
    [InlineData(3, 1458, false, 0, "none", 621669, "2dbfaf57c6846ef8b283175dbe32baba93470aa1d573816fc9bd029f0c109668")]
    [InlineData(4, 42, false, 0, "none", 140907, "a5d963e318527b8ce4ed33b4186a0ec08f76f7b1ffa192f91f536e055c654cfe")]
    [InlineData(4, 1458, false, 0, "none", 621669, "a5d963e318527b8ce4ed33b4186a0ec08f76f7b1ffa192f91f536e055c654cfe")]
    [InlineData(5, 42, false, 0, "none", 140907, "6ee59e9d12028ec0e8432e2d48606c37aa02e8eb8dabd4febf4207b976fb57cc")]
    [InlineData(5, 1458, false, 0, "none", 621669, "6ee59e9d12028ec0e8432e2d48606c37aa02e8eb8dabd4febf4207b976fb57cc")]
    [InlineData(6, 42, false, 0, "none", 140907, "8def5ad8b0f6db014a99ebf55803ffa482b75253edb2c57d32f72e9e766a9cc7")]
    [InlineData(6, 1458, false, 0, "none", 621669, "8def5ad8b0f6db014a99ebf55803ffa482b75253edb2c57d32f72e9e766a9cc7")]
    [InlineData(7, 42, false, 0, "none", 140907, "ef639537c80f1b92978a1c97222a4ffadba6595fb15d0f44f86030bbb8641498")]
    [InlineData(7, 1458, false, 0, "none", 621669, "ef639537c80f1b92978a1c97222a4ffadba6595fb15d0f44f86030bbb8641498")]
    [InlineData(8, 42, false, 0, "none", 668106, "f1082ff4c92b1c9fc91a36dcd3c0531d209270dbf6631826bc46532a8df052dc")]
    [InlineData(8, 1458, false, 0, "none", 422351, "f1082ff4c92b1c9fc91a36dcd3c0531d209270dbf6631826bc46532a8df052dc")]
    [InlineData(9, 42, true, 1, "1000,254,55", 421881, "e401880e8d8caf32be5c9083bc54a97466d912fa6d62ce338bdac3cba92d2599")]
    [InlineData(9, 1458, true, 1, "1000,254,51", 235109, "272e2235146fffedaba9eb0855c4b8fc3611ad31e11453764c32fc0a6dafcfd3")]
    [InlineData(10, 42, false, 0, "none", 140907, "b8214f98930b03c57d91e9f03bc2ab9e36ff44998bbccc649a23f5b772d51147")]
    [InlineData(10, 1458, false, 0, "none", 621669, "b8214f98930b03c57d91e9f03bc2ab9e36ff44998bbccc649a23f5b772d51147")]
    // Fixtures 11 and 12 pin the source's dead per-column ceiling/floor branch AS dead: each would be refused by
    // the symmetric test that branch looks like it wants to be, and the official method accepts both.
    [InlineData(11, 42, true, 1, "1000,250,55", 122004, "347618ba1ae5216887d37b72d6da4389b7f48d3c5e72ef9b4a9a5f6b8ca17215")]
    [InlineData(11, 1458, true, 1, "1000,250,51", 471763, "ec8624988e81a861874969a3138639af5a2f83b75202bdb4de3e9bd3f00e827c")]
    [InlineData(12, 42, true, 1, "1000,250,55", 122004, "3d4e8411745a4cad2302e1827ab01e37c31bf42b5e564ec1888bbbbe60fdd50f")]
    [InlineData(12, 1458, true, 1, "1000,250,51", 471763, "d9b19a583778a568a3c6084b167e6f7232755c104f00034e10f7dfeb3ebc6c3d")]
    // Fixture 13 pins Tile.lava(false) as a single-bit clear rather than a reset to water: shimmer under the
    // basin floor must come out as honey.
    [InlineData(13, 42, true, 1, "1000,250,55", 122004, "8d8a45bd4f42be8e09d40fcb1f51ad0b4c6e607432574b653c459f59c934f39e")]
    [InlineData(13, 1458, true, 1, "1000,250,51", 471763, "acda23c420bcabe2a54ab0bb856347badefe7ade518a20f7577395aa2a36bd11")]
    public void Placement_matches_official(
        int fixture,
        int seed,
        bool placed,
        int retainedCount,
        string retained,
        int nextDraw,
        string cellHash) =>
        AssertAgainstOfficial(fixture, seed, priorX: 0, priorY: 0, placed, retainedCount, retained, nextDraw, cellHash);

    [Theory]
    // The source rejects against already retained basins closer than 350, measured centre to settled row.
    [InlineData(651, 42, false, 1, "651,250,50", 668106, "de31d37c0dabfe9516e74ab4607271f297657da48945aa0390327c6fa18599c4")]
    [InlineData(651, 1458, false, 1, "651,250,50", 422351, "de31d37c0dabfe9516e74ab4607271f297657da48945aa0390327c6fa18599c4")]
    [InlineData(649, 42, true, 2, "1000,250,55", 122004, "be50302c32a96d9146a5b6382a706b5e466147b51e845b5c61a4c597a03a9104")]
    [InlineData(649, 1458, true, 2, "1000,250,51", 471763, "d75c2171b3c1ebfea0d5557d10dd4dc002349f33bd1c5a8dfec307f7857c60ee")]
    public void Proximity_to_a_retained_basin_matches_official(
        int priorX,
        int seed,
        bool placed,
        int retainedCount,
        string retained,
        int nextDraw,
        string cellHash) =>
        AssertAgainstOfficial(1, seed, priorX, SurfaceRow, placed, retainedCount, retained, nextDraw, cellHash);

    [Fact]
    public void Cancellation_precedes_cell_mutation_and_random_consumption()
    {
        Workspace workspace = CreateWorkspace(1);
        string before = HashRegion(workspace);
        var random = new RandomAdapter(42);
        var basin = new OasisBasin1458(workspace.TileStore, random, 400d, new CancellationToken(true));

        Assert.Throws<OperationCanceledException>(
            () => basin.TryPlace(CandidateX, CandidateY, []));
        Assert.Equal(before, HashRegion(workspace));
        Assert.Equal(new RandomAdapter(42).Next(), random.Next());
    }

    private static void AssertAgainstOfficial(
        int fixture,
        int seed,
        int priorX,
        int priorY,
        bool placed,
        int retainedCount,
        string retained,
        int nextDraw,
        string cellHash)
    {
        Workspace workspace = CreateWorkspace(fixture);
        var random = new RandomAdapter(seed);
        var basin = new OasisBasin1458(
            workspace.TileStore,
            random,
            worldSurface: 400d,
            TestContext.Current.CancellationToken);

        var anchors = new List<VanillaOasisAnchor1458>();
        if (priorX > 0)
            anchors.Add(new VanillaOasisAnchor1458(priorX, priorY, 50));

        VanillaOasisAnchor1458? result = basin.TryPlace(CandidateX, CandidateY, anchors);
        if (result is VanillaOasisAnchor1458 anchor)
            anchors.Add(anchor);

        string actualRetained = anchors.Count == 0
            ? "none"
            : $"{anchors[^1].X},{anchors[^1].Y},{anchors[^1].Width}";

        // One composed comparison so a divergence reports acceptance, retention, the shared RNG position and the
        // cell hash together instead of hiding the later ones behind the first failure.
        string expected = $"{placed}|{retainedCount}|{retained}|{nextDraw}|{cellHash}";
        string actual = $"{result is not null}|{anchors.Count}|{actualRetained}|{random.Next(1000000)}|{HashRegion(workspace)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    private static Workspace CreateWorkspace(int fixture)
    {
        var workspace = new Workspace(Width, Height);
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            workspace.TileStore.Set(x, y, Fixture(fixture, x, y));
        return workspace;
    }

    /// <summary>
    /// Serializes every field of every cell in the affected rectangle in the exact order the official probe
    /// used, so the two hashes are directly comparable. The runtime keeps the vanilla slope and half-brick bits
    /// in one <c>Shape</c> byte, which is unpacked back into the source's two values here.
    /// </summary>
    private static string HashRegion(Workspace workspace)
    {
        var sb = new StringBuilder();
        for (int x = CandidateX - 200; x <= CandidateX + 200; x++)
        for (int y = Math.Max(0, CandidateY - 100); y <= CandidateY + 250; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            int slope = tile.Shape >= 2 ? tile.Shape - 1 : 0;
            bool halfBrick = tile.Shape == 1;
            sb.Append(tile.IsActive ? '1' : '0').Append(',')
              .Append(tile.Type).Append(',')
              .Append(tile.Wall).Append(',')
              .Append(tile.FrameX).Append(',')
              .Append(tile.FrameY).Append(',')
              .Append(tile.LiquidAmount).Append(',')
              .Append((int)tile.LiquidKind).Append(',')
              .Append(slope).Append(',')
              .Append(halfBrick ? '1' : '0').Append(',')
              .Append(tile.TileColor).Append(',')
              .Append(tile.WallColor).Append(';');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTile Fixture(int fixture, int x, int y)
    {
        int surface = fixture == 8 ? 395 : SurfaceRow;
        bool active = y >= surface;
        ushort type = y < StoneRow ? (ushort)53 : (ushort)1;
        ushort wall = 0;
        byte liquid = 0;
        WorldLiquidKind kind = WorldLiquidKind.Water;

        switch (fixture)
        {
            case 1:
            case 8:
                break;
            case 2:
                // The probe start cell is solid, so the source refuses before any scan.
                if (x == CandidateX && y == CandidateY) active = true;
                break;
            case 3:
                // Stone inside the outer scan box but outside the inner one.
                if (x == CandidateX + 100 && y == surface + 2) type = 1;
                break;
            case 4:
                // Sandstone inside the inner box.
                if (x == CandidateX + 10 && y == surface + 2) type = 397;
                break;
            case 5:
                // Sandstone brick inside the inner box.
                if (x == CandidateX + 10 && y == surface + 2) type = 151;
                break;
            case 6:
                // Water in an open cell inside the inner box.
                if (x == CandidateX + 10 && y == surface - 4) liquid = 120;
                break;
            case 7:
                // A wall behind an open cell inside the inner box.
                if (x == CandidateX + 10 && y == surface - 4) wall = 216;
                break;
            case 9:
                // An air pocket below the surface under one flank of the basin.
                if (x >= CandidateX - 60 && x <= CandidateX - 50 && y >= surface + 3 && y <= surface + 8) active = false;
                break;
            case 10:
                // Sandstone outside the inner box but inside the outer one must still reject: the source only
                // exempts non-sand solids from the inner-box test, never from the type-53 test.
                if (x == CandidateX + 100 && y == surface + 2) type = 397;
                break;
            case 11:
                // Sand hanging six rows above the surface, offset from the candidate column so the initial
                // descent still lands on the real surface.
                if (x >= CandidateX + 5 && x <= CandidateX + 15 && y == surface - 6) active = true;
                break;
            case 12:
                // A one-row void directly under the surface in the same offset columns.
                if (x >= CandidateX + 5 && x <= CandidateX + 15 && y == surface + 1) active = false;
                break;
            case 13:
                // Active sand already holding shimmer inside the basin. Liquid on an active cell is not part of
                // the source's acceptance scan, so the site is accepted and the floor is rewritten.
                if (x >= CandidateX - 10 && x <= CandidateX + 10 && y >= surface + 2 && y <= surface + 6)
                {
                    liquid = 200;
                    kind = WorldLiquidKind.Shimmer;
                }

                break;
        }

        return new WorldTile
        {
            Type = type,
            Wall = wall,
            LiquidAmount = liquid,
            LiquidKind = kind,
            FrameX = active ? (short)0 : (short)-1,
            FrameY = active ? (short)0 : (short)-1,
            Flags = active ? WorldTileFlags.Active : WorldTileFlags.None
        };
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
