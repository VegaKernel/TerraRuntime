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
    private const int ServerPanelHeight = 5;
    private const int GraphRowHeight = 7;
    private const string ActiveTitlePrefix = "▶ ";
    private const string BaseSchemeName = "Base";
    private const string AccentSchemeName = "Accent";

    private readonly FrameView consoleFrame;
    private readonly FrameView serverFrame;
    private readonly FrameView tpsFrame;
    private readonly FrameView networkFrame;
    private readonly FrameView chatFrame;
    private readonly FrameView commandFrame;
    private readonly TextView consoleText;
    private readonly TextView serverText;
    private readonly TextView chatText;
    private readonly Label tpsLegend;
    private readonly Label networkLegend;
    private readonly Label commandFeedback;
    private readonly TextField commandInput;
    private readonly GraphView tpsGraph;
    private readonly GraphView networkGraph;
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
    private string appliedServerText = string.Empty;
    private string pendingServerText = string.Empty;
    private string appliedChatText = string.Empty;
    private string pendingChatText = string.Empty;
    private bool hasNetworkCounterSample;
    private DateTimeOffset lastNetworkCapturedAtUtc;
    private long lastMessageInboundFrames;
    private long lastMessageInboundBytes;
    private long lastMessageOutboundFrames;
    private long lastMessageOutboundBytes;

    public RuntimeOverviewDashboard()
    {
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        consoleText = CreateSelectableTextSurface(scrollBars: true);
        serverText = CreateSelectableTextSurface(scrollBars: false);
        chatText = CreateSelectableTextSurface(scrollBars: true);
        tpsGraph = CreateGraph();
        networkGraph = CreateGraph();
        tpsLegend = CreateLegend();
        networkLegend = CreateLegend();
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

        consoleFrame = CreateFrame("Console", consoleText, commandInput);
        serverFrame = CreateFrame("Server", serverText);
        tpsFrame = CreateFrame("TPS", tpsGraph);
        networkFrame = CreateFrame("Network", networkGraph);
        chatFrame = CreateFrame("Chat", chatText);
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
        consoleText.Y = 0;
        consoleText.Width = Dim.Fill();
        consoleText.Height = Dim.Fill(4);
        commandFrame.Add(commandPrompt, commandInput);
        consoleFrame.Add(consoleText, commandFeedback, commandFrame);

        serverText.X = 0;
        serverText.Y = 0;
        serverText.Width = Dim.Fill();
        serverText.Height = Dim.Fill();
        serverFrame.Add(serverText);

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

        chatText.X = 0;
        chatText.Y = 0;
        chatText.Width = Dim.Fill();
        chatText.Height = Dim.Fill();
        chatFrame.Add(chatText);

        AttachMaximize(consoleFrame);
        AttachMaximize(serverFrame);
        AttachMaximize(tpsFrame);
        AttachMaximize(networkFrame);
        AttachMaximize(chatFrame);

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

        Add(consoleFrame, serverFrame, tpsFrame, networkFrame, chatFrame);
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
        NetworkRates networkRates = CalculateNetworkRates(network);
        AppendHistory(runtime, networkRates);

        ReadOnlySpan<RuntimePlayerSnapshot> players = playersSnapshot.Players.Span;
        SetSelectableText(
            serverText,
            RenderServer(runtime, network, world, players.Length),
            ref appliedServerText,
            ref pendingServerText);
        SetSelectableText(
            consoleText,
            RenderConsole(runtime, players.Length, logs.Entries.Span),
            ref appliedConsoleText,
            ref pendingConsoleText,
            followTail: true);
        SetSelectableText(
            chatText,
            RenderLogs(chat.Entries.Span, maximumEntries: 64, emptyText: "<no chat yet>", includeLevelAndSource: false),
            ref appliedChatText,
            ref pendingChatText,
            followTail: true);

        tpsLegend.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"TPS {runtime.ObservedTicksPerSecond:F1} / {runtime.TargetTicksPerSecond}");
        networkLegend.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"IN {networkRates.InboundPacketsPerSecond:F1}p/s {networkRates.InboundKiBPerSecond:F1}K  " +
            $"OUT {networkRates.OutboundPacketsPerSecond:F1}p/s {networkRates.OutboundKiBPerSecond:F1}K");

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

    internal bool ConsoleSupportsSelectionForSmoke => consoleText.ReadOnly && consoleText.CanFocus;

    internal bool CommandInputVisibleForSmoke => commandInput.Visible;

    internal bool CommandInputHasFocusForSmoke => commandInput.HasFocus;

    internal bool DashboardCanFocusForSmoke => CanFocus && (SuperView?.CanFocus ?? true);

    internal int ConsoleViewportYForSmoke => consoleText.Viewport.Y;

    internal int ChatViewportYForSmoke => chatText.Viewport.Y;

    internal int ConsoleLinesForSmoke => consoleText.Lines;

    internal int ChatLinesForSmoke => chatText.Lines;

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
                SetCommandFeedback("help | save | interest on|off | system | players | npcs | projectiles | items | network | world | logs");
                return;
            case "clear":
                SetCommandFeedback(string.Empty);
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

        consoleFrame.X = 0;
        consoleFrame.Y = 0;
        consoleFrame.Width = Dim.Percent(52);
        consoleFrame.Height = Dim.Fill();

        serverFrame.X = Pos.Right(consoleFrame);
        serverFrame.Y = 0;
        serverFrame.Width = Dim.Fill();
        serverFrame.Height = ServerPanelHeight;

        tpsFrame.X = Pos.Right(consoleFrame);
        tpsFrame.Y = Pos.Bottom(serverFrame);
        tpsFrame.Width = Dim.Percent(24);
        tpsFrame.Height = GraphRowHeight;

        networkFrame.X = Pos.Right(tpsFrame);
        networkFrame.Y = Pos.Bottom(serverFrame);
        networkFrame.Width = Dim.Fill();
        networkFrame.Height = GraphRowHeight;

        chatFrame.X = Pos.Right(consoleFrame);
        chatFrame.Y = Pos.Bottom(tpsFrame);
        chatFrame.Width = Dim.Fill();
        chatFrame.Height = Dim.Fill();

        foreach (FrameView frame in EnumerateFrames())
            frame.SetNeedsLayout();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private FrameView GetFrame(string panelTitle) => panelTitle switch
    {
        "Console" => consoleFrame,
        "Server" => serverFrame,
        "TPS" => tpsFrame,
        "Network" => networkFrame,
        "Chat" => chatFrame,
        _ => throw new ArgumentOutOfRangeException(nameof(panelTitle))
    };

    private IEnumerable<FrameView> EnumerateFrames()
    {
        yield return consoleFrame;
        yield return serverFrame;
        yield return tpsFrame;
        yield return networkFrame;
        yield return chatFrame;
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
        graph.CellSize = new PointF(
            Math.Max(1f, (sampleCount - 1f) / width),
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

    private static string RenderConsole(
        RuntimeDashboardSnapshot runtime,
        int playerCount,
        ReadOnlySpan<RuntimeLogEntry> entries)
    {
        string summary = string.Create(
            CultureInfo.CurrentCulture,
            $"Tick #{runtime.Tick:N0}  |  {runtime.Lifecycle}  |  World {Sanitize(runtime.WorldName, 24)}  |  Players {playerCount}/{runtime.MaxPlayers}\n\n");
        return summary + RenderLogs(entries, maximumEntries: 64, emptyText: "<no runtime log entries>");
    }

    private static string RenderServer(
        RuntimeDashboardSnapshot runtime,
        RuntimeNetworkSnapshot network,
        RuntimeWorldSnapshot world,
        int playerCount)
    {
        return new StringBuilder(384)
            .Append(runtime.Lifecycle).Append(" | ")
            .Append(Sanitize(runtime.WorldName, 28)).Append(' ')
            .Append(runtime.WorldWidthTiles).Append('x').Append(runtime.WorldHeightTiles).AppendLine()
            .Append("players ").Append(playerCount).Append('/').Append(runtime.MaxPlayers)
            .Append(" | port ").Append(runtime.Port)
            .Append(" | interest ").Append(runtime.InterestManagementEnabled ? "ON" : "OFF").AppendLine()
            .Append("connections ").Append(runtime.ActiveConnections)
            .Append(" | accepted ").Append(runtime.AcceptedConnections)
            .Append(" | rejected ").Append(runtime.RejectedConnections)
            .Append(" | slow ").Append(network.SlowClients).AppendLine()
            .Append("cache ").Append(world.RuntimeCacheHit ? "hit" : "miss")
            .ToString();
    }

    private static string RenderLogs(
        ReadOnlySpan<RuntimeLogEntry> entries,
        int maximumEntries,
        string emptyText,
        bool includeLevelAndSource = true)
    {
        if (entries.Length == 0)
            return emptyText;

        int take = Math.Min(maximumEntries, entries.Length);
        int start = entries.Length - take;
        var text = new StringBuilder(take * 96);
        for (int i = start; i < entries.Length; i++)
        {
            RuntimeLogEntry entry = entries[i];
            if (text.Length != 0)
                text.AppendLine();
            text.Append(entry.TimestampUtc.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append(' ');
            if (includeLevelAndSource)
            {
                text.Append(FormatLevel(entry.Level)).Append(' ')
                    .Append(Sanitize(entry.Source, 12)).Append(' ');
            }
            text.Append(Sanitize(entry.Message, 120));
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
