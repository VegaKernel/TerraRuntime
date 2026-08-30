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
    GameIngressBackpressure = 7,
    SectionEncodingFailure = 8,
    MalformedPlayerMovement = 9,
    MalformedPlayerAppearance = 10,
    MalformedPlayerEquipment = 11,
    MalformedPlayerSpawn = 12,
    DynamicEntityBootstrapFailure = 13,
    MalformedChat = 14,
    SectionWorkRateLimited = 15
}

/// <summary>
/// Connection-owned coordinator for the minimal vanilla 1.4.5.8 join path and the first
/// authoritative gameplay handoff:
/// Hello -> packet 3 -> player sync -> packet 6/7 -> packet 8/7/9/10/.../49 -> packet 12/129 -> packet 13.
/// </summary>
public sealed class PlayerBootstrapFrameSink : ITerrariaFrameSink, IDisposable
{
    private static readonly byte[] FinishedConnectingFrame =
    [
        3,
        0,
        (byte)TerrariaMessageId.FinishedConnectingToServer
    ];

    private readonly PlayerSlotPool _slots;
    private readonly TerrariaConnectionOutboundQueue _outbound;
    private readonly PlayerBootstrapPacketSet _packets;
    private readonly ITerrariaFrameSink? _inner;
    private readonly GameCommandSourceId _source;
    private readonly IPlayerSpawnCommitIngress? _spawnIngress;
    private readonly IPlayerAppearanceIngress? _appearanceIngress;
    private readonly IPlayerEquipmentIngress? _equipmentIngress;
    private readonly IPlayerMovementIngress? _movementIngress;
    private readonly RuntimeChatRelay? _chatRelay;
    private PlayerJoinSession? _session;
    private PlayerHandle? _assignedPlayerHandle;
    private bool _spawnSubmitted;

    public PlayerBootstrapFrameSink(
        PlayerSlotPool slots,
        TerrariaConnectionOutboundQueue outbound,
        PlayerBootstrapPacketSet packets,
        ITerrariaFrameSink? inner = null)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(outbound);
        ArgumentNullException.ThrowIfNull(packets);
        _slots = slots;
        _outbound = outbound;
        _packets = packets;
        _source = GameCommandSourceId.System;
        _spawnIngress = null;
        _appearanceIngress = null;
        _equipmentIngress = null;
        _movementIngress = null;
        _chatRelay = null;
        _inner = inner;
    }

    public PlayerBootstrapFrameSink(
        PlayerSlotPool slots,
        TerrariaConnectionOutboundQueue outbound,
        PlayerBootstrapPacketSet packets,
        GameCommandSourceId source,
        IPlayerSpawnCommitIngress spawnIngress,
        ITerrariaFrameSink? inner = null)
        : this(slots, outbound, packets, source, spawnIngress, appearanceIngress: null, equipmentIngress: null, movementIngress: null, inner)
    {
    }

    public PlayerBootstrapFrameSink(
        PlayerSlotPool slots,
        TerrariaConnectionOutboundQueue outbound,
        PlayerBootstrapPacketSet packets,
        GameCommandSourceId source,
        IPlayerSpawnCommitIngress spawnIngress,
        IPlayerMovementIngress? movementIngress,
        ITerrariaFrameSink? inner = null)
        : this(slots, outbound, packets, source, spawnIngress, appearanceIngress: null, equipmentIngress: null, movementIngress, inner)
    {
    }

    public PlayerBootstrapFrameSink(
        PlayerSlotPool slots,
        TerrariaConnectionOutboundQueue outbound,
        PlayerBootstrapPacketSet packets,
        GameCommandSourceId source,
        IPlayerSpawnCommitIngress spawnIngress,
        IPlayerAppearanceIngress? appearanceIngress,
        IPlayerMovementIngress? movementIngress,
        ITerrariaFrameSink? inner = null)
        : this(slots, outbound, packets, source, spawnIngress, appearanceIngress, equipmentIngress: null, movementIngress, inner)
    {
    }

    public PlayerBootstrapFrameSink(
        PlayerSlotPool slots,
        TerrariaConnectionOutboundQueue outbound,
        PlayerBootstrapPacketSet packets,
        GameCommandSourceId source,
        IPlayerSpawnCommitIngress spawnIngress,
        IPlayerAppearanceIngress? appearanceIngress,
        IPlayerEquipmentIngress? equipmentIngress,
        IPlayerMovementIngress? movementIngress,
        ITerrariaFrameSink? inner = null,
        IWorldItemSnapshotReader? worldItems = null)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(outbound);
        ArgumentNullException.ThrowIfNull(packets);
        ArgumentNullException.ThrowIfNull(spawnIngress);
        if (source.IsSystem)
            throw new ArgumentException("Player bootstrap ingress requires a connection command source.", nameof(source));

        _slots = slots;
        _outbound = outbound;
        _packets = packets;
        _source = source;
        _spawnIngress = spawnIngress;
        _appearanceIngress = appearanceIngress;
        _equipmentIngress = equipmentIngress;
        _movementIngress = movementIngress;
        _chatRelay = RuntimeChatRelay.For(slots);
        _chatRelay.Register(source, outbound);
        _ = worldItems;
        _inner = inner;
    }

    public PlayerBootstrapStopReason StopReason { get; private set; }
    public PlayerJoinState? JoinState => _session?.State;
    public byte? PlayerSlot => _session is null || _session.State == PlayerJoinState.Closed
        ? null
        : _session.Slot.Value;
    public PlayerHandle? AssignedPlayerHandle => _assignedPlayerHandle;

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

            case TerrariaMessageId.SyncPlayer:
                return HandlePlayerAppearance(frame);

            case TerrariaMessageId.SyncEquipment:
                return HandlePlayerEquipment(frame);

            case TerrariaMessageId.RequestWorldData:
                return HandleWorldRequest(frame);

            case TerrariaMessageId.SpawnTileData:
                return HandleSectionRequest(frame);

            case TerrariaMessageId.PlayerSpawn:
                return HandlePlayerSpawn(frame);

            case TerrariaMessageId.PlayerControls:
                return HandlePlayerMovement(frame);

            case TerrariaMessageId.LoadNetModule:
                return HandleNetModule(frame);

            default:
                return _inner?.OnFrame(in frame) ?? TerrariaFrameSinkResult.Continue;
        }
    }

    public void Dispose()
    {
        _chatRelay?.Unregister(_source);
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
        byte[] continueFrame = PlayerJoinFrameEncoder.EncodeContinueConnecting(session.Slot);
        if (!TryQueue(continueFrame))
        {
            session.Dispose();
            return Stop(PlayerBootstrapStopReason.OutboundBackpressure);
        }

        _session = session;
        _assignedPlayerHandle = session.Handle;
        return TerrariaFrameSinkResult.Continue;
    }

    private TerrariaFrameSinkResult HandlePlayerAppearance(in TerrariaFrame frame)
    {
        TerrariaPlayerAppearanceDecodeResult decode = TerrariaPlayerAppearanceCodec.TryDecode(
            frame,
            out TerrariaPlayerAppearanceState appearance);
        if (decode != TerrariaPlayerAppearanceDecodeResult.Decoded)
            return Stop(PlayerBootstrapStopReason.MalformedPlayerAppearance);

        if (_appearanceIngress is null)
            return _inner?.OnFrame(in frame) ?? TerrariaFrameSinkResult.Continue;

        PlayerSlotId assignedSlot = _session!.Slot;
        var commit = new PlayerAppearanceCommitRequest(
            assignedSlot,
            appearance.SkinVariant,
            appearance.VoiceVariant,
            appearance.VoicePitchOffset,
            appearance.Hair,
            appearance.Name,
            appearance.HairDye,
            appearance.HideVisibleAccessory,
            appearance.HideMisc,
            ToCore(appearance.HairColor),
            ToCore(appearance.SkinColor),
            ToCore(appearance.EyeColor),
            ToCore(appearance.ShirtColor),
            ToCore(appearance.UnderShirtColor),
            ToCore(appearance.PantsColor),
            ToCore(appearance.ShoeColor),
            appearance.DifficultyFlags,
            appearance.TorchAndCartFlags,
            appearance.ConsumableUnlockFlags);

        var connection = new ConnectionHandle(_source, _session.Handle);
        if (!_appearanceIngress.TryPost(connection, in commit))
            return Stop(PlayerBootstrapStopReason.GameIngressBackpressure);

        return TerrariaFrameSinkResult.Continue;
    }

    private TerrariaFrameSinkResult HandlePlayerEquipment(in TerrariaFrame frame)
    {
        TerrariaPlayerEquipmentDecodeResult decode = TerrariaPlayerEquipmentCodec.TryDecode(
            frame,
            out TerrariaPlayerEquipmentState equipment);
        if (decode != TerrariaPlayerEquipmentDecodeResult.Decoded)
            return Stop(PlayerBootstrapStopReason.MalformedPlayerEquipment);

        if (_equipmentIngress is null)
            return _inner?.OnFrame(in frame) ?? TerrariaFrameSinkResult.Continue;

        var commit = new PlayerEquipmentCommitRequest(
            _session!.Slot,
            equipment.SlotId,
            equipment.Stack,
            equipment.Prefix,
            equipment.ItemNetId,
            equipment.ItemFlags);

        var connection = new ConnectionHandle(_source, _session.Handle);
        if (!_equipmentIngress.TryPost(connection, in commit))
            return Stop(PlayerBootstrapStopReason.GameIngressBackpressure);

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
        if (TerrariaJoinRequestDecoder.TryDecodeSectionRequest(frame, out TerrariaSectionBootstrapRequest request) != TerrariaJoinDecodeResult.Decoded)
            return Stop(PlayerBootstrapStopReason.MalformedJoinRequest);

        if (_session!.State == PlayerJoinState.AwaitingWorldRequest)
            return Stop(PlayerBootstrapStopReason.InvalidJoinState);

        if (_session.State is PlayerJoinState.AwaitingSpawn or PlayerJoinState.Playing)
            return TerrariaFrameSinkResult.Continue;

        if (_session.State != PlayerJoinState.AwaitingSectionRequest)
            return Stop(PlayerBootstrapStopReason.InvalidJoinState);

        PlayerBootstrapSectionResponseResult sectionResult = _packets.CreateSectionResponseDetailed(
            request.TileX,
            request.TileY,
            request.Team,
            out PlayerBootstrapSectionResponse sectionResponse);
        if (sectionResult == PlayerBootstrapSectionResponseResult.RateLimited)
            return Stop(PlayerBootstrapStopReason.SectionWorkRateLimited);
        if (sectionResult != PlayerBootstrapSectionResponseResult.Created)
            return Stop(PlayerBootstrapStopReason.SectionEncodingFailure);

        if (!TryQueue(_packets.WorldInfoFrame) ||
            !TryQueue(sectionResponse.StatusFrame))
        {
            return Stop(PlayerBootstrapStopReason.OutboundBackpressure);
        }

        for (int i = 0; i < sectionResponse.BaseSectionFrames.Length; i++)
        {
            if (!TryQueue(sectionResponse.BaseSectionFrames[i]))
                return Stop(PlayerBootstrapStopReason.OutboundBackpressure);

            foreach (ReadOnlyMemory<byte> postSectionFrame in _packets.BaseSectionPostFrames[i])
            {
                if (!TryQueue(postSectionFrame))
                    return Stop(PlayerBootstrapStopReason.OutboundBackpressure);
            }
        }

        foreach (ReadOnlyMemory<byte> sectionFrame in sectionResponse.AdditionalSectionFrames)
        {
            if (!TryQueue(sectionFrame))
                return Stop(PlayerBootstrapStopReason.OutboundBackpressure);
        }

        // Keep the first vanilla-client handoff deliberately minimal. Entity and global
        // persistence baselines must not sit between the final packet 10 and packet 49;
        // they can be synchronized after the client has entered the world.
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
            if (_spawnSubmitted)
                return TerrariaFrameSinkResult.Continue;

            var commit = new PlayerSpawnCommitRequest(
                assignedSlot,
                request.SpawnX,
                request.SpawnY,
                request.RespawnTimer,
                request.DeathsPve,
                request.DeathsPvp,
                request.Team,
                request.SpawnContext);

            if (!VanillaPlayerSpawnValidator.IsValid(in commit))
                return Stop(PlayerBootstrapStopReason.MalformedPlayerSpawn);

            if (!_spawnIngress.TryPost(_source, _session, in commit))
                return Stop(PlayerBootstrapStopReason.GameIngressBackpressure);

            _spawnSubmitted = true;
            if (!TryQueue(FinishedConnectingFrame))
                return Stop(PlayerBootstrapStopReason.OutboundBackpressure);

            _chatRelay?.MarkPlaying(_source, _session.Handle);
            return TerrariaFrameSinkResult.Continue;
        }

        return _inner?.OnFrame(in frame) ?? TerrariaFrameSinkResult.Continue;
    }

    private TerrariaFrameSinkResult HandlePlayerMovement(in TerrariaFrame frame)
    {
        PlayerJoinSession session = _session!;
        if (!_spawnSubmitted && session.State != PlayerJoinState.Playing)
            return TerrariaFrameSinkResult.Continue;

        TerrariaPlayerMovementDecodeResult decode = TerrariaPlayerMovementDecoder.TryDecode(
            frame,
            out TerrariaPlayerMovementRequest request);
        if (decode != TerrariaPlayerMovementDecodeResult.Decoded)
            return Stop(PlayerBootstrapStopReason.MalformedPlayerMovement);

        if (_movementIngress is null)
            return _inner?.OnFrame(in frame) ?? TerrariaFrameSinkResult.Continue;

        PlayerSlotId assignedSlot = session.Slot;
        var commit = new PlayerMovementCommitRequest(
            assignedSlot,
            request.ControlFlags,
            request.MovementFlags,
            request.MiscFlags1,
            request.MiscFlags2,
            request.SelectedItem,
            request.PositionX,
            request.PositionY,
            request.HasVelocity,
            request.VelocityX,
            request.VelocityY,
            request.HasMount,
            request.MountType,
            request.HasPotionOfReturnPositions,
            request.PotionOfReturnOriginalPositionX,
            request.PotionOfReturnOriginalPositionY,
            request.PotionOfReturnHomePositionX,
            request.PotionOfReturnHomePositionY,
            request.HasCameraTarget,
            request.CameraTargetX,
            request.CameraTargetY);

        var connection = new ConnectionHandle(_source, session.Handle);
        if (!_movementIngress.TryPost(connection, in commit))
            return Stop(PlayerBootstrapStopReason.GameIngressBackpressure);

        return TerrariaFrameSinkResult.Continue;
    }

    private TerrariaFrameSinkResult HandleNetModule(in TerrariaFrame frame)
    {
        PlayerJoinSession session = _session!;
        if (!_spawnSubmitted && session.State != PlayerJoinState.Playing)
            return _inner?.OnFrame(in frame) ?? TerrariaFrameSinkResult.Continue;

        TerrariaClientChatDecodeResult decode = TerrariaChatCodec.TryDecodeClientMessage(
            in frame,
            out TerrariaClientChatMessage message);
        if (decode is TerrariaClientChatDecodeResult.WrongModule or TerrariaClientChatDecodeResult.WrongDirection)
            return _inner?.OnFrame(in frame) ?? TerrariaFrameSinkResult.Continue;
        if (decode != TerrariaClientChatDecodeResult.Decoded)
            return Stop(PlayerBootstrapStopReason.MalformedChat);

        string text = message.Text.TrimEnd('\r', '\n');
        if (text.Length == 0)
            return TerrariaFrameSinkResult.Continue;

        // Packet 82 carries command traffic as well as ordinary chat. Only vanilla Say belongs
        // on the public chat relay; everything else is reserved for the command/module pipeline.
        if (!string.Equals(message.CommandName, "Say", StringComparison.OrdinalIgnoreCase) || text[0] == '/')
            return _inner?.OnFrame(in frame) ?? TerrariaFrameSinkResult.Continue;

        byte[] encoded = TerrariaChatCodec.EncodeServerMessage(
            session.Slot.Value,
            text,
            new TerrariaRgbColor(255, 255, 255));
        _chatRelay?.Broadcast(_source, session.Handle, encoded);
        return TerrariaFrameSinkResult.Continue;
    }

    private bool TryQueue(ReadOnlyMemory<byte> frame) =>
        _outbound.TryEnqueue(new OutboundFrame(frame)) == OutboundEnqueueResult.Enqueued;

    private TerrariaFrameSinkResult Stop(PlayerBootstrapStopReason reason)
    {
        StopReason = reason;
        _chatRelay?.Unregister(_source);
        _session?.Dispose();
        _session = null;
        return TerrariaFrameSinkResult.Stop;
    }

    private static PlayerRgbColor ToCore(TerrariaRgbColor color) =>
        new(color.R, color.G, color.B);
}
