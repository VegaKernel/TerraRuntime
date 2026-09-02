namespace TerraRuntime.Extensibility;

internal sealed class TrustedHostModuleLoadPolicy
{
    internal const string RequiredModulesEnvironmentVariable = "TERRARUNTIME_REQUIRED_HOST_MODULES";

    private readonly HashSet<string> requiredModuleFileNames;

    public TrustedHostModuleLoadPolicy(
        bool requireAllModules,
        IEnumerable<string>? requiredModuleFileNames = null)
    {
        RequireAllModules = requireAllModules;
        this.requiredModuleFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (requiredModuleFileNames is null)
            return;

        foreach (string fileName in requiredModuleFileNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            this.requiredModuleFileNames.Add(Path.GetFileName(fileName));
        }
    }

    public static TrustedHostModuleLoadPolicy Resilient { get; } = new(requireAllModules: false);
    public static TrustedHostModuleLoadPolicy Strict { get; } = new(requireAllModules: true);

    public bool RequireAllModules { get; }

    public static TrustedHostModuleLoadPolicy FromEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        string? raw = read(RequiredModulesEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            return Resilient;

        string[] entries = raw
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
            return Resilient;
        if (entries.Any(static entry => entry == "*"))
            return Strict;

        return new TrustedHostModuleLoadPolicy(requireAllModules: false, entries);
    }

    public bool IsRequired(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return RequireAllModules || requiredModuleFileNames.Contains(Path.GetFileName(path));
    }
}

internal enum TrustedHostModuleFaultPhase : byte
{
    Startup = 0,
    RuntimeAttach = 1,
    RuntimeDetach = 2,
    Stop = 3,
    ScopeRetirement = 4
}

internal sealed record TrustedHostModuleFault(
    string FileName,
    string? ModuleName,
    TrustedHostModuleFaultPhase Phase,
    bool Required,
    DateTimeOffset TimestampUtc,
    string ExceptionType,
    string Message,
    string Detail);
