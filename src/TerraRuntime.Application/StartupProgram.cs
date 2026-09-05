using System.Security.Cryptography;
using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.TerminalUI;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.Application.TerminalUI;

namespace TerraRuntime.Application;

public static class StartupProgram
{
    private static IDashboardSource? currentTerminalDashboards;

    internal static IDashboardSource? CurrentTerminalDashboards =>
        Volatile.Read(ref currentTerminalDashboards);

    public static int Main(string[] args) => Run(args);

    public static int Run(
        string[] args,
        ILifecycle? hostLifecycle = null,
        IDashboardSource? terminalDashboards = null,
        ITerraRuntimeWorldGeneratorSource? worldGenerators = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (WorldGenerationCreateSmoke.TryRun(args, out int worldgenSmokeExitCode))
            return worldgenSmokeExitCode;

        if (ContainsStandaloneMode(args))
            return StandaloneServerProgram.Main(args);

        RuntimeProductInfo.TryApplyConsoleTitle();

        var startupWorldGenerators = new StartupWorldGeneratorSource(worldGenerators);

        if (args.Any(static arg =>
                string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase)))
        {
            PrintUsage();
            return 0;
        }

        if (args.Contains("--list-world-generators", StringComparer.OrdinalIgnoreCase))
        {
            PrintWorldGenerators(startupWorldGenerators);
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
        bool startupTuiEnabled = IsTerminalUiRequested(serverArgs);
        if (StartupWorldCreationRequestParser.HasCreateWorldArgument(serverArgs))
        {
            if (HasWorldArgument(serverArgs))
            {
                Console.Error.WriteLine("--create-world cannot be combined with --world.");
                return 25;
            }

            if (!StartupWorldCreationRequestParser.TryParse(
                    serverArgs,
                    directories.WorldsDirectory,
                    out StartupWorldCreationRequest creationRequest,
                    out string? creationError))
            {
                Console.Error.WriteLine(creationError ?? "Invalid world creation options.");
                PrintUsage();
                return 25;
            }

            if (!TryCreateStartupWorld(
                    creationRequest,
                    startupWorldGenerators,
                    startupTuiEnabled,
                    out string? createdPath))
            {
                return 25;
            }

            serverArgs =
            [
                .. StartupWorldCreationRequestParser.RemoveCreationArguments(serverArgs),
                "--world",
                createdPath!
            ];
        }
        else if (!HasWorldArgument(serverArgs))
        {
            if (!LocalWorldSelector.TrySelectOrCreate(
                    directories.WorldsDirectory,
                    out LocalWorldSelection selection))
            {
                return 0;
            }

            StartupConsolePresentation.ClearForTransition();

            string? worldPath;
            if (selection.Kind == LocalWorldSelectionKind.CreateWorld)
            {
                if (!InteractiveWorldCreationPrompt.TryPrompt(
                        directories.WorldsDirectory,
                        startupWorldGenerators,
                        out StartupWorldCreationRequest creationRequest))
                {
                    return 0;
                }

                StartupConsolePresentation.ClearForTransition();

                if (!TryCreateStartupWorld(
                        creationRequest,
                        startupWorldGenerators,
                        startupTuiEnabled,
                        out worldPath))
                {
                    return 25;
                }
            }
            else
            {
                worldPath = selection.WorldPath;
            }

            if (string.IsNullOrWhiteSpace(worldPath))
                return 0;

            serverArgs = [.. serverArgs, "--world", worldPath];
        }

        if (!ServerHostOptions.TryParse(serverArgs, out ServerHostOptions? options, out string? error) || options is null)
        {
            Console.Error.WriteLine(error ?? "Invalid server host options.");
            PrintUsage();
            return 23;
        }

        StartupProgressUiHost? startupUi = options.TerminalUiEnabled
            ? StartupProgressUiHost.StartServerStartup(
                GetWorldDisplayName(options.WorldPath),
                static message => Console.Error.WriteLine(message))
            : null;

        IDashboardSource? previous =
            Interlocked.Exchange(ref currentTerminalDashboards, terminalDashboards);
        try
        {
            int exitCode = TerrariaServerHost.RunAsync(
                    options,
                    hostLifecycle: hostLifecycle,
                    worldGenerators: startupWorldGenerators)
                .GetAwaiter()
                .GetResult();
            if (exitCode != 0 && startupUi?.OwnsTerminal == true)
                startupUi.FailAndRelease($"Server startup stopped with exit code {exitCode}.");
            return exitCode;
        }
        finally
        {
            startupUi?.Dispose();
            Interlocked.Exchange(ref currentTerminalDashboards, previous);
        }
    }

    private static bool TryCreateStartupWorld(
        StartupWorldCreationRequest request,
        ITerraRuntimeWorldGeneratorSource generators,
        bool terminalUiEnabled,
        out string? worldPath)
    {
        long maxTileCount = ServerWorldLoadPolicy.CreateLimits().MaxTileCount;
        var persistence = new RuntimeWorldCreationPersistencePipeline(generators, maxTileCount);
        long nowBinary = DateTime.UtcNow.ToBinary();
        StartupProgressUiHost? progressUi = terminalUiEnabled
            ? StartupProgressUiHost.StartWorldGeneration(
                request.Generation.WorldName,
                static message => Console.Error.WriteLine(message))
            : null;

        if (progressUi is null)
        {
            Console.WriteLine(
                $"Generating world '{request.Generation.WorldName}' with " +
                $"'{request.Generation.GeneratorId.Value}' " +
                $"({request.Generation.WidthTiles}x{request.Generation.HeightTiles}, " +
                $"seed={request.Generation.Seed}, mode={request.Generation.Options.GameMode}, " +
                $"evil={request.Generation.Options.Evil})...");
        }

        try
        {
            RuntimeWorldCreationPersistenceResult creation = persistence.TryCreateAndPersist(
                request.Generation,
                request.OutputPath,
                Guid.NewGuid(),
                worldId: RandomNumberGenerator.GetInt32(1, int.MaxValue),
                creationTimeBinary: nowBinary,
                lastPlayedBinary: nowBinary,
                progressSink: progressUi);
            if (!creation.Succeeded || string.IsNullOrWhiteSpace(creation.WorldPath))
            {
                progressUi?.FailAndRelease($"World creation failed: {creation.Status}.");
                PrintWorldCreationFailure(request, creation, generators, maxTileCount);
                worldPath = null;
                return false;
            }

            worldPath = creation.WorldPath;
            if (progressUi?.OwnsTerminal == true)
                progressUi.CompleteAndRelease("World generated, validated and published atomically");
            else
                Console.WriteLine($"World created: '{worldPath}'.");
            return true;
        }
        finally
        {
            progressUi?.Dispose();
        }
    }

    private static void PrintWorldCreationFailure(
        StartupWorldCreationRequest request,
        RuntimeWorldCreationPersistenceResult result,
        ITerraRuntimeWorldGeneratorSource generators,
        long maxTileCount)
    {
        Console.Error.WriteLine($"World creation failed: {result.Status}.");

        switch (result.Status)
        {
            case RuntimeWorldCreationPersistenceStatus.GeneratorNotFound:
                Console.Error.WriteLine(
                    $"Generator '{request.Generation.GeneratorId.Value}' is not registered. Available generators:");
                foreach (var id in StartupWorldGeneratorCatalog.Capture(generators))
                    Console.Error.WriteLine($"  {id.Value}");
                break;

            case RuntimeWorldCreationPersistenceStatus.GenerationBudgetExceeded:
                Console.Error.WriteLine(
                    $"Requested tile count exceeds the server creation budget of {maxTileCount:N0} tiles.");
                break;

            case RuntimeWorldCreationPersistenceStatus.AlreadyExists:
                Console.Error.WriteLine(
                    $"Destination already exists and will not be overwritten: '{request.OutputPath}'.");
                break;

            case RuntimeWorldCreationPersistenceStatus.CompositionFailed when result.Composition is { } composition:
                Console.Error.WriteLine(
                    $"Fresh .wld composition failed: result={composition.Result}, code={composition.StageResultCode}, " +
                    $"validation={composition.Validation.Result}/{composition.Validation.Stage}/" +
                    $"{composition.Validation.StageResultCode}.");
                break;

            case RuntimeWorldCreationPersistenceStatus.GenerationFailed when result.Creation is { } creation:
                Console.Error.WriteLine(
                    $"Generation execution failed: {creation.Generation.Execution?.Status}.");
                break;

            case RuntimeWorldCreationPersistenceStatus.FinalizationFailed when result.Creation is { } creation:
                Console.Error.WriteLine(
                    $"Generation finalization failed: {creation.Finalization?.Status}.");
                break;

            case RuntimeWorldCreationPersistenceStatus.PublishFailed when result.Publication is { } publication:
                Console.Error.WriteLine($"Atomic world publication failed: {publication.Result}.");
                break;
        }
    }

    private static void PrintWorldGenerators(ITerraRuntimeWorldGeneratorSource source)
    {
        var ids = StartupWorldGeneratorCatalog.Capture(source);
        if (ids.Length == 0)
        {
            Console.WriteLine("No world generators are registered.");
            return;
        }

        Console.WriteLine("Registered world generators:");
        foreach (var id in ids)
            Console.WriteLine($"  {id.Value}");
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
            if (string.Equals(arg, "--world", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsTerminalUiRequested(IEnumerable<string> args) =>
        !args.Any(static arg => string.Equals(arg, "--no-tui", StringComparison.OrdinalIgnoreCase));

    private static string GetWorldDisplayName(string worldPath)
    {
        string displayName = Path.GetFileNameWithoutExtension(worldPath);
        return string.IsNullOrWhiteSpace(displayName) ? worldPath : displayName;
    }

    private static void PrintUsage()
    {
        Console.WriteLine($"{RuntimeProductInfo.DisplayName} · .NET 11 server runtime.");
        Console.WriteLine();
        Console.WriteLine("Interactive startup:");
        Console.WriteLine("  TerraRuntime.Server");
        Console.WriteLine("    Scans the runtime Worlds folder and lets you load or create a .wld world.");
        Console.WriteLine();
        Console.WriteLine("Server startup:");
        Console.WriteLine("  TerraRuntime.Server --world <path.wld> [--bind 0.0.0.0] [--port 7777] [--max-players 8] [--max-world-runtimes 8] [--sandbox-materialization-concurrency 1] [--interest-management] [--no-tui]");
        Console.WriteLine();
        Console.WriteLine("World generators:");
        Console.WriteLine("  TerraRuntime.Server --list-world-generators");
        Console.WriteLine("    Lists built-in and trusted-host registered generators.");
        Console.WriteLine("  TerraRuntime.Server --create-world <name> --world-generator <id> --world-seed <uint64> --world-width <tiles> --world-height <tiles> [--world-game-mode <classic|expert|master|journey>] [--world-evil <corruption|crimson>] [--world-output <path.wld>] [server options]");
        Console.WriteLine("    Creates a validated Terraria 1.4.5.8 .wld without overwriting an existing world, then starts it.");
        Console.WriteLine("    Game mode defaults to Classic; world evil defaults to Corruption.");
        Console.WriteLine();
        Console.WriteLine("Level 1 sandbox worlds:");
        Console.WriteLine("  Manage sandbox worlds from the terminal UI; operator actions use typed runtime operations, not text commands.");
        Console.WriteLine("  Generated and .wld sources are materialized by a bounded background worker queue.");
        Console.WriteLine();
        Console.WriteLine("Terminal UI is enabled by default. Use --no-tui to disable it.");
        Console.WriteLine("Smoke modes: --loop-smoke, --protocol-smoke, --network-smoke, --world-smoke, --tui-smoke.");
        Console.WriteLine("Checkpoint save: --save-wld <path.wld>.");
    }
}
