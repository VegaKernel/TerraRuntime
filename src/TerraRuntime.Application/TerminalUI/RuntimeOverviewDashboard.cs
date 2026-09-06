using TerraRuntime.Application.Bots;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.Text;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Application.Operations;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

#pragma warning disable CS0618 // Terminal.Gui TextView is still the built-in selectable read-only surface in 2.4.17.

namespace TerraRuntime.Application.TerminalUI;

/// <summary>
/// Runtime-owned tiled dashboard. Runtime snapshots arrive already detached from authoritative state; this view
/// only formats them, maintains bounded UI-local history and handles presentation-only interaction.
/// </summary>
internal sealed class RuntimeOverviewDashboard : View
{
    private const int HistoryLength = 60;
    private const int MaximumFeedEntries = 64;
    private const int GraphRowHeight = 8;
    // One second beyond RuntimeConnectionRoute's transfer barrier keeps sandbox ownership alive while a TUI closes.
    private static readonly TimeSpan OperatorCommandShutdownTimeout = TimeSpan.FromSeconds(4);
    private const string ActiveTitlePrefix = "▶ ";
    private const string BaseSchemeName = "Base";
    private const string AccentSchemeName = "Accent";

    private readonly FrameView consoleFrame;
    private readonly FrameView networkFrame;
    private readonly FrameView worldsFrame;
    private readonly FrameView commandFrame;
    private readonly TextView consoleText;
    private readonly SandboxWorldTreeView worldsText;
    private readonly Label networkLegend;
    private readonly Label commandFeedback;
    private readonly Button sandboxAddButton;
    private readonly Button botAddButton;
    private readonly DropDownList feedLogModeDropDown;
    private readonly DropDownList feedChatDropDown;
    private readonly TextField commandInput;
    private readonly NetworkTrafficChartView networkGraph;
    private readonly SandboxOperations? sandboxOperations;
    private readonly Func<SandboxTreeSnapshot>? sandboxTreeSource;
    private readonly RuntimeBotOperations? botOperations;
    private readonly HashSet<long> observedTerminalSandboxJobs = [];
    private readonly NetworkTrafficSample[] history = new NetworkTrafficSample[HistoryLength];
    private int historyCount;
    private int historyNext;
    private FrameView? maximized;
    private string appliedConsoleText = string.Empty;
    private string pendingConsoleText = string.Empty;
    private string? appliedWorkspaceStatus;
    private OperationsLogLevel? minimumLogLevel = OperationsLogLevel.Information;
    private bool showChat = true;
    private RuntimeLogSnapshot latestLogs;
    private RuntimeLogSnapshot latestChat;
    private bool hasFeedSnapshot;
    private bool hasNetworkCounterSample;
    private Task<string>? pendingSandboxCommand;
    private Task<string>? pendingBotCommand;
    private SandboxCreateWindow? sandboxCreateWindow;
    private BotSettingsWindow? botSettingsWindow;
    private RuntimePlayerSnapshot[] latestPrimaryPlayers = [];
    private DateTimeOffset lastNetworkCapturedAtUtc;
    private long lastMessageInboundFrames;
    private long lastMessageInboundBytes;
    private long lastMessageOutboundFrames;
    private long lastMessageOutboundBytes;
    private bool updatingFeedControls;

    public RuntimeOverviewDashboard(
        SandboxOperations? sandboxOperations = null,
        Func<SandboxTreeSnapshot>? sandboxTreeSource = null,
        RuntimeBotOperations? botOperations = null)
    {
        this.sandboxOperations = sandboxOperations;
        this.sandboxTreeSource = sandboxTreeSource;
        this.botOperations = botOperations;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        consoleText = CreateSelectableTextSurface(scrollBars: true);
        worldsText = new SandboxWorldTreeView
        {
            ViewportSettings = ViewportSettingsFlags.HasScrollBars,
            SchemeName = BaseSchemeName
        };
        networkGraph = new NetworkTrafficChartView { SchemeName = BaseSchemeName };
        networkLegend = CreateLegend();

        feedLogModeDropDown = CreateFeedDropDown(1, 18,
            ["Logs OFF", "Logs DEBUG+", "Logs INFO+", "Logs WARN+", "Logs ERROR+"]);
        feedChatDropDown = CreateFeedDropDown(22, 13, ["Chat ON", "Chat OFF"]);
        feedLogModeDropDown.ValueChanged += (_, args) => ApplyLogFeedSelection(args.NewValue);
        feedChatDropDown.ValueChanged += (_, args) => ApplyChatFeedSelection(args.NewValue);
        UpdateFeedControlText();

        commandFeedback = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(4),
            Width = Dim.Fill(),
            Text = string.Empty,
            SchemeName = BaseSchemeName
        };
        commandInput = new TextField
        {
            X = 2,
            Y = 0,
            Width = Dim.Fill(1),
            Text = string.Empty,
            SchemeName = AccentSchemeName,
            CanFocus = true
        };
        var commandPrompt = new Label
        {
            X = 0,
            Y = 0,
            Text = ">",
            SchemeName = AccentSchemeName
        };

        sandboxAddButton = new Button
        {
            X = 1,
            Y = 0,
            Text = "+ Sandbox",
            NoPadding = true,
            NoDecorations = true,
            CanFocus = true,
            SchemeName = BaseSchemeName,
            Enabled = sandboxOperations is not null
        };
        sandboxAddButton.Accepted += (_, _) => ShowSandboxCreateWindow();
        botAddButton = new Button
        {
            X = Pos.Right(sandboxAddButton) + 2,
            Y = 0,
            Text = "+ Bot",
            NoPadding = true,
            NoDecorations = true,
            CanFocus = true,
            SchemeName = BaseSchemeName,
            Enabled = botOperations is not null
        };
        botAddButton.Accepted += (_, _) => CreateBotAsync();
        consoleFrame = CreateFrame("Console", consoleText, commandInput, feedLogModeDropDown, feedChatDropDown);
        networkFrame = CreateFrame("Network", networkGraph);
        worldsFrame = CreateFrame("Worlds / Players", worldsText);
        commandFrame = new FrameView
        {
            X = 0,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(),
            Height = 3,
            CanFocus = true,
            SchemeName = AccentSchemeName
        };

        consoleText.X = 0;
        consoleText.Y = 1;
        consoleText.Width = Dim.Fill();
        consoleText.Height = Dim.Fill(5);
        commandFrame.Add(commandPrompt, commandInput);
        consoleFrame.Add(feedLogModeDropDown, feedChatDropDown, consoleText, commandFeedback, commandFrame);

        networkLegend.X = 1;
        networkLegend.Y = 0;
        networkLegend.Width = Dim.Fill(1);
        networkGraph.X = 0;
        networkGraph.Y = 1;
        networkGraph.Width = Dim.Fill();
        networkGraph.Height = Dim.Fill();
        networkFrame.Add(networkLegend, networkGraph);

        worldsText.X = 0;
        worldsText.Y = 1;
        worldsText.Width = Dim.Fill();
        worldsText.Height = Dim.Fill();
        worldsFrame.Add(sandboxAddButton, botAddButton, worldsText);
        worldsText.TransferRequested += (player, sandbox) =>
            ExecuteSandboxOperationAsync(new SandboxOperation.MoveExact(player, sandbox));
        worldsText.DestroyRequested += ConfirmSandboxDestroy;
        worldsText.KickRequested += ConfirmPlayerKick;
        worldsText.PlayerOpenRequested += player => PlayerOpenRequested?.Invoke(player);
        worldsText.BotOpenRequested += ShowBotSettings;
        worldsText.BotDespawnRequested += DespawnBotAsync;

        AttachMaximize(consoleFrame);
        AttachMaximize(networkFrame);
        AttachMaximize(worldsFrame);

        commandInput.Accepting += (_, args) =>
        {
            string command = commandInput.Text.Trim();
            commandInput.Text = string.Empty;
            args.Handled = true;
            if (command.Length != 0)
                ExecuteConsoleCommand(command);
        };

        KeyDown += (_, key) =>
        {
            if (key == Key.P.WithCtrl)
            {
                commandInput.SetFocus();
                key.Handled = true;
            }
        };

        Add(consoleFrame, networkFrame, worldsFrame);
        ApplyTiledLayout();

        Initialized += (_, _) =>
        {
            // Terminal.Gui requires every SuperView in the navigation chain to be focusable. The dashboard is hosted
            // inside a plain workspace View, so make that immediate parent focusable before focusing the command field.
            if (SuperView is { } superView)
                superView.CanFocus = true;
            commandInput.SetFocus();
        };
    }

    private void ConfirmSandboxDestroy(SandboxName sandbox)
    {
        if (App is null)
            return;
        int? result = MessageBox.Query(
            App,
            "Destroy sandbox",
            $"Destroy sandbox '{sandbox.Value}'? Players in this world will be disconnected or transferred according to runtime policy.",
            "Destroy",
            "Cancel");
        if (result == 0)
            ExecuteSandboxOperationAsync(new SandboxOperation.Destroy(sandbox));
    }

    private void ConfirmPlayerKick(string playerSelector)
    {
        if (App is null)
            return;
        int? result = MessageBox.Query(
            App,
            "Kick player",
            $"Kick player '{playerSelector}' from the server?",
            "Kick",
            "Cancel");
        if (result == 0)
            ExecuteSandboxOperationAsync(new SandboxOperation.Kick(playerSelector));
    }

    public void Refresh(
        RuntimeDashboardSnapshot runtime,
        RuntimeNetworkSnapshot network,
        RuntimeWorldSnapshot world,
        RuntimePlayersSnapshot playersSnapshot,
        RuntimeLogSnapshot logs,
        RuntimeLogSnapshot chat,
        string? status)
    {
        _ = world;
        NetworkRates networkRates = CalculateNetworkRates(network);
        AppendHistory(networkRates);

        ReadOnlySpan<RuntimePlayerSnapshot> players = playersSnapshot.Players.Span;
        latestPrimaryPlayers = players.ToArray();
        RuntimeBotSnapshot[] bots = botOperations?.CaptureSnapshot() ?? [];
        latestLogs = logs;
        latestChat = chat;
        hasFeedSnapshot = true;

        RefreshFeedProjection();
        SandboxTreeSnapshot sandboxTree = sandboxTreeSource?.Invoke() ?? default;
        (string[] worldTreeLines, SandboxWorldTreeRow[] worldTreeRows) = RenderWorldTree(
            runtime,
            players,
            sandboxTree.Worlds.Span,
            bots);
        worldsText.SetRows(worldTreeLines, worldTreeRows);

        networkLegend.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"IN {networkRates.InboundPacketsPerSecond:F1} p/s · {FormatByteRate(networkRates.InboundKiBPerSecond)}  " +
            $"OUT {networkRates.OutboundPacketsPerSecond:F1} p/s · {FormatByteRate(networkRates.OutboundKiBPerSecond)}");

        if (FindWorkspace() is { } workspace)
        {
            workspace.Title = runtime.Port > 0
                ? $"{RuntimeProductInfo.DisplayName} :{runtime.Port} - System Dashboard"
                : $"{RuntimeProductInfo.DisplayName} - System Dashboard";
        }

        if (!string.IsNullOrWhiteSpace(status) &&
            !string.Equals(status, appliedWorkspaceStatus, StringComparison.Ordinal))
        {
            SetCommandFeedback(status);
            appliedWorkspaceStatus = status;
        }

        PublishSandboxCommandCompletion();
        PublishBotCommandCompletion();
        ObserveSandboxJobs(sandboxTree.Jobs.Span);

        UpdateGraphs();
        SetNeedsDraw();
    }

    public event Action<RuntimePlayerSnapshot>? PlayerOpenRequested;

    internal void FocusCommandInput() => commandInput.SetFocus();

    internal void FocusWorldTree() => worldsText.SetFocus();

    private void ShowSandboxCreateWindow()
    {
        if (sandboxOperations is null)
        {
            SetCommandFeedback("sandbox: creation is unavailable");
            return;
        }
        if (sandboxCreateWindow is not null)
        {
            sandboxCreateWindow.SetFocus();
            return;
        }

        var window = new SandboxCreateWindow(sandboxOperations);
        sandboxCreateWindow = window;
        window.CreateRequested += operation => SubmitSandboxCreate(window, operation);
        window.CloseRequested += CloseSandboxCreateWindow;
        Add(window);
        window.SetFocus();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void SubmitSandboxCreate(SandboxCreateWindow window, SandboxOperation.Create operation)
    {
        if (sandboxOperations is null)
        {
            window.SetFeedback("sandbox: creation is unavailable");
            return;
        }

        string result = sandboxOperations.Execute(operation);
        SetCommandFeedback(result);
        if (!result.Contains("accepted as operation", StringComparison.OrdinalIgnoreCase))
        {
            window.SetFeedback(result);
            return;
        }

        CloseSandboxCreateWindow();
    }

    private void CloseSandboxCreateWindow()
    {
        SandboxCreateWindow? window = sandboxCreateWindow;
        if (window is null)
            return;
        sandboxCreateWindow = null;
        Remove(window);
        window.Dispose();
        worldsText.SetFocus();
        SetNeedsDraw();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            WaitForPendingOperatorCommand(pendingSandboxCommand);
            WaitForPendingOperatorCommand(pendingBotCommand);
        }
        base.Dispose(disposing);
    }

    internal void TogglePanelForSmoke(string panelTitle) => ToggleMaximize(GetFrame(panelTitle));

    internal int GetVisiblePanelCountForSmoke() => EnumerateFrames().Count(static frame => frame.Visible);

    internal string GetPanelTitleForSmoke(string panelTitle) =>
        GetFrame(panelTitle).Title?.ToString() ?? string.Empty;

    internal string? GetPanelSchemeForSmoke(string panelTitle) =>
        GetFrame(panelTitle).SchemeName;

    internal Rectangle GetPanelFrameForSmoke(string panelTitle) => GetFrame(panelTitle).Frame;

    internal bool CommandInputFrameUsesAccentForSmoke =>
        string.Equals(commandFrame.SchemeName, AccentSchemeName, StringComparison.Ordinal);

    internal bool HasTitleDoubleClickBindingForSmoke(string panelTitle)
    {
        FrameView frame = GetFrame(panelTitle);
        return frame.Border.View is View borderView &&
               borderView.MouseBindings
                   .GetCommands(MouseFlags.LeftButtonDoubleClicked)
                   .Contains(Command.Accept);
    }

    internal string GetNetworkLegendForSmoke() => networkLegend.Text?.ToString() ?? string.Empty;

    internal string GetFeedControlsForSmoke() =>
        $"{feedLogModeDropDown.Text} | {feedChatDropDown.Text}";

    internal string GetConsoleTextForSmoke() => consoleText.Text?.ToString() ?? string.Empty;

    internal string GetWorldsTextForSmoke() => worldsText.RenderedText;

    internal int GetNetworkPlotWidthForSmoke() => networkGraph.PlotWidthForSmoke;

    internal (double Inbound, double Outbound) GetNetworkScaleMaximumsForSmoke() =>
        (networkGraph.InboundScaleMaximumForSmoke, networkGraph.OutboundScaleMaximumForSmoke);

    internal bool SandboxAddEnabledForSmoke => sandboxAddButton.Enabled;

    internal string[] WorldActionButtonsForSmoke =>
        [sandboxAddButton.Text?.ToString() ?? string.Empty, botAddButton.Text?.ToString() ?? string.Empty];

    internal bool BotAddEnabledForSmoke => botAddButton.Enabled;

    internal void SetFeedForSmoke(bool logs, bool chat, OperationsLogLevel minimumLevel)
    {
        this.minimumLogLevel = logs ? minimumLevel : null;
        showChat = chat;
        UpdateFeedControlText();
        RefreshFeedProjection();
    }

    internal bool ConsoleSupportsSelectionForSmoke => consoleText.ReadOnly && consoleText.CanFocus;

    internal bool CommandInputVisibleForSmoke => commandInput.Visible;

    internal bool CommandInputHasFocusForSmoke => commandInput.HasFocus;

    internal bool DashboardCanFocusForSmoke => CanFocus && (SuperView?.CanFocus ?? true);

    internal int ConsoleViewportYForSmoke => consoleText.Viewport.Y;

    internal int ConsoleLinesForSmoke => consoleText.Lines;

    internal void ScrollConsoleToTopForSmoke() => consoleText.ScrollTo(Point.Empty);

    private void ExecuteConsoleCommand(string input)
    {
        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string command = parts.ElementAtOrDefault(0)?.ToLowerInvariant() ?? string.Empty;
        DashboardWorkspaceWindow? workspace = FindWorkspace();

        switch (command)
        {
            case "help":
            case "?":
                SetCommandFeedback("help | feed | save | interest | system | players | npcs | projectiles | items | network | world | logs");
                return;
            case "clear":
                SetCommandFeedback(string.Empty);
                return;
            case "feed":
                ExecuteFeedCommand(parts);
                return;
            case "save":
                if (workspace is null)
                {
                    SetCommandFeedback("console: workspace unavailable");
                    return;
                }
                SetCommandFeedback("console: world checkpoint requested");
                workspace.RequestWorldSaveCheckpoint();
                return;
            case "interest":
                if (workspace is null)
                {
                    SetCommandFeedback("console: workspace unavailable");
                    return;
                }
                if (parts.Length != 2 || !TryParseOnOff(parts[1], out bool enabled))
                {
                    SetCommandFeedback("usage: interest on|off");
                    return;
                }
                workspace.SetInterestManagementEnabled(enabled);
                SetCommandFeedback($"console: interest {(enabled ? "on" : "off")} requested");
                return;
            case "system":
            case "overview":
                workspace?.ShowSystemDashboard();
                return;
            case "players":
                workspace?.ShowPlayers();
                return;
            case "npcs":
                workspace?.ShowNpcs();
                return;
            case "projectiles":
                workspace?.ShowProjectiles();
                return;
            case "items":
                workspace?.ShowItems();
                return;
            case "network":
                workspace?.ShowNetwork();
                return;
            case "world":
                workspace?.ShowWorld();
                return;
            case "logs":
                workspace?.ShowLogs();
                return;
            default:
                SetCommandFeedback($"unknown runtime console command '{Sanitize(input, 64)}'; type help");
                return;
        }
    }

    private void CreateBotAsync()
    {
        if (botOperations is null)
        {
            SetCommandFeedback("bot: runtime bot operations are unavailable");
            return;
        }
        StartBotCommand(async () =>
        {
            RuntimeBotSnapshot? bot = await botOperations.CreateAsync().ConfigureAwait(false);
            return bot is RuntimeBotSnapshot created
                ? $"bot: created {created.Name} in primary"
                : "bot: creation rejected";
        });
    }

    private void DespawnBotAsync(int id)
    {
        if (botOperations is null)
            return;
        StartBotCommand(async () =>
            await botOperations.DespawnAsync(id).ConfigureAwait(false)
                ? $"bot: despawned #{id}"
                : $"bot: despawn #{id} rejected");
    }

    private void StartBotCommand(Func<Task<string>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (pendingBotCommand is { IsCompleted: false })
        {
            SetCommandFeedback("bot: another operator command is still running");
            return;
        }

        SetCommandFeedback("bot: processing operation");
        pendingBotCommand = Task.Run(operation);
    }

    private void PublishBotCommandCompletion()
    {
        if (pendingBotCommand is not { IsCompleted: true } completed)
            return;

        pendingBotCommand = null;
        try
        {
            SetCommandFeedback(completed.GetAwaiter().GetResult());
        }
        catch (Exception exception)
        {
            SetCommandFeedback($"bot: command failed: {exception.Message}");
        }
    }

    private void ShowBotSettings(RuntimeBotSnapshot bot)
    {
        if (botOperations is null)
            return;
        if (botSettingsWindow is not null)
            CloseBotSettings();

        var window = new BotSettingsWindow(bot, (RuntimePlayerSnapshot[])latestPrimaryPlayers.Clone(), botOperations);
        botSettingsWindow = window;
        window.CloseRequested += CloseBotSettings;
        Add(window);
        window.SetFocus();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void CloseBotSettings()
    {
        BotSettingsWindow? window = botSettingsWindow;
        if (window is null)
            return;
        botSettingsWindow = null;
        window.CloseRequested -= CloseBotSettings;
        Remove(window);
        window.Dispose();
        worldsText.SetFocus();
        SetNeedsDraw();
    }

    private static void WaitForPendingOperatorCommand(Task? command)
    {
        if (command is not { IsCompleted: false })
            return;
        try
        {
            _ = command.Wait(OperatorCommandShutdownTimeout);
        }
        catch (AggregateException)
        {
            // Completion/failure is projected into operator feedback while the view is alive.
        }
    }

    private void ExecuteSandboxOperationAsync(SandboxOperation operation)
    {
        if (sandboxOperations is null)
        {
            SetCommandFeedback("sandbox: player transfer operations are unavailable");
            return;
        }
        if (pendingSandboxCommand is { IsCompleted: false })
        {
            SetCommandFeedback("sandbox: another operator command is still running");
            return;
        }

        SetCommandFeedback(operation switch
        {
            SandboxOperation.Move move => $"sandbox: moving {move.PlayerSelector} to {move.Sandbox?.ToString() ?? "primary"}",
            SandboxOperation.MoveExact move => $"sandbox: moving #{move.Player.Slot.Value} to {move.Sandbox?.ToString() ?? "primary"}",
            _ => "sandbox: processing operation"
        });
        pendingSandboxCommand = Task.Run(() => sandboxOperations.Execute(operation));
    }

    private void PublishSandboxCommandCompletion()
    {
        if (pendingSandboxCommand is not { IsCompleted: true } completed)
            return;

        pendingSandboxCommand = null;
        try
        {
            SetCommandFeedback(completed.GetAwaiter().GetResult());
        }
        catch (Exception exception)
        {
            SetCommandFeedback($"sandbox: command failed: {exception.Message}");
        }
    }

    private void ObserveSandboxJobs(ReadOnlySpan<SandboxJobSnapshot> jobs)
    {
        if (jobs.Length == 0)
            return;

        string? latest = null;
        long latestId = 0;
        var retained = new HashSet<long>();
        foreach (SandboxJobSnapshot job in jobs)
        {
            if (job.Status is not (SandboxJobStatus.Completed or SandboxJobStatus.Failed or SandboxJobStatus.Canceled))
                continue;

            retained.Add(job.Id.Value);
            if (!observedTerminalSandboxJobs.Add(job.Id.Value) || job.Id.Value <= latestId)
                continue;
            latestId = job.Id.Value;
            latest = SandboxOperations.FormatJob(job);
        }

        observedTerminalSandboxJobs.IntersectWith(retained);
        if (latest is not null)
            SetCommandFeedback(latest);
    }

    private void ExecuteFeedCommand(string[] parts)
    {
        if (parts.Length == 1)
        {
            SetCommandFeedback("feed: logs off|debug|info|warn|error · chat on|off · all");
            return;
        }

        string action = parts[1].ToLowerInvariant();
        if (action == "all")
        {
            minimumLogLevel = OperationsLogLevel.Information;
            showChat = true;
            UpdateFeedControlText();
            RefreshFeedProjection();
            SetCommandFeedback("feed: logs INFO+ · chat ON");
            return;
        }

        if (parts.Length != 3)
        {
            SetCommandFeedback("usage: feed logs off|debug|info|warn|error | feed chat on|off");
            return;
        }

        switch (action)
        {
            case "logs":
                if (parts[2].Equals("off", StringComparison.OrdinalIgnoreCase) ||
                    parts[2].Equals("disable", StringComparison.OrdinalIgnoreCase))
                {
                    SetLogMode(null);
                    SetCommandFeedback("feed: logs OFF");
                    return;
                }

                if (parts[2].Equals("on", StringComparison.OrdinalIgnoreCase) ||
                    parts[2].Equals("enable", StringComparison.OrdinalIgnoreCase))
                {
                    SetLogMode(minimumLogLevel ?? OperationsLogLevel.Information);
                    SetCommandFeedback($"feed: logs {FormatLevelName(minimumLogLevel ?? OperationsLogLevel.Information)}+");
                    return;
                }

                if (!TryParseLogLevel(parts[2], out OperationsLogLevel logLevel))
                {
                    SetCommandFeedback("usage: feed logs off|debug|info|warn|error");
                    return;
                }
                SetLogMode(logLevel);
                SetCommandFeedback($"feed: logs {FormatLevelName(logLevel)}+");
                return;
            case "chat":
                if (!TryParseOnOff(parts[2], out bool chatEnabled))
                {
                    SetCommandFeedback("usage: feed chat on|off");
                    return;
                }
                SetChatVisibility(chatEnabled);
                SetCommandFeedback($"feed: chat {(chatEnabled ? "ON" : "OFF")}");
                return;
            case "level":
                // Backward-compatible alias from the previous dashboard revision. Selecting a level also enables logs.
                if (!TryParseLogLevel(parts[2], out OperationsLogLevel level))
                {
                    SetCommandFeedback("usage: feed level debug|info|warn|error");
                    return;
                }
                SetLogMode(level);
                SetCommandFeedback($"feed: logs {FormatLevelName(level)}+");
                return;
            default:
                SetCommandFeedback("usage: feed logs off|debug|info|warn|error | feed chat on|off");
                return;
        }
    }

    private void SetLogMode(OperationsLogLevel? level)
    {
        minimumLogLevel = level;
        UpdateFeedControlText();
        RefreshFeedProjection();
    }

    private void SetChatVisibility(bool enabled)
    {
        showChat = enabled;
        UpdateFeedControlText();
        RefreshFeedProjection();
    }

    private void ApplyLogFeedSelection(string? selection)
    {
        if (updatingFeedControls || string.IsNullOrWhiteSpace(selection))
            return;

        OperationsLogLevel? level = selection switch
        {
            "Logs OFF" => null,
            "Logs DEBUG+" => OperationsLogLevel.Debug,
            "Logs INFO+" => OperationsLogLevel.Information,
            "Logs WARN+" => OperationsLogLevel.Warning,
            "Logs ERROR+" => OperationsLogLevel.Error,
            _ => minimumLogLevel
        };
        SetLogMode(level);
    }

    private void ApplyChatFeedSelection(string? selection)
    {
        if (updatingFeedControls || string.IsNullOrWhiteSpace(selection))
            return;

        if (selection == "Chat ON")
            SetChatVisibility(true);
        else if (selection == "Chat OFF")
            SetChatVisibility(false);
    }

    private void UpdateFeedControlText()
    {
        updatingFeedControls = true;
        try
        {
            feedLogModeDropDown.Text = minimumLogLevel is OperationsLogLevel level
                ? $"Logs {FormatLevelName(level)}+"
                : "Logs OFF";
            feedChatDropDown.Text = $"Chat {(showChat ? "ON" : "OFF")}";
            feedLogModeDropDown.SetNeedsDraw();
            feedChatDropDown.SetNeedsDraw();
        }
        finally
        {
            updatingFeedControls = false;
        }
    }

    private void RefreshFeedProjection()
    {
        if (!hasFeedSnapshot)
            return;

        SetSelectableText(
            consoleText,
            RenderConsoleFeed(
                latestLogs.Entries.Span,
                latestChat.Entries.Span,
                minimumLogLevel,
                showChat),
            ref appliedConsoleText,
            ref pendingConsoleText,
            followTail: true);
    }

    private DashboardWorkspaceWindow? FindWorkspace()
    {
        View? current = this;
        while (current is not null)
        {
            if (current is DashboardWorkspaceWindow workspace)
                return workspace;
            current = current.SuperView;
        }
        return null;
    }

    private void SetCommandFeedback(string text)
    {
        commandFeedback.Text = text;
        commandFeedback.SetNeedsDraw();
        consoleFrame.SetNeedsDraw();
    }

    private static bool TryParseOnOff(string value, out bool enabled)
    {
        if (value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("enable", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            return true;
        }

        if (value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("disable", StringComparison.OrdinalIgnoreCase))
        {
            enabled = false;
            return true;
        }

        enabled = false;
        return false;
    }

    private static bool TryParseLogLevel(string value, out OperationsLogLevel level)
    {
        switch (value.ToLowerInvariant())
        {
            case "debug":
            case "dbg":
                level = OperationsLogLevel.Debug;
                return true;
            case "info":
            case "information":
                level = OperationsLogLevel.Information;
                return true;
            case "warn":
            case "warning":
                level = OperationsLogLevel.Warning;
                return true;
            case "err":
            case "error":
                level = OperationsLogLevel.Error;
                return true;
            default:
                level = default;
                return false;
        }
    }

    private static FrameView CreateFrame(string title, params View[] focusViews)
    {
        var frame = new FrameView
        {
            Title = title,
            CanFocus = true,
            SchemeName = BaseSchemeName
        };

        void UpdateFocusIndicator()
        {
            bool focused = frame.HasFocus || focusViews.Any(static view => view.HasFocus);
            frame.Title = focused ? ActiveTitlePrefix + title : title;
            frame.SchemeName = focused ? AccentSchemeName : BaseSchemeName;
            frame.SetNeedsDraw();
        }

        frame.HasFocusChanged += (_, _) => UpdateFocusIndicator();
        foreach (View focusView in focusViews)
            focusView.HasFocusChanged += (_, _) => UpdateFocusIndicator();
        return frame;
    }

    private static TextView CreateSelectableTextSurface(bool scrollBars)
    {
        var view = new TextView
        {
            ReadOnly = true,
            WordWrap = false,
            TabKeyAddsTab = false,
            EnterKeyAddsLine = false,
            SchemeName = BaseSchemeName
        };
        if (scrollBars)
            view.ViewportSettings = ViewportSettingsFlags.HasScrollBars;
        return view;
    }

    private static Label CreateLegend() => new()
    {
        SchemeName = BaseSchemeName
    };

    private static DropDownList CreateFeedDropDown(int x, int width, IEnumerable<string> items) => new CyclingDropDownList()
    {
        X = x,
        Y = 0,
        Width = width,
        CanFocus = true,
        ReadOnly = true,
        Source = new ListWrapper<string>(new ObservableCollection<string>(items.ToArray())),
        SchemeName = BaseSchemeName
    };

    private void AttachMaximize(FrameView frame)
    {
        frame.Initialized += (_, _) =>
        {
            if (frame.Border.View is not View borderView)
                return;

            borderView.MouseBindings.Add(MouseFlags.LeftButtonDoubleClicked, Command.Accept);
            borderView.Accepting += (_, args) =>
            {
                if (args.Context?.Binding is not MouseBinding { MouseEvent: { Position: { } position } } || position.Y != 0)
                    return;

                ToggleMaximize(frame);
                args.Handled = true;
            };
        };
    }

    private void ToggleMaximize(FrameView frame)
    {
        frame.SetFocus();
        if (ReferenceEquals(maximized, frame))
        {
            maximized = null;
            ApplyTiledLayout();
            return;
        }

        maximized = frame;
        foreach (FrameView candidate in EnumerateFrames())
            candidate.Visible = ReferenceEquals(candidate, frame);

        frame.X = 0;
        frame.Y = 0;
        frame.Width = Dim.Fill();
        frame.Height = Dim.Fill();
        frame.SetNeedsLayout();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private void ApplyTiledLayout()
    {
        foreach (FrameView frame in EnumerateFrames())
            frame.Visible = true;

        // Console is the primary operator surface. With the redundant Server tile gone it owns roughly two thirds
        // of the terminal; network telemetry and the world/player roster use the remaining side column.
        consoleFrame.X = 0;
        consoleFrame.Y = 0;
        consoleFrame.Width = Dim.Percent(64);
        consoleFrame.Height = Dim.Fill();

        networkFrame.X = Pos.Right(consoleFrame);
        networkFrame.Y = 0;
        networkFrame.Width = Dim.Fill();
        networkFrame.Height = GraphRowHeight;

        worldsFrame.X = Pos.Right(consoleFrame);
        worldsFrame.Y = Pos.Bottom(networkFrame);
        worldsFrame.Width = Dim.Fill();
        worldsFrame.Height = Dim.Fill();

        foreach (FrameView frame in EnumerateFrames())
            frame.SetNeedsLayout();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private FrameView GetFrame(string panelTitle) => panelTitle switch
    {
        "Console" => consoleFrame,
        "Network" => networkFrame,
        "Worlds" or "Worlds / Players" => worldsFrame,
        _ => throw new ArgumentOutOfRangeException(nameof(panelTitle))
    };

    private IEnumerable<FrameView> EnumerateFrames()
    {
        yield return consoleFrame;
        yield return networkFrame;
        yield return worldsFrame;
    }

    private void UpdateGraphs()
    {
        networkGraph.SetSamples(CaptureHistory());
    }

    private void AppendHistory(NetworkRates network)
    {
        history[historyNext] = new NetworkTrafficSample(
            SanitizeSample(network.InboundPacketsPerSecond),
            SanitizeSample(network.OutboundPacketsPerSecond));
        historyNext = (historyNext + 1) % history.Length;
        if (historyCount < history.Length)
            historyCount++;
    }

    private NetworkTrafficSample[] CaptureHistory()
    {
        var samples = new NetworkTrafficSample[historyCount];
        int oldest = (historyNext - historyCount + history.Length) % history.Length;
        for (int i = 0; i < samples.Length; i++)
            samples[i] = history[(oldest + i) % history.Length];
        return samples;
    }

    private NetworkRates CalculateNetworkRates(RuntimeNetworkSnapshot network)
    {
        if (!hasNetworkCounterSample)
        {
            SaveNetworkCounterSample(network);
            return default;
        }

        double elapsedSeconds = (network.CapturedAtUtc - lastNetworkCapturedAtUtc).TotalSeconds;
        bool validInterval = double.IsFinite(elapsedSeconds) && elapsedSeconds > 0d && elapsedSeconds <= 10d;
        bool countersMonotonic =
            network.MessageInboundFrames >= lastMessageInboundFrames &&
            network.MessageInboundBytes >= lastMessageInboundBytes &&
            network.MessageOutboundFrames >= lastMessageOutboundFrames &&
            network.MessageOutboundBytes >= lastMessageOutboundBytes;

        if (!validInterval || !countersMonotonic)
        {
            SaveNetworkCounterSample(network);
            return default;
        }

        long inboundFrames = network.MessageInboundFrames - lastMessageInboundFrames;
        long inboundBytes = network.MessageInboundBytes - lastMessageInboundBytes;
        long outboundFrames = network.MessageOutboundFrames - lastMessageOutboundFrames;
        long outboundBytes = network.MessageOutboundBytes - lastMessageOutboundBytes;
        SaveNetworkCounterSample(network);

        return new NetworkRates(
            inboundFrames / elapsedSeconds,
            outboundFrames / elapsedSeconds,
            inboundBytes / 1024d / elapsedSeconds,
            outboundBytes / 1024d / elapsedSeconds);
    }

    private void SaveNetworkCounterSample(RuntimeNetworkSnapshot network)
    {
        hasNetworkCounterSample = true;
        lastNetworkCapturedAtUtc = network.CapturedAtUtc;
        lastMessageInboundFrames = network.MessageInboundFrames;
        lastMessageInboundBytes = network.MessageInboundBytes;
        lastMessageOutboundFrames = network.MessageOutboundFrames;
        lastMessageOutboundBytes = network.MessageOutboundBytes;
    }

    private static string FormatByteRate(double kibPerSecond)
    {
        if (!double.IsFinite(kibPerSecond) || kibPerSecond <= 0d)
            return "0 KiB/s";
        if (kibPerSecond >= 1024d)
            return string.Create(CultureInfo.InvariantCulture, $"{kibPerSecond / 1024d:0.#} MiB/s");
        return string.Create(CultureInfo.InvariantCulture, $"{kibPerSecond:0.#} KiB/s");
    }

    private static void SetSelectableText(
        TextView view,
        string text,
        ref string appliedText,
        ref string pendingText,
        bool followTail = false)
    {
        if (string.Equals(appliedText, text, StringComparison.Ordinal))
        {
            pendingText = string.Empty;
            return;
        }

        pendingText = text;
        if (view.IsSelecting && view.SelectedLength > 0)
            return;

        bool wasAtTail = followTail && IsAtTail(view);
        Point previousViewport = view.Viewport.Location;

        view.Text = pendingText;
        appliedText = pendingText;
        pendingText = string.Empty;

        if (followTail)
        {
            if (wasAtTail)
                ScrollToTail(view);
            else
                view.ScrollTo(previousViewport);
        }

        view.SetNeedsDraw();
    }

    private static bool IsAtTail(TextView view)
    {
        if (view.Lines <= 1 || view.Viewport.Height <= 0)
            return true;

        return view.Viewport.Y + view.Viewport.Height >= view.Lines;
    }

    private static void ScrollToTail(TextView view) =>
        view.ScrollTo(new Point(0, Math.Max(0, view.Lines - 1)));

    private static string RenderConsoleFeed(
        ReadOnlySpan<RuntimeLogEntry> logs,
        ReadOnlySpan<RuntimeLogEntry> chat,
        OperationsLogLevel? minimumLogLevel,
        bool showChat)
    {
        var lines = new List<FeedEntry>(logs.Length + chat.Length);
        if (minimumLogLevel is OperationsLogLevel level)
        {
            for (int i = 0; i < logs.Length; i++)
            {
                RuntimeLogEntry entry = logs[i];
                if (entry.Level >= level)
                    lines.Add(new FeedEntry(entry, IsChat: false));
            }
        }

        if (showChat)
        {
            for (int i = 0; i < chat.Length; i++)
                lines.Add(new FeedEntry(chat[i], IsChat: true));
        }

        lines.Sort(static (left, right) =>
        {
            int timestamp = left.Entry.TimestampUtc.CompareTo(right.Entry.TimestampUtc);
            if (timestamp != 0)
                return timestamp;
            int sequence = left.Entry.Sequence.CompareTo(right.Entry.Sequence);
            if (sequence != 0)
                return sequence;
            return left.IsChat.CompareTo(right.IsChat);
        });

        int start = Math.Max(0, lines.Count - MaximumFeedEntries);
        var text = new StringBuilder(Math.Max(256, (lines.Count - start) * 96));
        if (lines.Count == 0)
        {
            text.Append(minimumLogLevel is not null || showChat
                ? "<no matching feed entries>"
                : "<feed disabled · enable Logs or Chat>");
            return text.ToString();
        }

        for (int i = start; i < lines.Count; i++)
        {
            if (i != start)
                text.AppendLine();
            FeedEntry line = lines[i];
            RuntimeLogEntry entry = line.Entry;
            text.Append(entry.TimestampUtc.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append(' ');
            if (line.IsChat)
            {
                text.Append("CHAT ").Append(Sanitize(entry.Message, 128));
            }
            else
            {
                text.Append(FormatLevel(entry.Level)).Append(' ')
                    .Append(Sanitize(entry.Source, 12)).Append(' ')
                    .Append(Sanitize(entry.Message, 120));
            }
        }

        return text.ToString();
    }

    private static (string[] Lines, SandboxWorldTreeRow[] Rows) RenderWorldTree(
        RuntimeDashboardSnapshot runtime,
        ReadOnlySpan<RuntimePlayerSnapshot> primaryPlayers,
        ReadOnlySpan<SandboxTreeWorldSnapshot> worlds,
        ReadOnlySpan<RuntimeBotSnapshot> bots)
    {
        var lines = new List<string>(Math.Max(2, primaryPlayers.Length + worlds.Length * 2));
        var rows = new List<SandboxWorldTreeRow>(Math.Max(2, primaryPlayers.Length + worlds.Length * 2));

        if (worlds.Length == 0)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"▼ {Sanitize(runtime.WorldName, 24)}  [primary]  TPS {runtime.ObservedTicksPerSecond:F1}/{runtime.TargetTicksPerSecond}"));
            rows.Add(new SandboxWorldTreeRow(SandboxWorldTreeRowKind.World, Target: null, PlayerSelector: null));
            AppendPrimaryRoster(lines, rows, primaryPlayers, bots);
            return (lines.ToArray(), rows.ToArray());
        }

        for (int worldIndex = 0; worldIndex < worlds.Length; worldIndex++)
        {
            SandboxTreeWorldSnapshot world = worlds[worldIndex];
            var line = new StringBuilder(80).Append("▼ ").Append(Sanitize(world.DisplayName, 22));
            if (world.IsPrimary)
                line.Append("  [primary]");
            else if (world.Runtime is WorldRuntimeSnapshot liveState)
            {
                line.Append("  [sandbox");
                if (liveState.Lifecycle != WorldRuntimeLifecycle.Running)
                    line.Append(" · ").Append(liveState.Lifecycle.ToString().ToLowerInvariant());
                line.Append(']');
            }
            else if (world.PendingJob is SandboxJobSnapshot pending)
                line.Append("  [sandbox · ").Append(pending.Status.ToString().ToLowerInvariant()).Append(']');
            else
                line.Append("  [sandbox]");

            if (world.Runtime is WorldRuntimeSnapshot live)
            {
                line.Append(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  TPS {live.ObservedTicksPerSecond:F1}/{live.TargetTicksPerSecond}"));
            }
            else
            {
                line.Append("  TPS --");
            }

            if (!world.IsPrimary && world.Sandbox is not null)
                line.Append("  [X]");
            lines.Add(line.ToString());
            rows.Add(new SandboxWorldTreeRow(SandboxWorldTreeRowKind.World, world.Sandbox, PlayerSelector: null));

            ReadOnlySpan<SandboxTreePlayerSnapshot> players = world.Players.Span;
            int botCount = world.IsPrimary ? bots.Length : 0;
            int rosterCount = players.Length + botCount;
            if (rosterCount == 0)
            {
                lines.Add("  └─ <no players>");
                rows.Add(new SandboxWorldTreeRow(SandboxWorldTreeRowKind.Placeholder, world.Sandbox, PlayerSelector: null));
                continue;
            }

            int rosterIndex = 0;
            for (int playerIndex = 0; playerIndex < players.Length; playerIndex++, rosterIndex++)
            {
                SandboxTreePlayerSnapshot player = players[playerIndex];
                var playerLine = new StringBuilder(48)
                    .Append(rosterIndex == rosterCount - 1 ? "  └─ " : "  ├─ ")
                    .Append('#').Append(player.Player.Slot).Append(' ')
                    .Append(Sanitize(player.Player.Name, 28));
                if (!player.IsPlaying)
                    playerLine.Append("  [joining]");
                playerLine.Append("  [X]");
                lines.Add(playerLine.ToString());
                rows.Add(new SandboxWorldTreeRow(SandboxWorldTreeRowKind.Player, world.Sandbox, player.Selector, player.Player));
            }
            if (world.IsPrimary)
            {
                for (int botIndex = 0; botIndex < bots.Length; botIndex++, rosterIndex++)
                    AppendBot(lines, rows, bots[botIndex], rosterIndex == rosterCount - 1);
            }
        }

        return (lines.ToArray(), rows.ToArray());
    }

    private static void AppendPrimaryRoster(
        List<string> lines,
        List<SandboxWorldTreeRow> rows,
        ReadOnlySpan<RuntimePlayerSnapshot> players,
        ReadOnlySpan<RuntimeBotSnapshot> bots)
    {
        int count = players.Length + bots.Length;
        if (count == 0)
        {
            lines.Add("  └─ <no players>");
            rows.Add(new SandboxWorldTreeRow(SandboxWorldTreeRowKind.Placeholder, Target: null, PlayerSelector: null));
            return;
        }

        int rosterIndex = 0;
        for (int i = 0; i < players.Length; i++, rosterIndex++)
        {
            RuntimePlayerSnapshot player = players[i];
            lines.Add($"{(rosterIndex == count - 1 ? "  └─ " : "  ├─ ")}#{player.Slot} {Sanitize(player.Name, 28)}  [X]");
            rows.Add(new SandboxWorldTreeRow(
                SandboxWorldTreeRowKind.Player,
                Target: null,
                PlayerSelector: $"#{player.Slot}",
                Player: player));
        }
        for (int i = 0; i < bots.Length; i++, rosterIndex++)
            AppendBot(lines, rows, bots[i], rosterIndex == count - 1);
    }

    private static void AppendBot(
        List<string> lines,
        List<SandboxWorldTreeRow> rows,
        RuntimeBotSnapshot bot,
        bool isLast)
    {
        var line = new StringBuilder(80)
            .Append(isLast ? "  └─ " : "  ├─ ")
            .Append(bot.Configuration.Body == RuntimeBotBodyKind.Npc
                ? $"[NpcBot #{bot.Configuration.NpcType.Value}] "
                : "[PlayerBot] ")
            .Append(Sanitize(bot.Name, 22))
            .Append("  [").Append(bot.Configuration.Mode.ToString().ToLowerInvariant());
        if (bot.Configuration.Target.IsAssigned)
            line.Append(" -> ").Append(Sanitize(bot.Configuration.Target.DisplayName, 18));
        if (bot.PvpEnabled)
            line.Append(" · pvp");
        if (bot.IsStuck)
            line.Append(" · stuck");
        line.Append("]  [X]");
        lines.Add(line.ToString());
        rows.Add(new SandboxWorldTreeRow(
            SandboxWorldTreeRowKind.Bot,
            Target: null,
            PlayerSelector: null,
            Player: null,
            Bot: bot));
    }

    private static double SanitizeSample(double value) =>
        double.IsFinite(value) && value >= 0d ? value : 0d;

    private static string Sanitize(string value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        int length = Math.Min(value.Length, maximumLength);
        char[] buffer = new char[length];
        for (int i = 0; i < length; i++)
            buffer[i] = char.IsControl(value[i]) ? ' ' : value[i];
        return new string(buffer);
    }

    private static string FormatLevel(OperationsLogLevel level) => level switch
    {
        OperationsLogLevel.Debug => "DBG ",
        OperationsLogLevel.Information => "INFO",
        OperationsLogLevel.Warning => "WARN",
        OperationsLogLevel.Error => "ERR ",
        _ => "?   "
    };

    private static string FormatLevelName(OperationsLogLevel level) => level switch
    {
        OperationsLogLevel.Debug => "DEBUG",
        OperationsLogLevel.Information => "INFO",
        OperationsLogLevel.Warning => "WARN",
        OperationsLogLevel.Error => "ERROR",
        _ => "?"
    };

    private readonly record struct FeedEntry(RuntimeLogEntry Entry, bool IsChat);

    private readonly record struct NetworkRates(
        double InboundPacketsPerSecond,
        double OutboundPacketsPerSecond,
        double InboundKiBPerSecond,
        double OutboundKiBPerSecond);
}

#pragma warning restore CS0618
