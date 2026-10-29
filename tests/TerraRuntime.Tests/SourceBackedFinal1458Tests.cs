using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;
using System.Security.Cryptography;
using System.Text;

namespace TerraRuntime.Tests;

public sealed class SourceBackedFinal1458Tests
{
    [Theory]
    [InlineData(WorldLiquidKind.Water)]
    [InlineData(WorldLiquidKind.Lava)]
    [InlineData(WorldLiquidKind.Honey)]
    [InlineData(WorldLiquidKind.Shimmer)]
    public void Final_cleanup_rail_retains_source_fifteen_cell_clear_and_earlier_liquid_tags(WorldLiquidKind kind)
    {
        // Actual original FinalCleanup on 600x500 stone, rail at300/200: 186..200 clear,
        // 184/185/201 remain full. Earlier visited cleared cells retain their liquid-type bits.
        var workspace = CreateStoneWorkspace(600, 500);
        for (int y = 184; y <= 201; y++)
        {
            ref var tile = ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(300, y)];
            tile.LiquidAmount = 255; tile.LiquidKind = kind;
            if (y == 200) { tile.Type = 314; tile.FrameX = 0; tile.FrameY = -1; }
        }
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 600, 500),
                workspace, new RandomAdapter(1458)));
        for (int y = 184; y <= 201; y++)
        {
            var tile = workspace.TileStore.Get(300, y);
            Assert.Equal(y is >= 186 and <= 200 ? (byte)0 : byte.MaxValue, tile.LiquidAmount);
            // Current-cell zero-tag normalization is a separate retained generator convention.
            if (y != 200) Assert.Equal(kind, tile.LiquidKind);
        }
    }

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
            water.LiquidAmount = byte.MaxValue;
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
        // The type-314 loop clears its own row and fourteen above; the subsequent liquid repair
        // fills the untouched row above that span to a full cell.
        Assert.Equal(byte.MaxValue, workspace.TileStore.Get(700, 85).LiquidAmount);
        for (int y = 86; y <= 100; y++) Assert.Equal((byte)0, workspace.TileStore.Get(700, y).LiquidAmount);
        Assert.Equal(byte.MaxValue, workspace.TileStore.Get(700, 101).LiquidAmount);
        WorldTile support = workspace.TileStore.Get(800, 101);
        Assert.True(support.IsActive);
        Assert.Equal((ushort)1, support.Type);
        Assert.Equal((byte)0, support.LiquidAmount);
    }

    [Fact]
    public void Final_cleanup_rail_in_lookahead_stops_loose_surface_extension_before_first_cell()
    {
        // WorldGen.FinalCleanup (1.4.5.8) scans ten cells ahead for type 314 before its
        // descending sand brush. A rail five rows down still suppresses the first extension.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        var sand = new WorldTile { Type = 53, Flags = WorldTileFlags.Active };
        var rail = new WorldTile { Type = 314, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(500, 100, in sand);
        workspace.TileStore.Set(500, 105, in rail);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        // Direct registered TerrariaServer 1.4.5.8 PassLegacy on the identical 1000x500 grid.
        Assert.Equal("3A8F2A2E4BDA0E36CC4FF5ECC7D43147ECF4D4098F5556ACB648F8F51DF7B32A", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        Assert.Equal((ushort)53, workspace.TileStore.Get(500, 100).Type);
        Assert.Equal((ushort)397, workspace.TileStore.Get(500, 101).Type);
        Assert.True(workspace.TileStore.Get(500, 101).IsActive);
        for (int y = 102; y <= 104; y++)
            Assert.False(workspace.TileStore.Get(500, y).IsActive);
        Assert.Equal((ushort)314, workspace.TileStore.Get(500, 105).Type);
    }

    [Fact]
    public void Final_cleanup_ordinary_torch_tileframe_matches_official_passlegacy_grid_and_rng()
    {
        // Registered original FinalCleanup PassLegacy, seed 1458, 1000x500. CheckTorch
        // resolves floor, left, right, wall, unsupported and floor-over-side priorities.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        void SetTile(int x, int y, ushort type, short frameX = 0, ushort wall = 0)
        {
            var tile = new WorldTile { Type = type, Wall = wall, FrameX = frameX,
                FrameY = type == 4 ? (short)154 : (short)0, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in tile);
        }

        SetTile(500, 100, 4);
        SetTile(500, 101, 1);
        SetTile(511, 100, 4);
        SetTile(510, 100, 1);
        SetTile(520, 100, 4);
        SetTile(521, 100, 1);
        SetTile(530, 100, 4, frameX: 66, wall: 7);
        SetTile(540, 100, 4);
        SetTile(550, 100, 4);
        SetTile(550, 101, 1);
        SetTile(549, 100, 1);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("0547C9ED463256E5A5622EE27021741B9E189B32BCB1706EFE2E669FE9A3C234", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        Assert.Equal((short)0, workspace.TileStore.Get(500, 100).FrameX);
        Assert.Equal((short)22, workspace.TileStore.Get(511, 100).FrameX);
        Assert.Equal((short)44, workspace.TileStore.Get(520, 100).FrameX);
        Assert.Equal((short)66, workspace.TileStore.Get(530, 100).FrameX);
        Assert.False(workspace.TileStore.Get(540, 100).IsActive);
        Assert.Equal((short)0, workspace.TileStore.Get(550, 100).FrameX);
    }

    [Fact]
    public void Final_cleanup_drip_tileframe_matches_all_five_official_identities()
    {
        // TileID.Sets.IsADripTile in the pinned 1.4.5.8 server is exactly
        // 373, 374, 375, 461, 709. The registered FinalCleanup PassLegacy
        // keeps each under stone and kills each unattached peer.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        ushort[] dripTypes = [373, 374, 375, 461, 709];
        for (int index = 0; index < dripTypes.Length; index++)
        {
            int x = 500 + index * 20;
            var drip = new WorldTile { Type = dripTypes[index], Flags = WorldTileFlags.Active };
            var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, 100, in drip);
            workspace.TileStore.Set(x, 99, in stone);
            workspace.TileStore.Set(x + 10, 100, in drip);
        }

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("2C74BF1F65E71D4A4225087F19ACE93F7A3140FD71D2D05CD1358247E67B10C2", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        for (int index = 0; index < dripTypes.Length; index++)
        {
            int x = 500 + index * 20;
            Assert.Equal(dripTypes[index], workspace.TileStore.Get(x, 100).Type);
            Assert.False(workspace.TileStore.Get(x + 10, 100).IsActive);
            Assert.Equal((short)-1, workspace.TileStore.Get(x + 10, 100).FrameX);
        }
    }

    [Fact]
    public void Final_cleanup_dart_trap_tileframe_matches_official_attachment_priority()
    {
        // Registered TerrariaServer 1.4.5.8 FinalCleanup PassLegacy: floor,
        // left, right, wall, unsupported and competing floor/left supports.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        foreach (int x in new[] { 500, 510, 520, 530, 540, 550 })
        {
            var trap = new WorldTile { Type = 136, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, 100, in trap);
        }
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(500, 101, in stone);
        workspace.TileStore.Set(509, 100, in stone);
        workspace.TileStore.Set(521, 100, in stone);
        workspace.TileStore.Set(550, 101, in stone);
        workspace.TileStore.Set(549, 100, in stone);
        var walled = workspace.TileStore.Get(530, 100);
        walled.Wall = 7;
        workspace.TileStore.Set(530, 100, in walled);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("9667408382F4F095F7E4EA0BA291875AC952C699A592DBD36AFC93B4F70FFB0B", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        Assert.Equal((short)0, workspace.TileStore.Get(500, 100).FrameX);
        Assert.Equal((short)18, workspace.TileStore.Get(510, 100).FrameX);
        Assert.Equal((short)36, workspace.TileStore.Get(520, 100).FrameX);
        Assert.Equal((short)54, workspace.TileStore.Get(530, 100).FrameX);
        Assert.False(workspace.TileStore.Get(540, 100).IsActive);
        Assert.Equal((short)0, workspace.TileStore.Get(550, 100).FrameX);
    }

    [Fact]
    public void Final_cleanup_multidirectional_switches_match_official_attachment_priority()
    {
        // Registered official PassLegacy: both frame-important identities keep
        // below/left/right/above priority and kill their unsupported peers.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        ushort[] types = [129, 149];
        for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
        {
            int start = 500 + typeIndex * 100;
            for (int caseIndex = 0; caseIndex < 6; caseIndex++)
            {
                var item = new WorldTile { Type = types[typeIndex], Flags = WorldTileFlags.Active, FrameY = 72 };
                workspace.TileStore.Set(start + caseIndex * 10, 100, in item);
            }
            var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(start, 101, in stone);
            workspace.TileStore.Set(start + 9, 100, in stone);
            workspace.TileStore.Set(start + 21, 100, in stone);
            workspace.TileStore.Set(start + 30, 99, in stone);
            workspace.TileStore.Set(start + 50, 101, in stone);
            workspace.TileStore.Set(start + 49, 100, in stone);
        }

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("33606BEB599BD8BA69AE55DE007A7686E030AA1B476F272E2B67D7E47A0C98BF", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        foreach (int start in new[] { 500, 600 })
        {
            Assert.Equal((short)0, workspace.TileStore.Get(start, 100).FrameY);
            Assert.Equal((short)54, workspace.TileStore.Get(start + 10, 100).FrameY);
            Assert.Equal((short)36, workspace.TileStore.Get(start + 20, 100).FrameY);
            Assert.Equal((short)18, workspace.TileStore.Get(start + 30, 100).FrameY);
            Assert.False(workspace.TileStore.Get(start + 40, 100).IsActive);
            Assert.Equal((short)0, workspace.TileStore.Get(start + 50, 100).FrameY);
        }
    }

    [Fact]
    public void Final_cleanup_dye_and_rock_golem_head_match_official_support_checks()
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        foreach (int x in new[] { 500, 510, 520, 530, 540, 550 })
        {
            var dye = new WorldTile { Type = 227, Flags = WorldTileFlags.Active,
                FrameX = (short)(x is 510 or 540 ? 238 : x is 520 or 550 ? 204 : 0) };
            workspace.TileStore.Set(x, 100, in dye);
        }
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(500, 101, in stone);
        workspace.TileStore.Set(510, 99, in stone);
        var head = new WorldTile { Type = 579, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(560, 100, in head);
        workspace.TileStore.Set(560, 101, in stone);
        workspace.TileStore.Set(570, 100, in head);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("77F73ED87EFD803542E97F8F01D398BBAE324BECFA70B73B1649B9916DFD0549", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        Assert.True(workspace.TileStore.Get(500, 100).IsActive);
        Assert.True(workspace.TileStore.Get(510, 100).IsActive);
        Assert.True(workspace.TileStore.Get(560, 100).IsActive);
        foreach (int x in new[] { 520, 530, 540, 550, 570 })
            Assert.False(workspace.TileStore.Get(x, 100).IsActive);
    }

    [Fact]
    public void Final_cleanup_platform_top_slope_support_uses_official_frame_bands()
    {
        // Source SolidTileAllowBottomSlope admits top-sloped platforms only
        // when PlatformProperTopFrame accepts their horizontal frame.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        for (int site = 0; site < 5; site++)
        {
            int x = 500 + site * 10;
            var objectTile = new WorldTile
            {
                Type = site is 2 or 3 ? (ushort)324 : (ushort)579,
                Flags = WorldTileFlags.Active
            };
            var platform = new WorldTile
            {
                Type = 19,
                Flags = WorldTileFlags.Active,
                FrameX = site is 1 or 3 ? (short)162 : (short)0,
                Shape = site == 4 ? (byte)0 : (byte)2
            };
            workspace.TileStore.Set(x, 100, in objectTile);
            workspace.TileStore.Set(x, 101, in platform);
        }

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("D8C861CB22E42C86E6673C514F90417202469C9421EF1C146D80938240B07D1D", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        foreach (int x in new[] { 500, 520, 540 })
            Assert.True(workspace.TileStore.Get(x, 100).IsActive);
        foreach (int x in new[] { 510, 530 })
            Assert.False(workspace.TileStore.Get(x, 100).IsActive);
    }

    [Fact]
    public void Final_cleanup_dye_on_rooted_cactus_survives_but_unrooted_peer_dies()
    {
        // Source CheckDye style 6 is positive only while CheckCactus can trace
        // type 80 to a sand-family root; losing the cactus re-frames the dye.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        var dye = new WorldTile { Type = 227, Flags = WorldTileFlags.Active, FrameX = 204 };
        var cactus = new WorldTile { Type = 80, Flags = WorldTileFlags.Active };
        var sand = new WorldTile { Type = 53, Flags = WorldTileFlags.Active };
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(500, 100, in dye);
        workspace.TileStore.Set(500, 101, in cactus);
        workspace.TileStore.Set(500, 102, in sand);
        workspace.TileStore.Set(500, 103, in stone);
        workspace.TileStore.Set(520, 100, in dye);
        workspace.TileStore.Set(520, 101, in cactus);
        workspace.TileStore.Set(600, 99, in dye);
        workspace.TileStore.Set(600, 100, in cactus);
        workspace.TileStore.Set(601, 100, in cactus);
        workspace.TileStore.Set(601, 101, in cactus);
        workspace.TileStore.Set(601, 102, in sand);
        workspace.TileStore.Set(601, 103, in stone);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("49CB8669B20A46219A6783E7AD5DBECCA2DB42D14D15D07FCB741D39D4A70330", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        Assert.True(workspace.TileStore.Get(500, 100).IsActive);
        Assert.True(workspace.TileStore.Get(500, 101).IsActive);
        Assert.False(workspace.TileStore.Get(520, 100).IsActive);
        Assert.False(workspace.TileStore.Get(520, 101).IsActive);
        Assert.True(workspace.TileStore.Get(600, 99).IsActive);
        Assert.True(workspace.TileStore.Get(600, 100).IsActive);
        Assert.True(workspace.TileStore.Get(601, 100).IsActive);
    }

    [Fact]
    public void Final_cleanup_orb_family_matches_official_intact_and_broken_objects()
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        ushort[] types = [12, 31, 639, 696];
        for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
        for (int broken = 0; broken < 2; broken++)
        {
            int left = 500 + typeIndex * 40 + broken * 20;
            ushort type = types[typeIndex];
            for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                if (broken != 0 && dx == 1 && dy == 1)
                    continue;
                var orb = new WorldTile
                {
                    Type = type,
                    Flags = WorldTileFlags.Active,
                    FrameX = (short)(dx * 18),
                    FrameY = (short)(dy * 18)
                };
                workspace.TileStore.Set(left + dx, 100 + dy, in orb);
            }
            if (type is 12 or 639)
            {
                var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(left, 102, in stone);
                workspace.TileStore.Set(left + 1, 102, in stone);
            }
        }
        foreach (int left in new[] { 660, 680 })
        {
            ushort type = left == 660 ? (ushort)12 : (ushort)639;
            for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                var orb = new WorldTile
                {
                    Type = type,
                    Flags = WorldTileFlags.Active,
                    FrameX = (short)(dx * 18),
                    FrameY = (short)(dy * 18)
                };
                workspace.TileStore.Set(left + dx, 100 + dy, in orb);
            }
        }

        Assert.True(workspace.TileStore.Get(520, 101).IsActive);
        Assert.Equal((ushort)12, workspace.TileStore.Get(520, 101).Type);
        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("D55B089E908603B2563CB2DE137B6B796FA801F8A76997D2AA63FA0E9D58F214", HashFixture(workspace));
        Assert.Equal(1839885534, random.Next());
        for (int index = 0; index < types.Length; index++)
        {
            int intact = 500 + index * 40;
            int broken = intact + 20;
            Assert.Equal(types[index], workspace.TileStore.Get(intact, 100).Type);
            Assert.Equal(types[index], workspace.TileStore.Get(intact + 1, 101).Type);
            Assert.False(workspace.TileStore.Get(broken, 100).IsActive);
            Assert.False(workspace.TileStore.Get(broken + 1, 100).IsActive);
            Assert.False(workspace.TileStore.Get(broken, 101).IsActive);
        }
        foreach (int left in new[] { 660, 680 })
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile removed = workspace.TileStore.Get(left + dx, 100 + dy);
            Assert.False(removed.IsActive);
            Assert.Equal((short)-1, removed.FrameX);
            Assert.Equal((short)-1, removed.FrameY);
        }
    }

    [Fact]
    public void Final_cleanup_echo_stalactites_match_official_attachment_and_footprint()
    {
        // Registered TerrariaServer 1.4.5.8 FinalCleanup PassLegacy, seed 1458.
        // Both echo sizes are tested hanging and standing, with and without support.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        for (int echoIndex = 0; echoIndex < 8; echoIndex++)
        {
            int x = 500 + echoIndex * 20;
            ushort type = echoIndex < 4 ? (ushort)693 : (ushort)694;
            int length = type == 694 ? 2 : 1;
            bool hanging = echoIndex % 4 < 2;
            bool supported = echoIndex % 2 == 0;
            int frameBand = hanging ? 0 : length * 18;
            for (int dy = 0; dy < length; dy++)
            {
                var echo = new WorldTile { Type = type, Flags = WorldTileFlags.Active,
                    FrameY = (short)(frameBand + dy * 18) };
                workspace.TileStore.Set(x, 100 + dy, in echo);
            }
            if (supported)
            {
                var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(x, hanging ? 99 : 100 + length, in stone);
            }
        }

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("AF940B01F97B1A61F44F8F119980BDFFA978A946F25F92624263071A86E70B87", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        for (int echoIndex = 0; echoIndex < 8; echoIndex++)
        {
            int x = 500 + echoIndex * 20;
            ushort type = echoIndex < 4 ? (ushort)693 : (ushort)694;
            int length = type == 694 ? 2 : 1;
            bool supported = echoIndex % 2 == 0;
            for (int dy = 0; dy < length; dy++)
            {
                WorldTile tile = workspace.TileStore.Get(x, 100 + dy);
                Assert.Equal(supported, tile.IsActive);
                if (supported) Assert.Equal(type, tile.Type);
                else Assert.Equal((short)-1, tile.FrameY);
            }
        }
    }

    [Fact]
    public void Final_cleanup_seaweed_matches_official_water_support_wall_and_style_rules()
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        foreach (int x in new[] { 500, 520, 540, 560 })
        for (int y = 298; y <= 301; y++)
        {
            var water = new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Water };
            workspace.TileStore.Set(x, y, in water);
        }
        void Set(int x, int y, ushort type, short frameX = 0, ushort wall = 0)
        {
            var tile = workspace.TileStore.Get(x, y);
            tile.Type = type; tile.Flags |= WorldTileFlags.Active; tile.FrameX = frameX; tile.Wall = wall;
            tile.LiquidAmount = type == 549 ? (byte)255 : (byte)0;
            workspace.TileStore.Set(x, y, in tile);
        }
        Set(500, 300, 549, frameX: 36); Set(500, 301, 53); Set(500, 302, 1);
        Set(520, 300, 549); Set(520, 301, 549); Set(520, 302, 53); Set(520, 303, 1);
        Set(540, 300, 549);
        Set(560, 300, 549, wall: 7); Set(560, 301, 53); Set(560, 302, 1);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("3CF7AB56C62FEE5051DD04CAADD3A912EB962E6FC3A5F299F96636350CB72DF4", HashFixture(workspace));
        Assert.Equal(147300460, random.Next());
        Assert.Equal((short)0, workspace.TileStore.Get(500, 300).FrameX);
        Assert.Equal((short)162, workspace.TileStore.Get(520, 300).FrameX);
        Assert.Equal((short)90, workspace.TileStore.Get(520, 301).FrameX);
        Assert.False(workspace.TileStore.Get(540, 300).IsActive);
        Assert.False(workspace.TileStore.Get(560, 300).IsActive);
    }

    [Fact]
    public void Final_cleanup_projectile_pressure_pad_matches_official_direction_retention_and_priority()
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        foreach (int x in new[] { 500, 520, 540, 560, 580, 600, 620 })
        {
            var pad = new WorldTile { Type = 442, Flags = WorldTileFlags.Active,
                FrameX = (short)(x switch { 500 or 520 => 0, 540 => 22, 560 or 600 => 44, _ => 66 }) };
            workspace.TileStore.Set(x, 100, in pad);
        }
        foreach ((int x, int y) in new[]
        {
            (500,101), (520,99), (539,100), (561,100),
            (599,100), (600,101), (619,100), (620,101)
        })
        {
            var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in stone);
        }
        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("7B18BFECAA4E35DA5B30C367760BEDD10DF3450396C1FA2AED351C3E7E417E66", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        Assert.Equal((short)0, workspace.TileStore.Get(500, 100).FrameX);
        Assert.Equal((short)22, workspace.TileStore.Get(520, 100).FrameX);
        Assert.Equal((short)44, workspace.TileStore.Get(540, 100).FrameX);
        Assert.Equal((short)66, workspace.TileStore.Get(560, 100).FrameX);
        Assert.False(workspace.TileStore.Get(580, 100).IsActive);
        Assert.Equal((short)44, workspace.TileStore.Get(600, 100).FrameX);
        Assert.Equal((short)0, workspace.TileStore.Get(620, 100).FrameX);
    }

    [Fact]
    public void Final_cleanup_bamboo_matches_official_support_and_neighbor_style_bands()
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        void Set(int x, int y, ushort type, short frameX = 0, short frameY = 0)
        {
            var tile = new WorldTile { Type = type, Flags = WorldTileFlags.Active,
                FrameX = frameX, FrameY = frameY };
            workspace.TileStore.Set(x, y, in tile);
        }
        Set(500, 300, 571, frameX: 72, frameY: 36); Set(500, 301, 60);
        for (int y = 298; y <= 300; y++) Set(520, y, 571, frameY: 18);
        Set(520, 301, 60);
        Set(540, 300, 571);
        Set(560, 299, 571, frameX: 270); Set(560, 300, 571, frameX: 36); Set(560, 301, 60);
        Set(580, 298, 571); Set(580, 299, 571, frameX: 90); Set(580, 300, 571); Set(580, 301, 60);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("30D596660465534D0BA0452D69BC0D9A964FFA44A77C65BAA279007EACDD6DA1", HashFixture(workspace));
        Assert.Equal(1461024880, random.Next());
        Assert.Equal((short)0, workspace.TileStore.Get(500, 300).FrameX);
        Assert.Equal((short)306, workspace.TileStore.Get(520, 298).FrameX);
        Assert.Equal((short)198, workspace.TileStore.Get(520, 299).FrameX);
        Assert.Equal((short)72, workspace.TileStore.Get(520, 300).FrameX);
        Assert.False(workspace.TileStore.Get(540, 300).IsActive);
        Assert.Equal((short)270, workspace.TileStore.Get(560, 299).FrameX);
        Assert.Equal((short)36, workspace.TileStore.Get(560, 300).FrameX);
        Assert.Equal((short)90, workspace.TileStore.Get(580, 299).FrameX);
    }

    [Fact]
    public void Final_cleanup_lily_pad_matches_official_generation_surface_motion()
    {
        // Original PassLegacy in generation netMode=0; client mode skips CheckLilyPad and
        // dedicated-server mode introduces networking that is absent during generation.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        foreach (int x in new[] { 500, 520, 540, 560, 580 })
        {
            var pad = new WorldTile { Type = 518, Flags = WorldTileFlags.Active, FrameX = 18 };
            workspace.TileStore.Set(x, 300, in pad);
        }
        void Set(int x, int y, ushort type, bool active = true, byte liquid = 0)
        {
            var tile = new WorldTile { Type = type,
                Flags = active ? WorldTileFlags.Active : WorldTileFlags.None, LiquidAmount = liquid };
            workspace.TileStore.Set(x, y, in tile);
        }
        Set(500, 301, 0, active: false, liquid: 255); Set(500, 305, 2);
        Set(520, 299, 0, active: false, liquid: 255); Set(520, 305, 2);
        Set(540, 305, 1);
        Set(560, 305, 60);
        var submerged = workspace.TileStore.Get(580, 300);
        submerged.LiquidAmount = 255;
        workspace.TileStore.Set(580, 300, in submerged);
        Set(580, 305, 2);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("1E894D45285F7C0B921A3DFE1A664DC3D6DD9E5BC7C5DDC177743669C48D5034", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        Assert.Equal(261937618, random.Next());
        Assert.False(workspace.TileStore.Get(500, 300).IsActive);
        Assert.Equal((ushort)518, workspace.TileStore.Get(500, 301).Type);
        Assert.Equal((ushort)518, workspace.TileStore.Get(520, 299).Type);
        Assert.False(workspace.TileStore.Get(540, 300).IsActive);
        Assert.Equal((short)36, workspace.TileStore.Get(560, 304).FrameY);
        Assert.Equal((ushort)518, workspace.TileStore.Get(580, 300).Type);
    }

    [Fact]
    public void Final_cleanup_cat_tail_matches_official_water_floor_and_stalk_validation()
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        foreach (int x in new[] { 500, 520, 540, 560 })
        {
            for (int y = 300; y <= 302; y++)
            {
                var stalk = new WorldTile { Type = 519, Flags = WorldTileFlags.Active,
                    LiquidAmount = (byte)(x == 540 && y == 302 ? 100 : 255) };
                workspace.TileStore.Set(x, y, in stalk);
            }
            var floor = new WorldTile { Type = x == 520 ? (ushort)1 : x == 560 ? (ushort)23 : (ushort)2,
                Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, 303, in floor);
        }

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("DAC08B94F17FBA63AB94C95DFC24D55C4AA78DDCA013EDF0CD1C6ED6FA8ECE54", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        for (int y = 300; y <= 302; y++)
        {
            Assert.Equal((ushort)519, workspace.TileStore.Get(500, y).Type);
            Assert.False(workspace.TileStore.Get(520, y).IsActive);
            Assert.False(workspace.TileStore.Get(540, y).IsActive);
            Assert.Equal((short)72, workspace.TileStore.Get(560, y).FrameY);
        }
    }

    [Fact]
    public void Final_cleanup_stalactite_matches_official_support_footprint_and_restyle()
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        void Set(int x, int y, ushort type, short frameX = 0, short frameY = 0)
        {
            var tile = new WorldTile { Type = type, Flags = WorldTileFlags.Active,
                FrameX = frameX, FrameY = frameY };
            workspace.TileStore.Set(x, y, in tile);
        }

        Set(500, 99, 1); Set(500, 100, 165, 54, 72);
        Set(520, 100, 165, 54, 90); Set(520, 101, 1);
        Set(540, 99, 1); Set(540, 100, 165, 54); Set(540, 101, 165, 54, 18);
        Set(560, 100, 165, 54, 36); Set(560, 101, 165, 54, 54); Set(560, 102, 1);
        Set(580, 100, 165, 54, 72);
        Set(600, 99, 147); Set(600, 100, 165, 54, 72);
        Set(620, 99, 1); Set(620, 100, 165, 54);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("36F4F7E2A11459A9177E2FFC20BB515644D7450DB2026B08872A6BFA7963A443", HashFixture(workspace));
        Assert.Equal(261937618, random.Next());
        Assert.Equal(147300460, random.Next());
        Assert.False(workspace.TileStore.Get(580, 100).IsActive);
        Assert.Equal((short)18, workspace.TileStore.Get(600, 100).FrameX);
        Assert.False(workspace.TileStore.Get(620, 100).IsActive);
    }

    [Fact]
    public void Final_cleanup_directional_decoration_keeps_source_style_with_anchor_priority()
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        void Set(int x, int y, ushort type, short frameY = 0)
        {
            var tile = new WorldTile { Type = type, Flags = WorldTileFlags.Active, FrameY = frameY };
            workspace.TileStore.Set(x, y, in tile);
        }
        for (int caseIndex = 0; caseIndex < 6; caseIndex++)
            Set(500 + caseIndex * 20, 100, 178, caseIndex == 4 ? (short)72 : (short)18);
        Set(500, 101, 1);
        Set(519, 100, 1);
        Set(541, 100, 1);
        Set(560, 99, 1);
        Set(580, 101, 1);
        Set(599, 100, 1);
        Set(620, 100, 178, 18);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("C46BFD206579D1A5C0412063A000603F4E1B657F3769880767AAD9FB28AE97BE", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        Assert.Equal((short)126, workspace.TileStore.Get(520, 100).FrameY);
        Assert.Equal((short)180, workspace.TileStore.Get(540, 100).FrameY);
        Assert.Equal((short)72, workspace.TileStore.Get(560, 100).FrameY);
        Assert.Equal((short)18, workspace.TileStore.Get(580, 100).FrameY);
        Assert.False(workspace.TileStore.Get(620, 100).IsActive);
    }

    [Fact]
    public void Final_cleanup_three_by_one_type235_matches_official_footprint_cleanup()
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        void Set(int x, int y, ushort type, bool active = true, short frameX = 0)
        {
            var tile = new WorldTile { Type = type,
                Flags = active ? WorldTileFlags.Active : WorldTileFlags.None, FrameX = frameX };
            workspace.TileStore.Set(x, y, in tile);
        }
        foreach (int left in new[] { 500, 540, 580 })
        {
            for (int dx = 0; dx < 3; dx++)
                Set(left + dx, 100, 235, frameX: (short)(dx * 18));
            for (int dx = 0; dx < 3; dx++)
                Set(left + dx, 101, 1);
        }
        Set(541, 101, 0, active: false);
        Set(581, 100, 0, active: false);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("030CDC39A9040158B123EFFCC68818D8F3F8B290C5A798091FE68ED9A3565881", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        Assert.True(workspace.TileStore.Get(500, 100).IsActive);
        Assert.False(workspace.TileStore.Get(540, 100).IsActive);
        Assert.False(workspace.TileStore.Get(582, 100).IsActive);
    }

    [Fact]
    public void Final_cleanup_two_by_one_frame_important_objects_match_official_passlegacy()
    {
        // Registered TerrariaServer 1.4.5.8 FinalCleanup PassLegacy, 1000x500, seed1458.
        // All seven Check2x1 identities cover intact pairs, missing members, bad frame spacing,
        // absent ordinary support, table-only support, and type103's frame-qualified
        // top-sloped platform anchor. The source full-grid six-field
        // digest and next RNG are pinned.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        foreach ((int left, ushort type, short frameX, bool rightMember, bool rightSupport) in new[]
        {
            (500, (ushort)16, (short)0, true, true), (540, (ushort)16, (short)0, true, false),
            (580, (ushort)16, (short)0, false, true), (620, (ushort)18, (short)360, true, true),
            (660, (ushort)18, (short)360, true, false), (700, (ushort)134, (short)0, true, true),
            (740, (ushort)649, (short)0, true, true), (780, (ushort)649, (short)0, true, false)
        })
        {
            var first = new WorldTile { Type = type, FrameX = frameX, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(left, 100, in first);
            if (rightMember)
            {
                var second = new WorldTile { Type = type,
                    FrameX = (short)(frameX + (left == 700 ? 36 : 18)), Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(left + 1, 100, in second);
            }
            var support = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(left, 101, in support);
            if (rightSupport) workspace.TileStore.Set(left + 1, 101, in support);
        }
        foreach ((int left, ushort type, bool tableSupport) in new[]
            { (820, (ushort)29, true), (840, (ushort)29, false),
              (860, (ushort)462, true), (880, (ushort)462, false) })
        {
            var first = new WorldTile { Type = type, Flags = WorldTileFlags.Active };
            var second = new WorldTile { Type = type, FrameX = 18, Flags = WorldTileFlags.Active };
            var support = new WorldTile { Type = tableSupport ? (ushort)19 : (ushort)1,
                Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(left, 100, in first);
            workspace.TileStore.Set(left + 1, 100, in second);
            workspace.TileStore.Set(left, 101, in support);
            workspace.TileStore.Set(left + 1, 101, in support);
        }
        var neighbourPlatform = new WorldTile { Type = 19, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(939, 101, in neighbourPlatform);
        foreach ((int left, short platformFrame) in new[]
            { (900, (short)0), (920, (short)144), (940, (short)450), (960, (short)144) })
        {
            var first = new WorldTile { Type = 103, Flags = WorldTileFlags.Active };
            var second = new WorldTile { Type = 103, FrameX = 18, Flags = WorldTileFlags.Active };
            var leftSupport = new WorldTile { Type = 19, FrameX = platformFrame,
                Shape = left >= 940 ? (byte)3 : (byte)2, Flags = WorldTileFlags.Active };
            var rightSupport = new WorldTile { Type = 19, FrameX = platformFrame,
                Shape = left is 900 or 940 ? (byte)0 : left >= 940 ? (byte)3 : (byte)2,
                Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(left, 100, in first);
            workspace.TileStore.Set(left + 1, 100, in second);
            workspace.TileStore.Set(left, 101, in leftSupport);
            workspace.TileStore.Set(left + 1, 101, in rightSupport);
        }

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("54540A1918E846F23F2328043DCC0F8F73B1D92764254CC8E5278948C79503ED", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        Assert.True(workspace.TileStore.Get(900, 100).IsActive);
        Assert.False(workspace.TileStore.Get(920, 100).IsActive);
        Assert.True(workspace.TileStore.Get(940, 100).IsActive);
        Assert.False(workspace.TileStore.Get(960, 100).IsActive);
    }

    [Fact]
    public void Final_cleanup_basic_chests_match_official_registered_passlegacy()
    {
        // Independent TerrariaServer 1.4.5.8 registered FinalCleanup PassLegacy, 1000x500,
        // seed1458: intact and unsupported/missing-member 21/467 objects. All six fields of
        // every cell and the next shared RNG draw are pinned, not just the local object footprint.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        foreach ((int left, ushort type, short baseFrame, bool complete, bool support) in new[]
        {
            (500, (ushort)21, (short)0, true, true), (540, (ushort)21, (short)36, true, false),
            (580, (ushort)21, (short)72, false, true), (620, (ushort)467, (short)0, true, true),
            (660, (ushort)467, (short)36, true, false), (700, (ushort)467, (short)72, false, true)
        })
        {
            for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                if (!complete && dx == 1 && dy == 1)
                    continue;
                var part = new WorldTile { Type = type, FrameX = (short)(baseFrame + dx * 18),
                    FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(left + dx, 100 + dy, in part);
            }
            var floor = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            for (int dx = 0; dx < (support ? 2 : 1); dx++)
                workspace.TileStore.Set(left + dx, 102, in floor);
        }

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("FD1B611258105C3069982838AD3F9621F2E79A1A973FDFF2F9FA39EE418C07ED", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        Assert.True(workspace.TileStore.Get(500, 100).IsActive);
        Assert.True(workspace.TileStore.Get(620, 100).IsActive);
        foreach (int left in new[] { 540, 580, 660, 700 })
            Assert.False(workspace.TileStore.Get(left, 100).IsActive);
    }

    [Fact]
    public void Final_cleanup_removes_only_empty_chest_metadata_and_rebases_remaining_slots()
    {
        var workspace = new Workspace(600, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        foreach (int left in new[] { 200, 240, 280 })
        {
            for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                var part = new WorldTile { Type = 21, FrameX = (short)(dx * 18),
                    FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(left + dx, 100 + dy, in part);
            }
            var floor = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(left, 102, in floor);
            workspace.TileStore.Set(left + 1, 102, in floor);
        }
        Assert.True(workspace.TryAddGeneratedChest(200, 100, "", []));
        Assert.True(workspace.TryAddGeneratedChest(240, 100, "", [new WorldChestItem(1, 1, 0)]));
        Assert.True(workspace.TryAddGeneratedChest(280, 100, "", []));
        WorldTile absent = default;
        workspace.TileStore.Set(201, 102, in absent);
        workspace.TileStore.Set(241, 102, in absent);

        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 600, 500),
                workspace, new RandomAdapter(1458)));

        Assert.False(workspace.TileStore.Get(200, 100).IsActive);
        Assert.True(workspace.TileStore.Get(240, 100).IsActive);
        Assert.True(workspace.TileStore.Get(280, 100).IsActive);
        WorldChest[] chests = workspace.CaptureGeneratedChests();
        Assert.Equal(new[] { (240, (short)0), (280, (short)1) },
            chests.Select(chest => (chest.X, chest.SlotId)));
        Assert.Equal(1, chests[0].Items[0].Stack);
    }

    [Fact]
    public void Final_cleanup_one_by_one_golf_and_logic_families_match_official_passlegacy()
    {
        // Registered TerrariaServer 1.4.5.8 FinalCleanup, seed1458, all 20 identities
        // dispatched by Check1x1/CheckGolf1x1/CheckLogicTiles. The oracle covers every
        // cell and the next shared RNG state, including Abigail's special grass gate.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        ushort[] ordinary = [36, 135, 141, 210, 239, 428, 593, 624, 650, 656, 700, 701];
        for (int n = 0; n < ordinary.Length; n++)
        {
            int x = 100 + n * 25;
            var objectTile = new WorldTile { Type = ordinary[n], Flags = WorldTileFlags.Active };
            var floor = new WorldTile { Type = ordinary[n] == 624 ? (ushort)2 : (ushort)1,
                Flags = WorldTileFlags.Active };
            var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, 100, in objectTile);
            workspace.TileStore.Set(x, 101, in floor);
            workspace.TileStore.Set(x, 130, in objectTile);
            workspace.TileStore.Set(x, 160, in objectTile);
            workspace.TileStore.Set(x, 161, in stone);
        }
        foreach ((int x, ushort type) in new[] { (450, (ushort)476), (500, (ushort)494) })
        {
            var valid = new WorldTile { Type = type, Flags = WorldTileFlags.Active };
            var badX = valid with { FrameX = 1 };
            var badY = valid with { FrameY = 1 };
            var floor = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, 100, in valid);
            workspace.TileStore.Set(x, 101, in floor);
            workspace.TileStore.Set(x + 1, 100, in badX);
            workspace.TileStore.Set(x + 1, 101, in floor);
            workspace.TileStore.Set(x + 2, 100, in badY);
            workspace.TileStore.Set(x + 2, 101, in floor);
            workspace.TileStore.Set(x + 3, 100, in valid);
        }
        foreach ((int x, ushort type) in new[]
            { (600, (ushort)419), (650, (ushort)420), (700, (ushort)423),
              (750, (ushort)424), (800, (ushort)429), (850, (ushort)445) })
        {
            var valid = new WorldTile { Type = type, Flags = WorldTileFlags.Active };
            var badX = valid with { FrameX = 1 };
            var badY = valid with { FrameY = 1 };
            workspace.TileStore.Set(x, 100, in valid);
            workspace.TileStore.Set(x + 1, 100, in badX);
            workspace.TileStore.Set(x + 2, 100, in badY);
            if (type == 419)
            {
                var chain = new WorldTile { Type = 420, Flags = WorldTileFlags.Active };
                for (int dx = 0; dx < 3; dx++)
                    workspace.TileStore.Set(x + dx, 101, in chain);
            }
        }
        var unsupportedChain = new WorldTile { Type = 419, Flags = WorldTileFlags.Active };
        var stoneSupport = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(625, 100, in unsupportedChain);
        workspace.TileStore.Set(625, 101, in stoneSupport);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("B8119A7EA9BA6C43D5A10C2CC1C3F55D916B1035D5CB3E77DB92329E255552E0", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        Assert.True(workspace.TileStore.Get(275, 100).IsActive); // Abigail's flower on grass.
        Assert.False(workspace.TileStore.Get(275, 160).IsActive); // Stone is invalid for this identity.
        Assert.False(workspace.TileStore.Get(625, 100).IsActive); // Chain cannot hang from stone.
    }

    [Fact]
    public void Final_cleanup_one_by_two_family_matches_official_registered_passlegacy()
    {
        // Official TerrariaServer 1.4.5.8 registered FinalCleanup, seed1458: all ten Check1x2
        // identities with intact, unsupported and missing-member pairs; all eleven type20 ground
        // styles and an inactive platform anchor. The digest covers every cell after sand settling.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        ushort[] objects = [15, 20, 216, 338, 390, 493, 497, 590, 595, 615];
        for (int n = 0; n < objects.Length; n++)
        {
            int x = 200 + n * 45;
            ushort floorType = objects[n] == 20 ? (ushort)2 : (ushort)1;
            var top = new WorldTile { Type = objects[n], Flags = WorldTileFlags.Active };
            var bottom = top with { FrameY = 18 };
            var secondStyleTop = top with { FrameY = 40 };
            var floor = new WorldTile { Type = floorType, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, 100, in top);
            workspace.TileStore.Set(x, 101, in bottom);
            workspace.TileStore.Set(x, 102, in floor);
            workspace.TileStore.Set(x, 130, in top);
            workspace.TileStore.Set(x, 131, in bottom);
            workspace.TileStore.Set(x, 160, in secondStyleTop);
            workspace.TileStore.Set(x, 162, in floor);
        }
        ushort[] grounds = [2, 147, 60, 23, 199, 109, 53, 116, 234, 112, 633];
        for (int n = 0; n < grounds.Length; n++)
        {
            int x = 770 + n * 18;
            var top = new WorldTile { Type = 20, Flags = WorldTileFlags.Active };
            var bottom = top with { FrameY = 18 };
            var floor = new WorldTile { Type = grounds[n], Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, 100, in top);
            workspace.TileStore.Set(x, 101, in bottom);
            workspace.TileStore.Set(x, 102, in floor);
            if (grounds[n] is 53 or 116 or 234 or 112)
            {
                var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(x, 103, in stone);
            }
        }
        var chair = new WorldTile { Type = 15, Flags = WorldTileFlags.Active };
        var chairBottom = chair with { FrameY = 18 };
        var inactivePlatform = new WorldTile { Type = 19 };
        workspace.TileStore.Set(750, 200, in chair);
        workspace.TileStore.Set(750, 201, in chairBottom);
        workspace.TileStore.Set(750, 202, in inactivePlatform);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("552D053093D246CE0221C52BD3E543446DFB1AC68256C8C190A060DE61C7E845", HashFixture(workspace));
        Assert.Equal(798503781, random.Next());
        Assert.True(workspace.TileStore.Get(750, 200).IsActive);
        Assert.False(workspace.TileStore.Get(200, 130).IsActive);
        Assert.False(workspace.TileStore.Get(200, 160).IsActive);
        Assert.Equal((short)72, workspace.TileStore.Get(788, 100).FrameX);
    }

    [Fact]
    public void Final_cleanup_hanging_one_by_two_family_matches_official_registered_passlegacy()
    {
        // TerrariaServer 1.4.5.8 registered FinalCleanup PassLegacy, seed1458.
        // All six non-tile-entity Check1x2Top identities: solid/platform ceilings,
        // missing ceiling/member and wrong second-row frame. Type698's tile entity is separate debt.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        ushort[] objects = [42, 270, 271, 572, 581, 660];
        for (int n = 0; n < objects.Length; n++)
        {
            int x = 200 + n * 80;
            foreach ((int y, ushort ceiling, bool complete, short lowerFrame) in new[]
                { (100, (ushort)1, true, (short)18), (140, (ushort)19, true, (short)18),
                  (220, (ushort)0, true, (short)18),
                  (260, (ushort)1, false, (short)18), (300, (ushort)1, true, (short)36) })
            {
                if (ceiling != 0)
                {
                    var support = new WorldTile { Type = ceiling, Flags = WorldTileFlags.Active };
                    workspace.TileStore.Set(x, y - 1, in support);
                }
                var upper = new WorldTile { Type = objects[n], Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(x, y, in upper);
                if (complete)
                {
                    var lower = upper with { FrameY = lowerFrame };
                    workspace.TileStore.Set(x, y + 1, in lower);
                }
            }
        }

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("027D71FF3E2581DEB50E05B71627A69927922ABAF5B6CE5CBE10834E2BF4B334", HashFixture(workspace));
        Assert.Equal(526643438, random.Next());
        foreach (int x in new[] { 200, 280, 360, 440, 520, 600 })
        {
            Assert.True(workspace.TileStore.Get(x, 100).IsActive);
            Assert.True(workspace.TileStore.Get(x, 140).IsActive);
            Assert.False(workspace.TileStore.Get(x, 220).IsActive);
            Assert.False(workspace.TileStore.Get(x, 260).IsActive);
            Assert.False(workspace.TileStore.Get(x, 300).IsActive);
        }
    }

    [Fact]
    public void Final_cleanup_ordinary_two_by_two_family_matches_official_registered_passlegacy()
    {
        // TerrariaServer 1.4.5.8 registered FinalCleanup PassLegacy, 1000x500, seed1458.
        // All 37 common-support Check2x2 identities are Main.tileFrameImportant in the official
        // table. Each has an intact, missing-member and missing-support object, with alternating
        // horizontal style bands; the hash covers every six-field cell and shared RNG state.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        ushort[] identities = [94, 97, 98, 99, 100, 125, 173, 282, 287, 319,
            335, 360, 411, 490, 505, 521, 522, 523, 524, 525, 526, 527,
            543, 564, 565, 568, 569, 570, 580, 594, 598, 620, 621, 622, 654, 751, 752];
        for (int n = 0; n < identities.Length; n++)
        {
            int x = 100 + n * 21;
            short style = (short)(n % 2 == 0 ? 0 : 36);
            foreach ((int y, bool complete, bool supported) in new[]
                { (100, true, true), (140, false, true), (180, true, false) })
            {
                for (int dx = 0; dx < 2; dx++)
                for (int dy = 0; dy < 2; dy++)
                {
                    if (!complete && dx == 1 && dy == 1)
                        continue;
                    var part = new WorldTile { Type = identities[n], FrameX = (short)(style + dx * 18),
                        FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active };
                    workspace.TileStore.Set(x + dx, y + dy, in part);
                }
                var floor = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(x, y + 2, in floor);
                if (supported)
                    workspace.TileStore.Set(x + 1, y + 2, in floor);
            }
        }

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal("532DEE852B10D6FFF82333233E512118084864B1DAB56C0F58F294ECD7C6B99B", HashFixture(workspace));
        Assert.Equal(755242424, random.Next());
        for (int n = 0; n < identities.Length; n++)
        {
            int x = 100 + n * 21;
            Assert.True(workspace.TileStore.Get(x, 100).IsActive);
            Assert.False(workspace.TileStore.Get(x, 140).IsActive);
            Assert.False(workspace.TileStore.Get(x, 180).IsActive);
        }
    }

    [Fact]
    public void Final_cleanup_type185_one_and_two_cell_piles_match_official_substrate_rules()
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        void Set(int x, int y, ushort type, short frameX = 0)
        {
            var tile = new WorldTile { Type = type, Flags = WorldTileFlags.Active, FrameX = frameX };
            workspace.TileStore.Set(x, y, in tile);
        }
        Set(500, 100, 185, 648); Set(500, 101, 147);
        Set(520, 100, 185, 648); Set(520, 101, 1);
        Set(540, 100, 185, 972); Set(540, 101, 53);
        Set(560, 100, 185, 972); Set(560, 101, 1);
        Set(580, 100, 185, 864); Set(580, 101, 1);
        Set(600, 100, 185);
        foreach (int left in new[] { 640, 660, 680, 700, 720, 740, 760, 780 })
        {
            short frame = (short)(left is 680 or 700 ? 900 : left is 720 or 740 ? 1476 : left is 760 or 780 ? 1368 : 0);
            var first = new WorldTile { Type = 185, Flags = WorldTileFlags.Active, FrameX = frame, FrameY = 18 };
            workspace.TileStore.Set(left, 100, in first);
            if (left != 660)
            {
                var second = new WorldTile { Type = 185, Flags = WorldTileFlags.Active, FrameX = (short)(frame + 18), FrameY = 18 };
                workspace.TileStore.Set(left + 1, 100, in second);
            }
            ushort floor = (ushort)(left is 680 or 700 ? 147 : left is 720 or 740 ? 53 : left == 780 ? 2 : 1);
            Set(left, 101, left is 700 or 740 ? (ushort)1 : floor);
            Set(left + 1, 101, left is 700 or 740 ? (ushort)1 : left == 780 ? (ushort)1 : floor);
        }

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        Assert.Equal((ushort)185, workspace.TileStore.Get(500, 100).Type);
        Assert.False(workspace.TileStore.Get(520, 100).IsActive);
        Assert.Equal((ushort)185, workspace.TileStore.Get(540, 100).Type);
        Assert.False(workspace.TileStore.Get(560, 100).IsActive);
        Assert.Equal((ushort)185, workspace.TileStore.Get(580, 100).Type);
        Assert.False(workspace.TileStore.Get(600, 100).IsActive);
        Assert.True(workspace.TileStore.Get(640, 100).IsActive);
        Assert.False(workspace.TileStore.Get(660, 100).IsActive);
        Assert.True(workspace.TileStore.Get(680, 100).IsActive);
        Assert.False(workspace.TileStore.Get(700, 100).IsActive);
        Assert.True(workspace.TileStore.Get(720, 100).IsActive);
        Assert.False(workspace.TileStore.Get(740, 100).IsActive);
        Assert.Equal((short)0, workspace.TileStore.Get(760, 100).FrameX);
        Assert.Equal((short)1368, workspace.TileStore.Get(780, 100).FrameX);
        for (int y = 101; y <= 112; y++)
            Assert.Equal(y <= 111 ? (ushort)53 : (ushort)397, workspace.TileStore.Get(540, y).Type);
        Assert.Equal("D954C20684F75C4613C670C8971995C2FA7DA89EFD28D63FDD52D2BBA525DB75", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
    }

    [Fact]
    public void Final_cleanup_one_cell_table_objects_match_official_support_rules()
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        void Set(int x, int y, ushort type)
        {
            var tile = new WorldTile { Type = type, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in tile);
        }
        ushort[] objects = [13, 33, 49, 50, 78, 174, 372, 646, 707];
        for (int n = 0; n < objects.Length; n++)
        {
            int x = 500 + 40 * n;
            Set(x, 100, objects[n]);
            Set(x, 101, 19);
            Set(x + 15, 100, objects[n]);
        }
        Set(900, 100, 78);
        Set(900, 101, 1);
        Set(920, 100, 13);
        Set(920, 101, 1);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));

        for (int n = 0; n < objects.Length; n++)
        {
            int x = 500 + 40 * n;
            Assert.True(workspace.TileStore.Get(x, 100).IsActive);
            Assert.False(workspace.TileStore.Get(x + 15, 100).IsActive);
        }
        Assert.True(workspace.TileStore.Get(900, 100).IsActive);
        Assert.False(workspace.TileStore.Get(920, 100).IsActive);
        Assert.Equal("3DEA150113D7E656D82D0B17377350553F94AF2DDA60FC13476867196146996D", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
    }

    [Fact]
    public void Final_cleanup_counts_existing_dirtiest_blocks_as_dirt_offers()
    {
        // TerrariaServer 1.4.5.8 TileID.Sets.Dirt = { 0, 668 }.
        // The independent registered FinalCleanup seed1458 first offers these sites.
        var workspace = new Workspace(600, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        (int x, int y)[] candidates = [(261, 205), (496, 239), (286, 225)];
        for (int n = 0; n < candidates.Length; n++)
        {
            (int x, int y) = candidates[n];
            var tile = new WorldTile
            {
                Type = n == 1 ? (ushort)0 : (ushort)668,
                Flags = WorldTileFlags.Active | WorldTileFlags.WireRed,
                TileColor = 5,
                Wall = (ushort)(7 + n),
                LiquidAmount = (byte)(80 + n)
            };
            workspace.TileStore.Set(x, y, in tile);
        }

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 600, 500), workspace, random));

        for (int n = 0; n < candidates.Length; n++)
        {
            (int x, int y) = candidates[n];
            WorldTile tile = workspace.TileStore.Get(x, y);
            Assert.Equal((ushort)668, tile.Type);
            Assert.True(tile.IsActive);
            Assert.Equal(WorldTileFlags.WireRed, tile.Flags & WorldTileFlags.WireRed);
            Assert.Equal((byte)5, tile.TileColor);
            Assert.Equal((ushort)(7 + n), tile.Wall);
            Assert.Equal((byte)(80 + n), tile.LiquidAmount);
        }
        Assert.Equal("8A59CE7099EE4244061B74E617A11F6CD193D35F1E5D2B32E08E864D0F05A04A", HashFixture(workspace));
        Assert.Equal(370853568, random.Next());
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
    public void Final_cleanup_uses_the_source_small_world_quota_for_final_grass_offers()
    {
        var workspace = new Workspace(420, 400);
        Assert.True(workspace.TrySetLayers(180, 250));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        for (int x = 0; x < 420; x++)
        for (int y = 0; y < 400; y++)
        {
            var dirt = new WorldTile { Type = 0, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in dirt);
        }

        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 420, 400), workspace, new RandomAdapter(1458)));

        int offers = 0;
        for (int x = 0; x < 420; x++)
        for (int y = 0; y < 400; y++)
            offers += workspace.TileStore.Get(x, y).Type == 668 ? 1 : 0;
        Assert.Equal(3, offers);
    }

    [Fact]
    public void Final_cleanup_matches_the_official_ordinary_passlegacy_whole_grid_fixture()
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy fixture, 600x500, seed 1458. The source
        // delegate receives the same dry heterogeneous grid and the assertion hashes every cell's
        // activity/type/wall/frame/liquid result after its complete ordinary tail, including the
        // 3,000 final grass attempts. Targeted assertions retain the shape and exact 15-row
        // type-314 liquid span that a coarse grid hash does not encode.
        const int width = 600;
        const int height = 500;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, effectiveCrimson: false, isRemix: false));

        void SetTile(int x, int y, ushort type, bool active, short frameX = 0, short frameY = 0,
            byte liquidAmount = 0, ushort wall = 0, WorldTileFlags extraFlags = WorldTileFlags.None)
        {
            var tile = new WorldTile
            {
                Type = type,
                Wall = wall,
                FrameX = frameX,
                FrameY = frameY,
                LiquidAmount = liquidAmount,
                Flags = active ? WorldTileFlags.Active | extraFlags : extraFlags
            };
            workspace.TileStore.Set(x, y, in tile);
        }

        SetTile(400, 130, 53, active: true);
        SetTile(400, 129, 323, active: true);
        SetTile(140, 120, 1, active: true, liquidAmount: 20, wall: 187);
        SetTile(180, 120, 137, active: true, extraFlags: WorldTileFlags.None);
        var trap = workspace.TileStore.Get(180, 120);
        trap.Shape = 1;
        workspace.TileStore.Set(180, 120, in trap);
        for (int y = 104; y <= 120; y++)
            SetTile(220, y, y == 120 ? (ushort)314 : (ushort)0, active: y == 120, liquidAmount: 100);
        SetTile(220, 121, 1, active: true, liquidAmount: byte.MaxValue);
        SetTile(260, 120, 332, active: true);
        SetTile(260, 122, 1, active: true);
        SetTile(450, 120, 0, active: false, liquidAmount: 100);
        SetTile(340, 120, 240, active: true);
        var leftPaintingWall = workspace.TileStore.Get(339, 120);
        leftPaintingWall.Wall = 7;
        workspace.TileStore.Set(339, 120, in leftPaintingWall);
        var rightPaintingWall = workspace.TileStore.Get(341, 120);
        rightPaintingWall.Wall = 8;
        workspace.TileStore.Set(341, 120, in rightPaintingWall);
        SetTile(380, 120, 484, active: true, frameX: 18, frameY: 18);
        SetTile(379, 118, 26, active: true);
        SetTile(420, 120, 374, active: true, wall: 7);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458())
            .Execute(new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height), workspace, random));

        Assert.Equal("A604BD6338328F10604F9110511437B6C01D2383C3AF5450D54D63BDE8464D6B", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
        Assert.Equal(WorldLiquidKind.Lava, workspace.TileStore.Get(140, 120).LiquidKind);
        Assert.Equal(byte.MaxValue, workspace.TileStore.Get(140, 120).LiquidAmount);
        Assert.Equal((byte)0, workspace.TileStore.Get(180, 120).Shape);
        Assert.Equal((byte)100, workspace.TileStore.Get(220, 104).LiquidAmount);
        Assert.Equal((byte)100, workspace.TileStore.Get(220, 105).LiquidAmount);
        for (int y = 106; y <= 120; y++)
            Assert.Equal((byte)0, workspace.TileStore.Get(220, y).LiquidAmount);
        Assert.Equal((ushort)1, workspace.TileStore.Get(260, 121).Type);
        Assert.True(workspace.TileStore.Get(260, 121).IsActive);
        Assert.Equal((byte)100, workspace.TileStore.Get(450, 120).LiquidAmount);
        Assert.Equal((ushort)7, workspace.TileStore.Get(340, 120).Wall);
        for (int x = 379; x <= 380; x++)
        for (int y = 119; y <= 120; y++)
            Assert.Equal((ushort)397, workspace.TileStore.Get(x, y).Type);
        Assert.False(workspace.TileStore.Get(420, 120).IsActive);
        Assert.Equal((short)-1, workspace.TileStore.Get(420, 120).FrameX);
    }

    [Fact]
    public void Final_cleanup_matches_the_official_canonical_dry_passlegacy_whole_grid_fixture()
    {
        // Registered original PassLegacy, seed 1458, canonical Small 4200x1200 geometry.
        // The input is deliberately dry/synthetic, but exercises rail-obstructed sand and the
        // previously covered material, object, liquid and wall branches in one complete pass.
        const int width = 4200;
        const int height = 1200;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(300, 500));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), width, false, false));

        void SetTile(int x, int y, ushort type, bool active, short frameX = 0, short frameY = 0,
            byte liquidAmount = 0, ushort wall = 0, byte shape = 0)
        {
            var tile = new WorldTile
            {
                Type = type, Wall = wall, FrameX = frameX, FrameY = frameY,
                LiquidAmount = liquidAmount, Shape = shape,
                Flags = active ? WorldTileFlags.Active : WorldTileFlags.None
            };
            workspace.TileStore.Set(x, y, in tile);
        }

        SetTile(400, 130, 53, true);
        SetTile(400, 129, 323, true);
        SetTile(140, 120, 1, true, liquidAmount: 20, wall: 187);
        SetTile(180, 120, 137, true, shape: 1);
        for (int y = 104; y <= 120; y++)
            SetTile(220, y, y == 120 ? (ushort)314 : (ushort)0, y == 120, liquidAmount: 100);
        SetTile(220, 121, 1, true, liquidAmount: byte.MaxValue);
        SetTile(260, 120, 332, true);
        SetTile(260, 122, 1, true);
        SetTile(450, 120, 0, false, liquidAmount: 100);
        SetTile(340, 120, 240, true);
        SetTile(339, 120, 0, false, wall: 7);
        SetTile(341, 120, 0, false, wall: 8);
        SetTile(380, 120, 484, true, frameX: 18, frameY: 18);
        SetTile(379, 118, 26, true);
        SetTile(420, 120, 374, true, wall: 7);
        SetTile(800, 250, 53, true);
        SetTile(800, 255, 314, true);
        SetTile(900, 220, 1, true, liquidAmount: 20, wall: 187);
        SetTile(1000, 220, 240, true);
        SetTile(999, 220, 0, false, wall: 7);
        SetTile(1200, 220, 332, true);
        SetTile(1200, 222, 1, true);

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, width, height), workspace, random));

        Assert.Equal("C06FBEB48979D6CA13603BFA58172EB7DF72C8FEF9F754B4A3AEA07CDA811A90", HashFixture(workspace));
        Assert.Equal(1056314798, random.Next());
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
    public void Settle_liquids_again_canonical_multi_basin_matches_official_passlegacy()
    {
        var workspace = CreateCanonicalLiquidBasins();
        const int width = 4200;
        const int height = 1200;
        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.SettleLiquidsAgain, new FinalState1458())
            .Execute(new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "CanonicalLiquids", 1458, width, height), workspace, random));

        Assert.Equal(906992634, random.Next());
        Assert.Equal("2765B5A309AB750342833807BB58B83FCABF7BBFF45DD5FA8B88E9F437514B1F", HashFixture(workspace));
    }

    [Fact]
    public void First_settle_liquids_canonical_multi_basin_matches_official_passlegacy()
    {
        var workspace = CreateCanonicalLiquidBasins();
        JungleStructurePass1458.ApplySettleLiquidsForTesting(workspace);

        // The two registered ordinary-world passes share the source's settlement sequence on this
        // no-special-seed terrain. The first pass has its own independently run official PassLegacy oracle.
        Assert.Equal("2765B5A309AB750342833807BB58B83FCABF7BBFF45DD5FA8B88E9F437514B1F", HashFixture(workspace));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Settle_liquids_canonical_four_kinds_match_official_passlegacy(bool laterPass)
    {
        var workspace = CreateCanonicalMixedLiquidBasins();
        if (laterPass)
        {
            var random = new RandomAdapter(1458);
            new FinalPass1458(FinalStage1458.SettleLiquidsAgain, new FinalState1458())
                .Execute(new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "CanonicalMixedLiquids", 1458, 4200, 1200), workspace, random));
            Assert.Equal(906992634, random.Next());
        }
        else
        {
            JungleStructurePass1458.ApplySettleLiquidsForTesting(workspace);
        }

        Assert.Equal("953EE58D1120E82E52F00182E8FE3AFE4869456F095F87007128687695BDB6C8", HashLiquidKindFixture(workspace));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Settle_liquids_canonical_interacting_kinds_match_official_passlegacy(bool laterPass)
    {
        var workspace = CreateCanonicalMixedLiquidBasins(interacting: true);
        if (laterPass)
        {
            var random = new RandomAdapter(1458);
            new FinalPass1458(FinalStage1458.SettleLiquidsAgain, new FinalState1458())
                .Execute(new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "InteractingLiquids", 1458, 4200, 1200), workspace, random));
            Assert.Equal(906992634, random.Next());
        }
        else
        {
            JungleStructurePass1458.ApplySettleLiquidsForTesting(workspace);
        }

        Assert.Equal("3132D18ED04FBEE14B3D1701398843FBE8C0727484040D6C68DF7E5D3E344251", HashLiquidKindFixture(workspace));
    }

    private static Workspace CreateCanonicalLiquidBasins()
    {
        const int width = 4200;
        const int height = 1200;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(300, 500));
        workspace.SetVanillaLiquidLines(600, 1000);
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), width, false, false));
        foreach ((int centerX, int sourceY, int floorY) in new[]
            { (1000, 450, 600), (2000, 650, 800), (3000, 900, 1050) })
        {
            for (int x = centerX - 12; x <= centerX + 12; x++)
            {
                var floor = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(x, floorY, in floor);
            }
            for (int y = sourceY - 5; y <= floorY; y++)
            foreach (int x in new[] { centerX - 12, centerX + 12 })
            {
                var wall = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(x, y, in wall);
            }
            var water = new WorldTile { LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
            workspace.TileStore.Set(centerX, sourceY, in water);
        }
        return workspace;
    }

    private static Workspace CreateCanonicalMixedLiquidBasins(bool interacting = false)
    {
        const int width = 4200;
        const int height = 1200;
        var workspace = new Workspace(width, height);
        Assert.True(workspace.TrySetLayers(300, 500));
        workspace.SetVanillaLiquidLines(600, 1000);
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), width, false, false));
        foreach ((int centerX, int sourceY, int floorY, WorldLiquidKind kind) in interacting
            ? new[] { (1000, 450, 600, WorldLiquidKind.Lava), (2000, 650, 800, WorldLiquidKind.Honey),
                      (3000, 900, 1050, WorldLiquidKind.Shimmer) }
            : new[] { (500, 450, 600, WorldLiquidKind.Water), (1500, 450, 600, WorldLiquidKind.Lava),
                      (2500, 650, 800, WorldLiquidKind.Honey), (3500, 650, 800, WorldLiquidKind.Shimmer) })
        {
            for (int x = centerX - 12; x <= centerX + 12; x++)
            {
                var floor = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(x, floorY, in floor);
            }
            for (int y = sourceY - 5; y <= floorY; y++)
            foreach (int x in new[] { centerX - 12, centerX + 12 })
            {
                var wall = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(x, y, in wall);
            }
            var liquid = new WorldTile { LiquidAmount = byte.MaxValue, LiquidKind = kind };
            workspace.TileStore.Set(interacting ? centerX + 4 : centerX, sourceY, in liquid);
            if (interacting)
            {
                var water = new WorldTile { LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Water };
                workspace.TileStore.Set(centerX - 4, sourceY, in water);
            }
        }
        return workspace;
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

    private static string HashLiquidKindFixture(Workspace workspace)
    {
        var snapshot = new StringBuilder(workspace.WidthTiles * workspace.HeightTiles * 22);
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            snapshot.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(',')
                .Append((byte)tile.LiquidKind).Append(';');
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
