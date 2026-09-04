using System.Globalization;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime;

public abstract record SandboxOperation
{
    private SandboxOperation() { }

    public sealed record List : SandboxOperation;
    public sealed record Status(SandboxName Name) : SandboxOperation;
    public sealed record Jobs : SandboxOperation;
    public sealed record Job(SandboxJobId Id) : SandboxOperation;
    public sealed record Create(SandboxCreateRequest Request) : SandboxOperation;
    public sealed record Move(string PlayerSelector, SandboxName? Sandbox) : SandboxOperation;
    public sealed record Respawn(string PlayerSelector, SandboxName? Sandbox) : SandboxOperation;
    public sealed record Regenerate(SandboxName Name, ulong? Seed) : SandboxOperation;
    public sealed record Destroy(SandboxName Name) : SandboxOperation;
    public sealed record Kick(string PlayerSelector) : SandboxOperation;
    public sealed record Cancel(SandboxJobId Id) : SandboxOperation;
}

/// <summary>Parses the operator sandbox grammar and resolves file assets below one configured root.</summary>
internal sealed class SandboxCommandParser
{
    private readonly string worldAssetRoot;
    private readonly int defaultWidthTiles;
    private readonly int defaultHeightTiles;

    internal int DefaultWidthTiles => defaultWidthTiles;
    internal int DefaultHeightTiles => defaultHeightTiles;

    internal SandboxCommandParser(string worldAssetRoot, int defaultWidthTiles, int defaultHeightTiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldAssetRoot);
        ArgumentOutOfRangeException.ThrowIfLessThan(defaultWidthTiles, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(defaultHeightTiles, 1);
        this.worldAssetRoot = Path.GetFullPath(worldAssetRoot);
        this.defaultWidthTiles = defaultWidthTiles;
        this.defaultHeightTiles = defaultHeightTiles;
    }

    internal static bool IsCommandRoot(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;
        string root = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ElementAtOrDefault(0)?.TrimStart('/') ?? string.Empty;
        return root.Equals("sandbox", StringComparison.OrdinalIgnoreCase) ||
               root.Equals("sb", StringComparison.OrdinalIgnoreCase) ||
               root.Equals("sb1", StringComparison.OrdinalIgnoreCase) ||
               root.Equals("sb2", StringComparison.OrdinalIgnoreCase) ||
               root.Equals("respawn", StringComparison.OrdinalIgnoreCase);
    }

    internal bool TryParse(string input, out SandboxOperation? operation, out string? error)
    {
        ArgumentNullException.ThrowIfNull(input);
        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return Fail(Usage, out operation, out error);

        string root = parts[0].TrimStart('/').ToLowerInvariant();
        try
        {
            return root switch
            {
                "sandbox" => TryParseSandbox(parts, out operation, out error),
                "sb" => TryParseSb(parts, out operation, out error),
                "sb1" => TryParseCreate(parts, WorldIsolationLevel.InProcess, out operation, out error),
                "sb2" => TryParseCreate(parts, WorldIsolationLevel.DedicatedProcess, out operation, out error),
                "respawn" => TryParseTransfer(parts, forceRespawn: true, out operation, out error),
                _ => Fail(Usage, out operation, out error)
            };
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or NotSupportedException)
        {
            return Fail(exception.Message, out operation, out error);
        }
    }

    private bool TryParseSb(string[] parts, out SandboxOperation? operation, out string? error)
    {
        if (parts.Length < 2 || parts[1].Equals("list", StringComparison.OrdinalIgnoreCase) ||
            parts[1].Equals("status", StringComparison.OrdinalIgnoreCase) ||
            parts[1].Equals("jobs", StringComparison.OrdinalIgnoreCase) ||
            parts[1].Equals("job", StringComparison.OrdinalIgnoreCase) ||
            parts[1].Equals("move", StringComparison.OrdinalIgnoreCase) ||
            parts[1].Equals("regen", StringComparison.OrdinalIgnoreCase) ||
            parts[1].Equals("destroy", StringComparison.OrdinalIgnoreCase) ||
            parts[1].Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseSandbox(parts, out operation, out error);
        }

        return TryParseCreate(parts, WorldIsolationLevel.InProcess, out operation, out error);
    }

    private bool TryParseSandbox(string[] parts, out SandboxOperation? operation, out string? error)
    {
        if (parts.Length < 2)
            return Fail("usage: sandbox list|status|jobs|job|move|regen|destroy|cancel", out operation, out error);

        switch (parts[1].ToLowerInvariant())
        {
            case "list" when parts.Length == 2:
                operation = new SandboxOperation.List(); error = null; return true;
            case "status" when parts.Length == 3:
                operation = new SandboxOperation.Status(new SandboxName(parts[2])); error = null; return true;
            case "jobs" when parts.Length == 2:
                operation = new SandboxOperation.Jobs(); error = null; return true;
            case "job" when parts.Length == 3 && TryOperationId(parts[2], out SandboxJobId jobId):
                operation = new SandboxOperation.Job(jobId); error = null; return true;
            case "move":
                return TryParseTransfer(parts[1..], forceRespawn: false, out operation, out error);
            case "cancel" when parts.Length == 3 && TryOperationId(parts[2], out SandboxJobId id):
                operation = new SandboxOperation.Cancel(id); error = null; return true;
            case "destroy" when parts.Length == 3:
                operation = new SandboxOperation.Destroy(new SandboxName(parts[2])); error = null; return true;
            case "regen":
                return TryParseRegenerate(parts, out operation, out error);
            default:
                return Fail("usage: sandbox list|status|jobs|job|move|regen|destroy|cancel", out operation, out error);
        }
    }

    private bool TryParseCreate(
        string[] parts,
        WorldIsolationLevel isolation,
        out SandboxOperation? operation,
        out string? error)
    {
        string root = parts[0].TrimStart('/').ToLowerInvariant();
        if (parts.Length < 4)
            return Fail($"usage: {root} <name> gen|file ...", out operation, out error);

        var name = new SandboxName(parts[1]);
        SandboxWorldSource source;
        switch (parts[2].ToLowerInvariant())
        {
            case "file" when parts.Length == 4:
                if (!TryResolveAsset(parts[3], out string? path, out error))
                {
                    operation = null;
                    return false;
                }
                source = new SandboxWorldSource.WorldFile(path!);
                break;
            case "gen":
                if (!TryParseGenerated(parts, 3, name.Value, out SandboxWorldSource.Generated? generated, out error))
                {
                    operation = null;
                    return false;
                }
                source = generated!;
                break;
            case "schem":
                return Fail($"{root} schematic materialization is not implemented yet.", out operation, out error);
            default:
                return Fail($"usage: {root} <name> gen <id> [seed <n|random>] [size <primary|w>x<h>] [mode <classic|expert|master|journey>] [evil <corruption|crimson>] | file <world>", out operation, out error);
        }

        operation = new SandboxOperation.Create(new SandboxCreateRequest(name, isolation, source));
        error = null;
        return true;
    }

    private static bool TryParseTransfer(
        string[] parts,
        bool forceRespawn,
        out SandboxOperation? operation,
        out string? error)
    {
        // Top-level: respawn <player> <target>. Sandbox move slice: move <player> <target>.
        if (parts.Length != 3)
            return Fail(forceRespawn
                ? "usage: respawn <player> <sandbox|primary>"
                : "usage: sandbox move <player> <sandbox|primary>", out operation, out error);

        SandboxName? sandbox = parts[2].Equals("primary", StringComparison.OrdinalIgnoreCase)
            ? null
            : new SandboxName(parts[2]);
        operation = forceRespawn
            ? new SandboxOperation.Respawn(parts[1], sandbox)
            : new SandboxOperation.Move(parts[1], sandbox);
        error = null;
        return true;
    }

    private bool TryParseGenerated(
        string[] parts,
        int generatorIndex,
        string worldName,
        out SandboxWorldSource.Generated? source,
        out string? error)
    {
        if (parts.Length <= generatorIndex)
            return Fail("generator ID is required", out source, out error);

        var generatorId = new WorldGeneratorId(parts[generatorIndex]);
        ulong seed = 0;
        int width = defaultWidthTiles;
        int height = defaultHeightTiles;
        WorldGenerationGameMode gameMode = WorldGenerationGameMode.Classic;
        WorldGenerationEvil evil = WorldGenerationEvil.Corruption;
        int index = generatorIndex + 1;
        while (index < parts.Length)
        {
            switch (parts[index].ToLowerInvariant())
            {
                case "seed" when index + 1 < parts.Length:
                    string seedValue = parts[index + 1];
                    if (seedValue.Equals("random", StringComparison.OrdinalIgnoreCase))
                        seed = RandomSeed();
                    else if (!ulong.TryParse(seedValue, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                        return Fail("seed must be an unsigned integer or random", out source, out error);
                    index += 2;
                    break;
                case "size" when index + 1 < parts.Length:
                    string sizeValue = parts[index + 1];
                    if (sizeValue.Equals("primary", StringComparison.OrdinalIgnoreCase))
                    {
                        width = defaultWidthTiles;
                        height = defaultHeightTiles;
                    }
                    else
                    {
                        string[] dimensions = sizeValue.Split('x', 'X');
                        if (dimensions.Length != 2 ||
                            !int.TryParse(dimensions[0], NumberStyles.None, CultureInfo.InvariantCulture, out width) ||
                            !int.TryParse(dimensions[1], NumberStyles.None, CultureInfo.InvariantCulture, out height) ||
                            width <= 0 || height <= 0)
                        {
                            return Fail("size must be primary or positive <width>x<height> dimensions", out source, out error);
                        }
                    }
                    index += 2;
                    break;
                case "mode" when index + 1 < parts.Length:
                    if (!Enum.TryParse(parts[index + 1], ignoreCase: true, out gameMode) || !Enum.IsDefined(gameMode))
                        return Fail("mode must be classic, expert, master or journey", out source, out error);
                    index += 2;
                    break;
                case "evil" when index + 1 < parts.Length:
                    if (!Enum.TryParse(parts[index + 1], ignoreCase: true, out evil) || !Enum.IsDefined(evil))
                        return Fail("evil must be corruption or crimson", out source, out error);
                    index += 2;
                    break;
                default:
                    return Fail($"unexpected generation option '{parts[index]}'", out source, out error);
            }
        }

        source = new SandboxWorldSource.Generated(
            generatorId,
            worldName,
            seed,
            width,
            height,
            new WorldGenerationOptions(gameMode, evil));
        error = null;
        return true;
    }

    internal bool TryBuildGeneratedRequest(
        string name,
        WorldIsolationLevel isolation,
        string generatorId,
        string seedText,
        int? widthTiles,
        int? heightTiles,
        WorldGenerationGameMode gameMode,
        WorldGenerationEvil evil,
        out SandboxCreateRequest request,
        out string? error)
    {
        request = default;
        try
        {
            var sandbox = new SandboxName(name);
            var generator = new WorldGeneratorId(generatorId);
            ulong seed;
            if (seedText.Equals("random", StringComparison.OrdinalIgnoreCase))
                seed = RandomSeed();
            else if (!ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
            {
                error = "seed must be an unsigned integer or random";
                return false;
            }

            int width = widthTiles ?? defaultWidthTiles;
            int height = heightTiles ?? defaultHeightTiles;
            if (width <= 0 || height <= 0)
            {
                error = "world dimensions must be positive";
                return false;
            }
            if (!Enum.IsDefined(gameMode) || !Enum.IsDefined(evil))
            {
                error = "world mode or evil selection is invalid";
                return false;
            }

            request = new SandboxCreateRequest(
                sandbox,
                isolation,
                new SandboxWorldSource.Generated(
                    generator,
                    sandbox.Value,
                    seed,
                    width,
                    height,
                    new WorldGenerationOptions(gameMode, evil)));
            error = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    internal bool TryBuildWorldFileRequest(
        string name,
        WorldIsolationLevel isolation,
        string relativeWorldPath,
        out SandboxCreateRequest request,
        out string? error)
    {
        request = default;
        try
        {
            var sandbox = new SandboxName(name);
            if (!TryResolveAsset(relativeWorldPath, out string? path, out error))
                return false;
            request = new SandboxCreateRequest(sandbox, isolation, new SandboxWorldSource.WorldFile(path!));
            error = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TryParseRegenerate(string[] parts, out SandboxOperation? operation, out string? error)
    {
        if (parts.Length != 3 && parts.Length != 5)
            return Fail("usage: sandbox regen <name> [seed <number|random>]", out operation, out error);

        ulong? seed = null;
        if (parts.Length == 5)
        {
            if (!parts[3].Equals("seed", StringComparison.OrdinalIgnoreCase))
                return Fail("usage: sandbox regen <name> [seed <number|random>]", out operation, out error);
            if (parts[4].Equals("random", StringComparison.OrdinalIgnoreCase))
                seed = RandomSeed();
            else if (ulong.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed))
                seed = parsed;
            else
                return Fail("seed must be an unsigned integer or random", out operation, out error);
        }

        operation = new SandboxOperation.Regenerate(new SandboxName(parts[2]), seed);
        error = null;
        return true;
    }

    private bool TryResolveAsset(string relativePath, out string? path, out string? error)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            path = null; error = "world asset path must be relative"; return false;
        }
        string candidate = Path.GetFullPath(Path.Combine(worldAssetRoot, relativePath));
        string relative = Path.GetRelativePath(worldAssetRoot, candidate);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            path = null; error = "world asset path escapes the configured root"; return false;
        }
        if (!string.Equals(Path.GetExtension(candidate), ".wld", StringComparison.OrdinalIgnoreCase))
        {
            path = null; error = "world asset must be a .wld file"; return false;
        }
        path = candidate; error = null; return true;
    }

    private static bool TryOperationId(string value, out SandboxJobId id)
    {
        bool parsed = long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long raw) && raw > 0;
        id = parsed ? new SandboxJobId(raw) : default;
        return parsed;
    }

    private static ulong RandomSeed()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    private const string Usage =
        "usage: sandbox list|status|jobs|job|move|regen|destroy|cancel | sb1 <name> gen|file ... | sb2 <name> gen|file ... | respawn <player> <sandbox|primary>";

    private static bool Fail<T>(string message, out T? operation, out string? error)
    {
        operation = default; error = message; return false;
    }
}

/// <summary>Executes typed sandbox operations and formats bounded operator feedback.</summary>
public sealed class SandboxOperations
{
    private readonly SandboxHost host;
    private readonly SandboxCommandParser parser;
    private readonly Level1PlayerTransferCoordinator? transfers;

    internal SandboxOperations(
        SandboxHost host,
        string worldAssetRoot,
        int defaultWidthTiles,
        int defaultHeightTiles,
        Level1PlayerTransferCoordinator? transfers = null)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        parser = new SandboxCommandParser(worldAssetRoot, defaultWidthTiles, defaultHeightTiles);
        this.transfers = transfers;
    }

    internal string Execute(string command)
    {
        bool respawnCommand = command.TrimStart().TrimStart('/').StartsWith("respawn", StringComparison.OrdinalIgnoreCase);
        if (!parser.TryParse(command, out SandboxOperation? operation, out string? error) || operation is null)
            return $"{(respawnCommand ? "respawn" : "sandbox")}: {error}";
        return Execute(operation);
    }

    public string Execute(SandboxOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation switch
        {
            SandboxOperation.List => FormatSandboxes(host.CaptureSandboxes()),
            SandboxOperation.Status status => host.TryGetSandbox(status.Name, out SandboxSnapshot snapshot)
                ? FormatSandbox(snapshot)
                : $"sandbox: '{status.Name}' not found",
            SandboxOperation.Jobs => FormatJobs(host.CaptureJobs()),
            SandboxOperation.Job job => host.TryGetJob(job.Id, out SandboxJobSnapshot snapshot)
                ? FormatJob(snapshot)
                : $"sandbox: operation {job.Id} not found",
            SandboxOperation.Create create => host.TryCreate(create.Request, out SandboxJobId id, out string? error)
                ? $"sandbox: create accepted as operation {id}"
                : $"sandbox: {error}",
            SandboxOperation.Move move => ExecuteTransfer(move.PlayerSelector, move.Sandbox, forceRespawn: false),
            SandboxOperation.Respawn respawn => ExecuteTransfer(respawn.PlayerSelector, respawn.Sandbox, forceRespawn: true),
            SandboxOperation.Regenerate regenerate => host.TryRegenerate(regenerate.Name, regenerate.Seed, out SandboxJobId id, out string? error)
                ? $"sandbox: regeneration accepted as operation {id}"
                : $"sandbox: {error}",
            SandboxOperation.Destroy destroy => host.TryDestroy(destroy.Name, out SandboxJobId id, out string? error)
                ? $"sandbox: destroy accepted as operation {id}"
                : $"sandbox: {error}",
            SandboxOperation.Kick kick => ExecuteKick(kick.PlayerSelector),
            SandboxOperation.Cancel cancel => host.TryCancel(cancel.Id)
                ? $"sandbox: cancellation requested for operation {cancel.Id}"
                : $"sandbox: operation {cancel.Id} is missing or already complete",
            _ => "sandbox: unsupported operation"
        };
    }

    internal SandboxTreeSnapshot CaptureTreeSnapshot() =>
        transfers?.CaptureTreeSnapshot() ?? default;

    internal static string FormatJob(in SandboxJobSnapshot job)
    {
        string result = $"sandbox: operation {job.Id} {job.Kind.ToString().ToLowerInvariant()} '{job.Sandbox}' {job.Status.ToString().ToLowerInvariant()}";
        if (!string.IsNullOrWhiteSpace(job.Error))
            result += $": {job.Error}";
        else if (job.RuntimeIdentity is WorldRuntimeIdentity identity)
            result += $" runtime={identity.RuntimeId} session={identity.SessionId}";
        return result;
    }

    internal int DefaultWidthTiles => parser.DefaultWidthTiles;
    internal int DefaultHeightTiles => parser.DefaultHeightTiles;
    internal WorldGeneratorId[] CaptureWorldGeneratorIds() => host.CaptureWorldGeneratorIds();

    internal bool TryBuildGeneratedCreate(
        string name,
        WorldIsolationLevel isolation,
        string generatorId,
        string seedText,
        int? widthTiles,
        int? heightTiles,
        WorldGenerationGameMode gameMode,
        WorldGenerationEvil evil,
        out SandboxOperation.Create? operation,
        out string? error)
    {
        if (!parser.TryBuildGeneratedRequest(name, isolation, generatorId, seedText, widthTiles, heightTiles, gameMode, evil, out SandboxCreateRequest request, out error))
        {
            operation = null;
            return false;
        }
        operation = new SandboxOperation.Create(request);
        return true;
    }

    internal bool TryBuildWorldFileCreate(
        string name,
        WorldIsolationLevel isolation,
        string relativeWorldPath,
        out SandboxOperation.Create? operation,
        out string? error)
    {
        if (!parser.TryBuildWorldFileRequest(name, isolation, relativeWorldPath, out SandboxCreateRequest request, out error))
        {
            operation = null;
            return false;
        }
        operation = new SandboxOperation.Create(request);
        return true;
    }

    private string ExecuteKick(string player)
    {
        if (transfers is null)
            return "sandbox: player connection operations are unavailable";
        return transfers.TryKick(player, out string? error)
            ? $"sandbox: kick requested for {player}"
            : $"sandbox: {error}";
    }

    private string ExecuteTransfer(string player, SandboxName? sandbox, bool forceRespawn)
    {
        string prefix = forceRespawn ? "respawn" : "sandbox";
        if (transfers is null)
            return $"{prefix}: player transfer operations are unavailable";
        if (!transfers.TryMove(player, sandbox, forceRespawn, out string? error))
            return $"{prefix}: {error}";
        string target = sandbox?.ToString() ?? "primary";
        return forceRespawn
            ? $"respawn: {player} -> {target} completed"
            : $"sandbox: {player} -> {target} completed";
    }

    private static string FormatSandboxes(SandboxSnapshot[] sandboxes)
    {
        if (sandboxes.Length == 0)
            return "sandbox: no live sandboxes";
        return string.Join(" | ", sandboxes.Select(static sandbox =>
            $"{sandbox.Name} {sandbox.Runtime.Lifecycle} tick={sandbox.Runtime.Tick} session={sandbox.Runtime.Identity.SessionId}"));
    }

    private static string FormatJobs(SandboxJobSnapshot[] jobs)
    {
        if (jobs.Length == 0)
            return "sandbox: no retained operations";
        return string.Join(" | ", jobs.Select(static job => FormatJob(job)["sandbox: ".Length..]));
    }

    private static string FormatSandbox(in SandboxSnapshot sandbox) =>
        $"sandbox: {sandbox.Name} {sandbox.Runtime.Lifecycle} " +
        $"runtime={sandbox.Runtime.Identity.RuntimeId} session={sandbox.Runtime.Identity.SessionId} " +
        $"world='{sandbox.Runtime.WorldName}' tick={sandbox.Runtime.Tick} players={sandbox.Runtime.Connections} " +
        $"entities={sandbox.Runtime.Npcs}/{sandbox.Runtime.Projectiles}/{sandbox.Runtime.WorldItems}" +
        (sandbox.PendingJob is SandboxJobId operation ? $" pending-operation={operation}" : string.Empty);
}
