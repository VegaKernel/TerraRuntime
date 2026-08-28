using System.Diagnostics;
using TerraRuntime.Operations;
using Terminal.Gui.App;

namespace TerraRuntime.TerminalUI;

internal sealed class TerminalUiHost : IDisposable
{
    private static readonly long RefreshIntervalTicks = Math.Max(1L, Stopwatch.Frequency / 2);

    private readonly IRuntimeDashboardOperations dashboardOperations;
    private readonly IPlayerOperations playerOperations;
    private readonly INetworkOperations networkOperations;
    private readonly IWorldOperations worldOperations;
    private readonly ILogOperations logOperations;
    private readonly CancellationTokenSource stopUi;
    private readonly Thread thread;
    private int disposed;

    private TerminalUiHost(
        IRuntimeDashboardOperations dashboardOperations,
        IPlayerOperations playerOperations,
        INetworkOperations networkOperations,
        IWorldOperations worldOperations,
        ILogOperations logOperations,
        CancellationToken serverCancellation)
    {
        this.dashboardOperations = dashboardOperations ?? throw new ArgumentNullException(nameof(dashboardOperations));
        this.playerOperations = playerOperations ?? throw new ArgumentNullException(nameof(playerOperations));
        this.networkOperations = networkOperations ?? throw new ArgumentNullException(nameof(networkOperations));
        this.worldOperations = worldOperations ?? throw new ArgumentNullException(nameof(worldOperations));
        this.logOperations = logOperations ?? throw new ArgumentNullException(nameof(logOperations));
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
        INetworkOperations networkOperations,
        IWorldOperations worldOperations,
        ILogOperations logOperations,
        CancellationToken serverCancellation)
    {
        var host = new TerminalUiHost(
            dashboardOperations,
            playerOperations,
            networkOperations,
            worldOperations,
            logOperations,
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
        try
        {
            using IApplication app = Application.Create().Init();
            using var window = new DashboardWindow(
                dashboardOperations,
                playerOperations,
                networkOperations,
                worldOperations,
                logOperations);
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
            app.Run(window);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Terminal UI stopped or could not initialize; server remains available: {exception.Message}");
        }
    }
}
