using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.TerminalUI;
using TerraRuntime.HostContracts.WorldGeneration;

namespace TerraRuntime.Extensibility;

internal sealed class TrustedHostModuleLoader :
    ILifecycle,
    IDashboardSource,
    ITerraRuntimeWorldGeneratorSource,
    IAsyncDisposable
{
    private const int MaximumRetainedFaults = 128;

    private static readonly HashSet<string> AllowedTerraRuntimeReferences = new(StringComparer.Ordinal)
    {
        "TerraRuntime.HostContracts",
        "TerraRuntime.Contracts"
    };

    private readonly string directory;
    private readonly TrustedHostModuleLoadPolicy policy;
    private readonly TextWriter diagnostics;
    private readonly List<LoadedHostModule> loaded = [];
    private readonly List<TrustedHostModuleFault> faults = [];
    private readonly object faultGate = new();
    private readonly TerminalDashboardRegistry terminalDashboards = new();
    private readonly HostWorldGeneratorRegistry worldGenerators = new();
    private readonly TrustedHostModuleHealthDashboardProvider healthDashboard;
    private bool started;
    private bool runtimeAttached;
    private bool disposed;

    public TrustedHostModuleLoader(string directory)
        : this(directory, TrustedHostModuleLoadPolicy.Strict, Console.Error)
    {
    }

    internal TrustedHostModuleLoader(
        string directory,
        TrustedHostModuleLoadPolicy policy,
        TextWriter? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.GetFullPath(directory);
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
        this.diagnostics = diagnostics ?? TextWriter.Null;
        healthDashboard = new TrustedHostModuleHealthDashboardProvider(CaptureFaults);
    }

    internal IDashboardRegistry TerminalDashboards => terminalDashboards;
    internal IGeneratorRegistry WorldGenerators => worldGenerators;

    public ReadOnlyMemory<IDashboardProvider> CaptureDashboards()
    {
        ReadOnlyMemory<IDashboardProvider> moduleDashboards =
            terminalDashboards.CaptureDashboards();
        if (CaptureFaults().IsEmpty)
            return moduleDashboards;

        var result = new IDashboardProvider[moduleDashboards.Length + 1];
        moduleDashboards.Span.CopyTo(result);
        result[^1] = healthDashboard;
        return result;
    }

    public ReadOnlyMemory<WorldGeneratorId> CaptureWorldGeneratorIds() =>
        worldGenerators.CaptureWorldGeneratorIds();

    public ReadOnlyMemory<TrustedHostModuleFault> CaptureFaults()
    {
        lock (faultGate)
            return faults.ToArray();
    }

    public bool TryResolveWorldGenerator(
        WorldGeneratorId id,
        out IWorldGenerationProvider? provider) =>
        worldGenerators.TryResolveWorldGenerator(id, out provider);

    public async ValueTask<int> StartAllAsync(
        IEnvironment environment,
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
                try
                {
                    await TryLoadAndStartAsync(path, environment, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    bool required = policy.IsRequired(path);
                    ReportFault(path, null, TrustedHostModuleFaultPhase.Startup, required, exception);
                    if (required)
                    {
                        throw new InvalidOperationException(
                            $"Required trusted host module '{Path.GetFileName(path)}' failed to start.",
                            exception);
                    }
                }
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
        IRuntime runtime,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(runtime);

        if (!started)
            throw new InvalidOperationException("Trusted host modules must be started before runtime attachment.");
        if (runtimeAttached)
            throw new InvalidOperationException("A TerraRuntime world is already attached to the trusted host modules.");

        try
        {
            foreach (LoadedHostModule loadedModule in loaded.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (loadedModule.Module is IModuleWorldActivation activation &&
                    !activation.IsEnabledForWorld(runtime.Info))
                {
                    continue;
                }

                var runtimeScope = new ScopedHostRuntime(runtime);
                loadedModule.RuntimeScope = runtimeScope;
                try
                {
                    await loadedModule.Module
                        .AttachRuntimeAsync(runtimeScope, cancellationToken)
                        .ConfigureAwait(false);
                    loadedModule.RuntimeAttached = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await RetireRuntimeScopeAfterFailedAttachAsync(loadedModule, runtimeScope)
                        .ConfigureAwait(false);
                    throw;
                }
                catch (Exception attachmentFailure)
                {
                    Exception fault = await RetireRuntimeScopeAfterFailedAttachAsync(
                            loadedModule,
                            runtimeScope,
                            attachmentFailure)
                        .ConfigureAwait(false);
                    bool required = policy.IsRequired(loadedModule.Path);
                    ReportFault(
                        loadedModule.Path,
                        loadedModule.Module.Name,
                        TrustedHostModuleFaultPhase.RuntimeAttach,
                        required,
                        fault);

                    if (required)
                    {
                        throw new InvalidOperationException(
                            $"Required trusted host module '{loadedModule.Module.Name}' failed to attach to the runtime.",
                            fault);
                    }

                    await RetireLoadedModuleAsync(
                            loadedModule,
                            attemptModuleDetach: true,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }

            runtimeAttached = true;
        }
        catch (Exception attachmentFailure)
        {
            try
            {
                await DetachAttachedModulesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception detachFailure)
            {
                throw new AggregateException(
                    "Trusted host runtime attachment failed and rollback also reported failures.",
                    attachmentFailure,
                    detachFailure);
            }

            throw;
        }
    }

    public async ValueTask DetachRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (!runtimeAttached)
            return;

        try
        {
            await DetachAttachedModulesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            runtimeAttached = false;
        }
    }

    public async ValueTask<int> ReloadAllAsync(
        IEnvironment environment,
        IRuntime runtime,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(runtime);
        if (!started)
            throw new InvalidOperationException("Trusted host modules must be started before reload.");

        await DetachRuntimeAsync(cancellationToken).ConfigureAwait(false);
        await StopLoadedModulesAsync(cancellationToken).ConfigureAwait(false);
        int reloaded = await StartAllAsync(environment, cancellationToken).ConfigureAwait(false);
        try
        {
            await AttachRuntimeAsync(runtime, cancellationToken).ConfigureAwait(false);
            return reloaded;
        }
        catch
        {
            await StopLoadedModulesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;

        disposed = true;
        if (runtimeAttached)
        {
            try
            {
                await DetachRuntimeAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Shutdown is already in progress. Keep unwinding and preserve the full failure in diagnostics
                // instead of letting a trusted extension turn normal process teardown into an unhandled exception.
                ReportFault(
                    directory,
                    null,
                    TrustedHostModuleFaultPhase.RuntimeDetach,
                    required: true,
                    exception);
            }
        }

        await StopLoadedModulesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask TryLoadAndStartAsync(
        string path,
        IEnvironment environment,
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

        TerminalDashboardRegistry.Scope? dashboardScope = null;
        HostWorldGeneratorRegistry.Scope? generatorScope = null;
        try
        {
            ValidateRuntimeBoundary(assembly, path);

            Type[] moduleTypes = assembly
                .GetExportedTypes()
                .Where(static type =>
                    !type.IsAbstract &&
                    !type.IsInterface &&
                    typeof(IModule).IsAssignableFrom(type))
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
                    $"{nameof(IModule)} implementation; found {moduleTypes.Length}.");
            }

            if (Activator.CreateInstance(moduleTypes[0]) is not IModule module)
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

            dashboardScope = terminalDashboards.CreateScope();
            generatorScope = worldGenerators.CreateScope();
            var moduleEnvironment = new ScopedHostEnvironment(environment, dashboardScope, generatorScope);
            await module.StartAsync(moduleEnvironment, cancellationToken).ConfigureAwait(false);
            loaded.Add(new LoadedHostModule(
                path,
                module,
                loadContext,
                dashboardScope,
                generatorScope));
            dashboardScope = null;
            generatorScope = null;
            Console.WriteLine($"Trusted host module loaded: {module.Name} ({Path.GetFileName(path)}).");
        }
        catch
        {
            dashboardScope?.Dispose();
            generatorScope?.Dispose();
            loadContext.Unload();
            throw;
        }
    }

    private async ValueTask<Exception> RetireRuntimeScopeAfterFailedAttachAsync(
        LoadedHostModule loadedModule,
        ScopedHostRuntime runtimeScope,
        Exception? attachmentFailure = null)
    {
        Exception? retirementFailure = null;
        try
        {
            await runtimeScope.RetireAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            retirementFailure = exception;
            ReportFault(
                loadedModule.Path,
                loadedModule.Module.Name,
                TrustedHostModuleFaultPhase.ScopeRetirement,
                policy.IsRequired(loadedModule.Path),
                exception);
        }
        finally
        {
            loadedModule.RuntimeScope = null;
            loadedModule.RuntimeAttached = false;
        }

        if (attachmentFailure is null)
            return retirementFailure ?? new OperationCanceledException("Trusted host module runtime attachment was cancelled.");
        if (retirementFailure is null)
            return attachmentFailure;

        return new AggregateException(
            "Trusted host runtime attachment and scope retirement both failed.",
            attachmentFailure,
            retirementFailure);
    }

    private async ValueTask DetachAttachedModulesAsync(CancellationToken cancellationToken)
    {
        List<Exception>? requiredFailures = null;
        LoadedHostModule[] snapshot = loaded.ToArray();
        for (int index = snapshot.Length - 1; index >= 0; index--)
        {
            LoadedHostModule loadedModule = snapshot[index];
            bool required = policy.IsRequired(loadedModule.Path);
            bool moduleDetachFailed = false;
            if (loadedModule.RuntimeAttached)
            {
                try
                {
                    await loadedModule.Module
                        .DetachRuntimeAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    moduleDetachFailed = true;
                    ReportFault(
                        loadedModule.Path,
                        loadedModule.Module.Name,
                        TrustedHostModuleFaultPhase.RuntimeDetach,
                        required,
                        exception);
                    if (required)
                        (requiredFailures ??= []).Add(exception);
                }
            }

            try
            {
                if (loadedModule.RuntimeScope is not null)
                    await loadedModule.RuntimeScope.RetireAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                ReportFault(
                    loadedModule.Path,
                    loadedModule.Module.Name,
                    TrustedHostModuleFaultPhase.ScopeRetirement,
                    required,
                    exception);
                if (required)
                    (requiredFailures ??= []).Add(exception);
                else
                    moduleDetachFailed = true;
            }
            finally
            {
                loadedModule.RuntimeScope = null;
                loadedModule.RuntimeAttached = false;
            }

            if (moduleDetachFailed && !required)
            {
                // A module that could not cleanly detach is no longer trusted for the next world/session.
                // Retire only that optional module after its runtime scope has been revoked.
                await RetireLoadedModuleAsync(
                        loadedModule,
                        attemptModuleDetach: false,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        if (requiredFailures is { Count: > 0 })
        {
            throw new AggregateException(
                "One or more required trusted host modules failed to detach from TerraRuntime.",
                requiredFailures);
        }
    }

    private async ValueTask RetireLoadedModuleAsync(
        LoadedHostModule loadedModule,
        bool attemptModuleDetach,
        CancellationToken cancellationToken)
    {
        loaded.Remove(loadedModule);
        bool required = policy.IsRequired(loadedModule.Path);

        if (attemptModuleDetach)
        {
            try
            {
                await loadedModule.Module.DetachRuntimeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                ReportFault(
                    loadedModule.Path,
                    loadedModule.Module.Name,
                    TrustedHostModuleFaultPhase.RuntimeDetach,
                    required,
                    exception);
            }
        }

        try
        {
            if (loadedModule.RuntimeScope is not null)
                await loadedModule.RuntimeScope.RetireAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ReportFault(
                loadedModule.Path,
                loadedModule.Module.Name,
                TrustedHostModuleFaultPhase.ScopeRetirement,
                required,
                exception);
        }
        finally
        {
            loadedModule.RuntimeScope = null;
            loadedModule.RuntimeAttached = false;
        }

        try
        {
            await loadedModule.Module.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ReportFault(
                loadedModule.Path,
                loadedModule.Module.Name,
                TrustedHostModuleFaultPhase.Stop,
                required,
                exception);
        }
        finally
        {
            // Registries are loader-owned so a broken StopAsync cannot leave module objects rooted through
            // dashboard or world-generator registrations after the collectible context is asked to unload.
            loadedModule.DashboardScope.Dispose();
            loadedModule.WorldGeneratorScope.Dispose();
            loadedModule.LoadContext.Unload();
        }
    }

    private async ValueTask StopLoadedModulesAsync(CancellationToken cancellationToken)
    {
        LoadedHostModule[] snapshot = loaded.ToArray();
        for (int index = snapshot.Length - 1; index >= 0; index--)
        {
            await RetireLoadedModuleAsync(
                    snapshot[index],
                    attemptModuleDetach: snapshot[index].RuntimeAttached,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        loaded.Clear();
        terminalDashboards.Clear();
        started = false;
        runtimeAttached = false;
    }

    private void ReportFault(
        string path,
        string? moduleName,
        TrustedHostModuleFaultPhase phase,
        bool required,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var fault = new TrustedHostModuleFault(
            Path.GetFileName(path),
            moduleName,
            phase,
            required,
            DateTimeOffset.UtcNow,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.ToString());

        lock (faultGate)
        {
            if (faults.Count >= MaximumRetainedFaults)
                faults.RemoveAt(0);
            faults.Add(fault);
        }

        try
        {
            diagnostics.WriteLine(
                $"Trusted host module fault: file='{fault.FileName}' module='{fault.ModuleName ?? "unknown"}' " +
                $"phase={fault.Phase} required={fault.Required}.");
            diagnostics.WriteLine(fault.Detail);
            diagnostics.Flush();
        }
        catch
        {
            // Diagnostics are best effort. A broken output stream must never turn an already-contained module
            // failure into a second host failure.
        }
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

    private sealed class ScopedHostEnvironment : IEnvironment
    {
        private readonly IEnvironment source;

        public ScopedHostEnvironment(
            IEnvironment source,
            IDashboardRegistry terminalDashboards,
            IGeneratorRegistry worldGenerators)
        {
            this.source = source;
            TerminalDashboards = terminalDashboards;
            WorldGenerators = worldGenerators;
        }

        public string RootDirectory => source.RootDirectory;
        public string HostModulesDirectory => source.HostModulesDirectory;
        public string ServerPluginsDirectory => source.ServerPluginsDirectory;
        public string WorldsDirectory => source.WorldsDirectory;
        public string ConfigDirectory => source.ConfigDirectory;
        public string DataDirectory => source.DataDirectory;
        public string LogsDirectory => source.LogsDirectory;
        public IDashboardRegistry TerminalDashboards { get; }
        public IGeneratorRegistry WorldGenerators { get; }
    }

    private sealed class LoadedHostModule
    {
        public LoadedHostModule(
            string path,
            IModule module,
            HostModuleLoadContext loadContext,
            IDisposable dashboardScope,
            IDisposable worldGeneratorScope)
        {
            Path = path;
            Module = module;
            LoadContext = loadContext;
            DashboardScope = dashboardScope;
            WorldGeneratorScope = worldGeneratorScope;
        }

        public string Path { get; }
        public IModule Module { get; }
        public HostModuleLoadContext LoadContext { get; }
        public IDisposable DashboardScope { get; }
        public IDisposable WorldGeneratorScope { get; }
        public ScopedHostRuntime? RuntimeScope { get; set; }
        public bool RuntimeAttached { get; set; }
    }
}
