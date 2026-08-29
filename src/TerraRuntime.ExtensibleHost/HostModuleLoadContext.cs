using System.Reflection;
using System.Runtime.Loader;
using TerraRuntime.HostContracts;

namespace TerraRuntime.ExtensibleHost;

internal sealed class HostModuleLoadContext : AssemblyLoadContext
{
    private static readonly string HostContractsAssemblyName =
        typeof(ITerraRuntimeHostModule).Assembly.GetName().Name
        ?? throw new InvalidOperationException("TerraRuntime.HostContracts assembly has no simple name.");

    private readonly AssemblyDependencyResolver resolver;
    private readonly string dependencyDirectory;

    public HostModuleLoadContext(string modulePath)
        : base($"TerraRuntime.HostModule:{Path.GetFileNameWithoutExtension(modulePath)}", isCollectible: true)
    {
        string fullPath = Path.GetFullPath(modulePath);
        resolver = new AssemblyDependencyResolver(fullPath);
        dependencyDirectory = Path.Combine(
            Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory,
            Path.GetFileNameWithoutExtension(fullPath));
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (string.Equals(assemblyName.Name, HostContractsAssemblyName, StringComparison.Ordinal))
            return typeof(ITerraRuntimeHostModule).Assembly;

        string? resolvedPath = resolver.ResolveAssemblyToPath(assemblyName);
        if (!string.IsNullOrWhiteSpace(resolvedPath))
            return LoadFromAssemblyPath(resolvedPath);

        if (!string.IsNullOrWhiteSpace(assemblyName.Name))
        {
            string fallbackPath = Path.Combine(dependencyDirectory, $"{assemblyName.Name}.dll");
            if (File.Exists(fallbackPath))
                return LoadFromAssemblyPath(fallbackPath);
        }

        return null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        string? resolvedPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return string.IsNullOrWhiteSpace(resolvedPath)
            ? nint.Zero
            : LoadUnmanagedDllFromPath(resolvedPath);
    }
}
