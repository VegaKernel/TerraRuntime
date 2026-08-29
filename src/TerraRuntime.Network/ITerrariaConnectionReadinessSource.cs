namespace TerraRuntime.Network;

/// <summary>
/// Optional connection-lifetime signal consumed by the network watchdog. A production sink chain may
/// expose whether protocol/bootstrap work has reached its fully usable state without leaking gameplay
/// types into the network layer.
/// </summary>
public interface ITerrariaConnectionReadinessSource
{
    bool ConnectionReady { get; }
}
