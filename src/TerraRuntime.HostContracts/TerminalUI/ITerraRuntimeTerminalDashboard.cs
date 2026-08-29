using Terminal.Gui.ViewBase;

namespace TerraRuntime.HostContracts.TerminalUI;

/// <summary>
/// Trusted host-module extension point for one independent terminal dashboard.
/// A provider contributes a complete root view for the dashboard workspace; it cannot inject controls
/// into TerraRuntime's built-in system dashboard.
/// </summary>
public interface ITerraRuntimeTerminalDashboardProvider
{
    string Id { get; }

    string Title { get; }

    /// <summary>
    /// Creates one dashboard root on the Terminal.Gui UI thread. The returned view becomes owned by
    /// the TerraRuntime view tree for that UI session and is disposed with that tree.
    /// </summary>
    View CreateDashboard();

    /// <summary>
    /// Refreshes provider-owned state represented by the supplied root view. Called on the UI thread.
    /// </summary>
    void Refresh(View rootView);
}

/// <summary>
/// Registration surface exposed to trusted host modules during bootstrap.
/// Registration is metadata/factory-only; no TerraRuntime mutable state is exposed.
/// </summary>
public interface ITerraRuntimeTerminalDashboardRegistry
{
    bool TryRegister(ITerraRuntimeTerminalDashboardProvider provider);

    bool TryUnregister(string id);
}

/// <summary>
/// Read-only dashboard-provider source consumed by the TerraRuntime terminal host.
/// </summary>
public interface ITerraRuntimeTerminalDashboardSource
{
    ReadOnlyMemory<ITerraRuntimeTerminalDashboardProvider> CaptureDashboards();
}
