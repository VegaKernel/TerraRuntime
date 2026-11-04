using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class CaveHouseBigRooms1458Tests
{
    [Theory]
    [InlineData(VanillaSecretWorldSeed1458.AbandonedManors, 22)]
    [InlineData(VanillaSecretWorldSeed1458.IAmError, 24)]
    public void Big_abandoned_house_room_search_follows_source_width_height_and_random_order(
        VanillaSecretWorldSeed1458 secret, int expectedDraws)
    {
        var workspace = Create(withFloor: true);
        workspace.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.None, secret));
        var random = new MinimumRandom();

        Assert.True(CaveHousePlacement1458.TryCreateRoomsForTesting(workspace, random,
            300, 190, out CaveHouseRoom1458[] rooms));

        // HouseUtils.CreateRooms_BigAbandonedHouses leaves the initial room out.
        // Its final +3 loop mutates Rectangle copies, so these Y values stay put.
        Assert.Equal(7, rooms.Length);
        for (int index = 0; index < rooms.Length; index++)
            Assert.Equal(new CaveHouseRoom1458(285, 185 - index * 7, 15, 8), rooms[index]);
        Assert.Equal(expectedDraws, random.Draws);
        Assert.Equal(0, random.NextDoubleDraws);
    }

    [Fact]
    public void Error_world_decides_big_house_before_failed_floor_search()
    {
        var workspace = Create(withFloor: false);
        workspace.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.None,
            VanillaSecretWorldSeed1458.IAmError));
        var random = new MinimumRandom();

        Assert.False(CaveHousePlacement1458.TryCreateRoomsForTesting(workspace, random,
            300, 190, out CaveHouseRoom1458[] rooms));
        Assert.Empty(rooms);
        Assert.Equal(1, random.Draws);
        Assert.Equal(0, random.NextDoubleDraws);
    }

    [Fact]
    public void Big_house_right_branch_uses_previous_room_edge_and_source_minimum_count()
    {
        var workspace = Create(withFloor: true);
        workspace.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.None,
            VanillaSecretWorldSeed1458.AbandonedManors));
        var random = new MinimumRandom(highest: true);

        Assert.True(CaveHousePlacement1458.TryCreateRoomsForTesting(workspace, random,
            300, 190, out CaveHouseRoom1458[] rooms));

        // Seven possible rooms minus Next(4)==3; the right branch clamps each
        // candidate against the previous room's right edge before subtracting width.
        Assert.Equal(4, rooms.Length);
        for (int index = 0; index < rooms.Length; index++)
            Assert.Equal(new CaveHouseRoom1458(270, 181 - index * 11, 30, 12), rooms[index]);
        Assert.Equal(13, random.Draws);
        Assert.Equal(0, random.NextDoubleDraws);
    }

    [Fact]
    public void Outer_house_offer_samples_big_house_switch_and_clamps_before_biome_placement()
    {
        var manor = new VanillaWorldSeedProfile1458(VanillaSpecialWorldSeed1458.None,
            VanillaSecretWorldSeed1458.AbandonedManors);
        var noDraw = new MinimumRandom();
        Assert.Equal(350, ChestPlacementPass1458.AdjustBigHouseOffer1458(
            300, 260, 220, 1200, manor, noDraw));
        Assert.Equal(970, ChestPlacementPass1458.AdjustBigHouseOffer1458(
            980, 260, 220, 1200, manor, noDraw));
        Assert.Equal(0, noDraw.Draws);

        var error = new VanillaWorldSeedProfile1458(VanillaSpecialWorldSeed1458.None,
            VanillaSecretWorldSeed1458.IAmError);
        var selected = new MinimumRandom();
        Assert.Equal(580, ChestPlacementPass1458.AdjustBigHouseOffer1458(
            400, 260, 220, 1200, error, selected));
        Assert.Equal(1, selected.Draws);

        var rejected = new MinimumRandom(highest: true);
        Assert.Equal(400, ChestPlacementPass1458.AdjustBigHouseOffer1458(
            400, 260, 220, 1200, error, rejected));
        Assert.Equal(1, rejected.Draws);

        // Terraria's MathHelper.Clamp returns its minimum when minimum > maximum.
        Assert.Equal(580, ChestPlacementPass1458.AdjustBigHouseOffer1458(
            260, 260, 220, 500, error, new MinimumRandom()));
        var remixManor = manor with { Special = VanillaSpecialWorldSeed1458.Remix };
        Assert.Equal(290, ChestPlacementPass1458.AdjustBigHouseOffer1458(
            200, 260, 220, 1200, remixManor, new MinimumRandom()));
    }

    private static Workspace Create(bool withFloor)
    {
        var workspace = new Workspace(600, 500);
        if (withFloor)
        {
            var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            for (int x = 100; x <= 500; x++) workspace.TileStore.Set(x, 200, in stone);
        }
        return workspace;
    }

    private sealed class MinimumRandom(bool highest = false) : IWorldGenerationVanillaRandom
    {
        public int Draws { get; private set; }
        public int NextDoubleDraws { get; private set; }
        public int Next() { Draws++; return highest ? int.MaxValue : 0; }
        public int Next(int max) { Draws++; return highest ? max - 1 : 0; }
        public int Next(int min, int max) { Draws++; return highest ? max - 1 : min; }
        public double NextDouble() { Draws++; NextDoubleDraws++; return highest ? 0.999999 : 0; }
        public void NextBytes(byte[] bytes) { Draws++; Array.Clear(bytes); }
    }
}
