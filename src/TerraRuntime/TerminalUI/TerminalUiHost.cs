using System.Diagnostics;
using TerraRuntime.Operations;
using Terminal.Gui.App;

namespace TerraRuntime.TerminalUI;

internal sealed class TerminalUiHost : IDisposable
{
    private static readonly long RefreshIntervalTicks = Math.Max(1L, Stopwatch.Frequency / 2);

    private readonly IRuntimeDashboardOperations dashboardOperations;
    private readonly IPlayerOperations playerOperations;
    private readonly INpcOperations npcOperations;
    private readonly INetworkOperations networkOperations;
    private readonly IWorldOperations worldOperations;
    private readonly ILogOperations logOperations;
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
        CancellationToken serverCancellation)
    {
        this.dashboardOperations = dashboardOperations ?? throw new ArgumentNullException(nameof(dashboardOperations));
        this.playerOperations = playerOperations ?? throw new ArgumentNullException(nameof(playerOperations));
        this.npcOperations = npcOperations ?? throw new ArgumentNullException(nameof(npcOperations));
        this.networkOperations = networkOperations ?? throw new ArgumentNullException(nameof(networkOperations));
        this.worldOperations = worldOperations ?? throw new ArgumentNullException(nameof(worldOperations));
        this.logOperations = logOperations ?? throw new ArgumentNullException(nameof(logOperations));
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
        CancellationToken serverCancellation)
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
            serverCancellation);
        host.thread.Start();
        return host;
    }

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
        bool announcedActive = false;
        try
        {
            using (IApplication app = Application.Create().Init())
            using (var window = new DashboardWindow(
                dashboardOperations,
                playerOperations,
                npcOperations,
                networkOperations,
                worldOperations,
                logOperations))
            {
                long nextRefresh = 0;

                app.Iteration += (_, _) =>
                {
                    long now = Stopwatch.GetTimestamp();
                    if (now < nextRefresh)
                        return;

                    window.RefreshSnapshot();
                    nextRefresh = now + RefreshIntervalTicks;
                };

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

                window.RefreshSnapshot();
                NotifyActivity(active: true);
                announcedActive = true;
                app.Run(window);
            }
        }
        catch (Exception exception)
        {
            if (announcedActive)
            {
                NotifyActivity(active: false);
                announcedActive = false;
            }

            ReportFailure(
                $"Terminal UI stopped or could not initialize; server remains available: {exception.Message}");
        }
        finally
        {
            if (announcedActive)
                NotifyActivity(active: false);
        }
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
