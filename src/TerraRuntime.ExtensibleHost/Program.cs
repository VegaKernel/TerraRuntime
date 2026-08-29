namespace TerraRuntime.ExtensibleHost;

internal static class Program
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
                $"Could not initialize the extensible TerraRuntime host under '{layout.RootDirectory}': {exception.Message}");
            return 30;
        }

        await using var loader = new TrustedHostModuleLoader(layout.HostModulesDirectory);
        var environment = new TerraRuntimeHostEnvironment(
            layout,
            loader.TerminalDashboards,
            loader.WorldGenerators);
        try
        {
            int loadedCount = await loader.StartAllAsync(environment).ConfigureAwait(false);
            Console.WriteLine($"Trusted host modules active: {loadedCount}.");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Trusted host module startup failed: {exception.Message}");
            return 31;
        }

        if (args.Contains("--host-module-smoke", StringComparer.Ordinal))
            return 0;

        return global::TerraRuntime.StartupProgram.Run(args, loader, loader);
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
