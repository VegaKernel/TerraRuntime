namespace TerraRuntime;

internal static class LocalWorldSelector
{
    public static bool TrySelect(string worldsDirectory, out string? worldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldsDirectory);

        while (true)
        {
            string[] worlds = DiscoverWorlds([worldsDirectory]);

            if (Console.IsInputRedirected)
            {
                if (worlds.Length == 1)
                {
                    worldPath = worlds[0];
                    Console.WriteLine($"No world was specified; using the only local world '{worldPath}'.");
                    return true;
                }

                Console.Error.WriteLine(
                    worlds.Length == 0
                        ? $"No world was specified and no local .wld files were found in '{worldsDirectory}'. Use --world <path.wld>."
                        : $"No world was specified and multiple local .wld files were found in '{worldsDirectory}', but input is redirected. Use --world <path.wld>.");
                worldPath = null;
                return false;
            }

            PrintMenu(worldsDirectory, worlds);
            string? input = Console.ReadLine();
            if (input is null)
            {
                worldPath = null;
                return false;
            }

            input = input.Trim();
            if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input, "quit", StringComparison.OrdinalIgnoreCase))
            {
                worldPath = null;
                return false;
            }

            if (string.Equals(input, "r", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input, "refresh", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(input, "p", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input, "path", StringComparison.OrdinalIgnoreCase))
            {
                Console.Write("World path: ");
                string? explicitPath = Console.ReadLine();
                if (TryValidateWorldPath(explicitPath, out worldPath))
                    return true;

                Console.Error.WriteLine("That path is not an existing .wld file.");
                continue;
            }

            if (int.TryParse(input, out int selection) && selection >= 1 && selection <= worlds.Length)
            {
                worldPath = worlds[selection - 1];
                return true;
            }

            Console.Error.WriteLine("Select a world number, P for an explicit path, R to refresh, or Q to quit.");
        }
    }

    internal static string[] DiscoverWorlds(IEnumerable<string> searchDirectories)
    {
        ArgumentNullException.ThrowIfNull(searchDirectories);
        var worlds = new HashSet<string>(GetPathComparer());

        foreach (string directory in searchDirectories)
        {
            if (!Directory.Exists(directory))
                continue;

            try
            {
                foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    if (!string.Equals(Path.GetExtension(path), ".wld", StringComparison.OrdinalIgnoreCase))
                        continue;

                    worlds.Add(Path.GetFullPath(path));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not scan Worlds directory '{directory}': {exception.Message}");
            }
        }

        return worlds
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static path => path, GetPathComparer())
            .ToArray();
    }

    private static void PrintMenu(string worldsDirectory, IReadOnlyList<string> worlds)
    {
        Console.WriteLine();
        Console.WriteLine("TerraRuntime local world selection");
        Console.WriteLine($"Worlds directory: {worldsDirectory}");
        Console.WriteLine();

        if (worlds.Count == 0)
        {
            Console.WriteLine("No local .wld worlds found.");
        }
        else
        {
            for (int i = 0; i < worlds.Count; i++)
                Console.WriteLine($"  {i + 1}. {Path.GetFileNameWithoutExtension(worlds[i])}  [{worlds[i]}]");
        }

        Console.WriteLine("  P. Enter a world path");
        Console.WriteLine("  R. Refresh Worlds folder");
        Console.WriteLine("  Q. Quit");
        Console.Write("Select world: ");
    }

    private static bool TryValidateWorldPath(string? value, out string? worldPath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            worldPath = null;
            return false;
        }

        string candidate = value.Trim().Trim('"');
        try
        {
            candidate = Path.GetFullPath(candidate);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            worldPath = null;
            return false;
        }

        if (!string.Equals(Path.GetExtension(candidate), ".wld", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(candidate))
        {
            worldPath = null;
            return false;
        }

        worldPath = candidate;
        return true;
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
