using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime;

public abstract record SandboxOperation
{
    private SandboxOperation()
    {
    }

    public sealed record List : SandboxOperation;
    public sealed record Status(SandboxName Name) : SandboxOperation;
    public sealed record Create(SandboxCreateRequest Request) : SandboxOperation;
    public sealed record Regenerate(SandboxName Name, ulong? Seed) : SandboxOperation;
    public sealed record Destroy(SandboxName Name) : SandboxOperation;
    public sealed record Jobs : SandboxOperation;
    public sealed record Job(SandboxJobId Id) : SandboxOperation;
    public sealed record Cancel(SandboxJobId Id) : SandboxOperation;
}

/// <summary>Parses the debug command grammar and resolves file assets below one configured root.</summary>
public sealed class SandboxCommandParser
{
    private readonly string worldAssetRoot;

    public SandboxCommandParser(string worldAssetRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldAssetRoot);
        this.worldAssetRoot = Path.GetFullPath(worldAssetRoot);
    }

    public bool TryParse(string input, out SandboxOperation? operation, out string? error)
    {
        ArgumentNullException.ThrowIfNull(input);
        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 0 && parts[0].Equals("sandbox", StringComparison.OrdinalIgnoreCase))
            parts = parts[1..];
        if (parts.Length == 0)
            return Fail("usage: sandbox list|status|create|regen|destroy|jobs|job|cancel", out operation, out error);

        try
        {
            switch (parts[0].ToLowerInvariant())
            {
                case "list" when parts.Length == 1:
                    operation = new SandboxOperation.List();
                    error = null;
                    return true;
                case "status" when parts.Length == 2:
                    operation = new SandboxOperation.Status(new SandboxName(parts[1]));
                    error = null;
                    return true;
                case "jobs" when parts.Length == 1:
                    operation = new SandboxOperation.Jobs();
                    error = null;
                    return true;
                case "job" when parts.Length == 2 && TryJobId(parts[1], out SandboxJobId jobId):
                    operation = new SandboxOperation.Job(jobId);
                    error = null;
                    return true;
                case "cancel" when parts.Length == 2 && TryJobId(parts[1], out SandboxJobId cancelId):
                    operation = new SandboxOperation.Cancel(cancelId);
                    error = null;
                    return true;
                case "destroy" when parts.Length == 2:
                    operation = new SandboxOperation.Destroy(new SandboxName(parts[1]));
                    error = null;
                    return true;
                case "regen":
                    return TryParseRegenerate(parts, out operation, out error);
                case "create":
                    return TryParseCreate(parts, out operation, out error);
                default:
                    return Fail("usage: sandbox list|status|create|regen|destroy|jobs|job|cancel", out operation, out error);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or NotSupportedException)
        {
            return Fail(exception.Message, out operation, out error);
        }
    }

    private bool TryParseCreate(string[] parts, out SandboxOperation? operation, out string? error)
    {
        if (parts.Length < 5 || !parts[2].Equals("l1", StringComparison.OrdinalIgnoreCase))
            return Fail("usage: sandbox create <name> l1 gen|file ...", out operation, out error);

        var name = new SandboxName(parts[1]);
        SandboxWorldSource source;
        switch (parts[3].ToLowerInvariant())
        {
            case "file" when parts.Length == 5:
                if (!TryResolveAsset(parts[4], out string? path, out error))
                {
                    operation = null;
                    return false;
                }
                source = new SandboxWorldSource.WorldFile(path!);
                break;
            case "gen":
                if (!TryParseGenerated(parts, out SandboxWorldSource.Generated? generated, out error))
                {
                    operation = null;
                    return false;
                }
                source = generated!;
                break;
            case "schem":
                return Fail("Level 1 schematic materialization is not implemented yet.", out operation, out error);
            default:
                return Fail("usage: sandbox create <name> l1 gen <id> [seed <n|random>] [size <w>x<h>] | file <world>", out operation, out error);
        }

        operation = new SandboxOperation.Create(
            new SandboxCreateRequest(name, WorldIsolationLevel.InProcess, source));
        error = null;
        return true;
    }

    private static bool TryParseGenerated(
        string[] parts,
        out SandboxWorldSource.Generated? source,
        out string? error)
    {
        if (parts.Length < 5)
            return Fail("generator ID is required", out source, out error);

        var generatorId = new WorldGeneratorId(parts[4]);
        ulong seed = 0;
        int width = 4200;
        int height = 1200;
        int index = 5;
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
                    string[] dimensions = parts[index + 1].Split('x', 'X');
                    if (dimensions.Length != 2 ||
                        !int.TryParse(dimensions[0], NumberStyles.None, CultureInfo.InvariantCulture, out width) ||
                        !int.TryParse(dimensions[1], NumberStyles.None, CultureInfo.InvariantCulture, out height) ||
                        width <= 0 || height <= 0)
                    {
                        return Fail("size must use positive <width>x<height> dimensions", out source, out error);
                    }
                    index += 2;
                    break;
                default:
                    return Fail($"unexpected generation option '{parts[index]}'", out source, out error);
            }
        }

        source = new SandboxWorldSource.Generated(
            generatorId,
            $"Sandbox-{generatorId.Value}",
            seed,
            width,
            height,
            WorldGenerationOptions.Default);
        error = null;
        return true;
    }

    private static bool TryParseRegenerate(
        string[] parts,
        out SandboxOperation? operation,
        out string? error)
    {
        if (parts.Length != 2 && parts.Length != 4)
            return Fail("usage: sandbox regen <name> [seed <number|random>]", out operation, out error);

        ulong? seed = null;
        if (parts.Length == 4)
        {
            if (!parts[2].Equals("seed", StringComparison.OrdinalIgnoreCase))
                return Fail("usage: sandbox regen <name> [seed <number|random>]", out operation, out error);
            if (parts[3].Equals("random", StringComparison.OrdinalIgnoreCase))
                seed = RandomSeed();
            else if (ulong.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed))
                seed = parsed;
            else
                return Fail("seed must be an unsigned integer or random", out operation, out error);
        }

        operation = new SandboxOperation.Regenerate(new SandboxName(parts[1]), seed);
        error = null;
        return true;
    }

    private bool TryResolveAsset(string relativePath, out string? path, out string? error)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            path = null;
            error = "world asset path must be relative";
            return false;
        }

        string candidate = Path.GetFullPath(Path.Combine(worldAssetRoot, relativePath));
        string relative = Path.GetRelativePath(worldAssetRoot, candidate);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            path = null;
            error = "world asset path escapes the configured root";
            return false;
        }
        if (!string.Equals(Path.GetExtension(candidate), ".wld", StringComparison.OrdinalIgnoreCase))
        {
            path = null;
            error = "world asset must be a .wld file";
            return false;
        }

        path = candidate;
        error = null;
        return true;
    }

    private static bool TryJobId(string value, out SandboxJobId id)
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

    private static bool Fail<T>(string message, out T? operation, out string? error)
    {
        operation = default;
        error = message;
        return false;
    }
}

/// <summary>Executes typed sandbox operations and formats bounded operator feedback.</summary>
public sealed class SandboxOperations
{
    private readonly SandboxHost host;
    private readonly SandboxCommandParser parser;

    public SandboxOperations(SandboxHost host, string worldAssetRoot)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        parser = new SandboxCommandParser(worldAssetRoot);
    }

    public string Execute(string command)
    {
        if (!parser.TryParse(command, out SandboxOperation? operation, out string? error) || operation is null)
            return $"sandbox: {error}";
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
            SandboxOperation.Create create => host.TryCreate(create.Request, out SandboxJobId id, out string? error)
                ? $"sandbox: create accepted as job {id}"
                : $"sandbox: {error}",
            SandboxOperation.Regenerate regenerate => host.TryRegenerate(regenerate.Name, regenerate.Seed, out SandboxJobId id, out string? error)
                ? $"sandbox: regeneration accepted as job {id}"
                : $"sandbox: {error}",
            SandboxOperation.Destroy destroy => host.TryDestroy(destroy.Name, out SandboxJobId id, out string? error)
                ? $"sandbox: destroy accepted as job {id}"
                : $"sandbox: {error}",
            SandboxOperation.Jobs => FormatJobs(host.CaptureJobs()),
            SandboxOperation.Job job => host.TryGetJob(job.Id, out SandboxJobSnapshot snapshot)
                ? FormatJob(snapshot)
                : $"sandbox: job {job.Id} not found",
            SandboxOperation.Cancel cancel => host.TryCancel(cancel.Id)
                ? $"sandbox: cancellation requested for job {cancel.Id}"
                : $"sandbox: job {cancel.Id} is missing or already complete",
            _ => "sandbox: unsupported operation"
        };
    }

    private static string FormatSandboxes(SandboxSnapshot[] sandboxes)
    {
        if (sandboxes.Length == 0)
            return "sandbox: no live sandboxes";
        return string.Join(" | ", sandboxes.Select(static sandbox =>
            $"{sandbox.Name} {sandbox.Runtime.Lifecycle} tick={sandbox.Runtime.Tick} session={sandbox.Runtime.Identity.SessionId}"));
    }

    private static string FormatSandbox(in SandboxSnapshot sandbox) =>
        $"sandbox: {sandbox.Name} {sandbox.Runtime.Lifecycle} " +
        $"runtime={sandbox.Runtime.Identity.RuntimeId} session={sandbox.Runtime.Identity.SessionId} " +
        $"world='{sandbox.Runtime.WorldName}' tick={sandbox.Runtime.Tick} players={sandbox.Runtime.Connections} " +
        $"entities={sandbox.Runtime.Npcs}/{sandbox.Runtime.Projectiles}/{sandbox.Runtime.WorldItems}" +
        (sandbox.PendingJob is SandboxJobId job ? $" pending-job={job}" : string.Empty);

    private static string FormatJobs(SandboxJobSnapshot[] jobs)
    {
        if (jobs.Length == 0)
            return "sandbox: no jobs";
        var text = new StringBuilder();
        foreach (SandboxJobSnapshot job in jobs)
        {
            if (text.Length != 0)
                text.Append(" | ");
            text.Append(job.Id).Append(' ').Append(job.Kind).Append(' ').Append(job.Sandbox).Append(' ').Append(job.Status);
        }
        return text.ToString();
    }

    private static string FormatJob(in SandboxJobSnapshot job) =>
        $"sandbox: job {job.Id} {job.Kind} {job.Sandbox} {job.Status}" +
        (job.RuntimeIdentity is WorldRuntimeIdentity identity ? $" runtime={identity.RuntimeId} session={identity.SessionId}" : string.Empty) +
        (string.IsNullOrWhiteSpace(job.Error) ? string.Empty : $" error={job.Error}");
}
