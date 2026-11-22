using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WorldFileTileChestRewriterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Live_pressure_keys_replace_preserved_keys_in_complete_reloadable_world(bool empty)
    {
        byte[] original = CreateCompleteCurrentWorld();
        Assert.True(WorldFileLoader.TryLoad(original, CreateLimits(), out var loaded).IsLoaded);
        var world = loaded!;
        Assert.True(WorldFilePreservedSections.TryCapture(original, world.Envelope, out var preserved));
        using var pressure = new MemoryStream();
        WorldPressurePlate[] expected = empty ? [] : [new(1, 2)];
        Assert.Equal(WorldFilePressurePlateEncodeResult.Encoded,
            WorldFilePressurePlateEncoder.TryEncode(expected, world.Header.Dimensions, pressure, out _));
        using var destination = new MemoryStream();
        Assert.Equal(WorldFileTileChestRewriteResult.Rewritten,
            WorldFileTileChestRewriter.TryRewrite(world.Envelope, world.Header, preserved!.Header.Span,
                preserved, CaptureImage(world.Tiles), world.Chests, preserved.Signs.Span, preserved.Npcs.Span,
                preserved.TownRooms.Span, destination, out _, null, null, null, pressure.ToArray()));
        Assert.True(WorldFileLoader.TryLoad(destination.ToArray(), CreateLimits(), out var restored).IsLoaded);
        Assert.Equal(expected, restored!.PressurePlates);
        Assert.True(WorldFilePreservedSections.TryCapture(destination.ToArray(), restored.Envelope, out var after));
        Assert.Equal(preserved.TileEntities.ToArray(), after!.TileEntities.ToArray());
        Assert.Equal(preserved.Bestiary.ToArray(), after.Bestiary.ToArray());
        Assert.Equal(preserved.CreativePowers.ToArray(), after.CreativePowers.ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("0100000001000000")]
    [InlineData("0000000000")]
    [InlineData("01000000FFFFFFFF00000000")]
    [InlineData("0200000001000000020000000100000002000000")]
    public void Invalid_replacement_pressure_section_refuses_before_writing(string bytes)
    {
        byte[] original = CreateCompleteCurrentWorld();
        Assert.True(WorldFileLoader.TryLoad(original, CreateLimits(), out var loaded).IsLoaded);
        var world = loaded!;
        Assert.True(WorldFilePreservedSections.TryCapture(original, world.Envelope, out var preserved));
        using var destination = new MemoryStream();
        Assert.Equal(WorldFileTileChestRewriteResult.InvalidPressurePlateSection,
            WorldFileTileChestRewriter.TryRewrite(world.Envelope, world.Header, preserved!.Header.Span,
                preserved, CaptureImage(world.Tiles), world.Chests, preserved.Signs.Span, preserved.Npcs.Span,
                preserved.TownRooms.Span, destination, out long written, null, null, null, Convert.FromHexString(bytes)));
        Assert.Equal(0, written); Assert.Equal(0, destination.Length);
    }
}
