namespace TerraRuntime.Operations;

internal interface IWorldOperations
{
    RuntimeWorldSnapshot CaptureSnapshot();

    /// <summary>
    /// Requests a canonical world checkpoint through the persistence subsystem's thread-safe ingress.
    /// The detached snapshot is still captured later by the authoritative game-thread owner.
    /// Returns false when persistence is completing or no save ingress is available.
    /// </summary>
    bool TryRequestSave() => false;
}
