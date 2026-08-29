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
}
