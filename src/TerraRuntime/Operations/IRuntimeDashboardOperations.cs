namespace TerraRuntime.Operations;

internal interface IRuntimeDashboardOperations
{
    RuntimeDashboardSnapshot CaptureSnapshot();

    /// <summary>
    /// Queues a world-scoped interest-management state change through the authoritative command ingress.
    /// Returns false when the bounded command queue rejects the request.
    /// </summary>
    bool TrySetInterestManagementEnabled(bool enabled);
}
