using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerVisibilityTrackerTests
{
    [Fact]
    public void Hysteresis_keeps_visible_pair_until_leave_radius_is_exceeded()
    {
        var dimensions = new WorldDimensions(2_400, 300);
        var index = new RuntimePlayerSpatialIndex(dimensions);
        var tracker = new RuntimePlayerVisibilityTracker(index, enterRadiusSections: 2, leaveRadiusSections: 3);
        var first = new PlayerSlotId(1);
        var second = new PlayerSlotId(2);
        Span<PlayerSlotId> entered = stackalloc PlayerSlotId[256];
        Span<PlayerSlotId> left = stackalloc PlayerSlotId[256];

        Assert.True(index.Update(first, PixelsAtSection(0), PixelsAtSection(0)));
        Assert.True(index.Update(second, PixelsAtSection(2), PixelsAtSection(0)));

        RuntimePlayerVisibilityUpdate update = tracker.Refresh(first, entered, left);
        Assert.Equal(new RuntimePlayerVisibilityUpdate(1, 0, 0), update);
        Assert.Equal(second, entered[0]);
        Assert.True(tracker.IsVisible(first, second));
        Assert.True(tracker.IsVisible(second, first));

        Assert.True(index.Update(second, PixelsAtSection(3), PixelsAtSection(0)));
        update = tracker.Refresh(second, entered, left);
        Assert.Equal(new RuntimePlayerVisibilityUpdate(0, 1, 0), update);
        Assert.True(tracker.IsVisible(first, second));

        Assert.True(index.Update(second, PixelsAtSection(4), PixelsAtSection(0)));
        update = tracker.Refresh(second, entered, left);
        Assert.Equal(new RuntimePlayerVisibilityUpdate(0, 0, 1), update);
        Assert.Equal(first, left[0]);
        Assert.False(tracker.IsVisible(first, second));

        Assert.True(index.Update(second, PixelsAtSection(3), PixelsAtSection(0)));
        update = tracker.Refresh(second, entered, left);
        Assert.Equal(new RuntimePlayerVisibilityUpdate(0, 0, 0), update);
        Assert.False(tracker.IsVisible(first, second));

        Assert.True(index.Update(second, PixelsAtSection(2), PixelsAtSection(0)));
        update = tracker.Refresh(second, entered, left);
        Assert.Equal(new RuntimePlayerVisibilityUpdate(1, 0, 0), update);
        Assert.Equal(first, entered[0]);
        Assert.True(tracker.IsVisible(first, second));
    }

    [Fact]
    public void Teleport_reports_leave_and_enter_in_one_refresh()
    {
        var dimensions = new WorldDimensions(2_400, 300);
        var index = new RuntimePlayerSpatialIndex(dimensions);
        var tracker = new RuntimePlayerVisibilityTracker(index, enterRadiusSections: 1, leaveRadiusSections: 2);
        var subject = new PlayerSlotId(10);
        var oldPeer = new PlayerSlotId(11);
        var newPeer = new PlayerSlotId(12);
        Span<PlayerSlotId> entered = stackalloc PlayerSlotId[256];
        Span<PlayerSlotId> left = stackalloc PlayerSlotId[256];

        Assert.True(index.Update(subject, PixelsAtSection(1), PixelsAtSection(0)));
        Assert.True(index.Update(oldPeer, PixelsAtSection(0), PixelsAtSection(0)));
        Assert.True(index.Update(newPeer, PixelsAtSection(8), PixelsAtSection(0)));
        Assert.Equal(1, tracker.Refresh(subject, entered, left).Entered);
        Assert.True(tracker.IsVisible(subject, oldPeer));

        Assert.True(index.Update(subject, PixelsAtSection(7), PixelsAtSection(0)));
        RuntimePlayerVisibilityUpdate update = tracker.Refresh(subject, entered, left);

        Assert.Equal(new RuntimePlayerVisibilityUpdate(1, 0, 1), update);
        Assert.Equal(newPeer, entered[0]);
        Assert.Equal(oldPeer, left[0]);
        Assert.False(tracker.IsVisible(subject, oldPeer));
        Assert.True(tracker.IsVisible(subject, newPeer));
        Assert.Equal(1, tracker.Snapshot.VisiblePairs);
    }

    [Fact]
    public void Invalid_position_clears_stale_visibility_membership()
    {
        var dimensions = new WorldDimensions(800, 300);
        var index = new RuntimePlayerSpatialIndex(dimensions);
        var tracker = new RuntimePlayerVisibilityTracker(index, enterRadiusSections: 1, leaveRadiusSections: 2);
        var subject = new PlayerSlotId(20);
        var peer = new PlayerSlotId(21);
        Span<PlayerSlotId> entered = stackalloc PlayerSlotId[256];
        Span<PlayerSlotId> left = stackalloc PlayerSlotId[256];

        Assert.True(index.Update(subject, PixelsAtSection(0), PixelsAtSection(0)));
        Assert.True(index.Update(peer, PixelsAtSection(1), PixelsAtSection(0)));
        Assert.Equal(1, tracker.Refresh(subject, entered, left).Entered);

        Assert.False(index.Update(subject, float.NaN, 0f));
        RuntimePlayerVisibilityUpdate update = tracker.Refresh(subject, entered, left);

        Assert.Equal(new RuntimePlayerVisibilityUpdate(0, 0, 1), update);
        Assert.Equal(peer, left[0]);
        Assert.False(tracker.IsVisible(subject, peer));
        Assert.Equal(0, tracker.Snapshot.VisiblePairs);
    }

    [Fact]
    public void Router_maintains_visibility_while_interest_management_is_disabled()
    {
        var control = new InterestManagementControl(enabled: false);
        var router = new RuntimeInterestRouter(
            control,
            new WorldDimensions(1_200, 300),
            playerEnterRadiusSections: 1,
            playerLeaveRadiusSections: 2);
        var first = new PlayerSlotId(30);
        var second = new PlayerSlotId(31);

        router.TrackPlayer(first, PixelsAtSection(0), PixelsAtSection(0));
        RuntimePlayerVisibilityUpdate update = router.TrackPlayer(second, PixelsAtSection(1), PixelsAtSection(0));

        Assert.Equal(1, update.Entered);
        Assert.True(router.IsPlayerVisible(first, second));
        Assert.Equal(1, router.PlayerVisibilitySnapshot?.VisiblePairs);

        var observer = new RuntimePlayerInterestState(first, true, PixelsAtSection(0), PixelsAtSection(0));
        var subject = new RuntimePlayerInterestState(second, true, PixelsAtSection(5), PixelsAtSection(0));
        Assert.True(router.ShouldRelayPlayerMovement(in observer, in subject));
    }

    [Fact]
    public void Constructor_rejects_leave_radius_smaller_than_enter_radius()
    {
        var index = new RuntimePlayerSpatialIndex(new WorldDimensions(400, 300));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RuntimePlayerVisibilityTracker(index, enterRadiusSections: 3, leaveRadiusSections: 2));
    }

    private static float PixelsAtSection(int section) =>
        ((section * TerrariaSectionGeometry.WidthTiles) + 10) * 16f;
}
