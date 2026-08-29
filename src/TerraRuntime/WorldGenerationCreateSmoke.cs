using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime;

/// <summary>
/// Short-lived executable proof for CI: generate one standard-small built-in world, validate its complete canonical
/// image through TerraRuntime, publish it atomically, then exit without opening a game listener.
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
            Console.Error.WriteLine($"Usage: TerraRuntime.Server {Option} <path.wld>");
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

        var request = new WorldGenerationRequest(
            new WorldGeneratorId("terraruntime:flat"),
            "TerraRuntimeGeneratedSmoke",
            Seed: 1458UL,
            WidthTiles: 4200,
            HeightTiles: 1200);
        var generators = new StartupWorldGeneratorSource(host: null);
        var pipeline = new RuntimeWorldCreationPersistencePipeline(
            generators,
            TerrariaServerHost.CreateServerWorldLoadLimits().MaxTileCount);
        long timestamp = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc).ToBinary();

        RuntimeWorldCreationPersistenceResult result = pipeline.TryCreateAndPersist(
            request,
            outputPath,
            Guid.Parse("14580000-0000-4000-8000-000000000001"),
            worldId: 145800001,
            gameMode: 0,
            crimson: false,
            creationTimeBinary: timestamp,
            lastPlayedBinary: timestamp);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine(
                $"Worldgen create smoke failed: status={result.Status}, " +
                $"composition={result.Composition?.Result}, publication={result.Publication?.Result}.");
            exitCode = 32;
            return true;
        }

        Console.WriteLine(
            $"Worldgen create smoke passed: path='{result.WorldPath}', generator={request.GeneratorId.Value}, " +
            $"seed={request.Seed}, size={request.WidthTiles}x{request.HeightTiles}.");
        exitCode = 0;
        return true;
    }
}
