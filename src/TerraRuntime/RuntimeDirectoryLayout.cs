namespace TerraRuntime;

internal sealed class RuntimeDirectoryLayout
{
    public RuntimeDirectoryLayout(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
        WorldsDirectory = Path.Combine(RootDirectory, "Worlds");
        ConfigDirectory = Path.Combine(RootDirectory, "config");
        DataDirectory = Path.Combine(RootDirectory, "data");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
    }

    public string RootDirectory { get; }

    public string WorldsDirectory { get; }

    public string ConfigDirectory { get; }

    public string DataDirectory { get; }

    public string LogsDirectory { get; }

    public static RuntimeDirectoryLayout CreateDefault() => new(AppContext.BaseDirectory);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(WorldsDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
