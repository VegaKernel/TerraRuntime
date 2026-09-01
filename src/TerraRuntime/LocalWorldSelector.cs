namespace TerraRuntime;

internal enum LocalWorldSelectionKind : byte
{
    ExistingWorld = 0,
    CreateWorld = 1
}

internal readonly record struct LocalWorldSelection(
    LocalWorldSelectionKind Kind,
    string? WorldPath = null);

internal static class LocalWorldSelector
{
    public static bool TrySelect(string worldsDirectory, out string? worldPath)
    {
        if (!TrySelectCore(worldsDirectory, allowCreation: false, out LocalWorldSelection selection) ||
            selection.Kind != LocalWorldSelectionKind.ExistingWorld ||
            string.IsNullOrWhiteSpace(selection.WorldPath))
        {
            worldPath = null;
            return false;
        }

        worldPath = selection.WorldPath;
        return true;
    }

    public static bool TrySelectOrCreate(string worldsDirectory, out LocalWorldSelection selection) =>
        TrySelectCore(worldsDirectory, allowCreation: true, out selection);

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

    private static bool TrySelectCore(
        string worldsDirectory,
        bool allowCreation,
        out LocalWorldSelection selection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldsDirectory);

        while (true)
        {
            string[] worlds = DiscoverWorlds([worldsDirectory]);

            if (Console.IsInputRedirected)
            {
                if (worlds.Length == 1)
                {
                    string worldPath = worlds[0];
                    Console.WriteLine($"No world was specified; using the only local world '{Path.GetFileNameWithoutExtension(worldPath)}'.");
                    selection = new LocalWorldSelection(LocalWorldSelectionKind.ExistingWorld, worldPath);
                    return true;
                }

                Console.Error.WriteLine(
                    worlds.Length == 0
                        ? $"No world was specified and no local .wld files were found in '{worldsDirectory}'. Use --world <path.wld> or --create-world ... ."
                        : $"No world was specified and multiple local .wld files were found in '{worldsDirectory}', but input is redirected. Use --world <path.wld>.");
                selection = default;
                return false;
            }

            PrintMenu(worldsDirectory, worlds, allowCreation);
            string? input = Console.ReadLine();
            if (input is null)
            {
                selection = default;
                return false;
            }

            input = input.Trim();
            if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input, "quit", StringComparison.OrdinalIgnoreCase))
            {
                selection = default;
                return false;
            }

            if (string.Equals(input, "r", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input, "refresh", StringComparison.OrdinalIgnoreCase))
            {
                StartupConsolePresentation.ClearForTransition();
                continue;
            }

            if (allowCreation &&
                (string.Equals(input, "n", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(input, "new", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(input, "create", StringComparison.OrdinalIgnoreCase)))
            {
                selection = new LocalWorldSelection(LocalWorldSelectionKind.CreateWorld);
                return true;
            }

            if (string.Equals(input, "p", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input, "path", StringComparison.OrdinalIgnoreCase))
            {
                Console.Write("World path: ");
                string? explicitPath = Console.ReadLine();
                if (TryValidateWorldPath(explicitPath, out string? worldPath))
                {
                    selection = new LocalWorldSelection(LocalWorldSelectionKind.ExistingWorld, worldPath);
                    return true;
                }

                ClearAndReportRetry("That path is not an existing .wld file.");
                continue;
            }

            if (int.TryParse(input, out int selectedIndex) && selectedIndex >= 1 && selectedIndex <= worlds.Length)
            {
                selection = new LocalWorldSelection(
                    LocalWorldSelectionKind.ExistingWorld,
                    worlds[selectedIndex - 1]);
                return true;
            }

            ClearAndReportRetry(
                allowCreation
                    ? "Select a world number, N to create, P for an explicit path, R to refresh, or Q to quit."
                    : "Select a world number, P for an explicit path, R to refresh, or Q to quit.");
        }
    }

    private static void ClearAndReportRetry(string message)
    {
        StartupConsolePresentation.ClearForTransition();
        Console.Error.WriteLine(message);
    }

    private static void PrintMenu(
        string worldsDirectory,
        IReadOnlyList<string> worlds,
        bool allowCreation)
    {
        Console.WriteLine();
        Console.WriteLine(RuntimeProductInfo.BuildTitle("local world selection"));
        Console.WriteLine($"Worlds directory: {worldsDirectory}");
        Console.WriteLine();

        if (worlds.Count == 0)
        {
            Console.WriteLine("No local .wld worlds found.");
        }
        else
        {
            for (int i = 0; i < worlds.Count; i++)
                Console.WriteLine($"  {i + 1}. {Path.GetFileNameWithoutExtension(worlds[i])}");
        }

        if (allowCreation)
            Console.WriteLine("  N. Create a new world");
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
