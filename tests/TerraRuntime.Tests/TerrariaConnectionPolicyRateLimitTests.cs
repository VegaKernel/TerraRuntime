using System.Buffers;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TerrariaConnectionPolicyRateLimitTests
{
    [Fact]
    public void Default_policy_enables_connection_and_expensive_message_hard_abuse_ceilings()
    {
        TerrariaConnectionPolicyOptions options = TerrariaConnectionPolicyOptions.Default;
        ConnectionMessageRateLimits limits = options.MessageRateLimits;

        Assert.Equal(TimeSpan.FromSeconds(1), options.RateBudget.Window);
        Assert.Equal(4_096, options.RateBudget.MaxFrames);
        Assert.Equal(2L * 1024 * 1024, options.RateBudget.MaxBytes);

        Assert.Equal(16, limits.Count);
        AssertBudget(limits, TerrariaMessageId.RequestWorldData, 16, 4 * 1024);
        AssertBudget(limits, TerrariaMessageId.SpawnTileData, 120, 16 * 1024);
        AssertBudget(limits, TerrariaMessageId.PlayerControls, 600, 96 * 1024);
        AssertBudget(limits, TerrariaMessageId.SyncEquipment, 600, 64 * 1024);
        AssertBudget(limits, TerrariaMessageId.TileManipulation, 480, 64 * 1024);
        AssertBudget(limits, TerrariaMessageId.WorldItemDrop, 240, 64 * 1024);
        AssertBudget(limits, TerrariaMessageId.ProjectileNew, 1_200, 256 * 1024);
        AssertBudget(limits, TerrariaMessageId.RequestChestOpen, 120, 16 * 1024);
        AssertBudget(limits, TerrariaMessageId.LoadNetModule, 120, 256 * 1024);

        Assert.False(limits.TryGet((byte)TerrariaMessageId.Hello, out _));
        Assert.False(limits.TryGet((byte)TerrariaMessageId.PlayerInfo, out _));
    }

    [Fact]
    public void Explicit_policy_constructor_remains_message_limit_free()
    {
        var options = new TerrariaConnectionPolicyOptions(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30));

        Assert.Equal(ConnectionRateBudgetOptions.AccountingOnly, options.RateBudget);
        Assert.Equal(0, options.MessageRateLimits.Count);
    }

    [Fact]
    public void Stops_before_forwarding_a_frame_that_exceeds_the_configured_rate_budget()
    {
        var state = new TerrariaConnectionPolicyState(
            new TerrariaConnectionPolicyOptions(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)));
        var accountant = new TerrariaConnectionRateAccountant(
            new ConnectionRateBudgetOptions(TimeSpan.FromSeconds(1), maxFrames: 1, maxBytes: null));
        var inner = new CountingSink();
        var policy = new TerrariaConnectionPolicySink(inner, state, accountant);
        TerrariaFrame hello = Decode(CurrentHelloPacket());
        TerrariaFrame next = Decode([3, 0, (byte)TerrariaMessageId.PlayerInfo]);

        Assert.Equal(TerrariaFrameSinkResult.Continue, policy.OnFrame(in hello));
        Assert.Equal(TerrariaFrameSinkResult.Stop, policy.OnFrame(in next));
        Assert.Equal(TerrariaConnectionStopReason.RateLimited, state.StopReason);
        Assert.Equal(1, inner.Count);
        Assert.Equal(1, accountant.Snapshot.RejectedFrames);
    }

    [Fact]
    public void Stops_only_when_the_configured_message_budget_is_exceeded_and_reports_the_reject_aggregately()
    {
        var messageLimits = new ConnectionMessageRateLimits(
            new ConnectionMessageRateRule(
                (byte)TerrariaMessageId.ProjectileNew,
                new ConnectionRateBudgetOptions(TimeSpan.FromSeconds(1), maxFrames: 1, maxBytes: null)));
        var options = new TerrariaConnectionPolicyOptions(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            ConnectionRateBudgetOptions.AccountingOnly,
            messageLimits);
        var state = new TerrariaConnectionPolicyState(options);
        var accountant = new TerrariaConnectionRateAccountant(
            ConnectionRateBudgetOptions.AccountingOnly,
            state.TimeProvider);
        var inner = new CountingSink();
        var policy = new TerrariaConnectionPolicySink(inner, state, accountant);
        TerrariaFrame hello = Decode(CurrentHelloPacket());
        TerrariaFrame firstProjectile = Decode([3, 0, (byte)TerrariaMessageId.ProjectileNew]);
        TerrariaFrame movement = Decode([3, 0, (byte)TerrariaMessageId.PlayerControls]);
        TerrariaFrame secondProjectile = Decode([3, 0, (byte)TerrariaMessageId.ProjectileNew]);

        Assert.Equal(TerrariaFrameSinkResult.Continue, policy.OnFrame(in hello));
        Assert.Equal(TerrariaFrameSinkResult.Continue, policy.OnFrame(in firstProjectile));
        Assert.Equal(TerrariaFrameSinkResult.Continue, policy.OnFrame(in movement));
        Assert.Equal(TerrariaFrameSinkResult.Stop, policy.OnFrame(in secondProjectile));
        Assert.Equal(TerrariaConnectionStopReason.RateLimited, state.StopReason);
        Assert.Equal(3, inner.Count);
        Assert.Equal(4, accountant.Snapshot.TotalFrames);
        Assert.Equal(1, accountant.Snapshot.RejectedFrames);
    }

    [Fact]
    public void Rejects_duplicate_message_rate_rules()
    {
        var budget = new ConnectionRateBudgetOptions(TimeSpan.FromSeconds(1), maxFrames: 1, maxBytes: null);

        Assert.Throws<ArgumentException>(() => new ConnectionMessageRateLimits(
            new ConnectionMessageRateRule((byte)TerrariaMessageId.ProjectileNew, budget),
            new ConnectionMessageRateRule((byte)TerrariaMessageId.ProjectileNew, budget)));
    }

    [Fact]
    public void Unconfigured_message_ids_do_not_get_message_specific_accounting()
    {
        var limits = new ConnectionMessageRateLimits(
            new ConnectionMessageRateRule(
                (byte)TerrariaMessageId.ProjectileNew,
                new ConnectionRateBudgetOptions(TimeSpan.FromSeconds(1), maxFrames: 1, maxBytes: null)));
        var accountant = new TerrariaMessageRateAccountant(limits);

        Assert.Equal(
            ConnectionRateDecision.Allowed,
            accountant.Observe((byte)TerrariaMessageId.PlayerControls, 3));
        Assert.Equal(default, accountant.GetSnapshot((byte)TerrariaMessageId.PlayerControls));
        Assert.Equal(
            ConnectionRateDecision.Allowed,
            accountant.Observe((byte)TerrariaMessageId.ProjectileNew, 3));
        Assert.Equal(
            ConnectionRateDecision.FrameLimitExceeded,
            accountant.Observe((byte)TerrariaMessageId.ProjectileNew, 3));
        Assert.Equal(2, accountant.GetSnapshot((byte)TerrariaMessageId.ProjectileNew).TotalFrames);
        Assert.Equal(1, accountant.GetSnapshot((byte)TerrariaMessageId.ProjectileNew).RejectedFrames);
    }

    private static void AssertBudget(
        ConnectionMessageRateLimits limits,
        TerrariaMessageId messageId,
        int expectedFrames,
        long expectedBytes)
    {
        Assert.True(limits.TryGet((byte)messageId, out ConnectionRateBudgetOptions budget));
        Assert.Equal(TimeSpan.FromSeconds(1), budget.Window);
        Assert.Equal(expectedFrames, budget.MaxFrames);
        Assert.Equal(expectedBytes, budget.MaxBytes);
    }

    private static TerrariaFrame Decode(byte[] packet)
    {
        var input = new ReadOnlySequence<byte>(packet);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        return frame;
    }

    private static byte[] CurrentHelloPacket() =>
    [
        15, 0,
        (byte)TerrariaMessageId.Hello,
        11,
        (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a',
        (byte)'3', (byte)'2', (byte)'6'
    ];

    private sealed class CountingSink : ITerrariaFrameSink
    {
        public int Count { get; private set; }

        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
        {
            Count++;
            return TerrariaFrameSinkResult.Continue;
        }
    }
}
