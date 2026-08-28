namespace TerraRuntime.Operations;

internal readonly record struct RuntimeNetworkSnapshot(
    int ActiveConnections,
    int RegisteredConnections,
    long AcceptedConnections,
    long RejectedConnections,
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
