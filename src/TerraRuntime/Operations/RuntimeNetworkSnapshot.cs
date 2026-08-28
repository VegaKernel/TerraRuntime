namespace TerraRuntime.Operations;

internal readonly record struct RuntimeNetworkSnapshot(
    int ActiveConnections,
    int RegisteredConnections,
    long AcceptedConnections,
    long RejectedConnections,
    int TrackedOutboundQueues,
    long QueuedOutboundFrames,
    long QueuedOutboundBytes,
    long RejectedOutboundFrames,
    int SlowClients,
    long RelayedAppearanceFrames,
    long AppearanceBaselineFrames,
    long RelayedEquipmentFrames,
    long EquipmentBaselineFrames,
    long DroppedEquipmentSnapshotUpdates,
    long PlayerActiveBaselineFrames,
    long PlayerDeactivationFrames,
    long RelayedMovementFrames,
    long MovementResyncFrames,
    DateTimeOffset CapturedAtUtc);
