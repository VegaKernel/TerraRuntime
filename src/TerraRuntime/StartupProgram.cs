using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.TerminalUI;

namespace TerraRuntime;

public static class StartupProgram
{
    public static int Main(string[] args) => Run(args);

    public static int Run(
        string[] args,
        ITerraRuntimeHostLifecycle? hostLifecycle = null,
        ITerraRuntimeTerminalDashboardSource? terminalDashboards = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (ContainsStandaloneMode(args))
            return Program.Main(args);

        if (args.Any(static arg =>
                string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase)))
        {
            PrintUsage();
            return 0;
        }

        RuntimeDirectoryLayout directories = RuntimeDirectoryLayout.CreateDefault();
        try
        {
            directories.EnsureCreated();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"Could not initialize TerraRuntime directories under '{directories.RootDirectory}': {exception.Message}");
            return 24;
        }

        string[] serverArgs = args;
        if (!HasWorldArgument(serverArgs))
        {
            if (!LocalWorldSelector.TrySelect(directories.WorldsDirectory, out string? worldPath) ||
                string.IsNullOrWhiteSpace(worldPath))
            {
                return 0;
            }

            serverArgs = [.. serverArgs, "--world", worldPath];
        }

        if (!ServerHostOptions.TryParse(serverArgs, out ServerHostOptions? options, out string? error) || options is null)
        {
            Console.Error.WriteLine(error ?? "Invalid server host options.");
            PrintUsage();
            return 23;
        }

        return TerrariaServerHost.RunAsync(
                options,
                hostLifecycle: hostLifecycle,
                terminalDashboards: terminalDashboards)
            .GetAwaiter()
            .GetResult();
    }

    private static bool ContainsStandaloneMode(IEnumerable<string> args)
    {
        foreach (string arg in args)
        {
            if (arg is "--loop-smoke" or
                "--protocol-smoke" or
                "--network-smoke" or
                "--world-smoke" or
                "--tui-smoke" or
                "--save-wld")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasWorldArgument(IEnumerable<string> args)
    {
        foreach (string arg in args)
        {
            if (arg == "--world")
                return true;
        }

        return false;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("TerraRuntime .NET 11 server runtime.");
        Console.WriteLine();
        Console.WriteLine("Interactive startup:");
        Console.WriteLine("  TerraRuntime.Server");
        Console.WriteLine("    Scans the runtime Worlds folder and lets you choose a .wld world.");
        Console.WriteLine();
        Console.WriteLine("Server startup:");
        Console.WriteLine("  TerraRuntime.Server --world <path.wld> [--port 7777] [--max-players 8] [--interest-management] [--no-tui]");
        Console.WriteLine();
        Console.WriteLine("Terminal UI is enabled by default. Use --no-tui to disable it.");
        Console.WriteLine("Smoke modes: --loop-smoke, --protocol-smoke, --network-smoke, --world-smoke, --tui-smoke.");
        Console.WriteLine("Checkpoint save: --save-wld <path.wld>.");
    }
}
