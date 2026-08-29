using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.TerminalUI;
using TerraRuntime.HostContracts.WorldGeneration;

namespace TerraRuntime.ExtensibleHost;

internal sealed class TrustedHostModuleLoader :
    ITerraRuntimeHostLifecycle,
    ITerraRuntimeTerminalDashboardSource,
    ITerraRuntimeWorldGeneratorSource,
    IAsyncDisposable
{
    private static readonly HashSet<string> AllowedTerraRuntimeReferences = new(StringComparer.Ordinal)
    {
        "TerraRuntime.HostContracts",
        "TerraRuntime.Contracts"
    };

    private readonly string directory;
    private readonly List<LoadedHostModule> loaded = [];
    private readonly TerminalDashboardRegistry terminalDashboards = new();
    private readonly HostWorldGeneratorRegistry worldGenerators = new();
    private bool started;
    private bool runtimeAttached;
    private bool disposed;

    public TrustedHostModuleLoader(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
    }

    internal ITerraRuntimeTerminalDashboardRegistry TerminalDashboards => terminalDashboards;
    internal ITerraRuntimeWorldGeneratorRegistry WorldGenerators => worldGenerators;

    public ReadOnlyMemory<ITerraRuntimeTerminalDashboardProvider> CaptureDashboards() =>
        terminalDashboards.CaptureDashboards();

    public ReadOnlyMemory<WorldGeneratorId> CaptureWorldGeneratorIds() =>
        worldGenerators.CaptureWorldGeneratorIds();

    public bool TryResolveWorldGenerator(
        WorldGeneratorId id,
        out IWorldGenerationProvider? provider) =>
        worldGenerators.TryResolveWorldGenerator(id, out provider);

    public async ValueTask<int> StartAllAsync(
        ITerraRuntimeHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(environment);

        if (started)
            throw new InvalidOperationException("Trusted host modules have already been started.");

        Directory.CreateDirectory(directory);

        try
        {
            foreach (string path in Directory
                         .EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await TryLoadAndStartAsync(path, environment, cancellationToken).ConfigureAwait(false);
            }

            started = true;
            return loaded.Count;
        }
        catch
        {
            await StopLoadedModulesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask AttachRuntimeAsync(
        ITerraRuntimeHostRuntime runtime,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(runtime);

        if (!started)
            throw new InvalidOperationException("Trusted host modules must be started before runtime attachment.");
        if (runtimeAttached)
            throw new InvalidOperationException("A TerraRuntime world is already attached to the trusted host modules.");

        int attachedCount = 0;
        try
        {
            foreach (LoadedHostModule loadedModule in loaded)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await loadedModule.Module.AttachRuntimeAsync(runtime, cancellationToken).ConfigureAwait(false);
                attachedCount++;
            }

            runtimeAttached = true;
        }
        catch
        {
            await DetachPrefixAsync(attachedCount, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DetachRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (!runtimeAttached)
            return;

        await DetachPrefixAsync(loaded.Count, cancellationToken).ConfigureAwait(false);
        runtimeAttached = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;

        disposed = true;
        if (runtimeAttached)
            await DetachRuntimeAsync(CancellationToken.None).ConfigureAwait(false);
        await StopLoadedModulesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask TryLoadAndStartAsync(
        string path,
        ITerraRuntimeHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var loadContext = new HostModuleLoadContext(path);
        Assembly assembly;
        try
        {
            assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(path));
        }
        catch (BadImageFormatException)
        {
            loadContext.Unload();
            return;
        }
        catch
        {
            loadContext.Unload();
            throw;
        }

        HostWorldGeneratorRegistry.Scope? generatorScope = null;
        try
        {
            ValidateRuntimeBoundary(assembly, path);

            Type[] moduleTypes = assembly
                .GetExportedTypes()
                .Where(static type =>
                    !type.IsAbstract &&
                    !type.IsInterface &&
                    typeof(ITerraRuntimeHostModule).IsAssignableFrom(type))
                .ToArray();

            if (moduleTypes.Length == 0)
            {
                loadContext.Unload();
                return;
            }

            if (moduleTypes.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Trusted host module assembly '{Path.GetFileName(path)}' must export exactly one " +
                    $"{nameof(ITerraRuntimeHostModule)} implementation; found {moduleTypes.Length}.");
            }

            if (Activator.CreateInstance(moduleTypes[0]) is not ITerraRuntimeHostModule module)
            {
                throw new InvalidOperationException(
                    $"Trusted host module '{moduleTypes[0].FullName}' requires a public parameterless constructor.");
            }

            if (string.IsNullOrWhiteSpace(module.Name))
                throw new InvalidOperationException($"Trusted host module in '{Path.GetFileName(path)}' has an empty name.");

            if (loaded.Any(existing => string.Equals(existing.Module.Name, module.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"A trusted host module named '{module.Name}' is already loaded.");
            }

            generatorScope = worldGenerators.CreateScope();
            var moduleEnvironment = new ScopedHostEnvironment(environment, generatorScope);
            await module.StartAsync(moduleEnvironment, cancellationToken).ConfigureAwait(false);
            loaded.Add(new LoadedHostModule(path, module, loadContext, generatorScope));
            generatorScope = null;
            Console.WriteLine($"Trusted host module loaded: {module.Name} ({Path.GetFileName(path)}).");
        }
        catch
        {
            generatorScope?.Dispose();
            loadContext.Unload();
            throw;
        }
    }

    private async ValueTask DetachPrefixAsync(int count, CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        for (int index = count - 1; index >= 0; index--)
        {
            try
            {
                await loaded[index].Module.DetachRuntimeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }

        if (failures is { Count: > 0 })
            throw new AggregateException("One or more trusted host modules failed to detach from TerraRuntime.", failures);
    }

    private async ValueTask StopLoadedModulesAsync(CancellationToken cancellationToken)
    {
        terminalDashboards.Clear();

        for (int index = loaded.Count - 1; index >= 0; index--)
        {
            LoadedHostModule loadedModule = loaded[index];
            try
            {
                await loadedModule.Module.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    $"Trusted host module '{loadedModule.Module.Name}' failed during shutdown: {exception.Message}");
            }
            finally
            {
                // Retire every provider while its collectible AssemblyLoadContext is still alive. This prevents a
                // stale provider instance from keeping an unloaded module rooted through the generation registry.
                loadedModule.WorldGeneratorScope.Dispose();
                loadedModule.LoadContext.Unload();
            }
        }

        loaded.Clear();
        started = false;
    }

    private static void ValidateRuntimeBoundary(Assembly assembly, string path)
    {
        string[] forbiddenReferences = assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name)
            .Where(static name =>
                !string.IsNullOrWhiteSpace(name) &&
                name.StartsWith("TerraRuntime", StringComparison.Ordinal) &&
                !AllowedTerraRuntimeReferences.Contains(name))
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (forbiddenReferences.Length == 0)
            return;

        throw new InvalidOperationException(
            $"Trusted host module '{Path.GetFileName(path)}' references TerraRuntime implementation assemblies: " +
            $"{string.Join(", ", forbiddenReferences)}. Host modules must compile against TerraRuntime.HostContracts " +
            "and explicitly admitted contract assemblies only.");
    }

    private sealed class ScopedHostEnvironment : ITerraRuntimeHostEnvironment
    {
        private readonly ITerraRuntimeHostEnvironment source;

        public ScopedHostEnvironment(
            ITerraRuntimeHostEnvironment source,
            ITerraRuntimeWorldGeneratorRegistry worldGenerators)
        {
            this.source = source;
            WorldGenerators = worldGenerators;
        }

        public string RootDirectory => source.RootDirectory;
        public string HostModulesDirectory => source.HostModulesDirectory;
        public string ServerPluginsDirectory => source.ServerPluginsDirectory;
        public string WorldsDirectory => source.WorldsDirectory;
        public string ConfigDirectory => source.ConfigDirectory;
        public string DataDirectory => source.DataDirectory;
        public string LogsDirectory => source.LogsDirectory;
        public ITerraRuntimeTerminalDashboardRegistry TerminalDashboards => source.TerminalDashboards;
        public ITerraRuntimeWorldGeneratorRegistry WorldGenerators { get; }
    }

    private sealed record LoadedHostModule(
        string Path,
        ITerraRuntimeHostModule Module,
        HostModuleLoadContext LoadContext,
        IDisposable WorldGeneratorScope);
}
