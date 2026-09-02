namespace TerraRuntime.Extensibility;

public static class ExtensibleStartup
{
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return RunAsync(args).GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (ShouldBypassHostModules(args))
            return global::TerraRuntime.StartupProgram.Main(args);

        ExtensibleHostDirectoryLayout layout = ExtensibleHostDirectoryLayout.CreateDefault();
        try
        {
            layout.EnsureCreated();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"Could not initialize the extensible TerraRuntime host under '{layout.RootDirectory}'.");
            Console.Error.WriteLine(exception);
            return 30;
        }

        // Host modules are optional by default. Operators may mark selected files (or '*') as required through
        // TERRARUNTIME_REQUIRED_HOST_MODULES without widening the host-module contract surface.
        TrustedHostModuleLoadPolicy modulePolicy = TrustedHostModuleLoadPolicy.FromEnvironment();
        await using var loader = new TrustedHostModuleLoader(
            layout.HostModulesDirectory,
            modulePolicy,
            Console.Error);
        var environment = new TerraRuntimeHostEnvironment(
            layout,
            loader.TerminalDashboards,
            loader.WorldGenerators);
        try
        {
            int loadedCount = await loader.StartAllAsync(environment).ConfigureAwait(false);
            int faultCount = loader.CaptureFaults().Length;
            Console.WriteLine(
                $"Trusted host modules active: {loadedCount}; contained faults: {faultCount}; " +
                $"required-policy={(modulePolicy.RequireAllModules ? "all" : "selected/optional")}.");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Required trusted host module startup failed.");
            Console.Error.WriteLine(exception);
            return 31;
        }

        if (args.Contains("--host-module-smoke", StringComparer.Ordinal))
            return 0;

        return global::TerraRuntime.StartupProgram.Run(args, loader, loader, loader);
    }

    private static bool ShouldBypassHostModules(IEnumerable<string> args)
    {
        foreach (string arg in args)
        {
            if (arg is "--loop-smoke" or
                "--protocol-smoke" or
                "--network-smoke" or
                "--world-smoke" or
                "--tui-smoke" or
                "--save-wld" or
                "--help" or
                "-h")
            {
                return true;
            }
        }

        return false;
    }
}
