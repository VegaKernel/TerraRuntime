using global::Multiplicity.Packets;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

public enum PlayerBootstrapStopReason : byte
{
    None = 0,
    InvalidHandshake = 1,
    ServerFull = 2,
    InvalidJoinState = 3,
    MalformedJoinRequest = 4,
    OutboundBackpressure = 5,
    PlayerSlotMismatch = 6,
    GameIngressBackpressure = 7
}

/// <summary>
/// Connection-owned coordinator for the minimal vanilla 1.4.5.8 join path:
/// Hello -> packet 3 -> packet 6/7 -> packet 8/sections/49 -> packet 12 authoritative handoff.
/// </summary>
public sealed class PlayerBootstrapFrameSink : ITerrariaFrameSink, IDisposable
{
    private readonly PlayerSlotPool _slots;
    private readonly TerrariaConnectionOutboundQueue _outbound;
    private readonly PlayerBootstrapPacketSet _packets;
    private readonly ITerrariaFrameSink? _inner;
    private readonly GameCommandSourceId _source;
    private readonly IPlayerSpawnCommitIngress? _spawnIngress;
    private PlayerJoinSession? _session;

    public PlayerBootstrapFrameSink(
        PlayerSlotPool slots,
        TerrariaConnectionOutboundQueue outbound,
        PlayerBootstrapPacketSet packets,
        ITerrariaFrameSink? inner = null)
        : this(slots, outbound, packets, GameCommandSourceId.System, null, inner)
    {
    }

    public PlayerBootstrapFrameSink(
        PlayerSlotPool slots,
        TerrariaConnectionOutboundQueue outbound,
        PlayerBootstrapPacketSet packets,
        GameCommandSourceId source,
        IPlayerSpawnCommitIngress spawnIngress,
        ITerrariaFrameSink? inner = null)
        : this(slots, outbound, packets, source, spawnIngress, inner)
    {
        if (source.IsSystem)
            throw new ArgumentException("Player bootstrap ingress requires a connection command source.", nameof(source));
    }

    private PlayerBootstrapFrameSink(
        PlayerSlotPool slots,
        TerrariaConnectionOutboundQueue outbound,
        PlayerBootstrapPacketSet packets,
        GameCommandSourceId source,
        IPlayerSpawnCommitIngress? spawnIngress,
        ITerrariaFrameSink? inner)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(outbound);
        ArgumentNullException.ThrowIfNull(packets);
        _slots = slots;
        _outbound = outbound;
        _packets = packets;
        _source = source;
        _spawnIngress = spawnIngress;
        _inner = inner;
    }

    public PlayerBootstrapStopReason StopReason { get; private set; }
    public PlayerJoinState? JoinState => _session?.State;
    public byte? PlayerSlot => _session is null || _session.State == PlayerJoinState.Closed
        ? null
        : _session.Slot.Value;

    public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
    {
        if (StopReason != PlayerBootstrapStopReason.None)
            return TerrariaFrameSinkResult.Stop;

        if (_session is null)
            return HandleHandshake(frame);

        switch ((TerrariaMessageId)frame.MessageId)
        {
            case TerrariaMessageId.Hello:
                return Stop(PlayerBootstrapStopReason.InvalidJoinState);

            case TerrariaMessageId.RequestWorldData:
                return HandleWorldRequest(frame);

            case TerrariaMessageId.SpawnTileData:
                return HandleSectionRequest(frame);

            case TerrariaMessageId.PlayerSpawn:
                return HandlePlayerSpawn(frame);

            default:
                return _inner?.OnFrame(in frame) ?? TerrariaFrameSinkResult.Continue;
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }

    private TerrariaFrameSinkResult HandleHandshake(in TerrariaFrame frame)
    {
        ConnectRequestDecodeResult decode = TerrariaConnectRequestDecoder.TryDecode(frame, out TerrariaConnectRequest request);
        if (decode != ConnectRequestDecodeResult.Decoded || !request.IsCurrentProtocol)
            return Stop(PlayerBootstrapStopReason.InvalidHandshake);

        if (!_slots.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease) || lease is null)
            return Stop(PlayerBootstrapStopReason.ServerFull);

        var session = new PlayerJoinSession(lease);
        byte[] continueFrame = SerializePacket(PlayerJoinPacketFactory.CreateContinueConnecting(session.Slot));
        if (!TryQueue(continueFrame))
        {
            session.Dispose();
            return Stop(PlayerBootstrapStopReason.OutboundBackpressure);
        }

        _session = session;
        return TerrariaFrameSinkResult.Continue;
    }

    private TerrariaFrameSinkResult HandleWorldRequest(in TerrariaFrame frame)
    {
        if (TerrariaJoinRequestDecoder.TryDecodeWorldRequest(frame) != TerrariaJoinDecodeResult.Decoded)
            return Stop(PlayerBootstrapStopReason.MalformedJoinRequest);

        if (_session!.State == PlayerJoinState.AwaitingWorldRequest)
        {
            if (!TryQueue(_packets.WorldInfoFrame))
                return Stop(PlayerBootstrapStopReason.OutboundBackpressure);
            _session.ObserveWorldRequest();
            return TerrariaFrameSinkResult.Continue;
        }

        // Vanilla responds to repeated packet 6 with WorldInfo without rewinding its state.
        if (_session.State is PlayerJoinState.AwaitingSectionRequest or PlayerJoinState.AwaitingSpawn or PlayerJoinState.Playing)
        {
            return TryQueue(_packets.WorldInfoFrame)
                ? TerrariaFrameSinkResult.Continue
                : Stop(PlayerBootstrapStopReason.OutboundBackpressure);
        }

        return Stop(PlayerBootstrapStopReason.InvalidJoinState);
    }

    private TerrariaFrameSinkResult HandleSectionRequest(in TerrariaFrame frame)
    {
        if (TerrariaJoinRequestDecoder.TryDecodeSectionRequest(frame, out _) != TerrariaJoinDecodeResult.Decoded)
            return Stop(PlayerBootstrapStopReason.MalformedJoinRequest);

        if (_session!.State == PlayerJoinState.AwaitingWorldRequest)
            return Stop(PlayerBootstrapStopReason.InvalidJoinState);

        if (_session.State is PlayerJoinState.AwaitingSpawn or PlayerJoinState.Playing)
        {
            // The minimal bootstrap cache contains the base spawn sections only. Do not resend the whole
            // base set for repeated packet 8; later per-connection section tracking handles arbitrary requests.
            return TerrariaFrameSinkResult.Continue;
        }

        if (_session.State != PlayerJoinState.AwaitingSectionRequest)
            return Stop(PlayerBootstrapStopReason.InvalidJoinState);

        // Vanilla sends WorldInfo again immediately before the tile bootstrap.
        if (!TryQueue(_packets.WorldInfoFrame))
            return Stop(PlayerBootstrapStopReason.OutboundBackpressure);

        foreach (ReadOnlyMemory<byte> sectionFrame in _packets.BaseSectionFrames)
        {
            if (!TryQueue(sectionFrame))
                return Stop(PlayerBootstrapStopReason.OutboundBackpressure);
        }

        // Packet 49 is the client-side state transition from tile loading to spawning.
        if (!TryQueue(_packets.EnterWorldFrame))
            return Stop(PlayerBootstrapStopReason.OutboundBackpressure);

        _session.ObserveSectionRequest();
        return TerrariaFrameSinkResult.Continue;
    }

    private TerrariaFrameSinkResult HandlePlayerSpawn(in TerrariaFrame frame)
    {
        if (TerrariaJoinRequestDecoder.TryDecodePlayerSpawn(frame, out TerrariaPlayerSpawnRequest request) != TerrariaJoinDecodeResult.Decoded)
            return Stop(PlayerBootstrapStopReason.MalformedJoinRequest);

        if (_session!.State != PlayerJoinState.AwaitingSpawn && _session.State != PlayerJoinState.Playing)
            return Stop(PlayerBootstrapStopReason.InvalidJoinState);

        PlayerSlotId assignedSlot = _session.Slot;
        if (request.ClaimedPlayerId != assignedSlot.Value)
            return Stop(PlayerBootstrapStopReason.PlayerSlotMismatch);

        if (_spawnIngress is not null && _session.State == PlayerJoinState.AwaitingSpawn)
        {
            var commit = new PlayerSpawnCommitRequest(
                assignedSlot,
                request.SpawnX,
                request.SpawnY,
                request.RespawnTimer,
                request.DeathsPve,
                request.DeathsPvp,
                request.Team,
                request.SpawnContext);

            if (!_spawnIngress.TryPost(_source, _session, in commit))
                return Stop(PlayerBootstrapStopReason.GameIngressBackpressure);

            // The network thread has only submitted a candidate. State 3 -> 10 happens when the
            // authoritative game loop accepts and commits the command.
            return TerrariaFrameSinkResult.Continue;
        }

        return _inner?.OnFrame(in frame) ?? TerrariaFrameSinkResult.Continue;
    }

    private bool TryQueue(ReadOnlyMemory<byte> frame) =>
        _outbound.TryEnqueue(new OutboundFrame(frame)) == OutboundEnqueueResult.Enqueued;

    private TerrariaFrameSinkResult Stop(PlayerBootstrapStopReason reason)
    {
        StopReason = reason;
        _session?.Dispose();
        _session = null;
        return TerrariaFrameSinkResult.Stop;
    }

    private static byte[] SerializePacket(TerrariaPacket packet)
    {
        using var stream = new MemoryStream();
        packet.ToStream(stream);
        return stream.ToArray();
    }
}
