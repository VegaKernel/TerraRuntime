using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFilePreservedSectionsTests
{
    [Fact]
    public void Capture_detaches_only_preserved_sections_from_source_file()
    {
        int[] offsets =
        [
            10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110
        ];
        byte[] file = Enumerable.Range(0, 120).Select(static value => (byte)value).ToArray();
        var envelope = Envelope(offsets);

        Assert.True(WorldFilePreservedSections.TryCapture(file, envelope, out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        Assert.Equal(file.AsSpan(10, 10).ToArray(), preserved!.Header.ToArray());
        Assert.Equal(file.AsSpan(40, 10).ToArray(), preserved.Signs.ToArray());
        Assert.Equal(file.AsSpan(50, 10).ToArray(), preserved.Npcs.ToArray());
        Assert.Equal(file.AsSpan(60, 10).ToArray(), preserved.TileEntities.ToArray());
        Assert.Equal(file.AsSpan(70, 10).ToArray(), preserved.PressurePlates.ToArray());
        Assert.Equal(file.AsSpan(80, 10).ToArray(), preserved.TownRooms.ToArray());
        Assert.Equal(file.AsSpan(90, 10).ToArray(), preserved.Bestiary.ToArray());
        Assert.Equal(file.AsSpan(100, 10).ToArray(), preserved.CreativePowers.ToArray());
        Assert.Equal(80, preserved.TotalBytes);

        byte[] headerBeforeMutation = preserved.Header.ToArray();
        byte[] creativeBeforeMutation = preserved.CreativePowers.ToArray();
        file.AsSpan().Fill(0xFF);

        Assert.Equal(headerBeforeMutation, preserved.Header.ToArray());
        Assert.Equal(creativeBeforeMutation, preserved.CreativePowers.ToArray());
    }

    [Fact]
    public void Seekable_capture_matches_span_capture_and_restores_caller_position()
    {
        int[] offsets =
        [
            10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110
        ];
        byte[] file = Enumerable.Range(0, 120).Select(static value => (byte)value).ToArray();
        var envelope = Envelope(offsets);
        Assert.True(WorldFilePreservedSections.TryCapture(file, envelope, out WorldFilePreservedSections? expected));
        Assert.NotNull(expected);

        using var stream = new MemoryStream(file, writable: false);
        stream.Position = 37;
        Assert.True(WorldFilePreservedSections.TryCapture(stream, envelope, out WorldFilePreservedSections? actual));
        Assert.NotNull(actual);

        Assert.Equal(37, stream.Position);
        Assert.Equal(expected!.Header.ToArray(), actual!.Header.ToArray());
        Assert.Equal(expected.Signs.ToArray(), actual.Signs.ToArray());
        Assert.Equal(expected.Npcs.ToArray(), actual.Npcs.ToArray());
        Assert.Equal(expected.TileEntities.ToArray(), actual.TileEntities.ToArray());
        Assert.Equal(expected.PressurePlates.ToArray(), actual.PressurePlates.ToArray());
        Assert.Equal(expected.TownRooms.ToArray(), actual.TownRooms.ToArray());
        Assert.Equal(expected.Bestiary.ToArray(), actual.Bestiary.ToArray());
        Assert.Equal(expected.CreativePowers.ToArray(), actual.CreativePowers.ToArray());
    }

    [Fact]
    public void Capture_rejects_preserved_section_outside_source_file()
    {
        int[] offsets =
        [
            10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 200
        ];
        byte[] file = new byte[120];

        Assert.False(WorldFilePreservedSections.TryCapture(file, Envelope(offsets), out WorldFilePreservedSections? preserved));
        Assert.Null(preserved);
    }

    private static WorldFileEnvelope Envelope(int[] offsets) =>
        new(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 1,
            favoriteFlags: 0,
            offsets,
            VanillaWorldFormat326.TileTypeCount,
            new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3]);
}
