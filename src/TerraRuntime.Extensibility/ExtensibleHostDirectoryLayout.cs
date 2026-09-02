namespace TerraRuntime.Extensibility;

internal sealed class ExtensibleHostDirectoryLayout
{
    private ExtensibleHostDirectoryLayout(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        HostModulesDirectory = Path.Combine(RootDirectory, "HostModules");
        ServerPluginsDirectory = Path.Combine(RootDirectory, "ServerPlugins");
        WorldsDirectory = Path.Combine(RootDirectory, "Worlds");
        ConfigDirectory = Path.Combine(RootDirectory, "config");
        DataDirectory = Path.Combine(RootDirectory, "data");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
    }

    public string RootDirectory { get; }
    public string HostModulesDirectory { get; }
    public string ServerPluginsDirectory { get; }
    public string WorldsDirectory { get; }
    public string ConfigDirectory { get; }
    public string DataDirectory { get; }
    public string LogsDirectory { get; }

    public static ExtensibleHostDirectoryLayout CreateDefault() => new(AppContext.BaseDirectory);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(HostModulesDirectory);
        Directory.CreateDirectory(ServerPluginsDirectory);
        Directory.CreateDirectory(WorldsDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
