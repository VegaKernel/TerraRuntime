using System.Reflection;
using System.Runtime.Loader;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.HostContracts;
using Terminal.Gui.ViewBase;

namespace TerraRuntime.Extensibility;

internal sealed class HostModuleLoadContext : AssemblyLoadContext
{
    private static readonly IReadOnlyDictionary<string, Assembly> SharedContractAssemblies =
        CreateSharedContractAssemblies();

    // Do not let beforefieldinit defer this table until the Load callback. Resolving Terminal.Gui starts its
    // module initializer, which scans loaded assemblies; doing that after collectible modules are published
    // can deadlock against another module's type loading waiting for this same class initializer.
    // An explicit initializer completes before the first context constructor can publish such an assembly.
    static HostModuleLoadContext() { }

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
        if (!string.IsNullOrWhiteSpace(assemblyName.Name) &&
            SharedContractAssemblies.TryGetValue(assemblyName.Name, out Assembly? sharedAssembly))
        {
            return sharedAssembly;
        }

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

    private static IReadOnlyDictionary<string, Assembly> CreateSharedContractAssemblies()
    {
        Assembly hostContracts = typeof(IModule).Assembly;
        Assembly runtimeContracts = typeof(IInterestManagementControl).Assembly;
        Assembly terminalGui = typeof(View).Assembly;
        return new Dictionary<string, Assembly>(StringComparer.Ordinal)
        {
            [hostContracts.GetName().Name
                ?? throw new InvalidOperationException("TerraRuntime.HostContracts assembly has no simple name.")] = hostContracts,
            [runtimeContracts.GetName().Name
                ?? throw new InvalidOperationException("TerraRuntime.Contracts assembly has no simple name.")] = runtimeContracts,
            [terminalGui.GetName().Name
                ?? throw new InvalidOperationException("Terminal.Gui assembly has no simple name.")] = terminalGui
        };
    }
}
