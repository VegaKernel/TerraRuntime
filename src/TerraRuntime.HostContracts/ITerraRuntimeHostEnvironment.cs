using TerraRuntime.HostContracts.TerminalUI;
using TerraRuntime.HostContracts.WorldGeneration;

namespace TerraRuntime.HostContracts;

/// <summary>
/// Stable bootstrap information available to a trusted host module before runtime services are attached.
/// Paths are rooted at the extensible server deployment directory.
/// </summary>
public interface ITerraRuntimeHostEnvironment
{
    string RootDirectory { get; }
    string HostModulesDirectory { get; }
    string ServerPluginsDirectory { get; }
    string WorldsDirectory { get; }
    string ConfigDirectory { get; }
    string DataDirectory { get; }
    string LogsDirectory { get; }

    /// <summary>
    /// Optional local terminal-dashboard registration surface. Providers contribute complete independent
    /// dashboards only; they cannot inject controls into TerraRuntime's built-in system dashboard.
    /// </summary>
    ITerraRuntimeTerminalDashboardRegistry TerminalDashboards { get; }

    /// <summary>
    /// Explicit trusted-host registration surface for selectable custom world generators. The host owns discovery
    /// and provider lifetime; TerraRuntime owns plan validation, isolated execution and final world acceptance.
    /// </summary>
    ITerraRuntimeWorldGeneratorRegistry WorldGenerators { get; }
}
