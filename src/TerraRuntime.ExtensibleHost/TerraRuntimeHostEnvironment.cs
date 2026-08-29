using TerraRuntime.HostContracts;

namespace TerraRuntime.ExtensibleHost;

internal sealed class TerraRuntimeHostEnvironment : ITerraRuntimeHostEnvironment
{
    public TerraRuntimeHostEnvironment(ExtensibleHostDirectoryLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        RootDirectory = layout.RootDirectory;
        HostModulesDirectory = layout.HostModulesDirectory;
        ServerPluginsDirectory = layout.ServerPluginsDirectory;
        WorldsDirectory = layout.WorldsDirectory;
        ConfigDirectory = layout.ConfigDirectory;
        DataDirectory = layout.DataDirectory;
        LogsDirectory = layout.LogsDirectory;
    }

    public string RootDirectory { get; }
    public string HostModulesDirectory { get; }
    public string ServerPluginsDirectory { get; }
    public string WorldsDirectory { get; }
    public string ConfigDirectory { get; }
    public string DataDirectory { get; }
    public string LogsDirectory { get; }
}
