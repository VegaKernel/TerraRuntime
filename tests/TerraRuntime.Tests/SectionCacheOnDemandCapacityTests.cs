using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SectionCacheOnDemandCapacityTests
{
    [Fact]
    public void Default_capacity_matches_the_player_slot_ceiling()
    {
        WorldFileData world = LoadCompleteWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        using var pipeline = new SectionCacheRebuildPipeline(
            world,
            packets,
            workerCount: 1,
            workCapacity: 1,
            completionCapacity: 1);

        Assert.Equal(byte.MaxValue, pipeline.Snapshot.OnDemandCapacity);
    }

    [Fact]
    public async Task Distinct_requests_respect_capacity_while_same_section_still_deduplicates()
    {
        WorldFileData world = CreateMultiSectionWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        using var pipeline = new SectionCacheRebuildPipeline(
            world,
            packets,
            workerCount: 1,
            workCapacity: 1,
            completionCapacity: 1,
            onDemandCapacity: 1);

        WorldSectionId firstSection = new(0, 0);
        WorldSectionId secondSection = new(1, 1);
        Task<SectionRebuildRequestTicket> first = Task.Run(
            () => pipeline.RequestSection(firstSection),
            TestContext.Current.CancellationToken);
        Task<SectionRebuildRequestTicket> second = Task.Run(
            () => pipeline.RequestSection(secondSection),
            TestContext.Current.CancellationToken);

        SectionRebuildRequestTicket[] tickets = await Task.WhenAll(first, second);
        Assert.Equal(1, tickets.Count(static ticket => ticket.Accepted));
        Assert.Equal(1, tickets.Count(static ticket => !ticket.Accepted));

        WorldSectionId acceptedSection = tickets[0].Accepted ? firstSection : secondSection;
        SectionRebuildRequestTicket accepted = tickets[0].Accepted ? tickets[0] : tickets[1];
        SectionRebuildRequestTicket duplicate = pipeline.RequestSection(acceptedSection);

        Assert.True(duplicate.Accepted);
        Assert.Equal(accepted.Generation, duplicate.Generation);

        SectionCacheRebuildPipelineSnapshot snapshot = pipeline.Snapshot;
        Assert.Equal(1, snapshot.OnDemandCapacity);
        Assert.Equal(1, snapshot.OnDemandPendingRequests);
        Assert.Equal(3, snapshot.OnDemandRequests);
        Assert.Equal(1, snapshot.OnDemandUniqueRequests);
        Assert.Equal(1, snapshot.OnDemandDeduplicatedRequests);
        Assert.Equal(1, snapshot.OnDemandRejectedRequests);
    }

    [Fact]
    public async Task Concurrent_same_section_requests_share_one_admission_slot()
    {
        WorldFileData world = LoadCompleteWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        using var pipeline = new SectionCacheRebuildPipeline(
            world,
            packets,
            workerCount: 1,
            workCapacity: 1,
            completionCapacity: 1,
            onDemandCapacity: 1);
        WorldSectionId section = new(0, 0);
        using var start = new ManualResetEventSlim(false);

        Task<SectionRebuildRequestTicket> first = Task.Run(
            () =>
            {
                start.Wait(TestContext.Current.CancellationToken);
                return pipeline.RequestSection(section);
            },
            TestContext.Current.CancellationToken);
        Task<SectionRebuildRequestTicket> second = Task.Run(
            () =>
            {
                start.Wait(TestContext.Current.CancellationToken);
                return pipeline.RequestSection(section);
            },
            TestContext.Current.CancellationToken);

        start.Set();
        SectionRebuildRequestTicket[] tickets = await Task.WhenAll(first, second);

        Assert.All(tickets, static ticket => Assert.True(ticket.Accepted));
        Assert.Equal(tickets[0].Generation, tickets[1].Generation);
        SectionCacheRebuildPipelineSnapshot snapshot = pipeline.Snapshot;
        Assert.Equal(1, snapshot.OnDemandPendingRequests);
        Assert.Equal(2, snapshot.OnDemandRequests);
        Assert.Equal(1, snapshot.OnDemandUniqueRequests);
        Assert.Equal(1, snapshot.OnDemandDeduplicatedRequests);
        Assert.Equal(0, snapshot.OnDemandRejectedRequests);
    }

    private static WorldFileData CreateMultiSectionWorld()
    {
        WorldFileData source = LoadCompleteWorld();
        var dimensions = new WorldDimensions(widthTiles: 201, heightTiles: 151);
        WorldFileHeader header = source.Header with
        {
            RightWorld = dimensions.WidthTiles * 16,
            BottomWorld = dimensions.HeightTiles * 16,
            Dimensions = dimensions
        };
        return source with
        {
            Header = header,
            Tiles = new WorldTileStore(dimensions)
        };
    }

    private static WorldFileData LoadCompleteWorld()
    {
        byte[] source = (byte[])InvokeWorldLoaderTestHelper("CreateCompleteCurrentWorld")!;
        WorldFileLoadLimits limits = (WorldFileLoadLimits)InvokeWorldLoaderTestHelper("CreateLimits")!;
        Assert.True(WorldFileLoader.TryLoad(source, limits, out WorldFileData? loaded).IsLoaded);
        return Assert.IsType<WorldFileData>(loaded);
    }

    private static object? InvokeWorldLoaderTestHelper(string name)
    {
        MethodInfo method = typeof(WorldFileLoaderTests).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"World loader test helper '{name}' was not found.");
        return method.Invoke(null, null);
    }
}
