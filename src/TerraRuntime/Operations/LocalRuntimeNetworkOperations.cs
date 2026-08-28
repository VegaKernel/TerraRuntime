using TerraRuntime.Network;

namespace TerraRuntime.Operations;

internal sealed class LocalRuntimeNetworkOperations : INetworkOperations
{
    private const int MaximumQueueDetails = 2;
    private const int MaximumRateDetails = 2;

    private readonly TerrariaConnectionAdmissionGate admission;
    private readonly global::TerraRuntime.RuntimeConnectionRegistry connections;
    private readonly RuntimeConnectionQueueTelemetry queueTelemetry;
    private readonly RuntimeConnectionRateTelemetry rateTelemetry;
    private readonly global::TerraRuntime.RuntimeNpcReplicationRegistry? npcReplication;
    private readonly global::TerraRuntime.RuntimeProjectileReplicationRegistry? projectileReplication;

    public LocalRuntimeNetworkOperations(
        TerrariaConnectionAdmissionGate admission,
        global::TerraRuntime.RuntimeConnectionRegistry connections,
        RuntimeConnectionQueueTelemetry queueTelemetry,
        RuntimeConnectionRateTelemetry rateTelemetry,
        global::TerraRuntime.RuntimeNpcReplicationRegistry? npcReplication = null,
        global::TerraRuntime.RuntimeProjectileReplicationRegistry? projectileReplication = null)
    {
        this.admission = admission ?? throw new ArgumentNullException(nameof(admission));
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));
        this.queueTelemetry = queueTelemetry ?? throw new ArgumentNullException(nameof(queueTelemetry));
        this.rateTelemetry = rateTelemetry ?? throw new ArgumentNullException(nameof(rateTelemetry));
        this.npcReplication = npcReplication;
        this.projectileReplication = projectileReplication;
    }

    public RuntimeNetworkSnapshot CaptureSnapshot()
    {
        RuntimeConnectionQueueSnapshot queues = queueTelemetry.CaptureSnapshot(MaximumQueueDetails);
        RuntimeConnectionRateTelemetrySnapshot rates = rateTelemetry.CaptureSnapshot(MaximumRateDetails);
        return new RuntimeNetworkSnapshot(
            ActiveConnections: admission.ActiveConnections,
            RegisteredConnections: connections.Count,
            AcceptedConnections: admission.AcceptedConnections,
            RejectedConnections: admission.RejectedConnections,
            TrackedOutboundQueues: queues.TrackedQueues,
            QueuedOutboundFrames: queues.QueuedFrames,
            QueuedOutboundBytes: queues.QueuedBytes,
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
            ProjectileUnsupportedCommits: projectileReplication?.UnsupportedCommits ?? 0);
    }
}
