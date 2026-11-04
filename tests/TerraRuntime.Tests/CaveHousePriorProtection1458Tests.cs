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
    public void Not_the_bees_house_admits_hive_block_but_keeps_other_blacklisted_tiles_rejected()
    {
        // Official 1.4.5.8 HouseUtils.AreRoomsValid switches to BeelistedTiles in
        // notTheBeesWorld: tile 225 alone is removed from BlacklistedTiles.
        var baseline = Create();
        Assert.True(CaveHousePlacement1458.TryPlace(baseline.Workspace, baseline.State,
            baseline.Random, 300, 190, out var result));
        CaveHouseRoom1458 room = result.Rooms[0];
        int tileX = room.Left - 4, tileY = room.Top + 1;

        var ordinary = Create();
        var hiveBlock = new WorldTile { Type = 225, Flags = WorldTileFlags.Active };
        ordinary.Workspace.TileStore.Set(tileX, tileY, in hiveBlock);
        WorldTile[] ordinaryBefore = ordinary.Workspace.TileStore.Tiles.ToArray();
        Assert.False(CaveHousePlacement1458.TryPlace(ordinary.Workspace, ordinary.State,
            ordinary.Random, 300, 190, out _));
        Assert.Equal(ordinaryBefore, ordinary.Workspace.TileStore.Tiles.ToArray());
        Assert.Equal(0, ordinary.Workspace.GeneratedChestCount);
        Assert.Equal(2, ordinary.Random.Calls);

        var bees = Create();
        bees.Workspace.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.NotTheBees,
            VanillaSecretWorldSeed1458.None));
        bees.Workspace.TileStore.Set(tileX, tileY, in hiveBlock);
        Assert.True(CaveHousePlacement1458.TryPlace(bees.Workspace, bees.State,
            bees.Random, 300, 190, out _));
        Assert.Equal(1, bees.Workspace.GeneratedChestCount);
        Assert.Equal(hiveBlock, bees.Workspace.TileStore.Get(tileX, tileY));

        var protectedTile = Create();
        protectedTile.Workspace.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.NotTheBees,
            VanillaSecretWorldSeed1458.None));
        var larva = new WorldTile { Type = 226, Flags = WorldTileFlags.Active };
        protectedTile.Workspace.TileStore.Set(tileX, tileY, in larva);
        WorldTile[] protectedBefore = protectedTile.Workspace.TileStore.Tiles.ToArray();
        Assert.False(CaveHousePlacement1458.TryPlace(protectedTile.Workspace, protectedTile.State,
            protectedTile.Random, 300, 190, out _));
        Assert.Equal(protectedBefore, protectedTile.Workspace.TileStore.Tiles.ToArray());
        Assert.Equal(0, protectedTile.Workspace.GeneratedChestCount);
        Assert.Equal(2, protectedTile.Random.Calls);
    }

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

    [Fact]
    public void Cave_house_offers_respect_prior_hive_padding_without_blocking_touching_bounds()
    {
        // HiveBiome.AddProtectedStructure(origin-50, 100x100, padding 5) stores a
        // 110x110 protected image. HouseUtils.CanPlace pads each proposed room by
        // another five cells; touching is accepted, strict overlap is rejected.
        var baseline = Create();
        Assert.True(CaveHousePlacement1458.TryPlace(baseline.Workspace, baseline.State, baseline.Random,
            300, 190, out var accepted));
        int right = accepted.Rooms.Max(room => room.Right);
        int top = accepted.Rooms.Min(room => room.Top);

        var touching = Create();
        touching.Workspace.SetVanillaHiveProtectedAreas([new WorldTileRegion(right + 5, top - 40, 110, 110)]);
        // The legacy projection is not the canonical StructureMap used by HouseUtils.
        // Import the already padded source rectangle, as the real hive producer does.
        touching.Workspace.VanillaStructureMap.AddProtectedStructure(new(right + 5, top - 40, 110, 110));
        Assert.True(CaveHousePlacement1458.TryPlace(touching.Workspace, touching.State, touching.Random,
            300, 190, out var touchingResult));
        Assert.Equal(accepted.Rooms, touchingResult.Rooms);
        Assert.Equal(baseline.Workspace.TileStore.Tiles.ToArray(), touching.Workspace.TileStore.Tiles.ToArray());
        Assert.Equal(baseline.Random.Next(), touching.Random.Next());

        var blocked = Create();
        blocked.Workspace.SetVanillaHiveProtectedAreas([new WorldTileRegion(right + 4, top - 40, 110, 110)]);
        blocked.Workspace.VanillaStructureMap.AddProtectedStructure(new(right + 4, top - 40, 110, 110));
        WorldTile[] before = blocked.Workspace.TileStore.Tiles.ToArray();
        Assert.False(CaveHousePlacement1458.TryPlace(blocked.Workspace, blocked.State, blocked.Random,
            300, 190, out _));
        Assert.Equal(before, blocked.Workspace.TileStore.Tiles.ToArray());
        Assert.Equal(0, blocked.Workspace.GeneratedChestCount);
        Assert.Empty(blocked.State.ProtectedHouseRooms);
        Assert.Equal(2, blocked.Random.Calls);
    }

    [Fact]
    public void Cave_house_offers_respect_prior_jungle_shrine_rectangles()
    {
        // WorldGen.JungleShrines calls GenVars.structures.AddProtectedStructure(area)
        // without additional padding. HouseUtils.CanPlace applies its five-cell
        // candidate padding when the later cave-house pass reads that shared map.
        var baseline = Create();
        Assert.True(CaveHousePlacement1458.TryPlace(baseline.Workspace, baseline.State, baseline.Random,
            300, 190, out var accepted));
        int right = accepted.Rooms.Max(room => room.Right);
        int top = accepted.Rooms.Min(room => room.Top);

        var touching = Create();
        touching.Workspace.SetVanillaJungleShrineProtectedAreas([new WorldTileRegion(right + 5, top + 2, 3, 3)]);
        touching.Workspace.VanillaStructureMap.AddProtectedStructure(new(right + 5, top + 2, 3, 3));
        Assert.True(CaveHousePlacement1458.TryPlace(touching.Workspace, touching.State, touching.Random,
            300, 190, out var touchingResult));
        Assert.Equal(accepted.Rooms, touchingResult.Rooms);
        Assert.Equal(baseline.Random.Next(), touching.Random.Next());

        var blocked = Create();
        blocked.Workspace.SetVanillaJungleShrineProtectedAreas([new WorldTileRegion(right + 4, top + 2, 3, 3)]);
        blocked.Workspace.VanillaStructureMap.AddProtectedStructure(new(right + 4, top + 2, 3, 3));
        WorldTile[] before = blocked.Workspace.TileStore.Tiles.ToArray();
        Assert.False(CaveHousePlacement1458.TryPlace(blocked.Workspace, blocked.State, blocked.Random,
            300, 190, out _));
        Assert.Equal(before, blocked.Workspace.TileStore.Tiles.ToArray());
        Assert.Equal(0, blocked.Workspace.GeneratedChestCount);
        Assert.Empty(blocked.State.ProtectedHouseRooms);
        Assert.Equal(2, blocked.Random.Calls);
    }

    private static (Workspace Workspace, ChestPlacementState1458 State, CountingRandom Random) Create()
    {
        var workspace = new Workspace(600, 500);
        Assert.True(workspace.TrySetLayers(140d, 200d));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new CountingRandom(1), 4200, false, TerraRuntime.World.VanillaSpecialWorldSeed1458.None));
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
