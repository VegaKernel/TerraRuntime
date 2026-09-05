namespace TerraRuntime.Application;

public sealed record ServerHostOptions(
    string WorldPath,
    int Port,
    int MaxPlayers,
    bool InterestManagementEnabled = false,
    bool TerminalUiEnabled = true)
{
    public const string DefaultBindAddress = "0.0.0.0";
    public const int DefaultPort = 7777;
    public const int DefaultMaxPlayers = 8;
    public const int DefaultMaxWorldRuntimes = 8;
    public const int DefaultSandboxMaterializationConcurrency = 1;

    public string BindAddress { get; init; } = DefaultBindAddress;

    public int MaxWorldRuntimes { get; init; } = DefaultMaxWorldRuntimes;

    public int SandboxMaterializationConcurrency { get; init; } = DefaultSandboxMaterializationConcurrency;

    public static bool TryParse(string[] args, out ServerHostOptions? options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? worldPath = null;
        string bindAddress = DefaultBindAddress;
        int port = DefaultPort;
        int maxPlayers = DefaultMaxPlayers;
        bool interestManagementEnabled = false;
        bool terminalUiEnabled = true;
        int maxWorldRuntimes = DefaultMaxWorldRuntimes;
        int sandboxMaterializationConcurrency = DefaultSandboxMaterializationConcurrency;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--world":
                    if (!TryReadValue(args, ref i, out worldPath))
                    {
                        options = null;
                        error = "--world requires a .wld path.";
                        return false;
                    }
                    break;

                case "--bind":
                case "--bind-address":
                    if (!TryReadValue(args, ref i, out string? rawBindAddress) || !TryNormalizeBindAddress(rawBindAddress, out bindAddress))
                    {
                        options = null;
                        error = "--bind/--bind-address requires a numeric IPv4/IPv6 address, '*', 'any', or 'localhost'.";
                        return false;
                    }
                    break;

                case "--port":
                    if (!TryReadInt(args, ref i, 1, ushort.MaxValue, out port))
                    {
                        options = null;
                        error = $"--port requires an integer from 1 to {ushort.MaxValue}.";
                        return false;
                    }
                    break;

                case "--max-players":
                    if (!TryReadInt(args, ref i, 1, byte.MaxValue, out maxPlayers))
                    {
                        options = null;
                        error = $"--max-players requires an integer from 1 to {byte.MaxValue}.";
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
                    terminalUiEnabled = false;
                    break;

                case "--max-world-runtimes":
                    if (!TryReadInt(args, ref i, 2, 64, out maxWorldRuntimes))
                    {
                        options = null;
                        error = "--max-world-runtimes requires an integer from 2 to 64.";
                        return false;
                    }
                    break;

                case "--sandbox-materialization-concurrency":
                    if (!TryReadInt(args, ref i, 1, 4, out sandboxMaterializationConcurrency))
                    {
                        options = null;
                        error = "--sandbox-materialization-concurrency requires an integer from 1 to 4.";
                        return false;
                    }
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
            terminalUiEnabled)
        {
            BindAddress = bindAddress,
            MaxWorldRuntimes = maxWorldRuntimes,
            SandboxMaterializationConcurrency = sandboxMaterializationConcurrency
        };
        error = null;
        return true;
    }

    private static bool TryNormalizeBindAddress(string? value, out string normalized)
    {
        string candidate = value?.Trim() ?? string.Empty;
        if (candidate is "*" or "any" or "ANY")
            candidate = System.Net.IPAddress.Any.ToString();
        else if (candidate.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            candidate = System.Net.IPAddress.Loopback.ToString();

        if (!System.Net.IPAddress.TryParse(candidate, out System.Net.IPAddress? address))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = address.ToString();
        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, out string? value)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
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
