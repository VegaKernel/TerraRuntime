using System.Globalization;
using TerraRuntime.Operations;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.TerminalUI;

internal sealed class DashboardWindow : Runnable
{
    private readonly IRuntimeDashboardOperations operations;
    private readonly Label lifecycleLabel;
    private readonly Label worldLabel;
    private readonly Label tickLabel;
    private readonly Label tickTimingLabel;
    private readonly Label cpuTimingLabel;
    private readonly Label phaseLabel;
    private readonly Label commandLabel;
    private readonly Label networkLabel;
    private readonly Label optionsLabel;
    private readonly Label capturedLabel;

    public DashboardWindow(IRuntimeDashboardOperations operations)
    {
        this.operations = operations ?? throw new ArgumentNullException(nameof(operations));
        Title = "TerraRuntime";

        MenuBar menu = new()
        {
            Menus =
            [
                new MenuBarItem(
                    "_File",
                    [new MenuItem("_Close UI", "Keep the server running and close only the terminal UI", () => App?.RequestStop())]),
                new MenuBarItem(
                    "_Help",
                    [new MenuItem("_About", "", ShowAbout)])
            ]
        };

        StatusBar status = new();
        status.Add(new Shortcut(
            Application.GetDefaultKey(Command.Quit),
            "Close UI",
            () => App?.RequestStop()));

        View content = new()
        {
            Y = Pos.Bottom(menu),
            Width = Dim.Fill(),
            Height = Dim.Fill(status)
        };

        lifecycleLabel = CreateLabel(1);
        worldLabel = CreateLabel(Pos.Bottom(lifecycleLabel));
        tickLabel = CreateLabel(Pos.Bottom(worldLabel) + 1);
        tickTimingLabel = CreateLabel(Pos.Bottom(tickLabel));
        cpuTimingLabel = CreateLabel(Pos.Bottom(tickTimingLabel));
        phaseLabel = CreateLabel(Pos.Bottom(cpuTimingLabel));
        commandLabel = CreateLabel(Pos.Bottom(phaseLabel) + 1);
        networkLabel = CreateLabel(Pos.Bottom(commandLabel) + 1);
        optionsLabel = CreateLabel(Pos.Bottom(networkLabel));
        capturedLabel = CreateLabel(Pos.Bottom(optionsLabel) + 1);

        content.Add(
            lifecycleLabel,
            worldLabel,
            tickLabel,
            tickTimingLabel,
            cpuTimingLabel,
            phaseLabel,
            commandLabel,
            networkLabel,
            optionsLabel,
            capturedLabel);
        Add(menu, content, status);
    }

    public void RefreshSnapshot()
    {
        RuntimeDashboardSnapshot snapshot = operations.CaptureSnapshot();
        string cpuLast = snapshot.CpuTimeAvailable
            ? FormatMilliseconds(snapshot.LastTickCpuMilliseconds)
            : "n/a";
        string cpuWorst = snapshot.CpuTimeAvailable
            ? FormatMilliseconds(snapshot.WorstTickCpuMilliseconds)
            : "n/a";

        lifecycleLabel.Text = $"Lifecycle : {snapshot.Lifecycle}";
        worldLabel.Text = $"World     : {snapshot.WorldName}  {snapshot.WorldWidthTiles}x{snapshot.WorldHeightTiles}";
        tickLabel.Text = $"Tick      : {snapshot.Tick:N0}   TPS {snapshot.ObservedTicksPerSecond:F1}/{snapshot.TargetTicksPerSecond}";
        tickTimingLabel.Text = $"Tick wall : last {FormatMilliseconds(snapshot.LastTickMilliseconds)}   worst {FormatMilliseconds(snapshot.WorstTickMilliseconds)}";
        cpuTimingLabel.Text = $"Tick CPU  : last {cpuLast}   worst {cpuWorst}";
        phaseLabel.Text = $"Slow phase: {snapshot.SlowestPhase}  {FormatMilliseconds(snapshot.SlowestPhaseMilliseconds)}   missed deadlines {snapshot.MissedTickDeadlines:N0}";
        commandLabel.Text = $"Commands  : processed {snapshot.CommandsProcessed:N0}   pending {snapshot.PendingCommands:N0}   deferred {snapshot.DeferredCommands:N0}   rejected {snapshot.RejectedCommands:N0}";
        networkLabel.Text = $"Network   : active {snapshot.ActiveConnections}/{snapshot.MaxPlayers}   accepted {snapshot.AcceptedConnections:N0}   rejected {snapshot.RejectedConnections:N0}   port {snapshot.Port}";
        optionsLabel.Text = $"Runtime   : interest management {(snapshot.InterestManagementEnabled ? "enabled" : "disabled")}   budget exhaustions {snapshot.CommandBudgetExhaustions:N0}   oldest command {FormatMilliseconds(snapshot.OldestPendingCommandAgeMilliseconds)}";
        capturedLabel.Text = $"Snapshot  : {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC";
    }

    private static Label CreateLabel(Pos y) =>
        new()
        {
            X = 1,
            Y = y,
            Width = Dim.Fill(1)
        };

    private static string FormatMilliseconds(double milliseconds) =>
        milliseconds.ToString("F3", CultureInfo.InvariantCulture) + " ms";

    private void ShowAbout() =>
        MessageBox.Query(
            App!,
            "TerraRuntime",
            "Local operations dashboard. Closing this UI does not stop the game server.",
            "OK");
}
