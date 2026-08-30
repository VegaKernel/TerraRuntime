using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Creates a real Terraria 1.4.5.8 vanilla world by delegating generation to the pinned official dedicated server,
/// then validates the complete .wld with TerraRuntime before it is accepted. This is the user-facing
/// `terraruntime:vanilla` path while clean-room 109-pass parity remains unfinished.
/// </summary>
internal static class OfficialVanillaWorldGenerator1458
{
    internal const string Version = "1.4.5.8";
    internal const string GeneratorIdValue = "terraruntime:vanilla";
    internal const string DownloadUrl = "https://terraria.org/api/download/pc-dedicated-server/terraria-server-1458.zip";
    internal const string ExpectedManagedServerSha256 = "d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e";
    internal const string ServerPathEnvironmentVariable = "TERRARUNTIME_TERRARIA_SERVER_1458";

    private static readonly HttpClient Http = new();

    public static bool IsVanilla(WorldGeneratorId id) =>
        string.Equals(id.Value, GeneratorIdValue, StringComparison.Ordinal);

    public static bool TryCreate(
        in StartupWorldCreationRequest request,
        RuntimeDirectoryLayout directories,
        out string? worldPath,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(directories);
        worldPath = null;
        error = null;

        if (!IsVanilla(request.Generation.GeneratorId))
        {
            error = $"Generator '{request.Generation.GeneratorId.Value}' is not the official vanilla backend.";
            return false;
        }

        if (File.Exists(request.OutputPath))
        {
            error = $"Destination already exists and will not be overwritten: '{request.OutputPath}'.";
            return false;
        }

        if (!TryMapWorldSize(request.Generation.WidthTiles, request.Generation.HeightTiles, out int autoCreate))
        {
            error = "Exact vanilla generation supports Terraria's canonical sizes only: 4200x1200, 6400x1800, or 8400x2400.";
            return false;
        }

        try
        {
            ValidateConfigValue(request.OutputPath, nameof(request.OutputPath));
            ValidateConfigValue(request.Generation.WorldName, nameof(request.Generation.WorldName));
            ValidateConfigValue(request.Generation.SeedText ?? request.Generation.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture), "seed");

            string serverPath = ResolveOrInstallServer(directories);
            string outputDirectory = Path.GetDirectoryName(request.OutputPath) ?? directories.WorldsDirectory;
            Directory.CreateDirectory(outputDirectory);

            string workDirectory = Path.Combine(directories.DataDirectory, "official-terraria", Version, "work");
            Directory.CreateDirectory(workDirectory);
            string token = Guid.NewGuid().ToString("N");
            string configPath = Path.Combine(workDirectory, $"worldgen-{token}.txt");
            string logPath = Path.Combine(workDirectory, $"worldgen-{token}.log");
            int port = ReserveEphemeralPort();

            File.WriteAllText(configPath, BuildServerConfig(in request, autoCreate, port));
            try
            {
                var output = new ConcurrentQueue<string>();
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = serverPath,
                        WorkingDirectory = Path.GetDirectoryName(serverPath)!,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.StartInfo.ArgumentList.Add("-config");
                process.StartInfo.ArgumentList.Add(configPath);
                process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };

                if (!process.Start())
                {
                    error = "Could not start the official TerrariaServer 1.4.5.8 world generator.";
                    return false;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                bool valid = WaitForValidWorld(process, request.OutputPath, TimeSpan.FromMinutes(15));
                TryTerminate(process);
                File.WriteAllLines(logPath, output);

                if (!valid || !TryValidateWorld(request.OutputPath))
                {
                    error = $"Official TerrariaServer 1.4.5.8 did not produce a complete valid world. Log: '{logPath}'.";
                    return false;
                }

                worldPath = request.OutputPath;
                return true;
            }
            finally
            {
                TryDelete(configPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException or InvalidDataException or CryptographicException or PlatformNotSupportedException or TaskCanceledException)
        {
            error = $"Official TerrariaServer 1.4.5.8 generation failed: {exception.Message}";
            return false;
        }
    }

    internal static bool TryMapWorldSize(int width, int height, out int autoCreate)
    {
        autoCreate = (width, height) switch
        {
            (4200, 1200) => 1,
            (6400, 1800) => 2,
            (8400, 2400) => 3,
            _ => 0
        };
        return autoCreate != 0;
    }

    internal static string BuildServerConfig(in StartupWorldCreationRequest request, int autoCreate, int port)
    {
        int difficulty = request.Generation.Options.GameMode switch
        {
            WorldGenerationGameMode.Classic => 0,
            WorldGenerationGameMode.Expert => 1,
            WorldGenerationGameMode.Master => 2,
            WorldGenerationGameMode.Journey => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };

        return string.Join('\n',
            $"world={request.OutputPath}",
            $"autocreate={autoCreate}",
            $"seed={BuildServerSeed(in request, autoCreate)}",
            $"worldname={request.Generation.WorldName}",
            $"difficulty={difficulty}",
            "maxplayers=1",
            $"port={port}",
            "secure=0",
            "upnp=0",
            "npcstream=0",
            "priority=0",
            string.Empty);
    }

    internal static string BuildServerSeed(in StartupWorldCreationRequest request, int autoCreate)
    {
        string seed = request.Generation.SeedText ?? request.Generation.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (HasExplicitWorldSeedPrefix(seed))
            return seed;

        int difficulty = (int)request.Generation.Options.GameMode + 1;
        int evil = (int)request.Generation.Options.Evil + 1;
        return $"{autoCreate}.{difficulty}.{evil}.{seed}";
    }

    private static bool HasExplicitWorldSeedPrefix(string seed)
    {
        string[] parts = seed.Split('.', 4, StringSplitOptions.None);
        return parts.Length == 4 &&
            int.TryParse(parts[0], out int size) && size is >= 1 and <= 3 &&
            int.TryParse(parts[1], out int difficulty) && difficulty is >= 1 and <= 4 &&
            int.TryParse(parts[2], out int evil) && evil is >= 1 and <= 2;
    }

    private static string ResolveOrInstallServer(RuntimeDirectoryLayout directories)
    {
        string? explicitPath = Environment.GetEnvironmentVariable(ServerPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            string fullPath = Path.GetFullPath(explicitPath.Trim().Trim('"'));
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"{ServerPathEnvironmentVariable} points to a missing file.", fullPath);
            return fullPath;
        }

        string packageRoot = Path.Combine(directories.DataDirectory, "official-terraria", Version, "server");
        if (Directory.Exists(packageRoot))
        {
            VerifyManagedServerPackage(packageRoot);
            if (TryFindPlatformServer(packageRoot, out string? existing))
                return existing!;
        }

        string packageDirectory = Path.Combine(directories.DataDirectory, "official-terraria", Version);
        Directory.CreateDirectory(packageDirectory);
        string zipPath = Path.Combine(packageDirectory, "terraria-server-1458.zip");
        string tempZip = zipPath + ".tmp";
        TryDelete(tempZip);

        Console.WriteLine($"Downloading pinned official TerrariaServer {Version} for exact vanilla world generation...");
        using (HttpResponseMessage response = Http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
        {
            response.EnsureSuccessStatusCode();
            using Stream input = response.Content.ReadAsStream();
            using FileStream output = new(tempZip, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
        }

        File.Move(tempZip, zipPath, overwrite: true);
        if (Directory.Exists(packageRoot))
            Directory.Delete(packageRoot, recursive: true);
        Directory.CreateDirectory(packageRoot);
        ZipFile.ExtractToDirectory(zipPath, packageRoot, overwriteFiles: true);
        VerifyManagedServerPackage(packageRoot);

        if (!TryFindPlatformServer(packageRoot, out string? installed))
            throw new FileNotFoundException("The pinned TerrariaServer archive does not contain a supported platform executable.");
        return installed!;
    }

    private static void VerifyManagedServerPackage(string packageRoot)
    {
        string? managed = Directory.EnumerateFiles(packageRoot, "TerrariaServer.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (managed is null)
            throw new InvalidDataException("Pinned TerrariaServer package does not contain TerrariaServer.exe.");

        using FileStream stream = File.OpenRead(managed);
        string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actual, ExpectedManagedServerSha256, StringComparison.Ordinal))
            throw new CryptographicException($"TerrariaServer.exe SHA-256 mismatch: {actual}.");
    }

    private static bool TryFindPlatformServer(string packageRoot, out string? path)
    {
        path = null;
        if (!Directory.Exists(packageRoot))
            return false;

        string fileName;
        if (OperatingSystem.IsWindows())
            fileName = "TerrariaServer.exe";
        else if (OperatingSystem.IsLinux())
            fileName = "TerrariaServer.bin.x86_64";
        else
            throw new PlatformNotSupportedException("Exact vanilla generation currently supports Windows x64 and Linux x64.");

        path = Directory.EnumerateFiles(packageRoot, fileName, SearchOption.AllDirectories).FirstOrDefault();
        if (path is null)
            return false;

        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
        return true;
    }

    private static int ReserveEphemeralPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static bool WaitForValidWorld(Process process, string worldPath, TimeSpan timeout)
    {
        long deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        long lastLength = -1;
        int stableSamples = 0;

        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (File.Exists(worldPath))
            {
                try
                {
                    long length = new FileInfo(worldPath).Length;
                    if (length > 1_048_576 && length == lastLength)
                        stableSamples++;
                    else
                        stableSamples = 0;
                    lastLength = length;

                    if (stableSamples >= 3 && TryValidateWorld(worldPath))
                        return true;
                }
                catch (IOException)
                {
                    stableSamples = 0;
                }
            }

            if (process.HasExited)
                return TryValidateWorld(worldPath);
            Thread.Sleep(500);
        }
        return false;
    }

    private static bool TryValidateWorld(string worldPath)
    {
        if (!File.Exists(worldPath))
            return false;
        try
        {
            byte[] bytes = File.ReadAllBytes(worldPath);
            WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(
                bytes,
                TerrariaServerHost.CreateServerWorldLoadLimits(),
                out WorldFileData? world);
            return diagnostic.IsLoaded && world is not null;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void ValidateConfigValue(string value, string name)
    {
        if (value.IndexOfAny(['\r', '\n']) >= 0)
            throw new InvalidDataException($"{name} cannot contain line breaks.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
