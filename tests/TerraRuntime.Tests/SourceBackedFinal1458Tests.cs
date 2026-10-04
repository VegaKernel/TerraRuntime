using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;
using System.Security.Cryptography;
using System.Text;

namespace TerraRuntime.Tests;

public sealed class SourceBackedFinal1458Tests
{
    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");

    private static readonly WorldGenerationPassId[] FinalPassIds =
    [
        SourceBackedFinal1458.SettleLiquidsAgainId,
        SourceBackedFinal1458.CactusPalmTreesCoralId,
        SourceBackedFinal1458.TileCleanupId,
        SourceBackedFinal1458.LihzahrdAltarsId,
        SourceBackedFinal1458.WaterPlantsId,
        SourceBackedFinal1458.StalacId,
        SourceBackedFinal1458.RemoveBrokenTrapsId,
        SourceBackedFinal1458.FinalCleanupId
    ];

    [Theory]
    [InlineData(500, 100, 128, 0, 0, false, true)]
    [InlineData(380, 100, 128, 0, 0, false, false)]
    [InlineData(620, 100, 128, 0, 0, false, false)]
    [InlineData(500, 140, 128, 0, 0, false, false)]
    [InlineData(500, 100, 255, 0, 0, false, false)]
    [InlineData(500, 100, 128, 255, 0, false, false)]
    [InlineData(500, 100, 128, 0, 189, true, false)]
    [InlineData(500, 100, 128, 0, 196, true, false)]
    [InlineData(500, 100, 128, 0, 460, true, false)]
    [InlineData(500, 100, 128, 0, 717, true, false)]
    [InlineData(500, 100, 128, 0, 718, true, false)]
    [InlineData(500, 100, 128, 0, 719, true, false)]
    [InlineData(500, 100, 128, 0, 189, false, true)]
    public void Final_cleanup_clears_only_source_isolated_partial_surface_liquid(
        int x, int y, byte amount, byte neighbourAmount, ushort neighbourType, bool neighbourActive, bool clears)
    {
        // TerrariaServer 1.4.5.8 WorldGen.FinalCleanup uses the strict beach/surface bounds,
        // three neighbours, partial liquid and TileID.Sets.Clouds; terrain activity is not a gate.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        var wet = new WorldTile { Type = 0, Flags = WorldTileFlags.Active, LiquidAmount = amount };
        workspace.TileStore.Set(x, y, in wet);
        var neighbour = new WorldTile { Type = neighbourType, LiquidAmount = neighbourAmount,
            Flags = neighbourActive ? WorldTileFlags.Active : WorldTileFlags.None };
        workspace.TileStore.Set(x, y + 1, in neighbour);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500),
                workspace, new RandomAdapter(1458)));
        WorldTile after = workspace.TileStore.Get(x, y);
        Assert.Equal(clears ? (byte)0 : amount, after.LiquidAmount);
        Assert.True(after.IsActive);
        Assert.Equal(wet.Type, after.Type);
    }

    [Fact]
    public void Final_cleanup_retains_source_surface_material_and_liquid_repair_order()
    {
        // TerrariaServer 1.4.5.8 FinalCleanup: unsupported Sand extends through cuttable air, unsafe-wall
        // liquid becomes full lava, type 314 clears its source vertical liquid band, and type 332 creates
        // a clean supporting cell. These operations precede the existing partial-surface-liquid cleanup.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        var sand = new WorldTile { Type = 53, Flags = WorldTileFlags.Active, Shape = 2 };
        workspace.TileStore.Set(500, 100, in sand);
        var unsafeLiquid = new WorldTile { Wall = 187, LiquidAmount = 20, LiquidKind = WorldLiquidKind.Water };
        workspace.TileStore.Set(600, 100, in unsafeLiquid);
        var plant = new WorldTile { Type = 314, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(700, 100, in plant);
        for (int y = 85; y <= 100; y++)
        {
            var water = workspace.TileStore.Get(700, y);
            water.LiquidAmount = 100;
            workspace.TileStore.Set(700, y, in water);
        }
        var belowWaterColumn = workspace.TileStore.Get(700, 101);
        belowWaterColumn.LiquidAmount = byte.MaxValue;
        workspace.TileStore.Set(700, 101, in belowWaterColumn);
        var hanging = new WorldTile { Type = 332, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(800, 100, in hanging);

        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, new RandomAdapter(1458)));

        for (int y = 100; y <= 109; y++)
        {
            WorldTile placed = workspace.TileStore.Get(500, y);
            Assert.True(placed.IsActive);
            Assert.Equal((ushort)53, placed.Type);
            Assert.Equal((byte)0, placed.Shape);
        }
        WorldTile lava = workspace.TileStore.Get(600, 100);
        Assert.Equal(byte.MaxValue, lava.LiquidAmount);
        Assert.Equal(WorldLiquidKind.Lava, lava.LiquidKind);
        for (int y = 85; y <= 100; y++) Assert.Equal((byte)0, workspace.TileStore.Get(700, y).LiquidAmount);
        Assert.Equal(byte.MaxValue, workspace.TileStore.Get(700, 101).LiquidAmount);
        WorldTile support = workspace.TileStore.Get(800, 101);
        Assert.True(support.IsActive);
        Assert.Equal((ushort)332, support.Type);
        Assert.Equal((byte)0, support.LiquidAmount);
    }

    [Fact]
    public void Final_cleanup_repairs_boulder_frames_then_replaces_boulders_blocked_by_a_heart()
    {
        // TerrariaServer 1.4.5.8 FinalCleanup first restores the frame-derived 2x2 boulder footprint;
        // a Crimson Heart directly above then converts it to empty active cells (or Sand for tile 484).
        var workspace = new Workspace(100, 100);
        Assert.True(workspace.TrySetLayers(40, 60));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        var fragment = new WorldTile { Type = 484, Flags = WorldTileFlags.Active, FrameX = 18, FrameY = 18 };
        workspace.TileStore.Set(51, 51, in fragment);
        var heart = new WorldTile { Type = 26, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(50, 49, in heart);

        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 100, 100), workspace, new RandomAdapter(1458)));

        for (int x = 50; x <= 51; x++)
        for (int y = 50; y <= 51; y++)
        {
            WorldTile repaired = workspace.TileStore.Get(x, y);
            Assert.True(repaired.IsActive);
            Assert.Equal((ushort)397, repaired.Type);
            Assert.Equal((short)0, repaired.FrameX);
            Assert.Equal((short)0, repaired.FrameY);
        }
    }

    [Fact]
    public void Final_cleanup_fills_a_small_open_wall_gap_with_its_dominant_boundary_wall()
    {
        // TerrariaServer 1.4.5.8 FillWallHolesInArea flood-fills a bounded open gap before the
        // Final Cleanup scan; a sealed singleton is deliberately left alone.
        var workspace = new Workspace(100, 100);
        Assert.True(workspace.TrySetLayers(60, 80));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        for (int x = 49; x <= 52; x++)
        for (int y = 49; y <= 51; y++)
        {
            if (x is 50 or 51 && y == 50)
                continue;
            var solid = new WorldTile { Type = 1, Flags = WorldTileFlags.Active, Wall = 4 };
            workspace.TileStore.Set(x, y, in solid);
        }

        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 100, 100), workspace, new RandomAdapter(1458)));

        Assert.Equal((ushort)4, workspace.TileStore.Get(50, 50).Wall);
        Assert.Equal((ushort)4, workspace.TileStore.Get(51, 50).Wall);
    }

    [Fact]
    public void Final_cleanup_assigns_an_adjacent_wall_to_a_wallless_painting_in_source_priority_order()
    {
        // TerrariaServer 1.4.5.8 TileID.Sets.Paintings chooses left, right, up, then down when a
        // generated painting has lost its own wall.
        var workspace = new Workspace(100, 100);
        Assert.True(workspace.TrySetLayers(60, 80));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        var painting = new WorldTile { Type = 240, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(50, 50, in painting);
        var left = new WorldTile { Wall = 7 };
        workspace.TileStore.Set(49, 50, in left);
        var right = new WorldTile { Wall = 8 };
        workspace.TileStore.Set(51, 50, in right);

        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 100, 100), workspace, new RandomAdapter(1458)));

        Assert.Equal((ushort)7, workspace.TileStore.Get(50, 50).Wall);
    }

    [Fact]
    public void Settle_liquids_again_matches_official_passlegacy_fixture()
    {
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));

        for (int x = 296; x <= 304; x++)
        {
            var floor = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, 240, in floor);
        }
        var water = new WorldTile { LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
        workspace.TileStore.Set(300, 200, in water);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.SettleLiquidsAgain, new FinalState1458())
            .Execute(new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height), workspace, random));

        Assert.Equal(906992634, random.Next());
        Assert.Equal("0B0F5A4265C269D93B40FC4D9E026134B1FA0B42CFD92FAC304E105515D6D6F4", HashFixture(workspace));
    }

    [Fact]
    public void Tile_cleanup_only_flattens_shapes_the_source_SaveSlopes_gate_rejects()
    {
        var workspace = new Workspace(100, 100);
        Assert.True(workspace.TrySetLayers(10, 20));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));

        var unsupported = new WorldTile { Type = 51, Flags = WorldTileFlags.Active, Shape = 5 };
        var retainedException = new WorldTile { Type = 131, Flags = WorldTileFlags.Active, Shape = 4 };
        var inactive = new WorldTile
        {
            Type = 1,
            FrameX = 72,
            FrameY = 54,
            Flags = WorldTileFlags.Actuator | WorldTileFlags.Inactive,
            Shape = 2,
            LiquidAmount = 25,
            LiquidKind = WorldLiquidKind.Lava
        };
        workspace.TileStore.Set(10, 10, in unsupported);
        workspace.TileStore.Set(11, 10, in retainedException);
        workspace.TileStore.Set(12, 10, in inactive);
        var leftFacingTopSlope = new WorldTile { Type = 1, Flags = WorldTileFlags.Active, Shape = 2 };
        var rightFacingTopSlope = new WorldTile { Type = 1, Flags = WorldTileFlags.Active, Shape = 3 };
        var halfBrick = new WorldTile { Type = 1, Flags = WorldTileFlags.Active, Shape = 1 };
        workspace.TileStore.Set(50, 50, in leftFacingTopSlope);
        workspace.TileStore.Set(51, 50, in halfBrick);
        workspace.TileStore.Set(55, 50, in rightFacingTopSlope);
        workspace.TileStore.Set(54, 50, in halfBrick);
        workspace.TileStore.Set(58, 50, in leftFacingTopSlope);
        workspace.TileStore.Set(57, 50, in halfBrick);
        var submergedPlant = new WorldTile
        {
            Type = 3,
            FrameX = 18,
            FrameY = 36,
            Flags = WorldTileFlags.Active | WorldTileFlags.Inactive | WorldTileFlags.InvisibleBlock,
            LiquidAmount = byte.MaxValue,
            LiquidKind = WorldLiquidKind.Honey,
            TileColor = 7
        };
        workspace.TileStore.Set(48, 55, in submergedPlant);

        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 100, 100), workspace, new RandomAdapter(1458)));

        Assert.Equal((byte)0, workspace.TileStore.Get(10, 10).Shape);
        Assert.Equal((byte)4, workspace.TileStore.Get(11, 10).Shape);
        WorldTile preserved = workspace.TileStore.Get(12, 10);
        Assert.Equal((byte)0, preserved.Shape);
        Assert.Equal((short)72, preserved.FrameX);
        Assert.Equal((short)54, preserved.FrameY);
        Assert.Equal(WorldTileFlags.Actuator | WorldTileFlags.Inactive, preserved.Flags);
        Assert.Equal((byte)25, preserved.LiquidAmount);
        Assert.Equal(WorldLiquidKind.Lava, preserved.LiquidKind);
        Assert.Equal((byte)1, workspace.TileStore.Get(50, 50).Shape);
        Assert.Equal((byte)1, workspace.TileStore.Get(55, 50).Shape);
        Assert.Equal((byte)2, workspace.TileStore.Get(58, 50).Shape);
        WorldTile drowned = workspace.TileStore.Get(48, 55);
        Assert.False(drowned.IsActive);
        Assert.Equal((ushort)0, drowned.Type);
        Assert.Equal((short)-1, drowned.FrameX);
        Assert.Equal((short)-1, drowned.FrameY);
        Assert.Equal((byte)0, drowned.TileColor);
        Assert.Equal((byte)255, drowned.LiquidAmount);
        Assert.Equal(WorldLiquidKind.Honey, drowned.LiquidKind);
    }

    [Fact]
    public void Tile_cleanup_keeps_source_order_for_drips_and_liquid_blocking_walls()
    {
        var workspace = new Workspace(100, 100);
        Assert.True(workspace.TrySetLayers(10, 20));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));

        var ceiling = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        var water = new WorldTile { LiquidAmount = 128, LiquidKind = WorldLiquidKind.Water };
        var dungeonWallLiquid = new WorldTile { Wall = 13, LiquidAmount = 255, LiquidKind = WorldLiquidKind.Lava };
        var templeWallLiquid = new WorldTile { Wall = 87, LiquidAmount = 255, LiquidKind = WorldLiquidKind.Honey };
        workspace.TileStore.Set(50, 49, in ceiling);
        workspace.TileStore.Set(50, 48, in water);
        workspace.TileStore.Set(58, 59, in dungeonWallLiquid);
        workspace.TileStore.Set(59, 59, in templeWallLiquid);
        var trap = new WorldTile { Type = 137, Flags = WorldTileFlags.Active, FrameX = 0, FrameY = 0 };
        var trapNeighbour = new WorldTile { Type = 1, Flags = WorldTileFlags.Active, Shape = 1 };
        workspace.TileStore.Set(55, 55, in trap);
        workspace.TileStore.Set(54, 55, in trapNeighbour);

        var random = new DripRandom();
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 100, 100), workspace, random));

        WorldTile drip = workspace.TileStore.Get(50, 50);
        Assert.True(drip.IsActive);
        Assert.Equal((ushort)373, drip.Type);
        Assert.Equal((short)0, drip.FrameX);
        Assert.Equal((short)0, drip.FrameY);
        Assert.True(random.DrawCount > 3);
        Assert.Equal((byte)0, workspace.TileStore.Get(58, 59).LiquidAmount);
        Assert.Equal((byte)0, workspace.TileStore.Get(59, 59).LiquidAmount);
        Assert.False(workspace.TileStore.Get(54, 55).IsActive);
    }

    [Fact]
    public void Tile_cleanup_treats_type_379_as_non_solid_for_its_temporary_drip_ceiling_gate()
    {
        // TileCleanup itself sets Main.tileSolid[379] false before the scan. A full water source above
        // Bubble 379 therefore cannot offer the otherwise accepted ceiling drip at the empty target.
        var workspace = CreateFinalWorkspace();
        var bubble = new WorldTile { Type = 379, Flags = WorldTileFlags.Active };
        var water = new WorldTile { LiquidAmount = 128, LiquidKind = WorldLiquidKind.Water };
        workspace.TileStore.Set(50, 49, in bubble);
        workspace.TileStore.Set(50, 48, in water);

        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 100, 100),
                workspace,
                new DripRandom()));

        Assert.False(workspace.TileStore.Get(50, 50).IsActive);
    }

    [Fact]
    public void Tile_cleanup_type_379_fixture_matches_official_passlegacy_rng_and_target()
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixture, 600x500, seed 1458: a type-379 ceiling
        // is deliberately marked solid before TileCleanup, but the pass clears that temporary solidity.
        // The target remains inactive and the next shared genRand value is 1335025742.
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            workspace.TileStore.Set(x, y, in stone);

        var inactiveWater = new WorldTile { LiquidAmount = 128, LiquidKind = WorldLiquidKind.Water };
        var bubble = new WorldTile { Type = 379, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(300, 198, in inactiveWater);
        workspace.TileStore.Set(300, 199, in bubble);
        workspace.TileStore.Set(300, 200, default);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height),
                workspace,
                random));

        Assert.False(workspace.TileStore.Get(300, 200).IsActive);
        Assert.Equal(1335025742, random.Next());
    }

    [Fact]
    public void Tile_cleanup_repairs_every_piece_of_a_broken_crimson_heart()
    {
        var workspace = new Workspace(100, 100);
        Assert.True(workspace.TrySetLayers(10, 20));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: true, isRemix: false));

        // Only the lower-right piece remains. TerrariaServer TileCleanup derives the 2x2 origin from
        // frame coordinates and restores all four cells with the Crimson style offset.
        var survivor = new WorldTile
        {
            Type = 31,
            Flags = WorldTileFlags.Active,
            FrameX = 54,
            FrameY = 54,
            Shape = 3
        };
        workspace.TileStore.Set(51, 51, in survivor);

        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 100, 100)
        {
            Options = new(WorldGenerationGameMode.Classic, WorldGenerationEvil.Crimson)
        };
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(request, workspace, new RandomAdapter(1458)));

        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile repaired = workspace.TileStore.Get(50 + dx, 50 + dy);
            Assert.True(repaired.IsActive);
            Assert.Equal((ushort)31, repaired.Type);
            Assert.Equal((short)(36 + dx * 18), repaired.FrameX);
            Assert.Equal((short)(36 + dy * 18), repaired.FrameY);
            Assert.Equal((byte)0, repaired.Shape);
        }
    }

    [Theory]
    [InlineData((ushort)12)]
    [InlineData((ushort)28)]
    [InlineData((ushort)639)]
    public void Tile_cleanup_repairs_two_by_two_objects_and_their_missing_wall_terrain_support(ushort type)
    {
        var workspace = new Workspace(100, 100);
        Assert.True(workspace.TrySetLayers(10, 20));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));

        var survivor = new WorldTile
        {
            Type = type,
            Flags = WorldTileFlags.Active,
            FrameX = 54,
            FrameY = 54,
            Shape = 3
        };
        workspace.TileStore.Set(51, 51, in survivor);

        var snowWallSupport = new WorldTile { Wall = 40, Shape = 3 };
        var dirtWallSupport = new WorldTile { Wall = 1, Shape = 2 };
        workspace.TileStore.Set(50, 52, in snowWallSupport);
        workspace.TileStore.Set(51, 52, in dirtWallSupport);

        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 100, 100);
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(request, workspace, new RandomAdapter(1458)));

        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile repaired = workspace.TileStore.Get(50 + dx, 50 + dy);
            Assert.True(repaired.IsActive);
            Assert.Equal(type, repaired.Type);
            Assert.Equal((short)(36 + dx * 18), repaired.FrameX);
            Assert.Equal((short)(36 + dy * 18), repaired.FrameY);
            Assert.Equal((byte)0, repaired.Shape);
        }

        WorldTile snowSupport = workspace.TileStore.Get(50, 52);
        WorldTile dirtSupport = workspace.TileStore.Get(51, 52);
        Assert.True(snowSupport.IsActive);
        Assert.Equal((ushort)147, snowSupport.Type);
        Assert.Equal((byte)0, snowSupport.Shape);
        Assert.True(dirtSupport.IsActive);
        Assert.Equal((ushort)0, dirtSupport.Type);
        Assert.Equal((byte)0, dirtSupport.Shape);
    }

    [Fact]
    public void Tile_cleanup_repairs_crimson_three_by_two_heart_and_its_source_cleanup_neighbours()
    {
        var workspace = new Workspace(100, 100);
        Assert.True(workspace.TrySetLayers(10, 20));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: true, isRemix: false));

        var survivor = new WorldTile { Type = 26, Flags = WorldTileFlags.Active, FrameX = 90, FrameY = 18, Shape = 3 };
        workspace.TileStore.Set(52, 51, in survivor);
        for (int x = 50; x <= 52; x++)
        {
            var support = new WorldTile { Wall = 3, Shape = 3 };
            workspace.TileStore.Set(x, 52, in support);
        }

        var brokenBelow = new WorldTile { Type = 28, Flags = WorldTileFlags.Active, FrameY = 18 };
        var leftFragment = new WorldTile { Type = 12, Flags = WorldTileFlags.Active, FrameX = 0 };
        var rightFragment = new WorldTile { Type = 28, Flags = WorldTileFlags.Active, FrameX = 18 };
        workspace.TileStore.Set(50, 53, in brokenBelow);
        workspace.TileStore.Set(49, 50, in leftFragment);
        workspace.TileStore.Set(53, 50, in rightFragment);

        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 100, 100)
        {
            Options = new(WorldGenerationGameMode.Classic, WorldGenerationEvil.Crimson)
        };
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(request, workspace, new RandomAdapter(1458)));

        for (int dx = 0; dx < 3; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile repaired = workspace.TileStore.Get(50 + dx, 50 + dy);
            Assert.True(repaired.IsActive);
            Assert.Equal((ushort)26, repaired.Type);
            Assert.Equal((short)(54 + dx * 18), repaired.FrameX);
            Assert.Equal((short)(dy * 18), repaired.FrameY);
            Assert.Equal((byte)0, repaired.Shape);
        }

        for (int x = 50; x <= 52; x++)
        {
            WorldTile support = workspace.TileStore.Get(x, 52);
            Assert.True(support.IsActive);
            Assert.Equal((ushort)25, support.Type);
            Assert.Equal((byte)0, support.Shape);
            Assert.False(workspace.TileStore.Get(x, 53).IsActive);
        }

        Assert.False(workspace.TileStore.Get(49, 50).IsActive);
        Assert.False(workspace.TileStore.Get(53, 50).IsActive);
    }

    [Fact]
    public void Tile_cleanup_converts_spike_ball_support_to_sunplate()
    {
        var workspace = new Workspace(100, 100);
        Assert.True(workspace.TrySetLayers(10, 20));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        var spikeBall = new WorldTile { Type = 237, Flags = WorldTileFlags.Active };
        var lihzahrdBrick = new WorldTile { Type = 232, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(50, 50, in spikeBall);
        workspace.TileStore.Set(50, 51, in lihzahrdBrick);

        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 100, 100);
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(request, workspace, new RandomAdapter(1458)));

        Assert.Equal((ushort)226, workspace.TileStore.Get(50, 51).Type);
    }

    [Fact]
    public void Tile_cleanup_removes_unattached_type_162_but_keeps_source_protected_cases()
    {
        var workspace = new Workspace(100, 100);
        Assert.True(workspace.TrySetLayers(10, 20));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        var loose = new WorldTile { Type = 162, Flags = WorldTileFlags.Active };
        var wallBlocked = new WorldTile { Type = 162, Wall = 350, Flags = WorldTileFlags.Active };
        var chestBlocked = new WorldTile { Type = 162, Flags = WorldTileFlags.Active };
        var chestAbove = new WorldTile { Type = 21, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(50, 50, in loose);
        workspace.TileStore.Set(55, 50, in wallBlocked);
        workspace.TileStore.Set(60, 50, in chestBlocked);
        workspace.TileStore.Set(60, 49, in chestAbove);

        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 100, 100);
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(request, workspace, new RandomAdapter(1458)));

        Assert.False(workspace.TileStore.Get(50, 50).IsActive);
        Assert.True(workspace.TileStore.Get(55, 50).IsActive);
        Assert.True(workspace.TileStore.Get(60, 50).IsActive);
    }

    [Fact]
    public void Tile_cleanup_recovers_basic_chest_with_its_source_loot_style()
    {
        var workspace = new Workspace(100, 100);
        Assert.True(workspace.TrySetLayers(10, 20));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            var piece = new WorldTile { Type = 21, Flags = WorldTileFlags.Active, FrameX = (short)(dx * 18), FrameY = (short)(dy * 18) };
            workspace.TileStore.Set(50 + dx, 50 + dy, in piece);
        }
        Assert.True(workspace.TryAddGeneratedChest(50, 50, string.Empty, [new WorldChestItem(1, 1156, 0)]));
        var survivor = new WorldTile { Type = 21, Flags = WorldTileFlags.Active, FrameX = 18, FrameY = 18, Shape = 3 };
        workspace.TileStore.Set(51, 51, in survivor);
        var support = new WorldTile { Wall = 3 };
        workspace.TileStore.Set(50, 52, in support);
        workspace.TileStore.Set(51, 52, in support);

        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 100, 100);
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(request, workspace, new RandomAdapter(1458)));

        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile repaired = workspace.TileStore.Get(50 + dx, 50 + dy);
            Assert.True(repaired.IsActive);
            Assert.Equal((ushort)21, repaired.Type);
            Assert.Equal((short)(828 + dx * 18), repaired.FrameX);
            Assert.Equal((short)(dy * 18), repaired.FrameY);
        }
    }

    [Fact]
    public void Tile_cleanup_basic_chest_fixture_matches_official_passlegacy_cells_and_rng()
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixture, 600x500, seed 1458: the sole lower-right
        // ordinary chest piece is restored to a 2x2 style-0 chest over its already-solid stone support.
        // Official next shared genRand is 906992634.
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            workspace.TileStore.Set(x, y, in stone);

        var chestPiece = new WorldTile { Type = 21, Flags = WorldTileFlags.Active, FrameX = 18, FrameY = 18 };
        workspace.TileStore.Set(300, 200, in chestPiece);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height),
                workspace,
                random));

        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile chest = workspace.TileStore.Get(299 + dx, 199 + dy);
            Assert.True(chest.IsActive);
            Assert.Equal((ushort)21, chest.Type);
            Assert.Equal((short)(dx * 18), chest.FrameX);
            Assert.Equal((short)(dy * 18), chest.FrameY);
        }

        Assert.Equal(906992634, random.Next());
    }

    [Fact]
    public void Tile_cleanup_trap_neighbour_fixture_matches_official_passlegacy_cells_and_rng()
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixture, 600x500, seed 1458: a frame-0 trap
        // clears only the active bit of its left half-brick neighbour. No pass RNG is consumed.
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            workspace.TileStore.Set(x, y, in stone);

        var trap = new WorldTile { Type = 137, Flags = WorldTileFlags.Active, FrameX = 0, FrameY = 0 };
        var halfBrick = new WorldTile { Type = 1, Flags = WorldTileFlags.Active, Shape = 1 };
        workspace.TileStore.Set(300, 200, in trap);
        workspace.TileStore.Set(299, 200, in halfBrick);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height),
                workspace,
                random));

        WorldTile cleared = workspace.TileStore.Get(299, 200);
        Assert.False(cleared.IsActive);
        Assert.Equal((ushort)1, cleared.Type);
        Assert.Equal((byte)1, cleared.Shape);
        Assert.True(workspace.TileStore.Get(300, 200).IsActive);
        Assert.Equal(906992634, random.Next());
    }

    [Fact]
    public void Tile_cleanup_liquid_blocking_wall_fixture_matches_official_passlegacy_cells_and_rng()
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixture: ordinary wall 13 clears liquid in its
        // inactive cell without consuming shared generation RNG.
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            workspace.TileStore.Set(x, y, in stone);
        var target = new WorldTile { Wall = 13, LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
        workspace.TileStore.Set(300, 200, in target);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height), workspace, random));

        WorldTile cleared = workspace.TileStore.Get(300, 200);
        Assert.False(cleared.IsActive);
        Assert.Equal((ushort)13, cleared.Wall);
        Assert.Equal((byte)0, cleared.LiquidAmount);
        Assert.Equal(906992634, random.Next());
    }

    [Fact]
    public void Tile_cleanup_spike_ball_support_fixture_matches_official_passlegacy_cells_and_rng()
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixture: type 237 directly above type 232 converts
        // only that support to Sunplate 226 and preserves the shared RNG position.
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            workspace.TileStore.Set(x, y, in stone);
        var spikeBall = new WorldTile { Type = 237, Flags = WorldTileFlags.Active };
        var support = new WorldTile { Type = 232, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(300, 200, in spikeBall);
        workspace.TileStore.Set(300, 201, in support);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height), workspace, random));

        Assert.Equal((ushort)237, workspace.TileStore.Get(300, 200).Type);
        Assert.Equal((ushort)226, workspace.TileStore.Get(300, 201).Type);
        Assert.Equal(906992634, random.Next());
    }

    [Fact]
    public void Tile_cleanup_type_162_fixture_matches_official_passlegacy_cells_and_rng()
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixture: an unattached type 162 clears only its
        // active bit. The two inactive neighbours also retain the source's ordinary drip RNG offers.
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            workspace.TileStore.Set(x, y, in stone);
        var target = new WorldTile { Type = 162, Flags = WorldTileFlags.Active };
        var empty = new WorldTile();
        workspace.TileStore.Set(300, 200, in target);
        workspace.TileStore.Set(300, 199, in empty);
        workspace.TileStore.Set(300, 201, in empty);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height), workspace, random));

        WorldTile cleared = workspace.TileStore.Get(300, 200);
        Assert.False(cleared.IsActive);
        Assert.Equal((ushort)162, cleared.Type);
        Assert.Equal(1507290721, random.Next());
    }

    [Fact]
    public void Tile_cleanup_life_crystal_fixture_matches_official_passlegacy_cells_and_rng()
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixture, 600x500, seed 1458: the sole lower-right
        // type-12 piece at frame 54,54 restores the style-1 2x2 Life Crystal at (299..300,199..200).
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            workspace.TileStore.Set(x, y, in stone);

        var crystalPiece = new WorldTile { Type = 12, Flags = WorldTileFlags.Active, FrameX = 54, FrameY = 54 };
        workspace.TileStore.Set(300, 200, in crystalPiece);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height),
                workspace,
                random));

        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile crystal = workspace.TileStore.Get(299 + dx, 199 + dy);
            Assert.True(crystal.IsActive);
            Assert.Equal((ushort)12, crystal.Type);
            Assert.Equal((short)(36 + dx * 18), crystal.FrameX);
            Assert.Equal((short)(36 + dy * 18), crystal.FrameY);
        }

        Assert.Equal(906992634, random.Next());
    }

    [Fact]
    public void Tile_cleanup_shadow_orb_fixture_matches_official_passlegacy_cells_and_rng()
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixture, 600x500, seed 1458: the lower-right
        // framed source piece restores the ordinary type-31 frame bank at (299..300,199..200): in
        // a non-Crimson ordinary world the source ignores its old horizontal style and chooses bank zero.
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            workspace.TileStore.Set(x, y, in stone);

        var orbPiece = new WorldTile { Type = 31, Flags = WorldTileFlags.Active, FrameX = 54, FrameY = 18 };
        workspace.TileStore.Set(300, 200, in orbPiece);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height),
                workspace,
                random));

        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile orb = workspace.TileStore.Get(299 + dx, 199 + dy);
            Assert.True(orb.IsActive);
            Assert.Equal((ushort)31, orb.Type);
            Assert.Equal((short)(dx * 18), orb.FrameX);
            Assert.Equal((short)(dy * 18), orb.FrameY);
        }
        Assert.Equal(906992634, random.Next());
    }

    [Theory]
    [InlineData((ushort)639)]
    [InlineData((ushort)28)]
    public void Tile_cleanup_special_two_by_two_fixture_matches_official_passlegacy_cells_and_rng(ushort type)
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixtures, 600x500 all-stone seed 1458: lower-right
        // style-1 pieces of types 639 and 28 recover the 2x2 source frame bank at (299..300,199..200).
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            workspace.TileStore.Set(x, y, in stone);

        var piece = new WorldTile { Type = type, Flags = WorldTileFlags.Active, FrameX = 54, FrameY = 54 };
        workspace.TileStore.Set(300, 200, in piece);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height),
                workspace,
                random));

        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile repaired = workspace.TileStore.Get(299 + dx, 199 + dy);
            Assert.True(repaired.IsActive);
            Assert.Equal(type, repaired.Type);
            Assert.Equal((short)(36 + dx * 18), repaired.FrameX);
            Assert.Equal((short)(36 + dy * 18), repaired.FrameY);
        }
        Assert.Equal(906992634, random.Next());
    }

    [Fact]
    public void Tile_cleanup_crimson_heart_fixture_matches_official_passlegacy_cells_and_rng()
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixture, 600x500, seed 1458, ordinary corruption:
        // the sole right-hand type-26 piece at frame 36,18 restores a style-0 3x2 footprint at
        // (300..302,199..200), while the already-solid three-cell support remains stone.
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            workspace.TileStore.Set(x, y, in stone);

        var heartPiece = new WorldTile { Type = 26, Flags = WorldTileFlags.Active, FrameX = 36, FrameY = 18 };
        workspace.TileStore.Set(302, 200, in heartPiece);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.TileCleanup, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height),
                workspace,
                random));

        for (int dx = 0; dx < 3; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile heart = workspace.TileStore.Get(300 + dx, 199 + dy);
            Assert.True(heart.IsActive);
            Assert.Equal((ushort)26, heart.Type);
            Assert.Equal((short)(dx * 18), heart.FrameX);
            Assert.Equal((short)(dy * 18), heart.FrameY);
        }

        Assert.Equal(906992634, random.Next());
    }

    [Fact]
    public void Lihzahrd_altar_consumes_the_retained_temple_anchor_without_rng_or_site_search()
    {
        // TerrariaServer 1.4.5.8 GenPassNameID.LihzahrdAltar writes GenVars.lAltarX/lAltarY directly:
        // type 237 at its 3x2 footprint and active, unsloped type 226 below each column. This fixture
        // starts with unrelated active content so a material scan or an existing-altar early exit cannot pass.
        var workspace = CreateFinalWorkspace();
        workspace.SetVanillaLihzahrdAltarState(new VanillaLihzahrdAltarState1458(50, 50));
        for (int dx = 0; dx < 3; dx++)
        for (int dy = 0; dy < 3; dy++)
        {
            var stale = new WorldTile
            {
                Type = 1,
                FrameX = 72,
                FrameY = 54,
                Flags = WorldTileFlags.Active | WorldTileFlags.Actuator,
                Shape = 3,
                LiquidAmount = 87,
                LiquidKind = WorldLiquidKind.Honey
            };
            workspace.TileStore.Set(50 + dx, 50 + dy, in stale);
        }

        new FinalPass1458(FinalStage1458.LihzahrdAltars, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 100, 100),
                workspace,
                new ThrowingRandom()));

        for (int dx = 0; dx < 3; dx++)
        {
            for (int dy = 0; dy < 2; dy++)
            {
                WorldTile altar = workspace.TileStore.Get(50 + dx, 50 + dy);
                Assert.True(altar.IsActive);
                Assert.Equal((ushort)237, altar.Type);
                Assert.Equal((short)(dx * 18), altar.FrameX);
                Assert.Equal((short)(dy * 18), altar.FrameY);
            }

            WorldTile support = workspace.TileStore.Get(50 + dx, 52);
            Assert.True(support.IsActive);
            Assert.Equal((ushort)226, support.Type);
            Assert.Equal((byte)0, support.Shape);
        }
    }

    [Fact]
    public void Lihzahrd_altar_full_passlegacy_fixture_matches_the_official_grid_digest()
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixture, 600x500, seed 1458. GenVars.lAltarX/Y
        // is 300,200; every tile begins as active stone, and the target's prior frames deliberately
        // prove that the delegate changes only the source-described altar fields.
        const int width = 600;
        const int height = 500;
        var workspace = CreateStoneWorkspace(width, height);
        workspace.SetVanillaLihzahrdAltarState(new VanillaLihzahrdAltarState1458(300, 200));
        for (int dx = 0; dx < 3; dx++)
        for (int dy = 0; dy < 3; dy++)
        {
            var tile = workspace.TileStore.Get(300 + dx, 200 + dy);
            tile.FrameX = 72;
            tile.FrameY = 54;
            workspace.TileStore.Set(300 + dx, 200 + dy, in tile);
        }

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.LihzahrdAltars, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height),
                workspace,
                random));

        Assert.Equal("9F59BE7A2E2B8DF81DF32BA2D14D09D34377CEA75976AE67737BE9E073CD8A95", HashFixture(workspace));
        Assert.Equal(906992634, random.Next());
    }

    [Fact]
    public void Water_plants_cat_tail_fixture_matches_official_passlegacy_cells_and_rng()
    {
        // Direct TerrariaServer 1.4.5.8 Water Plants PassLegacy fixture, 600x500, seed 1458:
        // one five-cell water column over Grass at x=300. The source offers the submerged cell,
        // chooses a cat tail, then retains the exact five-segment growth and shared RNG position.
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        for (int y = 100; y <= 104; y++)
        {
            var water = new WorldTile { LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
            workspace.TileStore.Set(300, y, in water);
        }
        var grass = new WorldTile { Type = 2, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(300, 105, in grass);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.WaterPlants, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height),
                workspace,
                random));

        short[] frames = [90, 108, 108, 108, 72];
        for (int index = 0; index < frames.Length; index++)
        {
            WorldTile tail = workspace.TileStore.Get(300, 100 + index);
            Assert.True(tail.IsActive);
            Assert.Equal((ushort)519, tail.Type);
            Assert.Equal(frames[index], tail.FrameX);
            Assert.Equal((short)0, tail.FrameY);
            Assert.Equal(byte.MaxValue, tail.LiquidAmount);
        }
        Assert.Equal(1420393577, random.Next());
    }

    [Fact]
    public void Water_plants_lily_pad_fixture_matches_official_passlegacy_cells_and_rng()
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixture, 600x500, seed 7: the same five-cell
        // water column over grass selects a lily pad. Frame 72 exercises the column-dependent frame bank.
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        for (int y = 100; y <= 104; y++)
        {
            var water = new WorldTile { LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
            workspace.TileStore.Set(300, y, in water);
        }
        var grass = new WorldTile { Type = 2, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(300, 105, in grass);

        var random = new RandomAdapter(7);
        new FinalPass1458(FinalStage1458.WaterPlants, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 7, width, height),
                workspace,
                random));

        WorldTile pad = workspace.TileStore.Get(300, 100);
        Assert.True(pad.IsActive);
        Assert.Equal((ushort)518, pad.Type);
        Assert.Equal((short)72, pad.FrameX);
        Assert.Equal((short)0, pad.FrameY);
        Assert.Equal(byte.MaxValue, pad.LiquidAmount);
        for (int y = 101; y <= 104; y++)
            Assert.False(workspace.TileStore.Get(300, y).IsActive);
        Assert.Equal(902071154, random.Next());
    }

    [Fact]
    public void Water_plants_bamboo_fixture_matches_official_passlegacy_cells_and_rng()
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixture, 600x500, seed 1: an active Jungle Plant
        // in a three-cell water column over Jungle Grass enters PlaceBamboo and grows its source stack.
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        for (int y = 98; y <= 100; y++)
        {
            var water = new WorldTile { LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
            workspace.TileStore.Set(300, y, in water);
        }
        for (int y = 98; y <= 99; y++)
        {
            var plant = new WorldTile { Type = 3, Flags = WorldTileFlags.Active, LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
            workspace.TileStore.Set(300, y, in plant);
        }
        var junglePlant = new WorldTile { Type = 61, Flags = WorldTileFlags.Active, LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
        var jungleGrass = new WorldTile { Type = 60, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(300, 100, in junglePlant);
        workspace.TileStore.Set(300, 101, in jungleGrass);

        var random = new RandomAdapter(1);
        new FinalPass1458(FinalStage1458.WaterPlants, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1, width, height),
                workspace,
                random));

        for (int y = 96; y <= 100; y++)
        {
            WorldTile bamboo = workspace.TileStore.Get(300, y);
            Assert.True(bamboo.IsActive);
            Assert.Equal((ushort)571, bamboo.Type);
            Assert.Equal((short)0, bamboo.FrameX);
            Assert.Equal((short)0, bamboo.FrameY);
        }
        Assert.Equal(1928246059, random.Next());
    }

    [Fact]
    public void Water_plants_seaweed_fixture_matches_official_passlegacy_cells_and_rng()
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixture, 600x500, seed 1: a submerged seaweed
        // tile at y=200 over a stone floor at y=210 grows one source segment at y=199.
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        for (int y = 197; y <= 200; y++)
        {
            var water = new WorldTile { LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
            workspace.TileStore.Set(300, y, in water);
        }
        var seaweed = new WorldTile { Type = 549, Flags = WorldTileFlags.Active, LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(300, 200, in seaweed);
        workspace.TileStore.Set(300, 210, in stone);

        var random = new RandomAdapter(1);
        new FinalPass1458(FinalStage1458.WaterPlants, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1, width, height),
                workspace,
                random));

        for (int y = 199; y <= 200; y++)
        {
            WorldTile segment = workspace.TileStore.Get(300, y);
            Assert.True(segment.IsActive);
            Assert.Equal((ushort)549, segment.Type);
            Assert.Equal((short)0, segment.FrameX);
            Assert.Equal((short)0, segment.FrameY);
        }
        Assert.Equal(540780342, random.Next());
    }

    [Fact]
    public void Water_plants_mixed_terrain_full_passlegacy_fixture_matches_the_official_grid_digest()
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixture, 600x500, seed 1. This single ordinary
        // terrain fixture combines separate grass pools, a jungle-bamboo column, and two deep seaweed
        // columns; its whole-grid digest catches scan order, helper RNG, framing, and liquid mutations.
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        foreach (int x in new[] { 100, 200 })
        {
            for (int y = 100; y <= 104; y++)
            {
                var water = new WorldTile { LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
                workspace.TileStore.Set(x, y, in water);
            }
            var grass = new WorldTile { Type = 2, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, 105, in grass);
        }
        for (int y = 98; y <= 100; y++)
        {
            var water = new WorldTile { LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
            workspace.TileStore.Set(300, y, in water);
        }
        for (int y = 98; y <= 99; y++)
        {
            var plant = new WorldTile { Type = 3, Flags = WorldTileFlags.Active, LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
            workspace.TileStore.Set(300, y, in plant);
        }
        var junglePlant = new WorldTile { Type = 61, Flags = WorldTileFlags.Active, LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
        var jungleGrass = new WorldTile { Type = 60, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(300, 100, in junglePlant);
        workspace.TileStore.Set(300, 101, in jungleGrass);
        foreach (int x in new[] { 400, 450 })
        {
            for (int y = 197; y <= 200; y++)
            {
                var water = new WorldTile { LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
                workspace.TileStore.Set(x, y, in water);
            }
            var seaweed = new WorldTile { Type = 549, Flags = WorldTileFlags.Active, LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
            var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, 200, in seaweed);
            workspace.TileStore.Set(x, 210, in stone);
        }

        var random = new RandomAdapter(1);
        new FinalPass1458(FinalStage1458.WaterPlants, new FinalState1458())
            .Execute(new Context(
                new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1, width, height),
                workspace,
                random));

        Assert.Equal("0AFD55A6AD59A44C0A4E62B5022301E15900DF6658111CC5F19699F0635615EF", HashFixture(workspace));
        Assert.Equal(1773873583, random.Next());
    }

    [Fact]
    public void Complete_ordinary_plan_matches_every_applicable_source_registration_in_order()
    {
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "AllPasses", 1458, 4200, 1200);
        var builder = new CaptureBuilder();
        new SourceBackedFinal1458().BuildPlan(in request, builder);

        string[] catalog = PassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        Assert.Equal(109, catalog.Length);
        // AddPasses registers this first Jungle and Skyblock only inside skyblockWorldGen.
        Assert.Equal("Jungle", catalog[1]);
        Assert.Equal("Skyblock", catalog[2]);
        string[] expected = catalog.Where((_, index) => index is not (1 or 2))
            .Select(static name => new string(name.Where(char.IsLetterOrDigit).ToArray())).ToArray();
        string[] bridges = ["Reset", "TerrainLayers", "Biomes", "Caves", "Ores", "SecretSeeds", "Metadata"];
        string[] actual = builder.Entries.Select(static entry => entry.Descriptor.Id.Value.Split('/')[1])
            .Where(name => !bridges.Contains(name)).ToArray();

        Assert.Equal(114, builder.Entries.Count);
        Assert.Equal(107, actual.Length);
        Assert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase);
        // This proves registration/ordering only, not pass geometry or complete-world equality.
    }

    [Fact]
    public void Canonical_ordinary_world_registers_final_eight_passes_in_pinned_order()
    {
        var provider = new SourceBackedFinal1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "FinalOverlay",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        int microBiomes = builder.Entries.FindIndex(static entry =>
            entry.Descriptor.Id == SourceBackedMicroBiomes1458.MicroBiomesId);
        Assert.True(microBiomes >= 0);

        for (int index = 0; index < FinalPassIds.Length; index++)
        {
            CaptureEntry entry = builder.Entries[microBiomes + index + 1];
            Assert.Equal(FinalPassIds[index], entry.Descriptor.Id);
            Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, entry.Descriptor.RngMode);

            WorldGenerationPassId expectedDependency = index == 0
                ? SourceBackedMicroBiomes1458.MicroBiomesId
                : FinalPassIds[index - 1];
            Assert.Contains(expectedDependency, entry.Descriptor.RequiredAfter.ToArray());
        }

        CaptureEntry secrets = Assert.Single(builder.Entries, static entry => entry.Descriptor.Id == SecretSeedsId);
        Assert.Contains(SourceBackedFinal1458.FinalCleanupId,
            secrets.Descriptor.RequiredAfter.ToArray());
    }

    [Fact]
    public void Pinned_catalog_tail_matches_final_overlay_order()
    {
        string[] catalog = PassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();
        int microBiomes = Array.IndexOf(catalog, "Micro Biomes");
        string[] expected =
        [
            "Settle Liquids Again",
            "Cactus, Palm Trees, & Coral",
            "Tile Cleanup",
            "Lihzahrd Altars",
            "Water Plants",
            "Stalac",
            "Remove Broken Traps",
            "Final Cleanup"
        ];

        Assert.True(microBiomes >= 0);
        Assert.Equal(expected, catalog.Skip(microBiomes + 1).Take(expected.Length).ToArray());
    }

    [Theory]
    [InlineData(192, 128)]
    [InlineData(4200, 1199)]
    [InlineData(4199, 1200)]
    public void Noncanonical_world_does_not_inject_final_overlay(int width, int height)
    {
        var provider = new SourceBackedFinal1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "Synthetic",
            Seed: 1458,
            WidthTiles: width,
            HeightTiles: height);
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.DoesNotContain(builder.Entries, static entry => FinalPassIds.Contains(entry.Descriptor.Id));
    }

    [Fact]
    public void Special_seed_profile_does_not_inject_ordinary_final_overlay()
    {
        var provider = new SourceBackedFinal1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "Drunk",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "05162020"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.DoesNotContain(builder.Entries, static entry => FinalPassIds.Contains(entry.Descriptor.Id));
    }

    [Fact]
    public void Pure_remix_keeps_later_overlays_on_the_compatibility_path()
    {
        var provider = new SourceBackedFinal1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "Don't Dig Up",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "don't dig up"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(10, builder.Entries.Count);
        Assert.Contains(builder.Entries, static entry => entry.Descriptor.Id.Value == "terraria:1.4.5.8/TerrainLayers");
        Assert.Contains(builder.Entries, static entry => entry.Descriptor.Id.Value == "terraria:1.4.5.8/Dunes");
        Assert.DoesNotContain(builder.Entries, static entry => FinalPassIds.Contains(entry.Descriptor.Id));
        CaptureEntry terrain = Assert.Single(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedProvider1458.TerrainPassId);
        Assert.IsType<TerrainPass1458>(terrain.Pass);
    }

    private readonly record struct CaptureEntry(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private static string HashFixture(Workspace workspace)
    {
        var snapshot = new StringBuilder(workspace.WidthTiles * workspace.HeightTiles * 20);
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            snapshot.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private static Workspace CreateFinalWorkspace()
    {
        var workspace = new Workspace(100, 100);
        Assert.True(workspace.TrySetLayers(10, 20));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        return workspace;
    }

    private static Workspace CreateStoneWorkspace(int width, int height)
    {
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            workspace.TileStore.Set(x, y, in stone);
        return workspace;
    }

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CaptureEntry> Entries { get; } = [];

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            Entries.Add(new CaptureEntry(descriptor, pass));
    }

    private sealed class Context(
        WorldGenerationRequest request, Workspace workspace, IWorldGenerationVanillaRandom random) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportProgress(double fraction, string? message = null) { }
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

    private sealed class DripRandom : IWorldGenerationVanillaRandom
    {
        public int DrawCount { get; private set; }
        public int Next() => Take(0);
        public int Next(int maxValue) => Take(maxValue == 3 ? 1 : 0);
        public int Next(int minValue, int maxValue) => Take(minValue);
        public double NextDouble() => Take(0);
        public void NextBytes(byte[] buffer) => throw new NotSupportedException();

        private int Take(int value)
        {
            DrawCount++;
            return value;
        }
    }

    private sealed class ThrowingRandom : IWorldGenerationVanillaRandom
    {
        public int Next() => throw new Xunit.Sdk.XunitException("The Lihzahrd Altar pass must not consume RNG.");
        public int Next(int maxValue) => Next();
        public int Next(int minValue, int maxValue) => Next();
        public double NextDouble() => throw new Xunit.Sdk.XunitException("The Lihzahrd Altar pass must not consume RNG.");
        public void NextBytes(byte[] buffer) => throw new Xunit.Sdk.XunitException("The Lihzahrd Altar pass must not consume RNG.");
    }
}
