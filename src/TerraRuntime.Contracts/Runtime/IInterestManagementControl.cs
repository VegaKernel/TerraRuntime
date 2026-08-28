namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Narrow control-plane surface for a world's runtime-owned interest-management system.
/// Hosts such as Vega may enable or disable the mechanism, but do not control its spatial policy,
/// radii, clustering, hysteresis, resync rules or entity-specific routing decisions.
/// </summary>
public interface IInterestManagementControl
{
    bool IsEnabled { get; }

    /// <summary>
    /// Changes whether runtime interest management participates in outbound synchronization.
    /// Returns true only when the state changed.
    /// </summary>
    bool SetEnabled(bool enabled);
}
