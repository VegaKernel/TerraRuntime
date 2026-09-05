namespace TerraRuntime.Application;

/// <summary>
/// Single source of truth for operator-facing product identity. The version comes from the running assembly so
/// packaged/CoreCLR/NativeAOT builds display the version that was actually shipped instead of a duplicated UI constant.
/// </summary>
internal static class RuntimeProductInfo
{
    internal const string ProductName = "TerraRuntime";

    internal static string Version { get; } = ResolveVersion();

    internal static string DisplayName => $"{ProductName} v{Version}";

    internal static string BuildTitle(string section) =>
        string.IsNullOrWhiteSpace(section) ? DisplayName : $"{DisplayName} · {section.Trim()}";

    internal static void TryApplyConsoleTitle()
    {
        if (Console.IsOutputRedirected)
            return;

        try
        {
            Console.Title = DisplayName;
        }
        catch (Exception exception) when (exception is IOException or PlatformNotSupportedException)
        {
            // A terminal title is presentation-only. Unsupported hosts must never block server startup.
        }
    }

    private static string ResolveVersion()
    {
        System.Version? assemblyVersion = typeof(RuntimeProductInfo).Assembly.GetName().Version;
        if (assemblyVersion is null)
            return "dev";

        return assemblyVersion.Build >= 0
            ? assemblyVersion.ToString(3)
            : assemblyVersion.ToString(2);
    }
}

/// <summary>
/// Best-effort console transitions used only by the interactive startup flow. Redirected/headless runs retain all
/// output and never receive terminal-control side effects.
/// </summary>
internal static class StartupConsolePresentation
{
    internal static void ClearForTransition()
    {
        if (Console.IsOutputRedirected)
            return;

        try
        {
            Console.Clear();
        }
        catch (Exception exception) when (exception is IOException or PlatformNotSupportedException)
        {
            // Clearing is cosmetic; a restricted terminal must not alter startup semantics.
        }
    }
}
