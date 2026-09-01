using System.Drawing;
using System.Globalization;
using System.Text;
using TerraRuntime.Operations;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;
using TuiColor = Terminal.Gui.Drawing.Color;

#pragma warning disable CS0618 // Terminal.Gui TextView is still the built-in selectable read-only surface in 2.4.17.

namespace TerraRuntime.TerminalUI;

/// <summary>
/// Runtime-owned tiled dashboard. Runtime snapshots arrive already detached from authoritative state; this view
/// only formats them, maintains bounded UI-local history and handles presentation-only interaction.
/// </summary>
internal sealed class RuntimeOverviewDashboard : View
{
    private const int HistoryLength = 60;
    private const int MaximumFeedEntries = 64;
    private const int GraphRowHeight = 8;
    private const string ActiveTitlePrefix = "▶ ";
    private const string BaseSchemeName = "Base";
    private const string AccentSchemeName = "Accent";

    private readonly FrameView consoleFrame;
    private readonly FrameView tpsFrame;
    private readonly FrameView networkFrame;
    private readonly FrameView worldsFrame;
    private readonly FrameView commandFrame;
    private readonly TextView consoleText;
    private readonly TextView worldsText;
    private readonly Label tpsLegend;
    private readonly Label networkLegend;
    private readonly Label commandFeedback;
    private readonly Label worldsHint;
    private readonly Label feedLogModeToggle;
    private readonly Label feedChatToggle;
    private readonly TextField commandInput;
    private readonly GraphView tpsGraph;
    private readonly GraphView networkGraph;
    private readonly SandboxOperations? sandboxOperations;
    private readonly PathAnnotation tpsTargetPath = new()
    {
        LineColor = new TuiAttribute(TuiColor.Gray, TuiColor.Black)
    };
    private readonly PathAnnotation tpsPath = new()
    {
        LineColor = new TuiAttribute(TuiColor.BrightGreen, TuiColor.Black)
    };
    private readonly PathAnnotation inboundPath = new()
    {
        LineColor = new TuiAttribute(TuiColor.BrightCyan, TuiColor.Black)
    };
    private readonly PathAnnotation outboundPath = new()
    {
        LineColor = new TuiAttribute(TuiColor.BrightYellow, TuiColor.Black)
    };
    private readonly MetricSample[] history = new MetricSample[HistoryLength];
    private int historyCount;
    private int historyNext;
    private FrameView? maximized;
    private string appliedConsoleText = string.Empty;
    private string pendingConsoleText = string.Empty;
    private string appliedWorldsText = string.Empty;
    private string pendingWorldsText = string.Empty;
    private RuntimeLogLevel? minimumLogLevel = RuntimeLogLevel.Information;
    private bool showChat = true;
    private RuntimeDashboardSnapshot latestRuntime;
    private RuntimeLogSnapshot latestLogs;
    private RuntimeLogSnapshot latestChat;
    private int latestPlayerCount;
    private bool hasFeedSnapshot;
    private bool hasNetworkCounterSample;
    private DateTimeOffset lastNetworkCapturedAtUtc;
    private long lastMessageInboundFrames;
    private long lastMessageInboundBytes;
    private long lastMessageOutboundFrames;
    private long lastMessageOutboundBytes;

    public RuntimeOverviewDashboard(SandboxOperations? sandboxOperations = null)
    {
        this.sandboxOperations = sandboxOperations;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        consoleText = CreateSelectableTextSurface(scrollBars: true);
        worldsText = CreateSelectableTextSurface(scrollBars: true);
        tpsGraph = CreateGraph();
        networkGraph = CreateGraph();
        tpsLegend = CreateLegend();
        networkLegend = CreateLegend();

        feedLogModeToggle = CreateFeedControl(1, 17);
        feedChatToggle = CreateFeedControl(20, 12);
        BindFeedControl(feedLogModeToggle, CycleLogMode);
        BindFeedControl(feedChatToggle, () => SetChatVisibility(!showChat));
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

        worldsHint = new Label
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(1),
            Text = "single runtime · drag transfer waits for multi-world ingress",
            SchemeName = BaseSchemeName
        };

        consoleFrame = CreateFrame("Console", consoleText, commandInput, feedLogModeToggle, feedChatToggle);
        tpsFrame = CreateFrame("TPS", tpsGraph);
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
        consoleFrame.Add(feedLogModeToggle, feedChatToggle, consoleText, commandFeedback, commandFrame);

        tpsLegend.X = 1;
        tpsLegend.Y = 0;
        tpsLegend.Width = Dim.Fill(1);
        tpsGraph.X = 0;
        tpsGraph.Y = 1;
        tpsGraph.Width = Dim.Fill();
        tpsGraph.Height = Dim.Fill();
        tpsFrame.Add(tpsLegend, tpsGraph);

        networkLegend.X = 1;
        networkLegend.Y = 0;
        networkLegend.Width = Dim.Fill(1);
        networkGraph.X = 0;
        networkGraph.Y = 1;
        networkGraph.Width = Dim.Fill();
        networkGraph.Height = Dim.Fill();
        networkFrame.Add(networkLegend, networkGraph);

        worldsText.X = 0;
        worldsText.Y = 0;
        worldsText.Width = Dim.Fill();
        worldsText.Height = Dim.Fill(1);
        worldsFrame.Add(worldsText, worldsHint);

        AttachMaximize(consoleFrame);
        AttachMaximize(tpsFrame);
        AttachMaximize(networkFrame);
        AttachMaximize(worldsFrame);

        ConfigureTpsGraph();
        ConfigureNetworkGraph();

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

        Add(consoleFrame, tpsFrame, networkFrame, worldsFrame);
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
        AppendHistory(runtime, networkRates);

        ReadOnlySpan<RuntimePlayerSnapshot> players = playersSnapshot.Players.Span;
        latestRuntime = runtime;
        latestPlayerCount = players.Length;
        latestLogs = logs;
        latestChat = chat;
        hasFeedSnapshot = true;

        RefreshFeedProjection();
        SetSelectableText(
            worldsText,
            RenderWorldTree(runtime, players),
            ref appliedWorldsText,
            ref pendingWorldsText);

        tpsLegend.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"TPS {runtime.ObservedTicksPerSecond:F1} / {runtime.TargetTicksPerSecond}");
        networkLegend.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"IN {networkRates.InboundPacketsPerSecond:F1}p/s {networkRates.InboundKiBPerSecond:F1}K  " +
            $"OUT {networkRates.OutboundPacketsPerSecond:F1}p/s {networkRates.OutboundKiBPerSecond:F1}K");

        if (FindWorkspace() is { } workspace)
        {
            workspace.Title = runtime.Port > 0
                ? $"{RuntimeProductInfo.DisplayName} :{runtime.Port} - System Dashboard"
                : $"{RuntimeProductInfo.DisplayName} - System Dashboard";
        }

        if (!string.IsNullOrWhiteSpace(status))
            SetCommandFeedback(status);

        UpdateGraphs(runtime.TargetTicksPerSecond);
        SetNeedsDraw();
    }

    internal void FocusCommandInput() => commandInput.SetFocus();

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

    internal string GetTpsLegendForSmoke() => tpsLegend.Text?.ToString() ?? string.Empty;

    internal string GetNetworkLegendForSmoke() => networkLegend.Text?.ToString() ?? string.Empty;

    internal string GetFeedControlsForSmoke() =>
        $"{feedLogModeToggle.Text} | {feedChatToggle.Text}";

    internal string GetConsoleTextForSmoke() => consoleText.Text?.ToString() ?? string.Empty;

    internal string GetWorldsTextForSmoke() => worldsText.Text?.ToString() ?? string.Empty;

    internal PointF GetGraphCellSizeForSmoke(string panelTitle) => panelTitle switch
    {
        "TPS" => tpsGraph.CellSize,
        "Network" => networkGraph.CellSize,
        _ => throw new ArgumentOutOfRangeException(nameof(panelTitle))
    };

    internal void SetFeedForSmoke(bool logs, bool chat, RuntimeLogLevel minimumLevel)
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
                SetCommandFeedback("help | feed | save | interest | sandbox list|status|create|regen|destroy|jobs|job|cancel | system | players | npcs | projectiles | items | network | world | logs");
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
            case "sandbox":
                SetCommandFeedback(sandboxOperations?.Execute(input) ?? "sandbox: operations unavailable");
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
            minimumLogLevel = RuntimeLogLevel.Information;
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
                    SetLogMode(minimumLogLevel ?? RuntimeLogLevel.Information);
                    SetCommandFeedback($"feed: logs {FormatLevelName(minimumLogLevel ?? RuntimeLogLevel.Information)}+");
                    return;
                }

                if (!TryParseLogLevel(parts[2], out RuntimeLogLevel logLevel))
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
                if (!TryParseLogLevel(parts[2], out RuntimeLogLevel level))
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

    private void SetLogMode(RuntimeLogLevel? level)
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

    private void CycleLogMode()
    {
        minimumLogLevel = minimumLogLevel switch
        {
            null => RuntimeLogLevel.Debug,
            RuntimeLogLevel.Debug => RuntimeLogLevel.Information,
            RuntimeLogLevel.Information => RuntimeLogLevel.Warning,
            RuntimeLogLevel.Warning => RuntimeLogLevel.Error,
            RuntimeLogLevel.Error => null,
            _ => RuntimeLogLevel.Information
        };
        UpdateFeedControlText();
        RefreshFeedProjection();
    }

    private void UpdateFeedControlText()
    {
        feedLogModeToggle.Text = minimumLogLevel is RuntimeLogLevel level
            ? $"Logs {FormatLevelName(level)}+"
            : "Logs OFF";
        feedChatToggle.Text = $"Chat {(showChat ? "ON" : "OFF")}";
        feedLogModeToggle.SetNeedsDraw();
        feedChatToggle.SetNeedsDraw();
    }

    private void RefreshFeedProjection()
    {
        if (!hasFeedSnapshot)
            return;

        SetSelectableText(
            consoleText,
            RenderConsoleFeed(
                latestRuntime,
                latestPlayerCount,
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

    private static bool TryParseLogLevel(string value, out RuntimeLogLevel level)
    {
        switch (value.ToLowerInvariant())
        {
            case "debug":
            case "dbg":
                level = RuntimeLogLevel.Debug;
                return true;
            case "info":
            case "information":
                level = RuntimeLogLevel.Information;
                return true;
            case "warn":
            case "warning":
                level = RuntimeLogLevel.Warning;
                return true;
            case "err":
            case "error":
                level = RuntimeLogLevel.Error;
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

    private static Label CreateFeedControl(int x, int width) => new()
    {
        X = x,
        Y = 0,
        Width = width,
        CanFocus = true,
        SchemeName = BaseSchemeName
    };

    private static void BindFeedControl(Label label, Action action)
    {
        label.MouseBindings.Add(MouseFlags.LeftButtonPressed, Command.Accept);
        label.Accepting += (_, args) =>
        {
            action();
            args.Handled = true;
        };
    }

    private static GraphView CreateGraph() => new()
    {
        SchemeName = BaseSchemeName,
        GraphColor = new TuiAttribute(TuiColor.BrightGreen, TuiColor.Black)
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
        // of the terminal; the compact graph row and world/player roster use the remaining side column.
        consoleFrame.X = 0;
        consoleFrame.Y = 0;
        consoleFrame.Width = Dim.Percent(64);
        consoleFrame.Height = Dim.Fill();

        tpsFrame.X = Pos.Right(consoleFrame);
        tpsFrame.Y = 0;
        tpsFrame.Width = Dim.Percent(18);
        tpsFrame.Height = GraphRowHeight;

        networkFrame.X = Pos.Right(tpsFrame);
        networkFrame.Y = 0;
        networkFrame.Width = Dim.Fill();
        networkFrame.Height = GraphRowHeight;

        worldsFrame.X = Pos.Right(consoleFrame);
        worldsFrame.Y = Pos.Bottom(tpsFrame);
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
        "TPS" => tpsFrame,
        "Network" => networkFrame,
        "Worlds" or "Worlds / Players" => worldsFrame,
        _ => throw new ArgumentOutOfRangeException(nameof(panelTitle))
    };

    private IEnumerable<FrameView> EnumerateFrames()
    {
        yield return consoleFrame;
        yield return tpsFrame;
        yield return networkFrame;
        yield return worldsFrame;
    }

    private void ConfigureTpsGraph()
    {
        tpsGraph.Annotations.Add(tpsTargetPath);
        tpsGraph.Annotations.Add(tpsPath);
        tpsGraph.AxisX.Visible = false;
        tpsGraph.AxisY.Minimum = 0;
        tpsGraph.AxisY.Increment = 30;
        tpsGraph.AxisY.ShowLabelsEvery = 1;
        tpsGraph.AxisY.LabelGetter = value => value.Value.ToString("N0", CultureInfo.InvariantCulture);
        tpsGraph.MarginLeft = 4;
        tpsGraph.MarginBottom = 0;
    }

    private void ConfigureNetworkGraph()
    {
        networkGraph.Annotations.Add(inboundPath);
        networkGraph.Annotations.Add(outboundPath);
        networkGraph.AxisX.Visible = false;
        networkGraph.AxisY.Minimum = 0;
        networkGraph.AxisY.Increment = 1;
        networkGraph.AxisY.ShowLabelsEvery = 1;
        networkGraph.AxisY.LabelGetter = value => value.Value.ToString("N0", CultureInfo.InvariantCulture);
        networkGraph.MarginLeft = 5;
        networkGraph.MarginBottom = 0;
    }

    private void UpdateGraphs(int targetTicksPerSecond)
    {
        MetricSample[] samples = CaptureHistory();
        if (samples.Length == 0)
            return;

        tpsTargetPath.Points = samples.Select((_, index) => new PointF(index, targetTicksPerSecond)).ToList();
        tpsPath.Points = Points(samples, static sample => (float)sample.TicksPerSecond);
        float tpsMaximum = Math.Max(1f, targetTicksPerSecond * 1.05f);
        tpsGraph.AxisY.Increment = Math.Max(1f, targetTicksPerSecond / 2f);
        FitGraph(tpsGraph, samples.Length, tpsMaximum);

        inboundPath.Points = Points(samples, static sample => (float)sample.InboundPacketsPerSecond);
        outboundPath.Points = Points(samples, static sample => (float)sample.OutboundPacketsPerSecond);
        float networkMaximum = (float)Math.Max(
            1d,
            samples.Max(static sample => Math.Max(sample.InboundPacketsPerSecond, sample.OutboundPacketsPerSecond)) * 1.15d);
        networkGraph.AxisY.Increment = NiceIncrement(networkMaximum / 3f);
        FitGraph(networkGraph, samples.Length, networkMaximum);
    }

    private void AppendHistory(RuntimeDashboardSnapshot runtime, NetworkRates network)
    {
        history[historyNext] = new MetricSample(
            SanitizeSample(runtime.ObservedTicksPerSecond),
            SanitizeSample(network.InboundPacketsPerSecond),
            SanitizeSample(network.OutboundPacketsPerSecond));
        historyNext = (historyNext + 1) % history.Length;
        if (historyCount < history.Length)
            historyCount++;
    }

    private MetricSample[] CaptureHistory()
    {
        var samples = new MetricSample[historyCount];
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

    private static void FitGraph(GraphView graph, int sampleCount, float yMaximum)
    {
        if (graph.Viewport.Width <= graph.MarginLeft || graph.Viewport.Height <= graph.MarginBottom)
            return;

        int width = Math.Max(1, graph.Viewport.Width - (int)graph.MarginLeft);
        int height = Math.Max(1, graph.Viewport.Height - (int)graph.MarginBottom);

        // GraphView CellSize is data-units per terminal cell. Capping X at 1 made a 60-sample history occupy only
        // ~60 columns when a graph tile was maximized to a 120-160 column viewport. Allow sub-unit X scaling so the
        // bounded history expands across the full available width instead of remaining stuck at its tiled size.
        float horizontalCellSize = sampleCount <= 1
            ? 1f
            : Math.Max(0.01f, (sampleCount - 1f) / width);
        graph.CellSize = new PointF(
            horizontalCellSize,
            Math.Max(0.1f, yMaximum / height));
        graph.ScrollOffset = PointF.Empty;
        graph.SetNeedsDraw();
    }

    private static List<PointF> Points(
        IReadOnlyList<MetricSample> samples,
        Func<MetricSample, float> selector) =>
        samples.Select((sample, index) => new PointF(index, selector(sample))).ToList();

    private static float NiceIncrement(float value)
    {
        if (value <= 1f)
            return 1f;

        double power = Math.Pow(10d, Math.Floor(Math.Log10(value)));
        double normalized = value / power;
        double nice = normalized <= 1d ? 1d : normalized <= 2d ? 2d : normalized <= 5d ? 5d : 10d;
        return (float)(nice * power);
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
        RuntimeDashboardSnapshot runtime,
        int playerCount,
        ReadOnlySpan<RuntimeLogEntry> logs,
        ReadOnlySpan<RuntimeLogEntry> chat,
        RuntimeLogLevel? minimumLogLevel,
        bool showChat)
    {
        var lines = new List<FeedEntry>(logs.Length + chat.Length);
        if (minimumLogLevel is RuntimeLogLevel level)
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
        text.Append(
            string.Create(
                CultureInfo.CurrentCulture,
                $"Tick #{runtime.Tick:N0}  |  {runtime.Lifecycle}  |  World {Sanitize(runtime.WorldName, 24)}  |  Players {playerCount}/{runtime.MaxPlayers}"));
        text.AppendLine();
        text.AppendLine();

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

    private static string RenderWorldTree(
        RuntimeDashboardSnapshot runtime,
        ReadOnlySpan<RuntimePlayerSnapshot> players)
    {
        var text = new StringBuilder(Math.Max(96, players.Length * 40));
        text.Append("▼ ").Append(Sanitize(runtime.WorldName, 28)).Append("  [primary]").AppendLine();
        if (players.Length == 0)
        {
            text.Append("  └─ <no players>");
            return text.ToString();
        }

        for (int i = 0; i < players.Length; i++)
        {
            RuntimePlayerSnapshot player = players[i];
            text.Append(i == players.Length - 1 ? "  └─ " : "  ├─ ")
                .Append('#').Append(player.Slot).Append(' ')
                .Append(Sanitize(player.Name, 28));
            if (i != players.Length - 1)
                text.AppendLine();
        }
        return text.ToString();
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

    private static string FormatLevel(RuntimeLogLevel level) => level switch
    {
        RuntimeLogLevel.Debug => "DBG ",
        RuntimeLogLevel.Information => "INFO",
        RuntimeLogLevel.Warning => "WARN",
        RuntimeLogLevel.Error => "ERR ",
        _ => "?   "
    };

    private static string FormatLevelName(RuntimeLogLevel level) => level switch
    {
        RuntimeLogLevel.Debug => "DEBUG",
        RuntimeLogLevel.Information => "INFO",
        RuntimeLogLevel.Warning => "WARN",
        RuntimeLogLevel.Error => "ERROR",
        _ => "?"
    };

    private readonly record struct FeedEntry(RuntimeLogEntry Entry, bool IsChat);

    private readonly record struct MetricSample(
        double TicksPerSecond,
        double InboundPacketsPerSecond,
        double OutboundPacketsPerSecond);

    private readonly record struct NetworkRates(
        double InboundPacketsPerSecond,
        double OutboundPacketsPerSecond,
        double InboundKiBPerSecond,
        double OutboundKiBPerSecond);
}

#pragma warning restore CS0618
