using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Thread-safe world-scoped control plane for runtime interest management.
/// The authoritative runtime owns the routing policy; external hosts only toggle participation.
/// </summary>
public sealed class InterestManagementControl : IInterestManagementControl
{
    private int _enabled;

    public InterestManagementControl(bool enabled = false)
    {
        _enabled = enabled ? 1 : 0;
    }

    public bool IsEnabled => Volatile.Read(ref _enabled) != 0;

    public bool SetEnabled(bool enabled)
    {
        int next = enabled ? 1 : 0;
        return Interlocked.Exchange(ref _enabled, next) != next;
    }
}
