using TerraRuntime.World;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class CaveHousePriorProtection1458Tests
{
    [Fact]
    public void Cave_house_offers_respect_prior_shimmer_protection_before_any_structure_write()
    {
        // Official HouseUtils.AreRoomsValid -> StructureMap.CanPlace(room, blacklist, 5).
        // Direct 1.4.5.8 calls prove strict rectangle overlap, while AddStructure
        // (used by Marble/Granite) does not veto placement like AddProtectedStructure.
        var baseline = Create();
        baseline.Workspace.SetVanillaMarbleRegions([new(0, 0, 600, 500)]);
        baseline.Workspace.SetVanillaGraniteRegions([new(0, 0, 600, 500)]);
        Assert.True(CaveHousePlacement1458.TryPlace(baseline.Workspace, baseline.State, baseline.Random,
            300, 190, out var accepted));
        Assert.Equal(1, baseline.Workspace.GeneratedChestCount);
        int right = accepted.Rooms.Max(room => room.Right);
        int top = accepted.Rooms.Min(room => room.Top);
        int left = accepted.Rooms.Min(room => room.Left);

        // Exactly touching the house's five-cell padding remains admissible.
        var touching = Create();
        touching.Workspace.SetVanillaShimmerStructure(new(right + 5, top - 40, 200, 200));
        Assert.True(CaveHousePlacement1458.TryPlace(touching.Workspace, touching.State, touching.Random,
            300, 190, out var touchingResult));
        Assert.Equal(accepted.Rooms, touchingResult.Rooms);
        Assert.Equal(baseline.Workspace.TileStore.Tiles.ToArray(), touching.Workspace.TileStore.Tiles.ToArray());
        Assert.Equal(baseline.Random.Next(), touching.Random.Next());

        foreach (var protectedArea in new WorldTileRegion[]
        {
            new(right + 4, top - 40, 200, 200), // Intersects padding only, not the actual room.
            new(left - 40, top - 40, 200, 200) // Covers the rooms themselves.
        })
        {
            var blocked = Create();
            blocked.Workspace.SetVanillaShimmerStructure(protectedArea);
            WorldTile[] before = blocked.Workspace.TileStore.Tiles.ToArray();
            Assert.False(CaveHousePlacement1458.TryPlace(blocked.Workspace, blocked.State, blocked.Random,
                300, 190, out _));
            Assert.Equal(before, blocked.Workspace.TileStore.Tiles.ToArray());
            Assert.Equal(0, blocked.Workspace.GeneratedChestCount);
            Assert.Empty(blocked.State.ProtectedHouseRooms);
            Assert.Equal(2, blocked.Random.Calls); // Only CreateRooms' two ordinary selection samples.
            var expected = new CountingRandom(1458);
            expected.NextDouble(); expected.NextDouble();
            Assert.Equal(expected.Next(), blocked.Random.Next());
        }
    }

    private static (Workspace Workspace, ChestPlacementState1458 State, CountingRandom Random) Create()
    {
        var workspace = new Workspace(600, 500);
        Assert.True(workspace.TrySetLayers(140d, 200d));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new CountingRandom(1), 4200, false, false));
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 100; x <= 500; x++) workspace.TileStore.Set(x, 200, in stone);
        var random = new CountingRandom(1458);
        var state = new ChestPlacementState1458();
        state.EnsureInitialized(new Context(new WorldGenerationRequest(Provider1458.GeneratorId,
            "CaveHouseProtection", 1458, 600, 500), workspace, random), workspace);
        return (workspace, state, random);
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

    private sealed class CountingRandom(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Calls { get; private set; }
        public int Next() { Calls++; return random.Next(); }
        public int Next(int maxValue) { Calls++; return random.Next(maxValue); }
        public int Next(int minValue, int maxValue) { Calls++; return random.Next(minValue, maxValue); }
        public double NextDouble() { Calls++; return random.NextDouble(); }
        public void NextBytes(byte[] buffer) { Calls++; random.NextBytes(buffer); }
    }
}
