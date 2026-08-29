using System.Reflection;
using TerraRuntime;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class SectionRebuildRateLimitClassificationTests
{
    [Fact]
    public void Global_budget_reports_rate_limit_separately_from_requester_rejection()
    {
        var budget = new SectionRebuildGlobalBudget(
            new SectionRebuildGlobalBudgetOptions(TimeSpan.FromSeconds(1), MaxUniqueRequests: 1));
        var firstSection = new TerraRuntime.World.WorldSectionId(0, 0);
        var secondSection = new TerraRuntime.World.WorldSectionId(1, 0);

        SectionRebuildGlobalBudgetRequestResult first = budget.RequestDetailed(
            sectionIndex: 0,
            firstSection,
            _ => new SectionRebuildRequestTicket(Accepted: true, Generation: 1),
            out SectionRebuildRequestTicket firstTicket);
        SectionRebuildGlobalBudgetRequestResult limited = budget.RequestDetailed(
            sectionIndex: 1,
            secondSection,
            _ => throw new Xunit.Sdk.XunitException("Rate-limited request reached the expensive requester."),
            out SectionRebuildRequestTicket limitedTicket);

        Assert.Equal(SectionRebuildGlobalBudgetRequestResult.Accepted, first);
        Assert.True(firstTicket.Accepted);
        Assert.Equal(SectionRebuildGlobalBudgetRequestResult.GlobalRateLimited, limited);
        Assert.False(limitedTicket.Accepted);
    }

    [Fact]
    public void Section_work_rate_limit_is_published_as_rate_limited_rejection()
    {
        var packets = PlayerBootstrapPacketSet.CreateForTesting(
            worldInfoFrame: new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
            baseSectionFrames: [],
            enterWorldFrame: new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf });
        using var bootstrap = new PlayerBootstrapFrameSink(
            new PlayerSlotPool(1),
            new TerrariaConnectionOutboundQueue(
                new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 4_096, maxFrameBytes: 1_024)),
            packets);
        var vitals = new PlayerVitalsFrameSink(
            GameCommandSourceId.FromConnection(1),
            bootstrap,
            AcceptingVitalsIngress.Instance,
            AcceptingVitalsIngress.Instance);

        PropertyInfo stopReason = typeof(PlayerBootstrapFrameSink).GetProperty(nameof(PlayerBootstrapFrameSink.StopReason))
            ?? throw new Xunit.Sdk.XunitException("Bootstrap stop reason property was not found.");
        stopReason.SetValue(bootstrap, PlayerBootstrapStopReason.SectionWorkRateLimited);

        Assert.Equal(TerrariaFrameRejectionCategory.RateLimited, vitals.RejectionCategory);
    }

    private sealed class AcceptingVitalsIngress : IPlayerHealthIngress, IPlayerManaIngress
    {
        public static AcceptingVitalsIngress Instance { get; } = new();

        public bool TryPost(ConnectionHandle connection, in PlayerHealthCommitRequest request) => true;

        public bool TryPost(ConnectionHandle connection, in PlayerManaCommitRequest request) => true;
    }
}
