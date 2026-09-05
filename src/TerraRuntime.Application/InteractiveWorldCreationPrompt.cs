using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.HostContracts.WorldGeneration;

namespace TerraRuntime.Application;

internal static class InteractiveWorldCreationPrompt
{
    public static bool TryPrompt(
        string worldsDirectory,
        ITerraRuntimeWorldGeneratorSource generators,
        out StartupWorldCreationRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldsDirectory);
        ArgumentNullException.ThrowIfNull(generators);
        request = default;

        WorldGeneratorId[] ids = StartupWorldGeneratorCatalog.Capture(generators);
        if (ids.Length == 0)
        {
            Console.Error.WriteLine("No world generators are registered.");
            return false;
        }

        if (!TrySelectGenerator(ids, out WorldGeneratorId generatorId))
            return false;
        if (!TryReadRequired("World name", out string? worldName))
            return false;
        if (!TryReadSeed(out ulong seed))
            return false;
        if (!TryReadDimensions(out int width, out int height))
            return false;
        if (!TryReadGameMode(out WorldGenerationGameMode gameMode))
            return false;
        if (!TryReadWorldEvil(out WorldGenerationEvil evil))
            return false;

        string[] syntheticArgs =
        [
            "--create-world", worldName!,
            "--world-generator", generatorId.Value,
            "--world-seed", seed.ToString(CultureInfo.InvariantCulture),
            "--world-width", width.ToString(CultureInfo.InvariantCulture),
            "--world-height", height.ToString(CultureInfo.InvariantCulture),
            "--world-game-mode", gameMode.ToString(),
            "--world-evil", evil.ToString()
        ];

        if (!StartupWorldCreationRequestParser.TryParse(
                syntheticArgs,
                worldsDirectory,
                out request,
                out string? error))
        {
            Console.Error.WriteLine(error ?? "Invalid world creation input.");
            request = default;
            return false;
        }

        Console.WriteLine(
            $"Generation request: name='{request.Generation.WorldName}', generator='{request.Generation.GeneratorId.Value}', " +
            $"seed={request.Generation.Seed}, size={request.Generation.WidthTiles}x{request.Generation.HeightTiles}, " +
            $"mode={request.Generation.Options.GameMode}, evil={request.Generation.Options.Evil}.");
        return true;
    }

    private static bool TrySelectGenerator(
        IReadOnlyList<WorldGeneratorId> ids,
        out WorldGeneratorId generatorId)
    {
        Console.WriteLine();
        Console.WriteLine("Available world generators:");
        for (int index = 0; index < ids.Count; index++)
            Console.WriteLine($"  {index + 1}. {ids[index].Value}");

        while (true)
        {
            Console.Write("Select generator (or Q to cancel): ");
            string? input = Console.ReadLine()?.Trim();
            if (IsCancel(input))
            {
                generatorId = default;
                return false;
            }

            if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out int selection) &&
                selection >= 1 && selection <= ids.Count)
            {
                generatorId = ids[selection - 1];
                return true;
            }

            Console.Error.WriteLine("Select one of the listed generator numbers.");
        }
    }

    private static bool TryReadRequired(string label, out string? value)
    {
        while (true)
        {
            Console.Write($"{label} (or Q to cancel): ");
            string? input = Console.ReadLine();
            if (input is null || IsCancel(input.Trim()))
            {
                value = null;
                return false;
            }

            value = input.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return true;

            Console.Error.WriteLine($"{label} cannot be empty.");
        }
    }

    private static bool TryReadSeed(out ulong seed)
    {
        Span<byte> randomBytes = stackalloc byte[sizeof(ulong)];
        while (true)
        {
            Console.Write("Seed uint64 (Enter for a generated seed, Q to cancel): ");
            string? input = Console.ReadLine();
            if (input is null)
            {
                seed = default;
                return false;
            }

            input = input.Trim();
            if (IsCancel(input))
            {
                seed = default;
                return false;
            }

            if (input.Length == 0)
            {
                RandomNumberGenerator.Fill(randomBytes);
                seed = BinaryPrimitives.ReadUInt64LittleEndian(randomBytes);
                Console.WriteLine($"Generated seed: {seed}");
                return true;
            }

            if (ulong.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                return true;

            Console.Error.WriteLine("Seed must be an unsigned 64-bit integer.");
        }
    }

    private static bool TryReadDimensions(out int width, out int height)
    {
        Console.WriteLine();
        Console.WriteLine("World size:");
        Console.WriteLine("  1. Small   4200x1200");
        Console.WriteLine("  2. Medium  6400x1800");
        Console.WriteLine("  3. Large   8400x2400");
        Console.WriteLine("  4. Custom");

        while (true)
        {
            Console.Write("Select size (or Q to cancel): ");
            string? input = Console.ReadLine()?.Trim();
            if (IsCancel(input))
            {
                width = default;
                height = default;
                return false;
            }

            switch (input)
            {
                case "1":
                    width = 4200;
                    height = 1200;
                    return true;
                case "2":
                    width = 6400;
                    height = 1800;
                    return true;
                case "3":
                    width = 8400;
                    height = 2400;
                    return true;
                case "4":
                    return TryReadCustomDimensions(out width, out height);
                default:
                    Console.Error.WriteLine("Select 1, 2, 3 or 4.");
                    break;
            }
        }
    }

    private static bool TryReadGameMode(out WorldGenerationGameMode gameMode)
    {
        Console.WriteLine();
        Console.WriteLine("Game mode:");
        Console.WriteLine("  1. Classic");
        Console.WriteLine("  2. Expert");
        Console.WriteLine("  3. Master");
        Console.WriteLine("  4. Journey");

        while (true)
        {
            Console.Write("Select game mode (Enter for Classic, Q to cancel): ");
            string? input = Console.ReadLine();
            if (input is null || IsCancel(input.Trim()))
            {
                gameMode = default;
                return false;
            }

            switch (input.Trim())
            {
                case "":
                case "1":
                    gameMode = WorldGenerationGameMode.Classic;
                    return true;
                case "2":
                    gameMode = WorldGenerationGameMode.Expert;
                    return true;
                case "3":
                    gameMode = WorldGenerationGameMode.Master;
                    return true;
                case "4":
                    gameMode = WorldGenerationGameMode.Journey;
                    return true;
                default:
                    Console.Error.WriteLine("Select 1, 2, 3 or 4.");
                    break;
            }
        }
    }

    private static bool TryReadWorldEvil(out WorldGenerationEvil evil)
    {
        Console.WriteLine();
        Console.WriteLine("World evil:");
        Console.WriteLine("  1. Corruption");
        Console.WriteLine("  2. Crimson");

        while (true)
        {
            Console.Write("Select world evil (Enter for Corruption, Q to cancel): ");
            string? input = Console.ReadLine();
            if (input is null || IsCancel(input.Trim()))
            {
                evil = default;
                return false;
            }

            switch (input.Trim())
            {
                case "":
                case "1":
                    evil = WorldGenerationEvil.Corruption;
                    return true;
                case "2":
                    evil = WorldGenerationEvil.Crimson;
                    return true;
                default:
                    Console.Error.WriteLine("Select 1 or 2.");
                    break;
            }
        }
    }

    private static bool TryReadCustomDimensions(out int width, out int height)
    {
        if (!TryReadPositiveInt("Width in tiles", out width))
        {
            height = default;
            return false;
        }

        return TryReadPositiveInt("Height in tiles", out height);
    }

    private static bool TryReadPositiveInt(string label, out int value)
    {
        while (true)
        {
            Console.Write($"{label} (or Q to cancel): ");
            string? input = Console.ReadLine()?.Trim();
            if (IsCancel(input))
            {
                value = default;
                return false;
            }

            if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0)
                return true;

            Console.Error.WriteLine($"{label} must be a positive integer.");
        }
    }

    private static bool IsCancel(string? value) =>
        string.Equals(value, "q", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "quit", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "cancel", StringComparison.OrdinalIgnoreCase);
}
