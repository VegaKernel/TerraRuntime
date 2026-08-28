using TerraRuntime.Network;

namespace TerraRuntime.Operations;

internal sealed class LocalRuntimeNetworkOperations : INetworkOperations
{
    private const int MaximumQueueDetails = 2;

    private readonly TerrariaConnectionAdmissionGate admission;
    private readonly global::TerraRuntime.RuntimeConnectionRegistry connections;
    private readonly RuntimeConnectionQueueTelemetry queueTelemetry;

    public LocalRuntimeNetworkOperations(
        TerrariaConnectionAdmissionGate admission,
        global::TerraRuntime.RuntimeConnectionRegistry connections,
        RuntimeConnectionQueueTelemetry queueTelemetry)
    {
        this.admission = admission ?? throw new ArgumentNullException(nameof(admission));
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));
        this.queueTelemetry = queueTelemetry ?? throw new ArgumentNullException(nameof(queueTelemetry));
    }

    public RuntimeNetworkSnapshot CaptureSnapshot()
    {
        RuntimeConnectionQueueSnapshot queues = queueTelemetry.CaptureSnapshot(MaximumQueueDetails);
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
            RelayedAppearanceFrames: connections.RelayedAppearanceFrames,
            AppearanceBaselineFrames: connections.AppearanceBaselineFrames,
            RelayedEquipmentFrames: connections.RelayedEquipmentFrames,
            EquipmentBaselineFrames: connections.EquipmentBaselineFrames,
            DroppedEquipmentSnapshotUpdates: connections.DroppedEquipmentSnapshotUpdates,
            PlayerActiveBaselineFrames: connections.PlayerActiveBaselineFrames,
            PlayerDeactivationFrames: connections.PlayerDeactivationFrames,
            RelayedMovementFrames: connections.RelayedMovementFrames,
            MovementResyncFrames: connections.MovementResyncFrames,
            CapturedAtUtc: DateTimeOffset.UtcNow);
    }
}
