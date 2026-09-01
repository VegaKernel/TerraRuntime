using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.HostContracts;

/// <summary>
/// Immutable identity and deployment information for one running Terraria world exposed to a trusted host module.
/// Runtime identity is independent from the canonical .wld identity so cloned/ephemeral worlds remain distinguishable.
/// </summary>
public sealed record TerraRuntimeHostRuntimeInfo(
    string WorldName,
    string WorldPath,
    int WidthTiles,
    int HeightTiles,
    int Port,
    int MaxPlayers)
{
    /// <summary>
    /// Exact identity of this live runtime world. The default single-world host creates a fresh logical/runtime session
    /// pair; a future multi-world manager may provide an explicitly retained RuntimeId while rotating SessionId on restart.
    /// </summary>
    public WorldRuntimeIdentity RuntimeIdentity { get; init; } = new(
        WorldRuntimeId.CreateNew(),
        WorldSessionId.CreateNew());

    /// <summary>
    /// Isolation policy of this runtime. Existing hosts are in-process until a sandbox supervisor explicitly hosts a worker.
    /// </summary>
    public WorldIsolationLevel IsolationLevel { get; init; } = WorldIsolationLevel.InProcess;

    /// <summary>
    /// Persistence policy of this runtime. Existing canonical .wld startup remains persistent by default.
    /// </summary>
    public WorldPersistenceMode PersistenceMode { get; init; } = WorldPersistenceMode.Persistent;
}
