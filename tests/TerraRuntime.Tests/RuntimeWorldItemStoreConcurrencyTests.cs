using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldItemStoreConcurrencyTests
{
    [Fact]
    public async Task Concurrent_readers_observe_only_complete_single_writer_snapshots()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var store = new RuntimeWorldItemStore();
        WorldItemStateUpdate first = CreateFirst();
        WorldItemStateUpdate second = CreateSecond();
        Assert.True(store.TryUpsert(7, in first, out _));

        using var start = new ManualResetEventSlim(false);
        Task writer = Task.Run(() =>
        {
            start.Wait(cancellationToken);
            for (int i = 0; i < 25_000; i++)
            {
                if ((i & 255) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                WorldItemStateUpdate update = (i & 1) == 0 ? first : second;
                Assert.True(store.TryUpsert(7, in update, out _));
            }
        }, cancellationToken);

        Task pointReader = Task.Run(() =>
        {
            start.Wait(cancellationToken);
            for (int i = 0; i < 25_000; i++)
            {
                if ((i & 255) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                Assert.True(store.TryGetActive(7, out WorldItemSnapshot snapshot));
                AssertConsistent(snapshot);
            }
        }, cancellationToken);

        Task bulkReader = Task.Run(() =>
        {
            start.Wait(cancellationToken);
            var snapshots = new WorldItemSnapshot[RuntimeWorldItemStore.VanillaCapacity];
            for (int i = 0; i < 10_000; i++)
            {
                if ((i & 127) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                int count = store.CopyActive(snapshots);
                Assert.Equal(1, count);
                Assert.Equal((short)7, snapshots[0].Handle.Slot);
                AssertConsistent(snapshots[0]);
            }
        }, cancellationToken);

        start.Set();
        await Task.WhenAll(writer, pointReader, bulkReader);

        Assert.Equal(1, store.ActiveCount);
        Assert.True(store.TryGetActive(7, out WorldItemSnapshot finalSnapshot));
        AssertConsistent(finalSnapshot);
    }

    private static void AssertConsistent(WorldItemSnapshot snapshot)
    {
        switch (snapshot.ItemNetId)
        {
            case 100:
                Assert.Equal((short)2, snapshot.Stack);
                Assert.Equal((byte)1, snapshot.Prefix);
                Assert.Equal(100f, snapshot.PositionX);
                Assert.Equal(101f, snapshot.PositionY);
                Assert.Equal(1f, snapshot.VelocityX);
                Assert.Equal(-1f, snapshot.VelocityY);
                Assert.False(snapshot.Shimmered);
                Assert.Equal(0f, snapshot.ShimmerTime);
                Assert.Equal((byte)1, snapshot.OwnerPlayerId);
                Assert.Equal(2, snapshot.TimeToKeepReservation);
                Assert.Equal((byte)3, snapshot.GrabDelayPlayer);
                Assert.Equal(4, snapshot.GrabDelayTime);
                break;

            case 200:
                Assert.Equal((short)3, snapshot.Stack);
                Assert.Equal((byte)2, snapshot.Prefix);
                Assert.Equal(200f, snapshot.PositionX);
                Assert.Equal(201f, snapshot.PositionY);
                Assert.Equal(2f, snapshot.VelocityX);
                Assert.Equal(-2f, snapshot.VelocityY);
                Assert.True(snapshot.Shimmered);
                Assert.Equal(5f, snapshot.ShimmerTime);
                Assert.Equal((byte)7, snapshot.OwnerPlayerId);
                Assert.Equal(8, snapshot.TimeToKeepReservation);
                Assert.Equal((byte)9, snapshot.GrabDelayPlayer);
                Assert.Equal(10, snapshot.GrabDelayTime);
                break;

            default:
                throw new Xunit.Sdk.XunitException($"Observed torn/unexpected item state: netId={snapshot.ItemNetId}.");
        }
    }

    private static WorldItemStateUpdate CreateFirst() =>
        new(
            PositionX: 100f,
            PositionY: 101f,
            VelocityX: 1f,
            VelocityY: -1f,
            Stack: 2,
            Prefix: 1,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: 100,
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0,
            OwnerPlayerId: 1,
            TimeToKeepReservation: 2,
            GrabDelayPlayer: 3,
            GrabDelayTime: 4);

    private static WorldItemStateUpdate CreateSecond() =>
        new(
            PositionX: 200f,
            PositionY: 201f,
            VelocityX: 2f,
            VelocityY: -2f,
            Stack: 3,
            Prefix: 2,
            Ownership: WorldItemOwnershipMode.ReserveForLocalPlayer,
            ItemNetId: 200,
            Shimmered: true,
            ShimmerTime: 5f,
            EnemyGrabDelayTime: 6,
            OwnerPlayerId: 7,
            TimeToKeepReservation: 8,
            GrabDelayPlayer: 9,
            GrabDelayTime: 10);
}
