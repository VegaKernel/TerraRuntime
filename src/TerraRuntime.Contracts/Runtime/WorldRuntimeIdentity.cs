namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Cross-boundary identity of one currently running world.
/// RuntimeId identifies the logical world instance; SessionId prevents stale identities from surviving a restart.
/// </summary>
public readonly record struct WorldRuntimeIdentity(
    WorldRuntimeId RuntimeId,
    WorldSessionId SessionId)
{
    public bool IsAssigned => RuntimeId.IsAssigned && SessionId.IsAssigned;

    public override string ToString() => $"{RuntimeId}/session:{SessionId}";
}
