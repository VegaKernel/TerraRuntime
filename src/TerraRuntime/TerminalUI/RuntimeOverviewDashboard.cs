using System.Globalization;
using System.Text;
using TerraRuntime.Operations;
using Terminal.Gui.Configuration;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.TerminalUI;

/// <summary>
/// Vega-style tiled overview for the built-in dashboard. It consumes only detached operations snapshots;
/// double-clicking any tile toggles a full-workspace view without changing authoritative runtime state.
/// </summary>
internal sealed class RuntimeOverviewDashboard : View
{
    private const int HistoryLength = 48;
    private const string ActiveTitlePrefix = "▶ ";

    private readonly FrameView consoleFrame;
    private readonly FrameView serverFrame;
    private readonly FrameView performanceFrame;
    private readonly FrameView memoryFrame;
    private readonly FrameView chatFrame;
    private readonly Label consoleText;
    private readonly Label serverText;
    private readonly Label performanceText;
    private readonly Label memoryText;
    private readonly Label chatText;
    private readonly double[] tpsHistory = new double[HistoryLength];
    private readonly double[] cpuHistory = new double[HistoryLength];
    private readonly double[] memoryHistory = new double[HistoryLength];
    private int historyCount;
    private int historyNext;
    private FrameView? maximized;

    public RuntimeOverviewDashboard()
    {
        Width = Dim.Fill();
        Height = Dim.Fill();

        consoleFrame = CreateFrame("Console");
        serverFrame = CreateFrame("Server");
        performanceFrame = CreateFrame("TPS / CPU");
        memoryFrame = CreateFrame("Memory / GC");
        chatFrame = CreateFrame("Chat");

        consoleText = CreateContentLabel();
        serverText = CreateContentLabel();
        performanceText = CreateContentLabel();
        memoryText = CreateContentLabel();
        chatText = CreateContentLabel();

        consoleFrame.Add(consoleText);
        serverFrame.Add(serverText);
        performanceFrame.Add(performanceText);
        memoryFrame.Add(memoryText);
        chatFrame.Add(chatText);

        AttachMaximize(consoleFrame);
        AttachMaximize(serverFrame);
        AttachMaximize(performanceFrame);
        AttachMaximize(memoryFrame);
        AttachMaximize(chatFrame);

        Add(consoleFrame, serverFrame, performanceFrame, memoryFrame, chatFrame);
        ApplyTiledLayout();

        Initialized += (_, _) => consoleFrame.SetFocus();
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
        AppendHistory(runtime);

        ReadOnlySpan<RuntimePlayerSnapshot> players = playersSnapshot.Players.Span;
        serverText.Text = RenderServer(runtime, network, world, players.Length, status);
        performanceText.Text = RenderPerformance(runtime);
        memoryText.Text = RenderMemory(runtime);
        consoleText.Text = RenderConsole(runtime, logs.Entries.Span);
        chatText.Text = RenderLogs(chat.Entries.Span, maximumEntries: 8, emptyText: "<no chat yet>", includeLevelAndSource: false);

        SetNeedsDraw();
    }

    internal void TogglePanelForSmoke(string panelTitle)
    {
        FrameView frame = GetFrame(panelTitle);
        ToggleMaximize(frame);
    }

    internal string GetPanelTitleForSmoke(string panelTitle) =>
        GetFrame(panelTitle).Title?.ToString() ?? string.Empty;

    internal string? GetPanelSchemeForSmoke(string panelTitle) =>
        GetFrame(panelTitle).SchemeName;

    private static FrameView CreateFrame(string title)
    {
        var frame = new FrameView
        {
            Title = title,
            CanFocus = true,
            SchemeName = nameof(Schemes.Base)
        };

        frame.HasFocusChanged += (_, args) =>
        {
            frame.Title = args.Value ? ActiveTitlePrefix + title : title;
            frame.SchemeName = args.Value ? nameof(Schemes.Accent) : nameof(Schemes.Base);
            frame.SetNeedsDraw();
        };
        return frame;
    }

    private static Label CreateContentLabel() => new()
    {
        X = 1,
        Y = 0,
        Width = Dim.Fill(1),
        Height = Dim.Fill()
    };

    private void AttachMaximize(FrameView frame)
    {
        frame.MouseEvent += (_, mouse) =>
        {
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
                frame.SetFocus();

            if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
                return;

            frame.SetFocus();
            ToggleMaximize(frame);
            mouse.Handled = true;
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
        consoleFrame.Width = Dim.Percent(58);
        consoleFrame.Height = Dim.Fill();

        serverFrame.X = Pos.Right(consoleFrame);
        serverFrame.Y = 0;
        serverFrame.Width = Dim.Fill();
        serverFrame.Height = 7;

        performanceFrame.X = Pos.Right(consoleFrame);
        performanceFrame.Y = Pos.Bottom(serverFrame);
        performanceFrame.Width = Dim.Fill();
        performanceFrame.Height = 6;

        memoryFrame.X = Pos.Right(consoleFrame);
        memoryFrame.Y = Pos.Bottom(performanceFrame);
        memoryFrame.Width = Dim.Fill();
        memoryFrame.Height = 5;

        chatFrame.X = Pos.Right(consoleFrame);
        chatFrame.Y = Pos.Bottom(memoryFrame);
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
        "TPS / CPU" => performanceFrame,
        "Memory / GC" => memoryFrame,
        "Chat" => chatFrame,
        _ => throw new ArgumentOutOfRangeException(nameof(panelTitle))
    };

    private IEnumerable<FrameView> EnumerateFrames()
    {
        yield return consoleFrame;
        yield return serverFrame;
        yield return performanceFrame;
        yield return memoryFrame;
        yield return chatFrame;
    }

    private void AppendHistory(RuntimeDashboardSnapshot runtime)
    {
        tpsHistory[historyNext] = SanitizeSample(runtime.ObservedTicksPerSecond);
        cpuHistory[historyNext] = SanitizeSample(runtime.ProcessCpuPercent);
        memoryHistory[historyNext] = SanitizeSample(runtime.WorkingSetBytes / (1024d * 1024d));
        historyNext = (historyNext + 1) % HistoryLength;
        if (historyCount < HistoryLength)
            historyCount++;
    }

    private static string RenderConsole(RuntimeDashboardSnapshot runtime, ReadOnlySpan<RuntimeLogEntry> entries)
    {
        string tickCpu = runtime.CpuTimeAvailable
            ? $"{runtime.LastTickCpuMilliseconds:F2}/{runtime.WorstTickCpuMilliseconds:F2} ms"
            : "n/a";
        string summary = string.Create(
            CultureInfo.CurrentCulture,
            $"Tick #{runtime.Tick:N0}  wall {runtime.LastTickMilliseconds:F2}/{runtime.WorstTickMilliseconds:F2} ms  cpu {tickCpu}  slow {Sanitize(runtime.SlowestPhase, 18)} {runtime.SlowestPhaseMilliseconds:F2} ms  missed {runtime.MissedTickDeadlines:N0}\n" +
            $"Process     CPU {runtime.ProcessCpuPercent:F1}%  heap {FormatMebibytes(runtime.ManagedHeapBytes)}  working {FormatMebibytes(runtime.WorkingSetBytes)}  allocated {FormatMebibytes(runtime.TotalAllocatedBytes)}  GC {runtime.Gen0Collections:N0}/{runtime.Gen1Collections:N0}/{runtime.Gen2Collections:N0} pause {runtime.GcPauseTimePercentage:F2}%\n" +
            $"Commands    done {runtime.CommandsProcessed:N0}  pending {runtime.PendingCommands:N0}  deferred {runtime.DeferredCommands:N0}  rejected {runtime.RejectedCommands:N0}  budget {runtime.CommandBudgetExhaustions:N0}  oldest {runtime.OldestPendingCommandAgeMilliseconds:F1} ms\n\n");
        return summary + RenderLogs(entries, maximumEntries: 9, emptyText: "<no runtime log entries>");
    }

    private string RenderPerformance(RuntimeDashboardSnapshot runtime)
    {
        string tickCpu = runtime.CpuTimeAvailable
            ? $"{runtime.LastTickCpuMilliseconds:F2}/{runtime.WorstTickCpuMilliseconds:F2} ms"
            : "n/a";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"TPS {runtime.ObservedTicksPerSecond,5:F1}/{runtime.TargetTicksPerSecond,-3} {RenderHistory(tpsHistory, Math.Max(1d, runtime.TargetTicksPerSecond))}\n" +
            $"CPU {runtime.ProcessCpuPercent,5:F1}%      {RenderHistory(cpuHistory, 100d)}\n" +
            $"wall {runtime.LastTickMilliseconds:F2}/{runtime.WorstTickMilliseconds:F2} ms cpu {tickCpu}\n" +
            $"slow {Sanitize(runtime.SlowestPhase, 18)} {runtime.SlowestPhaseMilliseconds:F2} ms missed {runtime.MissedTickDeadlines:N0}");
    }

    private string RenderMemory(RuntimeDashboardSnapshot runtime)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"heap {FormatMebibytes(runtime.ManagedHeapBytes)} | working {FormatMebibytes(runtime.WorkingSetBytes)}\n" +
            $"allocated {FormatMebibytes(runtime.TotalAllocatedBytes)}\n" +
            $"working {RenderHistory(memoryHistory, Math.Max(64d, MaximumHistory(memoryHistory) * 1.10d))}\n" +
            $"GC {runtime.Gen0Collections:N0}/{runtime.Gen1Collections:N0}/{runtime.Gen2Collections:N0} pause {runtime.GcPauseTimePercentage:F2}%");
    }

    private static string RenderServer(
        RuntimeDashboardSnapshot runtime,
        RuntimeNetworkSnapshot network,
        RuntimeWorldSnapshot world,
        int playerCount,
        string? status)
    {
        var text = new StringBuilder(384)
            .Append(runtime.Lifecycle).Append(" | ")
            .Append(Sanitize(runtime.WorldName, 28)).Append(' ')
            .Append(runtime.WorldWidthTiles).Append('x').Append(runtime.WorldHeightTiles).AppendLine()
            .Append("players ").Append(playerCount).Append('/').Append(runtime.MaxPlayers)
            .Append(" | port ").Append(runtime.Port)
            .Append(" | interest ").Append(runtime.InterestManagementEnabled ? "ON" : "OFF").AppendLine()
            .Append("network active ").Append(runtime.ActiveConnections)
            .Append(" accepted ").Append(runtime.AcceptedConnections)
            .Append(" rejected ").Append(runtime.RejectedConnections)
            .Append(" slow ").Append(network.SlowClients).AppendLine()
            .Append("cache ").Append(world.RuntimeCacheHit ? "hit" : "miss");

        if (!string.IsNullOrWhiteSpace(status))
            text.AppendLine().Append(Sanitize(status, 120));
        else
            text.AppendLine().Append("double-click panel to maximize");
        return text.ToString();
    }

    private string RenderHistory(double[] history, double maximum)
    {
        if (historyCount == 0)
            return string.Empty;

        const string levels = "._-~=*#@";
        int width = Math.Min(24, historyCount);
        char[] graph = new char[width];
        int oldest = (historyNext - width + HistoryLength) % HistoryLength;
        double safeMaximum = maximum > 0d && double.IsFinite(maximum) ? maximum : 1d;
        for (int i = 0; i < width; i++)
        {
            double ratio = Math.Clamp(history[(oldest + i) % HistoryLength] / safeMaximum, 0d, 1d);
            graph[i] = levels[(int)Math.Round(ratio * (levels.Length - 1))];
        }
        return new string(graph);
    }

    private double MaximumHistory(double[] history)
    {
        double maximum = 0d;
        int count = historyCount;
        int oldest = (historyNext - count + HistoryLength) % HistoryLength;
        for (int i = 0; i < count; i++)
            maximum = Math.Max(maximum, history[(oldest + i) % HistoryLength]);
        return maximum;
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

    private static string FormatMebibytes(long bytes) =>
        (bytes / (1024d * 1024d)).ToString("F1", CultureInfo.CurrentCulture) + " MiB";
}
