using System.Buffers;
using System.Text;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class ProductionHandshakeStopReasonTests
{
    [Fact]
    public void Unsupported_protocol_flows_through_production_sink_chain_as_precise_lifetime_reason()
    {
        using ProductionChain chain = CreateProductionChain();
        TerrariaFrame hello = Hello("Terraria325");

        TerrariaFrameSinkResult result = chain.Policy.OnFrame(in hello);

        Assert.Equal(TerrariaFrameSinkResult.Stop, result);
        Assert.Equal(PlayerBootstrapStopReason.InvalidHandshake, chain.Bootstrap.StopReason);
        Assert.Equal(TerrariaFrameRejectionCategory.None, chain.Top.RejectionCategory);
        Assert.Equal(TerrariaConnectionStopReason.UnsupportedProtocol, chain.Top.ConnectionStopReason);
        Assert.Equal(TerrariaConnectionStopReason.UnsupportedProtocol, chain.State.StopReason);
        Assert.False(chain.State.HandshakeComplete);
    }

    [Fact]
    public void Malformed_version_banner_remains_malformed_frame_rejection_not_unsupported_protocol()
    {
        using ProductionChain chain = CreateProductionChain();
        TerrariaFrame hello = Hello("TerrariaXYZ");

        TerrariaFrameSinkResult result = chain.Policy.OnFrame(in hello);

        Assert.Equal(TerrariaFrameSinkResult.Stop, result);
        Assert.Equal(PlayerBootstrapStopReason.InvalidHandshake, chain.Bootstrap.StopReason);
        Assert.Equal(TerrariaFrameRejectionCategory.MalformedProtocol, chain.Top.RejectionCategory);
        Assert.Equal(TerrariaConnectionStopReason.None, chain.Top.ConnectionStopReason);
        Assert.Equal(TerrariaConnectionStopReason.FrameRejected, chain.State.StopReason);
        Assert.False(chain.State.HandshakeComplete);
    }

    private static ProductionChain CreateProductionChain()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(991);
        var commands = new AcceptingCommandIngress();
        var bootstrap = new PlayerBootstrapFrameSink(
            new PlayerSlotPool(1),
            new TerrariaConnectionOutboundQueue(
                new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 8_192, maxFrameBytes: 2_048)),
            PlayerBootstrapPacketSet.CreateForTesting(
                new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                Array.Empty<ReadOnlyMemory<byte>>(),
                new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }),
            source,
            new AcceptingSpawnIngress());
        var vitals = new PlayerVitalsFrameSink(
            source,
            bootstrap,
            new RuntimePlayerHealthIngress(commands),
            new RuntimePlayerManaIngress(commands));
        var items = new WorldItemFrameSink(
            source,
            bootstrap,
            vitals,
            new RuntimeWorldItemIngress(commands));
        var projectiles = new ProjectileLifecycleFrameSink(
            source,
            bootstrap,
            items,
            new RuntimeProjectileNetworkIngress(commands));
        var chests = new ChestInteractionFrameSink(
            source,
            bootstrap,
            projectiles,
            new RuntimeChestNetworkIngress(commands));
        var signs = new SignInteractionFrameSink(
            source,
            bootstrap,
            chests,
            new RuntimeSignNetworkIngress(commands));
        var state = new TerrariaConnectionPolicyState(new TerrariaConnectionPolicyOptions(
            handshakeTimeout: TimeSpan.FromSeconds(10),
            idleTimeout: Timeout.InfiniteTimeSpan,
            rateBudget: ConnectionRateBudgetOptions.AccountingOnly,
            messageRateLimits: ConnectionMessageRateLimits.None,
            joinTimeout: Timeout.InfiniteTimeSpan));
        var policy = new TerrariaConnectionPolicySink(signs, state);
        return new ProductionChain(bootstrap, signs, state, policy);
    }

    private static TerrariaFrame Hello(string version)
    {
        byte[] versionBytes = Encoding.ASCII.GetBytes(version);
        byte[] payload = new byte[versionBytes.Length + 1];
        payload[0] = checked((byte)versionBytes.Length);
        versionBytes.CopyTo(payload.AsSpan(1));
        return new TerrariaFrame(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)TerrariaMessageId.Hello,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));
    }

    private sealed class AcceptingCommandIngress : IGameCommandIngress<RuntimeCommand>
    {
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command) => true;
    }

    private sealed class AcceptingSpawnIngress : IPlayerSpawnCommitIngress
    {
        public bool TryPost(
            GameCommandSourceId source,
            PlayerJoinSession session,
            in PlayerSpawnCommitRequest request) => true;
    }

    private sealed class ProductionChain(
        PlayerBootstrapFrameSink bootstrap,
        SignInteractionFrameSink top,
        TerrariaConnectionPolicyState state,
        TerrariaConnectionPolicySink policy) : IDisposable
    {
        public PlayerBootstrapFrameSink Bootstrap { get; } = bootstrap;
        public SignInteractionFrameSink Top { get; } = top;
        public TerrariaConnectionPolicyState State { get; } = state;
        public TerrariaConnectionPolicySink Policy { get; } = policy;

        public void Dispose() => Bootstrap.Dispose();
    }
}
