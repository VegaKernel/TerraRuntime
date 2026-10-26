using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class CaveHousePlacement1458Tests
{
    [Fact]
    public void House_offer_ocean_and_desert_rectangle_boundaries_follow_the_official_pass()
    {
        // WorldGen.UndergroundHousesAndBuriedChests uses the strict oceanDepths beach
        // edges in its third loop, then RandomRectanglePoint on the adjusted, but
        // otherwise uncut, GenVars.UndergroundDesertHiveLocation in its fourth.
        const int width = 1400;
        Assert.True(ChestPlacementPass1458.IsOrdinaryHouseOceanOffer1458(379, 350, width, 260d, 360d));
        Assert.False(ChestPlacementPass1458.IsOrdinaryHouseOceanOffer1458(380, 350, width, 260d, 360d));
        Assert.False(ChestPlacementPass1458.IsOrdinaryHouseOceanOffer1458(1020, 350, width, 260d, 360d));
        Assert.True(ChestPlacementPass1458.IsOrdinaryHouseOceanOffer1458(1021, 350, width, 260d, 360d));
        Assert.False(ChestPlacementPass1458.IsOrdinaryHouseOceanOffer1458(379, 351, width, 260d, 360d));

        // The old runtime clipped Bottom to worldHeight-230; the source does not.
        Assert.Equal((326, 1120), ChestPlacementPass1458.GetAdditionalDesertHouseVerticalRange1458(
            desertTop: 300, desertBottom: 1120, worldSurface: 300.9d));
    }

    [Fact]
    public void Adjacent_room_beams_force_the_gap_and_respect_only_the_top_platform()
    {
        // Direct TerrariaServer 1.4.5.8 HouseBuilder.PlaceSupportBeams on two 20x10
        // rooms at (100,100) and (100,125): x=104/108/112/116 bridge y=110..124;
        // x=100 is blocked by the platform at (100,109). The source overwrites an
        // active chest-type cell in the bridge path because it does not scan for it.
        var workspace = new Workspace(600, 500);
        var platform = new WorldTile { Type = 19, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(100, 109, in platform);
        var preexistingObject = new WorldTile { Type = 21, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(104, 115, in preexistingObject);
        CaveHouseRoom1458[] rooms = [new(100, 100, 20, 10), new(100, 125, 20, 10)];

        CaveHousePlacement1458.PlaceSupportBeamsForTesting(workspace, rooms, beamType: 124);

        foreach (int y in new[] { 110, 115, 124 })
        {
            Assert.False(workspace.TileStore.Get(100, y).IsActive);
            foreach (int x in new[] { 104, 108, 112, 116 })
            {
                WorldTile cell = workspace.TileStore.Get(x, y);
                Assert.True(cell.IsActive);
                Assert.Equal((ushort)124, cell.Type);
            }
        }
        Assert.Equal((ushort)19, workspace.TileStore.Get(100, 109).Type);
    }
}
