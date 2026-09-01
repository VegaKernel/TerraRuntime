namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Persistence policy for a runtime-world instance.
/// </summary>
public enum WorldPersistenceMode : byte
{
    Unspecified = 0,
    Persistent = 1,
    Ephemeral = 2,
    SnapshotClone = 3
}
