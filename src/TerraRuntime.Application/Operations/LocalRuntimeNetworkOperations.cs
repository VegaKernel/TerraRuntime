using TerraRuntime.Network;

namespace TerraRuntime.Operations;

internal sealed class LocalRuntimeNetworkOperations : INetworkOperations
{
    private const int MaximumQueueDetails = 2;
    private const int MaximumRateDetails = 2;
    private const int MaximumMessageTrafficDetails = 8;

    private readonly TerrariaConnectionAdmissionGate admission;
    private readonly global::TerraRuntime.RuntimeConnectionRegistry connections;
    private readonly RuntimeConnectionQueueTelemetry queueTelemetry;
    private readonly RuntimeConnectionRateTelemetry rateTelemetry;
    private readonly global::TerraRuntime.RuntimeNpcReplicationRegistry? npcReplication;
    private readonly global::TerraRuntime.RuntimeProjectileReplicationRegistry? projectileReplication;
    private readonly global::TerraRuntime.RuntimeWorldItemReplicationRegistry? worldItemReplication;
    private readonly RuntimeConnectionStopTelemetry? stopTelemetry;

    public LocalRuntimeNetworkOperations(
        TerrariaConnectionAdmissionGate admission,
        global::TerraRuntime.RuntimeConnectionRegistry connections,
        RuntimeConnectionQueueTelemetry queueTelemetry,
        RuntimeConnectionRateTelemetry rateTelemetry,
        global::TerraRuntime.RuntimeNpcReplicationRegistry? npcReplication = null,
        global::TerraRuntime.RuntimeProjectileReplicationRegistry? projectileReplication = null,
        global::TerraRuntime.RuntimeWorldItemReplicationRegistry? worldItemReplication = null,
        RuntimeConnectionStopTelemetry? stopTelemetry = null)
    {
        this.admission = admission ?? throw new ArgumentNullException(nameof(admission));
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));
        this.queueTelemetry = queueTelemetry ?? throw new ArgumentNullException(nameof(queueTelemetry));
        this.rateTelemetry = rateTelemetry ?? throw new ArgumentNullException(nameof(rateTelemetry));
        this.npcReplication = npcReplication;
        this.projectileReplication = projectileReplication;
        this.worldItemReplication = worldItemReplication;
        this.stopTelemetry = stopTelemetry;
    }

    public RuntimeNetworkSnapshot CaptureSnapshot()
    {
        RuntimeConnectionQueueSnapshot queues = queueTelemetry.CaptureSnapshot(MaximumQueueDetails);
        OutboundQueueSizingEvidence queueSizing = OutboundQueueSizingEvidenceCalculator.Calculate(
            queues.ConfiguredMaxFrames,
            queues.ConfiguredMaxQueuedBytes,
            queues.PeakQueuedFrames,
            queues.PeakQueuedBytes,
            queues.RejectedFrames,
            queues.SlowClients);
        RuntimeConnectionRateTelemetrySnapshot rates = rateTelemetry.CaptureSnapshot(MaximumRateDetails);
        RuntimeConnectionStopTelemetrySnapshot stops = stopTelemetry?.CaptureSnapshot() ?? default;
        TerrariaFrameRejectionTelemetrySnapshot rejections = TerrariaFrameRejectionTelemetry.CaptureSnapshot();
        TerrariaMessageTrafficTelemetrySnapshot messages =
            TerrariaMessageTrafficTelemetry.Shared.CaptureSnapshot(MaximumMessageTrafficDetails);
        return new RuntimeNetworkSnapshot(
            ActiveConnections: admission.ActiveConnections,
            RegisteredConnections: connections.Count,
            AcceptedConnections: admission.AcceptedConnections,
            RejectedConnections: admission.RejectedConnections,
            TrackedOutboundQueues: queues.TrackedQueues,
            QueuedOutboundFrames: queues.QueuedFrames,
            QueuedOutboundBytes: queues.QueuedBytes,
            PeakQueuedOutboundFrames: queues.PeakQueuedFrames,
            PeakQueuedOutboundBytes: queues.PeakQueuedBytes,
            RejectedOutboundFrames: queues.RejectedFrames,
            SlowClients: queues.SlowClients,
            TopOutboundQueues: queues.TopQueues,
            TrackedInboundRates: rates.TrackedConnections,
            InboundWindowFrames: rates.WindowFrames,
            InboundWindowBytes: rates.WindowBytes,
            InboundTotalFrames: rates.TotalFrames,
            InboundTotalBytes: rates.TotalBytes,
            RejectedInboundFrames: rates.RejectedFrames,
            TopInboundRates: rates.TopConnections,
            RelayedAppearanceFrames: connections.RelayedAppearanceFrames,
            AppearanceBaselineFrames: connections.AppearanceBaselineFrames,
            RelayedEquipmentFrames: connections.RelayedEquipmentFrames,
            EquipmentBaselineFrames: connections.EquipmentBaselineFrames,
            DroppedEquipmentSnapshotUpdates: connections.DroppedEquipmentSnapshotUpdates,
            PlayerActiveBaselineFrames: connections.PlayerActiveBaselineFrames,
            PlayerDeactivationFrames: connections.PlayerDeactivationFrames,
            RelayedMovementFrames: connections.RelayedMovementFrames,
            MovementResyncFrames: connections.MovementResyncFrames,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            NpcRelayedFrames: npcReplication?.RelayedFrames ?? 0,
            NpcBaselineFrames: npcReplication?.BaselineFrames ?? 0,
            NpcRejectedFrames: npcReplication?.RejectedFrames ?? 0,
            NpcUnsupportedCommits: npcReplication?.UnsupportedCommits ?? 0,
            ProjectileRelayedFrames: projectileReplication?.RelayedFrames ?? 0,
            ProjectileBaselineFrames: projectileReplication?.BaselineFrames ?? 0,
            ProjectileRejectedFrames: projectileReplication?.RejectedFrames ?? 0,
            ProjectileUnsupportedCommits: projectileReplication?.UnsupportedCommits ?? 0,
            WorldItemRelayedFrames: worldItemReplication?.RelayedFrames ?? 0,
            WorldItemRejectedFrames: worldItemReplication?.RejectedFrames ?? 0,
            WorldItemUnsupportedCommits: worldItemReplication?.UnsupportedCommits ?? 0,
            AdmissionCapacityRejectedConnections: admission.CapacityRejectedConnections,
            AdmissionRateRejectedConnections: admission.RateRejectedConnections,
            StopProtocolFailures: stops.ProtocolFailures,
            StopRateLimited: stops.RateLimited,
            StopInvalidHandshake: stops.InvalidHandshake,
            StopUnsupportedProtocol: stops.UnsupportedProtocol,
            StopSlowClient: stops.SlowClient,
            StopApplicationStopped: stops.ApplicationStopped,
            StopHandshakeTimeout: stops.HandshakeTimeout,
            StopIdleTimeout: stops.IdleTimeout,
            StopJoinTimeout: stops.JoinTimeout,
            RejectedMalformedProtocol: rejections.MalformedProtocol,
            RejectedRateLimited: rejections.RateLimited,
            RejectedInvalidState: rejections.InvalidState,
            RejectedGameplay: rejections.GameplayRejected,
            RejectedBackpressure: rejections.Backpressure,
            StopFrameRejected: stops.FrameRejected,
            MessageInboundFrames: messages.InboundFrames,
            MessageInboundBytes: messages.InboundBytes,
            MessageOutboundFrames: messages.OutboundFrames,
            MessageOutboundBytes: messages.OutboundBytes,
            UnknownInboundMessages: messages.UnknownInboundFrames,
            UnknownOutboundMessages: messages.UnknownOutboundFrames,
            MalformedInboundMessages: messages.MalformedInboundFrames,
            MalformedOutboundMessages: messages.MalformedOutboundFrames,
            MessageTrafficWindow: messages.Window,
            MessageTraffic: messages.Messages,
            TopMessageTraffic: messages.TopMessages,
            OutboundStructuralMaxFrames: queueSizing.StructuralMaxFrames,
            OutboundStructuralMaxQueuedBytes: queueSizing.StructuralMaxQueuedBytes,
            OutboundFrameUtilizationBasisPoints: queueSizing.FrameUtilizationBasisPoints,
            OutboundByteUtilizationBasisPoints: queueSizing.ByteUtilizationBasisPoints,
            OutboundMeasuredFramesWithHeadroom: queueSizing.MeasuredFramesWithHeadroom,
            OutboundMeasuredBytesWithHeadroom: queueSizing.MeasuredBytesWithHeadroom,
            OutboundRecommendedMaxFrames: queueSizing.RecommendedMaxFrames,
            OutboundRecommendedMaxQueuedBytes: queueSizing.RecommendedMaxQueuedBytes,
            OutboundSizingHasMeasurements: queueSizing.HasMeasurements,
            OutboundSizingRequiresReview: queueSizing.RequiresReview);
    }
}
