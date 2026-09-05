using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.TerminalUI;
using TerraRuntime.HostContracts.WorldGeneration;

namespace TerraRuntime.Extensibility;

internal sealed class TerraRuntimeHostEnvironment : IEnvironment
{
    public TerraRuntimeHostEnvironment(
        ExtensibleHostDirectoryLayout layout,
        IDashboardRegistry terminalDashboards,
        IGeneratorRegistry worldGenerators)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(terminalDashboards);
        ArgumentNullException.ThrowIfNull(worldGenerators);

        RootDirectory = layout.RootDirectory;
        HostModulesDirectory = layout.HostModulesDirectory;
        ServerPluginsDirectory = layout.ServerPluginsDirectory;
        WorldsDirectory = layout.WorldsDirectory;
        ConfigDirectory = layout.ConfigDirectory;
        DataDirectory = layout.DataDirectory;
        LogsDirectory = layout.LogsDirectory;
        TerminalDashboards = terminalDashboards;
        WorldGenerators = worldGenerators;
    }

    public string RootDirectory { get; }
    public string HostModulesDirectory { get; }
    public string ServerPluginsDirectory { get; }
    public string WorldsDirectory { get; }
    public string ConfigDirectory { get; }
    public string DataDirectory { get; }
    public string LogsDirectory { get; }
    public IDashboardRegistry TerminalDashboards { get; }
    public IGeneratorRegistry WorldGenerators { get; }
}
