using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldSmoother1458Tests
{
    private const ushort StableSolid = 368;

    [Fact]
    public void Exposed_edge_consumes_source_roll_and_uses_normalized_slope_two_shape()
    {
        RuntimeWorldGenerationWorkspace workspace = CreateWorkspace();
        SetActive(workspace, 30, 30, VanillaTileIds.Dirt);
        SetActive(workspace, 30, 31, StableSolid);
        SetActive(workspace, 29, 31, StableSolid);
        SetActive(workspace, 31, 30, StableSolid);
        Clear(workspace, 30, 29);
        Clear(workspace, 29, 30);
        Clear(workspace, 31, 29);
        var random = new ScriptedRandom([1, 1, 0], fallback: 1);

        VanillaWorldSmoothingResult1458 result = VanillaWorldSmoother1458.Apply(
            workspace,
            random,
            CancellationToken.None);

        Assert.Equal(
            VanillaTileShape1458.SlopeDownLeft,
            (VanillaTileShape1458)workspace.TileStore.Get(30, 30).Shape);
        Assert.Equal(1, result.SlopedTiles);
        Assert.Equal(580, random.BoundedCalls);
    }

    [Fact]
    public void Exposed_edge_alternate_roll_produces_half_brick()
    {
        RuntimeWorldGenerationWorkspace workspace = CreateWorkspace();
        SetActive(workspace, 30, 30, VanillaTileIds.Dirt);
        SetActive(workspace, 30, 31, StableSolid);
        SetActive(workspace, 29, 31, StableSolid);
        SetActive(workspace, 31, 30, StableSolid);
        Clear(workspace, 30, 29);
        Clear(workspace, 29, 30);
        Clear(workspace, 31, 29);
        var random = new ScriptedRandom([1, 1, 1], fallback: 1);

        VanillaWorldSmoothingResult1458 result = VanillaWorldSmoother1458.Apply(
            workspace,
            random,
            CancellationToken.None);

        Assert.Equal(VanillaTileShape1458.HalfBrick, (VanillaTileShape1458)workspace.TileStore.Get(30, 30).Shape);
        Assert.Equal(1, result.HalfBricks);
        Assert.Equal(580, random.BoundedCalls);
    }

    [Fact]
    public void Covered_underside_uses_source_bottom_slope_orientation()
    {
        RuntimeWorldGenerationWorkspace workspace = CreateWorkspace();
        SetActive(workspace, 30, 30, VanillaTileIds.Dirt);
        SetActive(workspace, 30, 29, StableSolid);
        SetActive(workspace, 29, 30, StableSolid);
        SetActive(workspace, 29, 29, StableSolid);
        Clear(workspace, 30, 31);
        var random = new ScriptedRandom([0], fallback: 1);

        VanillaWorldSmoother1458.Apply(workspace, random, CancellationToken.None);

        Assert.Equal(VanillaTileShape1458.SlopeUpRight, (VanillaTileShape1458)workspace.TileStore.Get(30, 30).Shape);
        Assert.Equal(577, random.BoundedCalls);
    }

    [Fact]
    public void Sand_finish_normalizes_single_support_to_half_brick()
    {
        RuntimeWorldGenerationWorkspace workspace = CreateWorkspace();
        SetActive(workspace, 30, 30, VanillaTileIds.Sand, VanillaTileShape1458.SlopeDownRight);
        SetActive(workspace, 30, 31, StableSolid);
        Clear(workspace, 30, 29);
        Clear(workspace, 29, 30);
        Clear(workspace, 31, 30);
        var random = new ScriptedRandom([], fallback: 1);

        VanillaWorldSmoother1458.Apply(workspace, random, CancellationToken.None);

        Assert.Equal(VanillaTileShape1458.HalfBrick, (VanillaTileShape1458)workspace.TileStore.Get(30, 30).Shape);
        Assert.Equal(579, random.BoundedCalls);
    }

    [Fact]
    public void Finish_converts_orphan_top_slope_to_half_brick()
    {
        RuntimeWorldGenerationWorkspace workspace = CreateWorkspace();
        SetActive(workspace, 30, 30, VanillaTileIds.Dirt, VanillaTileShape1458.SlopeDownRight);
        Clear(workspace, 30, 29);
        var random = new ScriptedRandom([], fallback: 1);

        VanillaWorldSmoother1458.Apply(workspace, random, CancellationToken.None);

        Assert.Equal(VanillaTileShape1458.HalfBrick, (VanillaTileShape1458)workspace.TileStore.Get(30, 30).Shape);
    }

    [Fact]
    public void Tree_above_preserves_support_shape_through_can_kill_guard()
    {
        RuntimeWorldGenerationWorkspace workspace = CreateWorkspace();
        SetActive(workspace, 30, 30, VanillaTileIds.Dirt, VanillaTileShape1458.SlopeDownRight);
        var random = new ScriptedRandom([], fallback: 1);

        VanillaWorldSmoother1458.Apply(workspace, random, CancellationToken.None);

        Assert.Equal(
            VanillaTileShape1458.SlopeDownRight,
            (VanillaTileShape1458)workspace.TileStore.Get(30, 30).Shape);
    }

    [Fact]
    public void Diagonal_gap_is_filled_from_support_and_shaped_in_source_direction()
    {
        RuntimeWorldGenerationWorkspace workspace = CreateWorkspace();
        SetActive(workspace, 30, 31, StableSolid);
        SetActive(workspace, 29, 31, StableSolid);
        SetActive(workspace, 31, 30, StableSolid);
        Clear(workspace, 30, 30);
        Clear(workspace, 30, 29);
        Clear(workspace, 29, 30);
        Clear(workspace, 31, 29);
        var random = new ScriptedRandom([1, 1, 1, 0], fallback: 1);

        VanillaWorldSmoothingResult1458 result = VanillaWorldSmoother1458.Apply(
            workspace,
            random,
            CancellationToken.None);

        WorldTile tile = workspace.TileStore.Get(30, 30);
        Assert.True(tile.IsActive);
        Assert.Equal(StableSolid, tile.Type);
        Assert.Equal(VanillaTileShape1458.SlopeDownLeft, (VanillaTileShape1458)tile.Shape);
        Assert.Equal(1, result.FilledTiles);
    }

    [Fact]
    public void Isolated_clearable_solid_is_removed_without_touching_border()
    {
        RuntimeWorldGenerationWorkspace workspace = CreateWorkspace();
        SetActive(workspace, 30, 30, VanillaTileIds.Dirt);
        SetActive(workspace, 19, 30, VanillaTileIds.Dirt);
        Clear(workspace, 30, 29);
        Clear(workspace, 29, 30);
        Clear(workspace, 31, 30);
        var random = new ScriptedRandom([], fallback: 1);

        VanillaWorldSmoothingResult1458 result = VanillaWorldSmoother1458.Apply(
            workspace,
            random,
            CancellationToken.None);

        Assert.False(workspace.TileStore.Get(30, 30).IsActive);
        Assert.True(workspace.TileStore.Get(19, 30).IsActive);
        Assert.Equal(1, result.RemovedTiles);
    }

    [Fact]
    public void Capability_catalog_owns_pinned_generation_sets()
    {
        Assert.False(VanillaWorldSmoothingCatalog1458.CanBeClearedDuringGeneration(new TileTypeId(396)));
        Assert.True(VanillaWorldSmoothingCatalog1458.CanBeClearedDuringGeneration(VanillaTileIds.Dirt));
        Assert.True(VanillaWorldSmoothingCatalog1458.PreventsSlopesDuringGeneration(new TileTypeId(137)));
        Assert.False(VanillaWorldSmoothingCatalog1458.CanBePounded(new TileTypeId(484)));
        Assert.True(VanillaWorldSmoothingCatalog1458.ForbidsSlopingBelow(VanillaTileIds.Containers));
        Assert.True(VanillaWorldSmoothingCatalog1458.IsSandConversion(new TileTypeId(234)));
    }

    private static RuntimeWorldGenerationWorkspace CreateWorkspace()
    {
        var workspace = new RuntimeWorldGenerationWorkspace(64, 64);
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
                SetActive(workspace, x, y, VanillaTileIds.Trees);
        return workspace;
    }

    private static void Clear(RuntimeWorldGenerationWorkspace workspace, int x, int y)
    {
        WorldTile tile = default;
        workspace.TileStore.Set(x, y, in tile);
    }

    private static void SetActive(
        RuntimeWorldGenerationWorkspace workspace,
        int x,
        int y,
        TileTypeId type,
        VanillaTileShape1458 shape = VanillaTileShape1458.Full) =>
        SetActive(workspace, x, y, checked((ushort)type.Value), shape);

    private static void SetActive(
        RuntimeWorldGenerationWorkspace workspace,
        int x,
        int y,
        ushort type,
        VanillaTileShape1458 shape = VanillaTileShape1458.Full)
    {
        var tile = new WorldTile
        {
            Type = type,
            Flags = WorldTileFlags.Active,
            Shape = (byte)shape
        };
        workspace.TileStore.Set(x, y, in tile);
    }

    private sealed class ScriptedRandom(IEnumerable<int> values, int fallback) : IWorldGenerationVanillaRandom
    {
        private readonly Queue<int> values = new(values);

        public int BoundedCalls { get; private set; }
        public int Next() => Next(int.MaxValue);

        public int Next(int maxValue)
        {
            BoundedCalls++;
            int value = values.TryDequeue(out int scripted) ? scripted : fallback;
            return Math.Clamp(value, 0, maxValue - 1);
        }

        public int Next(int minValue, int maxValue) => minValue + Next(maxValue - minValue);
        public double NextDouble() => 0d;
        public void NextBytes(byte[] buffer) => Array.Clear(buffer);
    }
}
