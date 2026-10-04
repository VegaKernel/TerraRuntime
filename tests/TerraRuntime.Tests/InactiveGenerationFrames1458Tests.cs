using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class InactiveGenerationFrames1458Tests
{
    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(-1, -1, 0, true)]
    [InlineData(-1, 0, 0, false)]
    [InlineData(0, -1, 0, false)]
    [InlineData(18, 18, 0, false)]
    [InlineData(-2, -2, 0, false)]
    [InlineData(-1, -1, 1, false)]
    [InlineData(0, 0, 1, false)]
    public void Both_validators_admit_only_the_normalized_generation_frame_pairs(
        short frameX, short frameY, byte shape, bool admitted)
    {
        var workspace = new Workspace(600, 500);
        ref var tile = ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(300, 200)];
        tile.FrameX = frameX; tile.FrameY = frameY; tile.Shape = shape;
        var metadata = Metadata();
        Assert.Equal(admitted ? WorldValidationStatus.Valid : WorldValidationStatus.InvalidFlags,
            Validator1458.Validate(workspace, metadata).Status);
        Assert.Equal(admitted ? WorldValidationStatus.Valid : WorldValidationStatus.InvalidFlags,
            StructuralValidator.Validate(workspace, metadata).Status);
        Assert.Equal((frameX, frameY, shape), (tile.FrameX, tile.FrameY, tile.Shape));
    }

    [Fact]
    public void Actuated_active_chest_still_requires_a_valid_object_footprint()
    {
        var workspace = new Workspace(600, 500);
        ref var tile = ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(300, 200)];
        tile.Type = 21; tile.Flags = WorldTileFlags.Active | WorldTileFlags.Inactive;
        tile.FrameX = tile.FrameY = -1;
        Assert.Equal(WorldValidationStatus.OrphanFrameImportantObject,
            Validator1458.Validate(workspace, Metadata()).Status);
        Assert.Equal(WorldValidationStatus.OrphanFrameImportantObject,
            StructuralValidator.Validate(workspace, Metadata()).Status);
    }

    [Theory]
    [InlineData(3, null)]
    [InlineData(24, 955664633)]
    [InlineData(61, null)]
    [InlineData(110, 906992634)]
    [InlineData(184, 1335025742)]
    public void Source_physical_kill_frames_and_verified_rng_subset_are_structurally_admissible(
        ushort type, int? nextRandom)
    {
        // Independent original WorldGen.TileFrame under generatingWorld/netMode2, seed1458:
        // each unsupported plant is killed with Type0, paired unset frames, cleared shape.
        var workspace = new Workspace(600, 500);
        ref var tile = ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(300, 200)];
        tile.Type = type; tile.Flags = WorldTileFlags.Active;
        var random = new VanillaUnifiedRandom1458(1458);
        new GenerationTileFraming1458(workspace.TileStore, new RandomAdapter(random)).TileFrame(300, 200);
        Assert.False(tile.IsActive);
        Assert.Equal((0, -1, -1, 0), ((int)tile.Type, (int)tile.FrameX, (int)tile.FrameY, (int)tile.Shape));
        // Type3/61 physical removal is admitted here; their presentation RNG remains unported.
        if (nextRandom.HasValue) Assert.Equal(nextRandom.Value, random.Next());
        Assert.Equal(WorldValidationStatus.Valid, Validator1458.Validate(workspace, Metadata()).Status);
        Assert.Equal(WorldValidationStatus.Valid, StructuralValidator.Validate(workspace, Metadata()).Status);
    }

    private static RuntimeWorldGenerationMetadataSnapshot Metadata() => new(new(10, 10), new(20, 20), new(140d, 200d));

    public static IEnumerable<object[]> LiquidStates()
    {
        foreach (byte kind in new byte[] { 0, 1, 2, 3, 4, 255 })
        foreach (byte amount in new byte[] { 0, 128, 255 })
        foreach (bool active in new[] { false, true })
            yield return [kind, amount, active];
    }

    [Theory, MemberData(nameof(LiquidStates))]
    public void Defined_empty_liquid_tags_remain_admitted_without_relaxing_enum_validation(
        byte kind, byte amount, bool active)
    {
        var workspace = new Workspace(600, 500);
        ref var tile = ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(300, 200)];
        tile.Type = 1; tile.Flags = active ? WorldTileFlags.Active : WorldTileFlags.None;
        tile.LiquidAmount = amount; tile.LiquidKind = (WorldLiquidKind)kind;
        var expected = kind <= 3 ? WorldValidationStatus.Valid : WorldValidationStatus.InvalidLiquid;
        Assert.Equal(expected, Validator1458.Validate(workspace, Metadata()).Status);
        Assert.Equal(expected, StructuralValidator.Validate(workspace, Metadata()).Status);
        Assert.Equal((amount, kind), (tile.LiquidAmount, (byte)tile.LiquidKind));
    }

    private sealed class RandomAdapter(VanillaUnifiedRandom1458 random) : IWorldGenerationVanillaRandom
    {
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
