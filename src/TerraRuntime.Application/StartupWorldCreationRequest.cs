using System.Globalization;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Application;

internal readonly record struct StartupWorldCreationRequest(
    WorldGenerationRequest Generation,
    string OutputPath);

/// <summary>
/// Parses the non-interactive world-creation surface without coupling startup argument handling to generator
/// implementations or .wld persistence. Every value required for deterministic generation is explicit and the
/// destination is constrained to the runtime Worlds directory unless an explicit --world-output path is supplied.
/// </summary>
internal static class StartupWorldCreationRequestParser
{
    private static readonly string[] CreationOptions =
    [
        "--create-world",
        "--world-generator",
        "--world-seed",
        "--world-width",
        "--world-height",
        "--world-game-mode",
        "--world-evil",
        "--world-output"
    ];

    public static bool HasCreateWorldArgument(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Contains("--create-world", StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes the bootstrap-only creation options after a request has been parsed successfully, leaving unrelated
    /// server options untouched. Creation options are never forwarded into <see cref="ServerHostOptions"/>.
    /// </summary>
    public static string[] RemoveCreationArguments(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var retained = new List<string>(args.Count);

        for (int index = 0; index < args.Count; index++)
        {
            if (!IsCreationOption(args[index]))
            {
                retained.Add(args[index]);
                continue;
            }

            if (index + 1 < args.Count)
                index++;
        }

        return retained.ToArray();
    }

    public static bool TryParse(
        IReadOnlyList<string> args,
        string worldsDirectory,
        out StartupWorldCreationRequest request,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldsDirectory);

        request = default;
        error = null;

        if (!TryReadRequiredValue(args, "--create-world", out string? worldName, out error) ||
            !TryReadRequiredValue(args, "--world-generator", out string? generatorValue, out error) ||
            !TryReadRequiredValue(args, "--world-seed", out string? seedValue, out error) ||
            !TryReadRequiredValue(args, "--world-width", out string? widthValue, out error) ||
            !TryReadRequiredValue(args, "--world-height", out string? heightValue, out error))
        {
            return false;
        }

        WorldGeneratorId generatorId;
        try
        {
            generatorId = new WorldGeneratorId(generatorValue!);
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            error = $"Invalid --world-generator value: {exception.Message}";
            return false;
        }

        if (!ulong.TryParse(seedValue, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
        {
            error = "--world-seed must be an unsigned 64-bit integer.";
            return false;
        }

        if (!int.TryParse(widthValue, NumberStyles.None, CultureInfo.InvariantCulture, out int width) || width < 1)
        {
            error = "--world-width must be a positive integer.";
            return false;
        }

        if (!int.TryParse(heightValue, NumberStyles.None, CultureInfo.InvariantCulture, out int height) || height < 1)
        {
            error = "--world-height must be a positive integer.";
            return false;
        }

        if (!TryReadGameMode(args, out WorldGenerationGameMode gameMode, out error) ||
            !TryReadWorldEvil(args, out WorldGenerationEvil evil, out error))
        {
            return false;
        }

        var generation = new WorldGenerationRequest(generatorId, worldName!, seed, width, height)
        {
            SeedText = seedValue,
            Options = new WorldGenerationOptions(gameMode, evil)
        };
        try
        {
            generation.Validate();
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or OverflowException)
        {
            error = $"Invalid world-generation request: {exception.Message}";
            return false;
        }

        string outputPath;
        if (TryReadOptionalValue(args, "--world-output", out string? explicitOutput, out error))
        {
            if (error is not null)
                return false;

            if (!TryNormalizeOutputPath(explicitOutput!, out outputPath, out error))
                return false;
        }
        else
        {
            if (error is not null)
                return false;

            if (!TryBuildDefaultOutputPath(worldsDirectory, worldName!, out outputPath, out error))
                return false;
        }

        request = new StartupWorldCreationRequest(generation, outputPath);
        return true;
    }

    private static bool TryReadGameMode(
        IReadOnlyList<string> args,
        out WorldGenerationGameMode gameMode,
        out string? error)
    {
        if (!TryReadOptionalValue(args, "--world-game-mode", out string? value, out error))
        {
            if (error is not null)
            {
                gameMode = default;
                return false;
            }

            gameMode = WorldGenerationGameMode.Classic;
            return true;
        }

        if (Enum.TryParse(value, ignoreCase: true, out gameMode) && Enum.IsDefined(gameMode))
            return true;

        error = "--world-game-mode must be classic, expert, master, or journey.";
        gameMode = default;
        return false;
    }

    private static bool TryReadWorldEvil(
        IReadOnlyList<string> args,
        out WorldGenerationEvil evil,
        out string? error)
    {
        if (!TryReadOptionalValue(args, "--world-evil", out string? value, out error))
        {
            if (error is not null)
            {
                evil = default;
                return false;
            }

            evil = WorldGenerationEvil.Corruption;
            return true;
        }

        if (Enum.TryParse(value, ignoreCase: true, out evil) && Enum.IsDefined(evil))
            return true;

        error = "--world-evil must be corruption or crimson.";
        evil = default;
        return false;
    }

    private static bool IsCreationOption(string value)
    {
        foreach (string option in CreationOptions)
        {
            if (string.Equals(value, option, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool TryReadRequiredValue(
        IReadOnlyList<string> args,
        string option,
        out string? value,
        out string? error)
    {
        if (!TryReadOption(args, option, required: true, out value, out error))
            return false;

        return true;
    }

    private static bool TryReadOptionalValue(
        IReadOnlyList<string> args,
        string option,
        out string? value,
        out string? error) =>
        TryReadOption(args, option, required: false, out value, out error) && value is not null;

    private static bool TryReadOption(
        IReadOnlyList<string> args,
        string option,
        bool required,
        out string? value,
        out string? error)
    {
        value = null;
        error = null;
        int foundIndex = -1;

        for (int i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase))
                continue;

            if (foundIndex >= 0)
            {
                error = $"Option {option} may be specified only once.";
                return false;
            }

            foundIndex = i;
        }

        if (foundIndex < 0)
        {
            if (required)
                error = $"Missing required option {option}.";
            return !required;
        }

        if (foundIndex + 1 >= args.Count ||
            string.IsNullOrWhiteSpace(args[foundIndex + 1]) ||
            args[foundIndex + 1].StartsWith("--", StringComparison.Ordinal))
        {
            error = $"Option {option} requires a value.";
            return false;
        }

        value = args[foundIndex + 1].Trim();
        return true;
    }

    private static bool TryBuildDefaultOutputPath(
        string worldsDirectory,
        string worldName,
        out string outputPath,
        out string? error)
    {
        error = null;
        outputPath = string.Empty;

        if (worldName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            worldName.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
            worldName.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
            string.Equals(worldName, ".", StringComparison.Ordinal) ||
            string.Equals(worldName, "..", StringComparison.Ordinal))
        {
            error = "--create-world must be a valid file name when --world-output is not specified.";
            return false;
        }

        try
        {
            outputPath = Path.GetFullPath(Path.Combine(worldsDirectory, worldName + ".wld"));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Could not build world output path: {exception.Message}";
            return false;
        }
    }

    private static bool TryNormalizeOutputPath(string value, out string outputPath, out string? error)
    {
        outputPath = string.Empty;
        error = null;

        try
        {
            outputPath = Path.GetFullPath(value.Trim().Trim('"'));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Invalid --world-output path: {exception.Message}";
            return false;
        }

        if (!string.Equals(Path.GetExtension(outputPath), ".wld", StringComparison.OrdinalIgnoreCase))
        {
            error = "--world-output must end in .wld.";
            outputPath = string.Empty;
            return false;
        }

        return true;
    }
}