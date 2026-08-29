namespace TerraRuntime.HostContracts;

/// <summary>
/// Immutable identity and deployment information for the running Terraria world exposed to a trusted host module.
/// </summary>
public sealed record TerraRuntimeHostRuntimeInfo(
    string WorldName,
    string WorldPath,
    int WidthTiles,
    int HeightTiles,
    int Port,
    int MaxPlayers);
