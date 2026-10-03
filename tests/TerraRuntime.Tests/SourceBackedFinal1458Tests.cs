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
