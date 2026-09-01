using System.Globalization;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Short-lived executable proof for CI: generate one built-in world at the requested dimensions and seed, validate its
/// complete canonical image through TerraRuntime, publish it atomically, then exit without opening a game listener.
/// </summary>
internal static class WorldGenerationCreateSmoke
{
    private const string Option = "--worldgen-create-smoke";

    public static bool TryRun(IReadOnlyList<string> args, out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(args);
        int index = -1;
        for (int i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], Option, StringComparison.Ordinal))
                continue;

            if (index >= 0)
            {
                Console.Error.WriteLine($"{Option} may be specified only once.");
                exitCode = 31;
                return true;
            }

            index = i;
        }

        if (index < 0)
        {
            exitCode = default;
            return false;
        }

        if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            PrintUsage();
            exitCode = 31;
            return true;
        }

        string outputPath;
        try
        {
            outputPath = Path.GetFullPath(args[index + 1].Trim().Trim('"'));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Console.Error.WriteLine($"Invalid worldgen smoke output path: {exception.Message}");
            exitCode = 31;
            return true;
        }

        if (!string.Equals(Path.GetExtension(outputPath), ".wld", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Worldgen smoke output must end in .wld.");
            exitCode = 31;
            return true;
        }

        WorldGeneratorId generatorId = FlatWorldGenerationProvider.GeneratorId;
        if (index + 2 < args.Count && !string.IsNullOrWhiteSpace(args[index + 2]))
        {
            try
            {
                generatorId = new WorldGeneratorId(args[index + 2].Trim());
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine($"Invalid worldgen smoke generator id: {exception.Message}");
                exitCode = 31;
                return true;
            }
        }

        int widthTiles = 4200;
        int heightTiles = 1200;
        bool widthSpecified = index + 3 < args.Count && !string.IsNullOrWhiteSpace(args[index + 3]);
        bool heightSpecified = index + 4 < args.Count && !string.IsNullOrWhiteSpace(args[index + 4]);
        if (widthSpecified != heightSpecified)
        {
            Console.Error.WriteLine("Worldgen smoke dimensions must specify both width and height.");
            exitCode = 31;
            return true;
        }

        if (widthSpecified)
        {
            if (!int.TryParse(args[index + 3], NumberStyles.None, CultureInfo.InvariantCulture, out widthTiles) || widthTiles <= 0 ||
                !int.TryParse(args[index + 4], NumberStyles.None, CultureInfo.InvariantCulture, out heightTiles) || heightTiles <= 0)
            {
                Console.Error.WriteLine("Worldgen smoke width and height must be positive integers.");
                exitCode = 31;
                return true;
            }
        }

        ulong seed = 1458UL;
        bool seedSpecified = index + 5 < args.Count && !string.IsNullOrWhiteSpace(args[index + 5]);
        if (seedSpecified && !widthSpecified)
        {
            Console.Error.WriteLine("Worldgen smoke seed requires explicit width and height.");
            PrintUsage();
            exitCode = 31;
            return true;
        }

        if (seedSpecified &&
            !ulong.TryParse(args[index + 5], NumberStyles.None, CultureInfo.InvariantCulture, out seed))
        {
            Console.Error.WriteLine("Worldgen smoke seed must be an unsigned 64-bit integer.");
            exitCode = 31;
            return true;
        }

        if (index + 6 < args.Count)
        {
            Console.Error.WriteLine("Worldgen smoke received unexpected trailing arguments.");
            PrintUsage();
            exitCode = 31;
            return true;
        }

        var request = new WorldGenerationRequest(
            generatorId,
            generatorId == VanillaWorldGenerationProvider1458.GeneratorId
                ? "TerraRuntimeVanillaSmoke"
                : "TerraRuntimeGeneratedSmoke",
            Seed: seed,
            WidthTiles: widthTiles,
            HeightTiles: heightTiles)
        {
            SeedText = generatorId == VanillaWorldGenerationProvider1458.GeneratorId
                ? seed.ToString(CultureInfo.InvariantCulture)
                : null,
            Options = WorldGenerationOptions.Default
        };
        var generators = new StartupWorldGeneratorSource(host: null);
        var pipeline = new RuntimeWorldCreationPersistencePipeline(
            generators,
            TerrariaServerHost.CreateServerWorldLoadLimits().MaxTileCount);
        long timestamp = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc).ToBinary();

        RuntimeWorldCreationPersistenceResult result;
        try
        {
            result = pipeline.TryCreateAndPersist(
                request,
                outputPath,
                Guid.Parse("14580000-0000-4000-8000-000000000001"),
                worldId: 145800001,
                creationTimeBinary: timestamp,
                lastPlayedBinary: timestamp);
        }
        catch (Exception exception) when (StartupWorldCreationFailurePolicy.IsRecoverable(exception))
        {
            Console.Error.WriteLine(
                $"Worldgen create smoke threw unexpectedly: {exception.GetType().Name}: {exception.Message}");
            Console.Error.WriteLine(exception);
            exitCode = 33;
            return true;
        }

        if (!result.Succeeded)
        {
            RuntimeWorldGenerationFinalizationResult? finalization = result.Creation?.Finalization;
            WorldGenerationExecutionResult? execution = result.Creation?.Generation.Execution;
            string passId = execution is { } e && e.PassId.IsAssigned ? e.PassId.Value : string.Empty;
            string dependencyId = execution is { } dependencyExecution && dependencyExecution.DependencyId.IsAssigned
                ? dependencyExecution.DependencyId.Value
                : string.Empty;
            Console.Error.WriteLine(
                $"Worldgen create smoke failed: status={result.Status}, " +
                $"generation={result.Creation?.Generation.Status}, " +
                $"execution={execution?.Status}, pass={passId}, dependency={dependencyId}, " +
                $"finalization={finalization?.Status}, " +
                $"validation={finalization?.Validation?.Status}, " +
                $"validationDetail={finalization?.Validation?.Detail}, " +
                $"composition={result.Composition?.Result}, publication={result.Publication?.Result}.");
            if (execution?.Error is { } executionError)
                Console.Error.WriteLine(executionError);
            exitCode = 32;
            return true;
        }

        Console.WriteLine(
            $"Worldgen create smoke passed: path='{result.WorldPath}', generator={request.GeneratorId.Value}, " +
            $"seed={request.Seed}, size={request.WidthTiles}x{request.HeightTiles}, " +
            $"mode={request.Options.GameMode}, evil={request.Options.Evil}.");
        exitCode = 0;
        return true;
    }

    private static void PrintUsage() =>
        Console.Error.WriteLine(
            $"Usage: TerraRuntime.Server {Option} <path.wld> [generator-id [width height [seed]]]");
}
