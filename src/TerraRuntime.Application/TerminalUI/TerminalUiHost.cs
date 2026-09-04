using System.Diagnostics;
using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.TerminalUI;
using TerraRuntime.Operations;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;

namespace TerraRuntime.TerminalUI;

internal sealed class TerminalUiHost : IDisposable
{
    private static readonly TimeSpan SnapshotRefreshInterval = TimeSpan.FromMilliseconds(100);
    private static readonly long RefreshIntervalTicks = Math.Max(
        1L,
        (long)(Stopwatch.Frequency * SnapshotRefreshInterval.TotalSeconds));
    private static readonly TimeSpan UiPumpInterval = TimeSpan.FromMilliseconds(16);

    private readonly IRuntimeDashboardOperations dashboardOperations;
    private readonly IPlayerOperations playerOperations;
    private readonly INpcOperations npcOperations;
    private readonly IProjectileOperations? projectileOperations;
    private readonly IWorldItemOperations? worldItemOperations;
    private readonly INetworkOperations networkOperations;
    private readonly IWorldOperations worldOperations;
    private readonly ILogOperations logOperations;
    private readonly SandboxOperations? sandboxOperations;
    private readonly IRuntimeWorldInspectionOperations? worldInspectionOperations;
    private readonly IPlayerAdministrativeOperations? playerAdministration;
    private readonly RuntimeConnectionSessionDirectory? connectionSessions;
    private readonly Action<bool> activityChanged;
    private readonly Action<string> failureSink;
    private readonly CancellationTokenSource stopUi;
    private readonly Thread thread;
    private int disposed;

    private TerminalUiHost(
        IRuntimeDashboardOperations dashboardOperations,
        IPlayerOperations playerOperations,
        INpcOperations npcOperations,
        INetworkOperations networkOperations,
        IWorldOperations worldOperations,
        ILogOperations logOperations,
        Action<bool> activityChanged,
        Action<string> failureSink,
        CancellationToken serverCancellation,
        IProjectileOperations? projectileOperations,
        IWorldItemOperations? worldItemOperations,
        SandboxOperations? sandboxOperations,
        IRuntimeWorldInspectionOperations? worldInspectionOperations,
        IPlayerAdministrativeOperations? playerAdministration,
        RuntimeConnectionSessionDirectory? connectionSessions)
    {
        this.dashboardOperations = dashboardOperations ?? throw new ArgumentNullException(nameof(dashboardOperations));
        this.playerOperations = playerOperations ?? throw new ArgumentNullException(nameof(playerOperations));
        this.npcOperations = npcOperations ?? throw new ArgumentNullException(nameof(npcOperations));
        this.projectileOperations = projectileOperations;
        this.worldItemOperations = worldItemOperations;
        this.networkOperations = networkOperations ?? throw new ArgumentNullException(nameof(networkOperations));
        this.worldOperations = worldOperations ?? throw new ArgumentNullException(nameof(worldOperations));
        this.logOperations = logOperations ?? throw new ArgumentNullException(nameof(logOperations));
        this.sandboxOperations = sandboxOperations;
        this.worldInspectionOperations = worldInspectionOperations;
        this.playerAdministration = playerAdministration;
        this.connectionSessions = connectionSessions;
        this.activityChanged = activityChanged ?? throw new ArgumentNullException(nameof(activityChanged));
        this.failureSink = failureSink ?? throw new ArgumentNullException(nameof(failureSink));
        stopUi = CancellationTokenSource.CreateLinkedTokenSource(serverCancellation);
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "TerraRuntime Terminal UI"
        };
    }

    public static TerminalUiHost Start(
        IRuntimeDashboardOperations dashboardOperations,
        IPlayerOperations playerOperations,
        INpcOperations npcOperations,
        INetworkOperations networkOperations,
        IWorldOperations worldOperations,
        ILogOperations logOperations,
        Action<bool> activityChanged,
        Action<string> failureSink,
        CancellationToken serverCancellation,
        IProjectileOperations? projectileOperations = null,
        IWorldItemOperations? worldItemOperations = null,
        SandboxOperations? sandboxOperations = null,
        IRuntimeWorldInspectionOperations? worldInspectionOperations = null,
        IPlayerAdministrativeOperations? playerAdministration = null,
        RuntimeConnectionSessionDirectory? connectionSessions = null)
    {
        var host = new TerminalUiHost(
            dashboardOperations,
            playerOperations,
            npcOperations,
            networkOperations,
            worldOperations,
            logOperations,
            activityChanged,
            failureSink,
            serverCancellation,
            projectileOperations,
            worldItemOperations,
            sandboxOperations,
            worldInspectionOperations,
            playerAdministration,
            connectionSessions);
        host.thread.Start();
        return host;
    }

    internal static string? ResolveProductionDriverName(bool isWindows) =>
        isWindows ? DriverRegistry.Names.DOTNET : null;

    internal static TimeSpan UiPumpIntervalForTests => UiPumpInterval;
    internal static TimeSpan SnapshotRefreshIntervalForTests => SnapshotRefreshInterval;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        stopUi.Cancel();
        if (thread.IsAlive && Thread.CurrentThread != thread)
            thread.Join(TimeSpan.FromSeconds(2));
        stopUi.Dispose();
    }

    private void Run()
    {
        while (!stopUi.IsCancellationRequested)
        {
            RunDashboardSession();
            if (stopUi.IsCancellationRequested)
                return;

            if (!RunPlainConsole())
                return;
        }
    }

    private void RunDashboardSession()
    {
        bool activityAnnounced = false;
        try
        {
            using IApplication app = Application.Create();
            string? forcedDriver = ResolveProductionDriverName(OperatingSystem.IsWindows());
            if (forcedDriver is not null)
                app.ForceDriver = forcedDriver;

            // Mark the terminal as owned before driver initialization. If initialization fails,
            // the finally block still performs a real TUI -> plain-console transition.
            NotifyActivity(active: true);
            activityAnnounced = true;
            app.Init();
            TerminalUiTheme.Apply();

            var snapshotCache = new TerminalUiOperationsCache(
                dashboardOperations,
                playerOperations,
                npcOperations,
                networkOperations,
                worldOperations,
                logOperations,
                projectileOperations,
                worldItemOperations,
                sandboxOperations,
                worldInspectionOperations);
            IProjectileOperations? cachedProjectileOperations = projectileOperations is null ? null : snapshotCache;
            IWorldItemOperations? cachedWorldItemOperations = worldItemOperations is null ? null : snapshotCache;

            ITerraRuntimeTerminalDashboardSource? terminalDashboards =
                StartupProgram.CurrentTerminalDashboards;
            using var window = new DashboardWorkspaceWindow(
                snapshotCache,
                snapshotCache,
                snapshotCache,
                snapshotCache,
                snapshotCache,
                snapshotCache,
                terminalDashboards,
                cachedProjectileOperations,
                cachedWorldItemOperations,
                sandboxOperations,
                snapshotCache.CaptureSandboxTreeSnapshot,
                worldInspectionOperations is null ? null : snapshotCache,
                playerAdministration,
                connectionSessions);

            Task? backgroundRefresh = null;
            long nextRefresh = Stopwatch.GetTimestamp() + RefreshIntervalTicks;
            long appliedVersion = snapshotCache.Version;
            app.AddTimeout(UiPumpInterval, () =>
            {
                if (stopUi.IsCancellationRequested)
                {
                    app.RequestStop(window);
                    return false;
                }

                if (backgroundRefresh is { IsCompleted: true } completed)
                {
                    try
                    {
                        completed.GetAwaiter().GetResult();
                    }
                    catch (Exception exception)
                    {
                        ReportFailure(
                            $"Terminal UI snapshot refresh failed; keeping the previous detached snapshot: {exception.Message}");
                    }
                    backgroundRefresh = null;
                }

                long publishedVersion = snapshotCache.Version;
                if (publishedVersion != appliedVersion)
                {
                    window.RefreshSnapshot();
                    appliedVersion = publishedVersion;
                }

                long now = Stopwatch.GetTimestamp();
                if (backgroundRefresh is null && now >= nextRefresh)
                {
                    backgroundRefresh = Task.Run(snapshotCache.Refresh);
                    nextRefresh = now + RefreshIntervalTicks;
                }

                return true;
            });

            using CancellationTokenRegistration registration = stopUi.Token.Register(() =>
            {
                try
                {
                    app.Invoke(static application => application.RequestStop());
                }
                catch (Exception)
                {
                    // The application may already be disposing after the user closes only the UI.
                }
            });

            // The initial cache is captured before the window becomes interactive; from this point on the
            // Terminal.Gui thread only formats/publishes cached data and remains available for input processing.
            window.RefreshSnapshot();
            app.Run(window);
        }
        catch (Exception exception)
        {
            ReportFailure(
                $"Terminal UI stopped or could not initialize; switching to plain console: {exception.Message}");
        }
        finally
        {
            if (activityAnnounced)
                NotifyActivity(active: false);
        }
    }

    private bool RunPlainConsole()
    {
        Console.WriteLine("[console] Plain console active. Type 'tui' to reopen the dashboard or 'help' for console commands.");

        while (!stopUi.IsCancellationRequested)
        {
            Console.Write("> ");
            string? line;
            try
            {
                line = Console.ReadLine();
            }
            catch (Exception exception)
            {
                ReportFailure($"Console input failed: {exception.Message}");
                return false;
            }

            if (line is null)
            {
                // Redirected/closed stdin must not turn this background host into a busy loop.
                if (stopUi.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(250)))
                    return false;
                continue;
            }

            string command = line.Trim();
            if (command.Length == 0)
                continue;

            switch (command.ToLowerInvariant())
            {
                case "tui":
                case "ui":
                case "dashboard":
                    return true;

                case "help":
                    Console.WriteLine("tui | ui | dashboard  Reopen the TerraRuntime dashboard");
                    Console.WriteLine("clear                 Clear the plain console");
                    Console.WriteLine("help                  Show these console commands");
                    break;

                case "clear":
                    try
                    {
                        Console.Clear();
                    }
                    catch (IOException)
                    {
                        // Some redirected terminals do not support clearing; keep the console usable.
                    }
                    break;

                default:
                    Console.WriteLine($"Unknown console command '{command}'. Type 'help'.");
                    break;
            }
        }

        return false;
    }

    private void NotifyActivity(bool active)
    {
        try
        {
            activityChanged(active);
        }
        catch (Exception)
        {
            // UI lifetime signaling must never make the server unavailable.
        }
    }

    private void ReportFailure(string message)
    {
        try
        {
            failureSink(message);
        }
        catch (Exception)
        {
            Console.Error.WriteLine(message);
        }
    }
}
