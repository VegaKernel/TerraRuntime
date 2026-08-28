using TerraRuntime.Network;

namespace TerraRuntime.Operations;

internal sealed class LocalRuntimeNetworkOperations : INetworkOperations
{
    private readonly TerrariaConnectionAdmissionGate admission;
    private readonly global::TerraRuntime.RuntimeConnectionRegistry connections;

    public LocalRuntimeNetworkOperations(
        TerrariaConnectionAdmissionGate admission,
        global::TerraRuntime.RuntimeConnectionRegistry connections)
    {
        this.admission = admission ?? throw new ArgumentNullException(nameof(admission));
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public RuntimeNetworkSnapshot CaptureSnapshot() =>
        new(
            ActiveConnections: admission.ActiveConnections,
            RegisteredConnections: connections.Count,
            AcceptedConnections: admission.AcceptedConnections,
            RejectedConnections: admission.RejectedConnections,
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
