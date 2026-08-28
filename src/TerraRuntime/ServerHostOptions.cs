namespace TerraRuntime;

public sealed record ServerHostOptions(
    string WorldPath,
    int Port,
    int MaxPlayers,
    bool InterestManagementEnabled = false,
    bool TerminalUiEnabled = true)
{
    public const int DefaultPort = 7777;
    public const int DefaultMaxPlayers = 8;

    public static bool TryParse(string[] args, out ServerHostOptions? options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? worldPath = null;
        int port = DefaultPort;
        int maxPlayers = DefaultMaxPlayers;
        bool interestManagementEnabled = false;
        bool terminalUiEnabled = true;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--world":
                case "-world":
                    if (!TryReadValue(args, ref i, out worldPath))
                    {
                        options = null;
                        error = $"{arg} requires a .wld path.";
                        return false;
                    }
                    break;

                case "--port":
                case "-port":
                    if (!TryReadInt(args, ref i, 1, ushort.MaxValue, out port))
                    {
                        options = null;
                        error = $"{arg} requires an integer from 1 to {ushort.MaxValue}.";
                        return false;
                    }
                    break;

                case "--max-players":
                case "-maxplayers":
                    if (!TryReadInt(args, ref i, 1, byte.MaxValue, out maxPlayers))
                    {
                        options = null;
                        error = $"{arg} requires an integer from 1 to {byte.MaxValue}.";
                        return false;
                    }
                    break;

                case "--interest-management":
                    interestManagementEnabled = true;
                    break;

                case "--tui":
                    terminalUiEnabled = true;
                    break;

                case "--no-tui":
                case "--plain":
                    terminalUiEnabled = false;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(worldPath))
        {
            options = null;
            error = "A world path is required to start the server host.";
            return false;
        }

        options = new ServerHostOptions(
            Path.GetFullPath(worldPath),
            port,
            maxPlayers,
            interestManagementEnabled,
            terminalUiEnabled);
        error = null;
        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, out string? value)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith('-', StringComparison.Ordinal))
        {
            value = null;
            return false;
        }

        value = args[++index];
        return true;
    }

    private static bool TryReadInt(
        string[] args,
        ref int index,
        int minimum,
        int maximum,
        out int value)
    {
        if (!TryReadValue(args, ref index, out string? raw) ||
            !int.TryParse(raw, out value) ||
            value < minimum ||
            value > maximum)
        {
            value = default;
            return false;
        }

        return true;
    }
}
