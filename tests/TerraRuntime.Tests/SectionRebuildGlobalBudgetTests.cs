using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SectionRebuildGlobalBudgetTests
{
    [Fact]
    public void Hard_abuse_budget_is_server_wide_and_not_player_scaled()
    {
        SectionRebuildGlobalBudgetOptions options = SectionRebuildGlobalBudgetOptions.HardAbuse;

        Assert.Equal(TimeSpan.FromSeconds(1), options.Window);
        Assert.Equal(byte.MaxValue, options.MaxUniqueRequests);
    }

    [Fact]
    public void Duplicate_single_flight_waiters_do_not_consume_unique_budget()
    {
        var time = new ManualTimeProvider();
        var budget = new SectionRebuildGlobalBudget(
            new SectionRebuildGlobalBudgetOptions(TimeSpan.FromSeconds(1), MaxUniqueRequests: 2),
            time);
        var requester = new FakeRequester();
        WorldSectionId first = new(0, 0);
        WorldSectionId second = new(1, 0);
        WorldSectionId third = new(2, 0);

        SectionRebuildRequestTicket firstTicket = budget.Request(0, first, requester.Request);
        SectionRebuildRequestTicket duplicate = budget.Request(0, first, requester.Request);
        SectionRebuildRequestTicket secondTicket = budget.Request(1, second, requester.Request);
        SectionRebuildRequestTicket rejected = budget.Request(2, third, requester.Request);

        Assert.True(firstTicket.Accepted);
        Assert.True(duplicate.Accepted);
        Assert.Equal(firstTicket.Generation, duplicate.Generation);
        Assert.True(secondTicket.Accepted);
        Assert.False(rejected.Accepted);
        Assert.Equal(3, requester.CallCount);

        SectionRebuildGlobalBudgetSnapshot snapshot = budget.Snapshot;
        Assert.Equal(4, snapshot.Requests);
        Assert.Equal(2, snapshot.UniqueAdmissions);
        Assert.Equal(1, snapshot.DeduplicatedRequests);
        Assert.Equal(1, snapshot.RejectedRequests);
        Assert.Equal(2, snapshot.ActiveGenerations);
        Assert.Equal(2, snapshot.CurrentWindowUniqueAdmissions);
    }

    [Fact]
    public void Completed_generation_still_counts_until_fixed_window_rolls()
    {
        var time = new ManualTimeProvider();
        var budget = new SectionRebuildGlobalBudget(
            new SectionRebuildGlobalBudgetOptions(TimeSpan.FromSeconds(1), MaxUniqueRequests: 1),
            time);
        var requester = new FakeRequester();
        WorldSectionId first = new(0, 0);
        WorldSectionId second = new(1, 0);

        SectionRebuildRequestTicket firstTicket = budget.Request(0, first, requester.Request);
        Assert.True(firstTicket.Accepted);
        budget.Complete(0, firstTicket.Generation);
        requester.Complete(first);

        Assert.False(budget.Request(1, second, requester.Request).Accepted);
        Assert.Equal(1, requester.CallCount);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(budget.Request(1, second, requester.Request).Accepted);

        SectionRebuildGlobalBudgetSnapshot snapshot = budget.Snapshot;
        Assert.Equal(2, snapshot.UniqueAdmissions);
        Assert.Equal(1, snapshot.RejectedRequests);
        Assert.Equal(1, snapshot.CurrentWindowUniqueAdmissions);
    }

    [Fact]
    public void Coordinator_rejection_refunds_the_window_admission()
    {
        var time = new ManualTimeProvider();
        var budget = new SectionRebuildGlobalBudget(
            new SectionRebuildGlobalBudgetOptions(TimeSpan.FromSeconds(1), MaxUniqueRequests: 1),
            time);
        WorldSectionId first = new(0, 0);
        WorldSectionId second = new(1, 0);
        int calls = 0;

        SectionRebuildRequestTicket rejected = budget.Request(
            0,
            first,
            _ =>
            {
                calls++;
                return SectionRebuildRequestTicket.Rejected;
            });
        SectionRebuildRequestTicket accepted = budget.Request(
            1,
            second,
            _ =>
            {
                calls++;
                return new SectionRebuildRequestTicket(true, Generation: 1);
            });

        Assert.False(rejected.Accepted);
        Assert.True(accepted.Accepted);
        Assert.Equal(2, calls);

        SectionRebuildGlobalBudgetSnapshot snapshot = budget.Snapshot;
        Assert.Equal(1, snapshot.UniqueAdmissions);
        Assert.Equal(1, snapshot.RejectedRequests);
        Assert.Equal(1, snapshot.CurrentWindowUniqueAdmissions);
    }

    private sealed class FakeRequester
    {
        private readonly Dictionary<WorldSectionId, long> active = [];
        private long nextGeneration;

        public int CallCount { get; private set; }

        public SectionRebuildRequestTicket Request(WorldSectionId section)
        {
            CallCount++;
            if (active.TryGetValue(section, out long generation))
                return new SectionRebuildRequestTicket(true, generation);

            generation = ++nextGeneration;
            active.Add(section, generation);
            return new SectionRebuildRequestTicket(true, generation);
        }

        public void Complete(WorldSectionId section) => active.Remove(section);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref timestamp);

        public void Advance(TimeSpan duration) => Interlocked.Add(ref timestamp, duration.Ticks);
    }
}
