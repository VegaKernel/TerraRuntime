namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Isolation policy for a runtime-world instance.
/// This is orthogonal to persistence: an ephemeral world may run either in-process or in a dedicated process.
/// </summary>
public enum WorldIsolationLevel : byte
{
    Unspecified = 0,
    InProcess = 1,
    DedicatedProcess = 2
}
