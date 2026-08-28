namespace TerraRuntime;

internal static class StartupProgram
{
    public static int Main(string[] args)
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

        string[] serverArgs = args;
        if (!HasWorldArgument(serverArgs))
        {
            if (!LocalWorldSelector.TrySelect(out string? worldPath) || string.IsNullOrWhiteSpace(worldPath))
                return 0;

            serverArgs = [.. serverArgs, "--world", worldPath];
        }

        if (!ServerHostOptions.TryParse(serverArgs, out ServerHostOptions? options, out string? error) || options is null)
        {
            Console.Error.WriteLine(error ?? "Invalid server host options.");
            PrintUsage();
            return 23;
        }

        return TerrariaServerHost.RunAsync(options).GetAwaiter().GetResult();
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
            if (arg is "--world" or "-world")
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
        Console.WriteLine("    Scans local Worlds folders and lets you choose a .wld world.");
        Console.WriteLine();
        Console.WriteLine("Server startup:");
        Console.WriteLine("  TerraRuntime.Server --world <path.wld> [--port 7777] [--max-players 8] [--interest-management] [--no-tui]");
        Console.WriteLine("  Vega/Terraria aliases: -world <path.wld> -port 7777 -maxplayers 8");
        Console.WriteLine();
        Console.WriteLine("Terminal UI is enabled by default. Use --no-tui or --plain to disable it.");
        Console.WriteLine("Smoke modes: --loop-smoke, --protocol-smoke, --network-smoke, --world-smoke, --tui-smoke.");
        Console.WriteLine("Checkpoint save: --save-wld <path.wld>.");
    }
}
