using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerSpatialIndexTests
{
    [Fact]
    public void Nearby_query_tracks_section_moves_teleports_and_removal()
    {
        var dimensions = new WorldDimensions(8_400, 2_400);
        var index = new RuntimePlayerSpatialIndex(dimensions);
        var subject = new PlayerSlotId(0);
        var near = new PlayerSlotId(1);
        var far = new PlayerSlotId(2);

        Assert.True(index.Update(subject, PixelsAtTile(10), PixelsAtTile(10)));
        Assert.True(index.Update(near, PixelsAtTile(205), PixelsAtTile(10)));
        Assert.True(index.Update(far, PixelsAtTile(605), PixelsAtTile(10)));

        Span<PlayerSlotId> recipients = stackalloc PlayerSlotId[256];
        int count = index.CollectNearbyPlayers(subject, radiusSections: 1, recipients);

        Assert.Equal(1, count);
        Assert.Equal(near, recipients[0]);
        Assert.Equal(3, index.Snapshot.IndexedPlayers);

        Assert.True(index.Update(near, PixelsAtTile(805), PixelsAtTile(10)));
        Assert.Equal(0, index.CollectNearbyPlayers(subject, radiusSections: 1, recipients));
        Assert.Equal(1, index.Snapshot.SectionChanges);

        Assert.True(index.Update(subject, PixelsAtTile(810), PixelsAtTile(10)));
        count = index.CollectNearbyPlayers(subject, radiusSections: 0, recipients);
        Assert.Equal(1, count);
        Assert.Equal(near, recipients[0]);

        Assert.True(index.Remove(near));
        Assert.Equal(0, index.CollectNearbyPlayers(subject, radiusSections: 0, recipients));
        Assert.Equal(2, index.Snapshot.IndexedPlayers);
    }

    [Fact]
    public void Invalid_or_out_of_world_position_fails_open_by_removing_spatial_membership()
    {
        var dimensions = new WorldDimensions(400, 300);
        var index = new RuntimePlayerSpatialIndex(dimensions);
        var slot = new PlayerSlotId(255);

        Assert.True(index.Update(slot, PixelsAtTile(399) + 15f, PixelsAtTile(299) + 15f));
        Assert.True(index.TryGetSection(slot, out WorldSectionId section));
        Assert.Equal(new WorldSectionId(1, 1), section);

        Assert.False(index.Update(slot, dimensions.WidthTiles * 16f, 0f));
        Assert.False(index.TryGetSection(slot, out _));
        Assert.Equal(0, index.Snapshot.IndexedPlayers);
        Assert.Equal(1, index.Snapshot.OutOfBoundsUpdates);

        Assert.False(index.Update(slot, float.NaN, 0f));
        Assert.Equal(2, index.Snapshot.OutOfBoundsUpdates);
    }

    [Fact]
    public void Query_can_include_subject_without_duplicate_slots()
    {
        var dimensions = new WorldDimensions(400, 300);
        var index = new RuntimePlayerSpatialIndex(dimensions);
        var first = new PlayerSlotId(3);
        var second = new PlayerSlotId(67);

        Assert.True(index.Update(first, PixelsAtTile(10), PixelsAtTile(10)));
        Assert.True(index.Update(second, PixelsAtTile(20), PixelsAtTile(20)));

        Span<PlayerSlotId> recipients = stackalloc PlayerSlotId[256];
        int count = index.CollectNearbyPlayers(first, radiusSections: 0, recipients, includeSubject: true);

        Assert.Equal(2, count);
        Assert.Equal(first, recipients[0]);
        Assert.Equal(second, recipients[1]);
    }

    private static float PixelsAtTile(int tile) => tile * 16f;
}
