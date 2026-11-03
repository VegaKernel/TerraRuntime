using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationChestPersistenceValidation1458Tests
{
    private static RuntimeWorldGenerationMetadataSnapshot Metadata() => new(new(10, 10), new(20, 20), new(140, 200));

    [Theory]
    [InlineData(21)]
    [InlineData(467)]
    public void Source_remnants_require_late_projection_and_explicit_validation_admission(int type)
    {
        Workspace workspace = Partial(type);
        Assert.False(Validator1458.Validate(workspace, Metadata(), allowUnregisteredContainerFragments: true).IsValid);
        string grid = GenerationNaturalPrefix49Tests.GridHash(workspace);
        Assert.Single(workspace.CaptureGeneratedChests());
        Assert.Equal(1, workspace.PrepareVanillaChestPersistence());
        Assert.Empty(workspace.CaptureGeneratedChests());
        Assert.Equal(grid, GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(WorldValidationStatus.OrphanFrameImportantObject, Validator1458.Validate(workspace, Metadata()).Status);
        Assert.Equal(WorldValidationStatus.OrphanFrameImportantObject, StructuralValidator.Validate(workspace, Metadata()).Status);
        Assert.True(Validator1458.Validate(workspace, Metadata(), allowUnregisteredContainerFragments: true).IsValid);
    }

    [Theory]
    [InlineData(21, ValidationMode.Automatic)]
    [InlineData(467, ValidationMode.Automatic)]
    [InlineData(21, ValidationMode.GenericStructural)]
    [InlineData(467, ValidationMode.GenericStructural)]
    public void Other_finalization_modes_preserve_records_and_their_strict_contract(int type, ValidationMode mode)
    {
        Workspace workspace = Partial(type);
        ConfigureFinalization(workspace);
        Assert.Equal(FinalizationStatus.ValidationFailed, Finalizer.Finalize(workspace, mode).Status);
        Assert.Single(workspace.CaptureGeneratedChests());
    }

    [Theory]
    [InlineData(21)]
    [InlineData(467)]
    public void Explicit_fresh_vanilla_finalization_projects_once_and_is_repeatable(int type)
    {
        Workspace workspace = Partial(type);
        ConfigureFinalization(workspace);
        string grid = GenerationNaturalPrefix49Tests.GridHash(workspace);
        Assert.True(Finalizer.Finalize(workspace, ValidationMode.VanillaComplete).Succeeded);
        Assert.Empty(workspace.CaptureGeneratedChests());
        Assert.True(Finalizer.Finalize(workspace, ValidationMode.VanillaComplete).Succeeded);
        Assert.Equal(grid, GenerationNaturalPrefix49Tests.GridHash(workspace));
    }

    [Theory]
    [InlineData(21)]
    [InlineData(467)]
    public void Vanilla_mode_alone_cannot_discard_custom_generator_metadata(int type)
    {
        Workspace workspace = Partial(type);
        Assert.True(workspace.TrySetSpawn(10, 10));
        Assert.True(workspace.TrySetDungeon(20, 20));
        Assert.True(workspace.TrySetLayers(140, 200));
        Assert.Equal(FinalizationStatus.ValidationFailed,
            Finalizer.Finalize(workspace, ValidationMode.VanillaComplete).Status);
        Assert.Single(workspace.CaptureGeneratedChests());
    }

    [Theory]
    [InlineData(21)]
    [InlineData(467)]
    public void Surviving_records_still_require_their_actual_anchor_and_complete_footprint(int type)
    {
        Workspace workspace = Complete(type);
        WorldTile anchor = workspace.TileStore.Get(300, 200);
        anchor.FrameX = 18;
        workspace.TileStore.Set(300, 200, in anchor);
        Assert.Equal(0, workspace.PrepareVanillaChestPersistence());
        Assert.Single(workspace.CaptureGeneratedChests());
        Assert.Equal(WorldValidationStatus.InvalidChestAnchor,
            Validator1458.Validate(workspace, Metadata(), allowUnregisteredContainerFragments: true).Status);
    }

    [Theory]
    [InlineData(21, -1)]
    [InlineData(467, -1)]
    [InlineData(55, -1)]
    public void Admission_excludes_malformed_container_atlases_and_other_object_families(int type, int frame)
    {
        var workspace = new Workspace(600, 500);
        var tile = new WorldTile { Type = (ushort)type, FrameX = (short)frame, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(300, 200, in tile);
        Assert.Equal(WorldValidationStatus.OrphanFrameImportantObject,
            Validator1458.Validate(workspace, Metadata(), allowUnregisteredContainerFragments: true).Status);
    }

    private static Workspace Complete(int type)
    {
        var workspace = new Workspace(600, 500);
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            {
                var tile = new WorldTile { Type = (ushort)type, FrameX = (short)(x * 18), FrameY = (short)(y * 18), Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(300 + x, 200 + y, in tile);
            }
        Assert.True(workspace.TryAddGeneratedChest(300, 200, string.Empty, [new WorldChestItem(17, 9, 0)]));
        return workspace;
    }

    private static Workspace Partial(int type)
    {
        Workspace workspace = Complete(type);
        for (int x = 0; x < 2; x++)
        {
            // Actual Flowers -> SaveNewWorld -> LoadWorld retains top container cells and these plant frames.
            var tile = new WorldTile { Type = 3, FrameY = 18, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(300 + x, 201, in tile);
        }
        return workspace;
    }

    private static void ConfigureFinalization(Workspace workspace)
    {
        Assert.True(workspace.TrySetSpawn(10, 10));
        Assert.True(workspace.TrySetDungeon(20, 20));
        Assert.True(workspace.TrySetLayers(140, 200));
        // Authored noncanonical validation fixture; no claim of a generated complete vanilla world.
        workspace.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458
        {
            LeftBeachEnd = 50,
            RightBeachStart = 550,
            DungeonLocation = 20,
            SnowOriginLeft = 100,
            SnowOriginRight = 200,
            JungleOriginX = 400,
            HellChestItems = [],
            TreeX = [],
            TreeStyle = [],
            CaveBackX = [],
            CaveBackStyle = [],
            ForestBackgroundStyles = []
        });
    }
}
