namespace TerraRuntime.Application.Operations;

internal interface IRuntimeDashboardOperations
{
    RuntimeDashboardSnapshot CaptureSnapshot();

    /// <summary>
    /// Queues a world-scoped interest-management state change through the authoritative command ingress.
    /// Returns false when the bounded command queue rejects the request.
    /// </summary>
    bool TrySetInterestManagementEnabled(bool enabled);

    /// <summary>
    /// Attempts to replace the public listener endpoint without disconnecting already accepted clients.
    /// Implementations must leave the current endpoint active when a non-overlapping replacement cannot bind.
    /// </summary>
    ListenerChangeResult TryChangeListenerEndpoint(string bindAddress, int port) =>
        ListenerChangeResult.Rejected("Dynamic listener settings are not available for this host.");
}
