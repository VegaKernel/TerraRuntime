using TerraRuntime.Gameplay.Players;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerMovementAuthorityTests
{
    [Fact]
    public void Impossible_velocity_is_rejected_before_the_authoritative_queue()
    {
        var sink = new CountingIngress();
        var authority = new RuntimePlayerMovementAuthority(enforcePositionDiscontinuities: true);
        var ingress = new RuntimePlayerMovementIngress(sink, authority);
        ConnectionHandle connection = Connection(slot: 4, generation: 1, source: 10);

        PlayerMovementCommitRequest baseline = Request(connection.Player.Slot, 1_000f, 1_000f);
        Assert.True(ingress.TryPost(connection, in baseline));

        PlayerMovementCommitRequest impossible = Request(connection.Player.Slot, 1_010f, 1_010f) with
        {
            MovementFlags = VanillaPlayerMovementNormalizer.MovementVelocityPresentFlag,
            HasVelocity = true,
            VelocityX = RuntimePlayerMovementAuthority.MaximumAbsoluteVelocity + 1f,
            VelocityY = 0f
        };

        Assert.False(ingress.TryPost(connection, in impossible));
        Assert.Equal(1, sink.Count);

        RuntimePlayerMovementAuthoritySnapshot snapshot = ingress.CaptureAuthoritySnapshot();
        Assert.Equal(1, snapshot.Accepted);
        Assert.Equal(1, snapshot.VelocityRejected);
        Assert.Equal(0, snapshot.PositionRejected);
    }

    [Fact]
    public void New_generation_supersedes_stale_session_history_before_queueing()
    {
        var sink = new CountingIngress();
        var ingress = new RuntimePlayerMovementIngress(
            sink,
            new RuntimePlayerMovementAuthority(enforcePositionDiscontinuities: true));
        ConnectionHandle first = Connection(slot: 5, generation: 1, source: 11);
        ConnectionHandle second = Connection(slot: 5, generation: 2, source: 12);

        PlayerMovementCommitRequest firstMovement = Request(first.Player.Slot, 500f, 500f);
        PlayerMovementCommitRequest secondMovement = Request(second.Player.Slot, 800f, 800f);

        Assert.True(ingress.TryPost(first, in firstMovement));
        Assert.True(ingress.TryPost(second, in secondMovement));
        Assert.False(ingress.TryPost(first, in firstMovement));
        Assert.Equal(2, sink.Count);

        RuntimePlayerMovementAuthoritySnapshot snapshot = ingress.CaptureAuthoritySnapshot();
        Assert.Equal(1, snapshot.TrackedPlayers);
        Assert.Equal(1, snapshot.StaleGenerationRejected);
    }

    [Fact]
    public void Position_discontinuity_requires_one_server_permit_in_strict_mode()
    {
        var sink = new CountingIngress();
        var ingress = new RuntimePlayerMovementIngress(
            sink,
            new RuntimePlayerMovementAuthority(enforcePositionDiscontinuities: true));
        ConnectionHandle connection = Connection(slot: 6, generation: 1, source: 13);

        PlayerMovementCommitRequest baseline = Request(connection.Player.Slot, 1_000f, 1_000f);
        PlayerMovementCommitRequest teleport = Request(connection.Player.Slot, 10_000f, 10_000f);
        Assert.True(ingress.TryPost(connection, in baseline));
        Assert.False(ingress.TryPost(connection, in teleport));

        Assert.True(ingress.TryGrantMovementException(
            connection,
            RuntimePlayerMovementExceptionKind.Teleport,
            TimeSpan.FromSeconds(1),
            targetX: 10_000f,
            targetY: 10_000f,
            targetRadiusPixels: 64f));
        Assert.True(ingress.TryPost(connection, in teleport));

        PlayerMovementCommitRequest secondTeleport = Request(connection.Player.Slot, 20_000f, 20_000f);
        Assert.False(ingress.TryPost(connection, in secondTeleport));

        RuntimePlayerMovementAuthoritySnapshot snapshot = ingress.CaptureAuthoritySnapshot();
        Assert.Equal(2, snapshot.Accepted);
        Assert.Equal(3, snapshot.PositionViolations);
        Assert.Equal(2, snapshot.PositionRejected);
        Assert.Equal(1, snapshot.ExceptionalAccepted);
    }

    [Fact]
    public void Default_production_policy_observes_position_jumps_without_false_positive_rejection()
    {
        var sink = new CountingIngress();
        var ingress = new RuntimePlayerMovementIngress(sink);
        ConnectionHandle connection = Connection(slot: 7, generation: 1, source: 14);

        PlayerMovementCommitRequest baseline = Request(connection.Player.Slot, 1_000f, 1_000f);
        PlayerMovementCommitRequest discontinuity = Request(connection.Player.Slot, 50_000f, 1_000f);
        Assert.True(ingress.TryPost(connection, in baseline));
        Assert.True(ingress.TryPost(connection, in discontinuity));

        RuntimePlayerMovementAuthoritySnapshot snapshot = ingress.CaptureAuthoritySnapshot();
        Assert.False(snapshot.PositionEnforcementEnabled);
        Assert.Equal(2, snapshot.Accepted);
        Assert.Equal(1, snapshot.PositionViolations);
        Assert.Equal(0, snapshot.PositionRejected);
        Assert.Equal(2, sink.Count);
    }

    [Fact]
    public void Queue_rejection_never_advances_trusted_history()
    {
        var sink = new ToggleIngress { Accept = false };
        var ingress = new RuntimePlayerMovementIngress(
            sink,
            new RuntimePlayerMovementAuthority(enforcePositionDiscontinuities: true));
        ConnectionHandle connection = Connection(slot: 8, generation: 1, source: 15);

        PlayerMovementCommitRequest first = Request(connection.Player.Slot, 1_000f, 1_000f);
        Assert.False(ingress.TryPost(connection, in first));

        sink.Accept = true;
        PlayerMovementCommitRequest farAway = Request(connection.Player.Slot, 40_000f, 40_000f);
        Assert.True(ingress.TryPost(connection, in farAway));

        RuntimePlayerMovementAuthoritySnapshot snapshot = ingress.CaptureAuthoritySnapshot();
        Assert.Equal(1, snapshot.QueueRejected);
        Assert.Equal(1, snapshot.Accepted);
        Assert.Equal(0, snapshot.PositionViolations);
    }

    private static ConnectionHandle Connection(byte slot, ulong generation, int source) =>
        new(
            GameCommandSourceId.FromConnection(source),
            new PlayerHandle(
                new PlayerSlotId(slot),
                new PlayerSessionGeneration(generation)));

    private static PlayerMovementCommitRequest Request(PlayerSlotId slot, float x, float y) =>
        new(
            slot,
            0,
            0,
            0,
            0,
            0,
            x,
            y,
            false,
            0f,
            0f,
            false,
            0,
            false,
            0f,
            0f,
            0f,
            0f,
            false,
            0f,
            0f);

    private sealed class CountingIngress : IGameCommandIngress<RuntimeCommand>
    {
        public int Count { get; private set; }

        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        {
            Count++;
            return true;
        }
    }

    private sealed class ToggleIngress : IGameCommandIngress<RuntimeCommand>
    {
        public bool Accept { get; set; }
        public int Count { get; private set; }

        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        {
            Count++;
            return Accept;
        }
    }
}
