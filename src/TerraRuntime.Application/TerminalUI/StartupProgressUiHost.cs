using System.Diagnostics;
using System.Globalization;
using TerraRuntime.Contracts.Diagnostics;
using TerraRuntime.Contracts.Gameplay;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerraRuntime.Application.TerminalUI;

internal enum StartupProgressOperation : byte
{
    WorldGeneration = 0,
    ServerStartup = 1
}

internal sealed record StartupProgressSnapshot(
    StartupProgressOperation Operation,
    string World,
    string Stage,
    string Detail,
    int StageIndex,
    int StageCount,
    double Fraction,
    DateTimeOffset UpdatedAtUtc,
    bool Failed = false);

/// <summary>
/// Full-screen startup TUI. Producers only atomically publish small immutable progress snapshots; Terminal.Gui owns
/// rendering on its dedicated thread and retains its own framebuffer/double-buffered driver state.
/// </summary>
internal sealed class StartupProgressUiHost : IWorldGenerationProgressSink, IDisposable
{
    private static readonly TimeSpan UiPumpInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan FinalFrameWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource stopUi = new();
    private readonly Thread thread;
    private readonly Action<string>? failureSink;
    private StartupProgressSnapshot snapshot;
    private long version;
    private int ownsTerminal = 1;
    private int finalFrameRequested;
    private int disposed;

    private StartupProgressUiHost(
        StartupProgressOperation operation,
        string world,
        string stage,
        string detail,
        Action<string>? failureSink)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(world);
        this.failureSink = failureSink;
        snapshot = new StartupProgressSnapshot(
            operation,
            Sanitize(world, 96),
            stage,
            detail,
            0,
            1,
            0d,
            DateTimeOffset.UtcNow);
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "TerraRuntime Startup UI"
        };
    }

    internal bool OwnsTerminal => Volatile.Read(ref ownsTerminal) != 0;
    internal long Version => Volatile.Read(ref version);
    internal StartupProgressSnapshot Snapshot => Volatile.Read(ref snapshot);
    internal static TimeSpan PumpIntervalForTests => UiPumpInterval;

    internal static StartupProgressUiHost StartWorldGeneration(
        string world,
        Action<string>? failureSink = null)
    {
        var host = new StartupProgressUiHost(
            StartupProgressOperation.WorldGeneration,
            world,
            "Preparing generation plan",
            "Building deterministic world-generation pass plan",
            failureSink);
        StartupProgressTelemetry.Attach(host);
        host.thread.Start();
        return host;
    }

    internal static StartupProgressUiHost StartServerStartup(
        string world,
        Action<string>? failureSink = null)
    {
        var host = new StartupProgressUiHost(
            StartupProgressOperation.ServerStartup,
            world,
            "Preparing runtime",
            "Validating world and persistence state",
            failureSink);
        StartupProgressTelemetry.Attach(host);
        host.thread.Start();
        return host;
    }

    public void Report(in WorldGenerationProgress progress)
    {
        StartupProgressSnapshot current = Snapshot;
        if (current.Operation != StartupProgressOperation.WorldGeneration)
            return;

        int passCount = Math.Max(1, progress.PassCount);
        int passIndex = Math.Clamp(progress.PassIndex, 0, passCount - 1);
        double passFraction = ClampFraction(progress.Fraction);
        double overall = (passIndex + passFraction) / passCount;
        string passName = progress.PassId.IsAssigned ? progress.PassId.Value : "world-generation pass";
        string detail = string.IsNullOrWhiteSpace(progress.Message)
            ? $"Executing {passName}"
            : Sanitize(progress.Message, 120);

        Publish(
            "Generating world",
            detail,
            passIndex + 1,
            passCount,
            overall);
    }

    internal void ReportServerStage(
        string stage,
        string detail,
        int stageIndex,
        int stageCount,
        double fraction,
        bool failed = false)
    {
        if (Snapshot.Operation != StartupProgressOperation.ServerStartup)
            return;
        Publish(stage, detail, stageIndex, stageCount, fraction, failed);
    }

    internal void CompleteAndRelease(string detail)
    {
        StartupProgressSnapshot current = Snapshot;
        Publish(
            current.Operation == StartupProgressOperation.WorldGeneration ? "World ready" : "Server ready",
            detail,
            Math.Max(1, current.StageCount),
            Math.Max(1, current.StageCount),
            1d);
        Volatile.Write(ref finalFrameRequested, 1);
        WaitForThread(FinalFrameWait);
        if (thread.IsAlive)
        {
            stopUi.Cancel();
            WaitForThread(DisposeWait);
        }
    }

    internal void FailAndRelease(string detail)
    {
        StartupProgressSnapshot current = Snapshot;
        Publish(
            "Startup failed",
            detail,
            current.StageIndex,
            current.StageCount,
            current.Fraction,
            failed: true);
        Volatile.Write(ref finalFrameRequested, 1);
        WaitForThread(FinalFrameWait);
        if (thread.IsAlive)
        {
            stopUi.Cancel();
            WaitForThread(DisposeWait);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        stopUi.Cancel();
        WaitForThread(DisposeWait);
        StartupProgressTelemetry.Detach(this);
        Volatile.Write(ref ownsTerminal, 0);
        stopUi.Dispose();
    }

    private void Publish(
        string stage,
        string detail,
        int stageIndex,
        int stageCount,
        double fraction,
        bool failed = false)
    {
        StartupProgressSnapshot current = Snapshot;
        int normalizedCount = Math.Max(1, stageCount);
        var next = current with
        {
            Stage = Sanitize(stage, 72),
            Detail = Sanitize(detail, 160),
            StageIndex = Math.Clamp(stageIndex, 0, normalizedCount),
            StageCount = normalizedCount,
            Fraction = ClampFraction(fraction),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Failed = failed
        };
        Volatile.Write(ref snapshot, next);
        Interlocked.Increment(ref version);
    }

    private void Run()
    {
        Exception? failure = null;
        try
        {
            using IApplication app = Terminal.Gui.App.Application.Create();
            string? forcedDriver = Host.ResolveProductionDriverName(OperatingSystem.IsWindows());
            if (forcedDriver is not null)
                app.ForceDriver = forcedDriver;

            app.Init();
            Theme.Apply();
            using var window = new StartupProgressWindow();
            long appliedVersion = -1;
            int animationFrame = 0;
            long nextAnimation = Stopwatch.GetTimestamp();
            long animationTicks = Math.Max(1L, Stopwatch.Frequency / 8);
            long started = Stopwatch.GetTimestamp();

            app.AddTimeout(UiPumpInterval, () =>
            {
                if (stopUi.IsCancellationRequested)
                {
                    app.RequestStop(window);
                    return false;
                }

                long published = Version;
                long now = Stopwatch.GetTimestamp();
                bool animate = now >= nextAnimation;
                if (published != appliedVersion || animate)
                {
                    if (animate)
                    {
                        animationFrame++;
                        nextAnimation = now + animationTicks;
                    }

                    window.Refresh(Snapshot, Stopwatch.GetElapsedTime(started), animationFrame);
                    appliedVersion = published;
                }

                if (Volatile.Read(ref finalFrameRequested) != 0 && appliedVersion == Version)
                {
                    app.RequestStop(window);
                    return false;
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
                    // Application can already be shutting down after a terminal or driver failure.
                }
            });

            window.Refresh(Snapshot, TimeSpan.Zero, 0);
            app.Run(window);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            Volatile.Write(ref ownsTerminal, 0);
            StartupProgressTelemetry.Detach(this);
        }

        if (failure is not null)
            ReportFailure($"Startup Terminal UI could not initialize; continuing without it: {failure.Message}");
    }

    private void WaitForThread(TimeSpan timeout)
    {
        if (thread.IsAlive && Thread.CurrentThread != thread)
            thread.Join(timeout);
    }

    private void ReportFailure(string message)
    {
        try
        {
            if (failureSink is not null)
                failureSink(message);
            else
                Console.Error.WriteLine(message);
        }
        catch (Exception)
        {
            // Startup UI failure reporting must not alter server startup semantics.
        }
    }

    private static double ClampFraction(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : 0d;

    private static string Sanitize(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        int length = Math.Min(value.Length, maximumLength);
        char[] buffer = new char[length];
        for (int i = 0; i < length; i++)
            buffer[i] = char.IsControl(value[i]) ? ' ' : value[i];
        return new string(buffer);
    }
}

/// <summary>
/// Process-local startup progress bridge. RuntimeHostLog feeds it semantic startup events after the event has been
/// accepted by logging, so terminal ownership suppresses console output without making progress depend on log text.
/// </summary>
internal static class StartupProgressTelemetry
{
    private const int ServerStageCount = 8;
    private static StartupProgressUiHost? current;

    internal static bool IsTerminalOwned => Volatile.Read(ref current)?.OwnsTerminal == true;

    internal static void Attach(StartupProgressUiHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        StartupProgressUiHost? previous = Interlocked.Exchange(ref current, host);
        if (previous is not null && !ReferenceEquals(previous, host))
            previous.Dispose();
    }

    internal static void Detach(StartupProgressUiHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Interlocked.CompareExchange(ref current, null, host);
    }

    internal static void Observe(RuntimeLogEventId eventId, string message)
    {
        StartupProgressUiHost? host = Volatile.Read(ref current);
        if (host is null || host.Snapshot.Operation != StartupProgressOperation.ServerStartup)
            return;

        if (eventId == RuntimeLogEventIds.WorldCacheHit)
        {
            host.ReportServerStage("Loading world", "Validated runtime world cache", 3, ServerStageCount, 0.34d);
        }
        else if (eventId == RuntimeLogEventIds.WorldCacheMiss)
        {
            host.ReportServerStage("Loading world", "Reading and decoding canonical .wld", 3, ServerStageCount, 0.26d);
        }
        else if (eventId == RuntimeLogEventIds.PersistenceWorldCacheRebuilt ||
                 eventId == RuntimeLogEventIds.PersistenceWorldCacheWriteFailed)
        {
            host.ReportServerStage("Preparing runtime cache", "World snapshot is ready", 4, ServerStageCount, 0.50d);
        }
        else if (eventId == RuntimeLogEventIds.PersistenceSaveTemplateReady)
        {
            host.ReportServerStage("Preparing persistence", "Canonical save template is ready", 5, ServerStageCount, 0.64d);
        }
        else if (eventId == RuntimeLogEventIds.WorldBootstrapCacheHit ||
                 eventId == RuntimeLogEventIds.PersistenceBootstrapCacheRebuilt ||
                 eventId == RuntimeLogEventIds.PersistenceBootstrapCacheWriteFailed)
        {
            host.ReportServerStage("Preparing player bootstrap", "Join bootstrap packets are ready", 6, ServerStageCount, 0.78d);
        }
        else if (eventId == RuntimeLogEventIds.StartupProfile)
        {
            host.ReportServerStage("Starting runtime", "Authoritative runtime and listener are being activated", 7, ServerStageCount, 0.92d);
        }
        else if (eventId == RuntimeLogEventIds.WorldCheckpointRecovered)
        {
            host.ReportServerStage("Recovering world", "Validated checkpoint restored; restarting load", 2, ServerStageCount, 0.16d);
        }
        else if (eventId == RuntimeLogEventIds.WorldLoadFailed)
        {
            host.ReportServerStage("Checking recovery", "Canonical world load failed; checking validated checkpoint", 3, ServerStageCount, 0.30d);
        }
        else if (eventId == RuntimeLogEventIds.NetworkListenerReady)
        {
            host.ReportServerStage("Server ready", "Network listener is accepting Terraria clients", 8, ServerStageCount, 1d);
            host.CompleteAndRelease("Network listener is ready; opening System Dashboard");
            host.Dispose();
        }
        else if (eventId == RuntimeLogEventIds.WorldFileMissing ||
                 eventId == RuntimeLogEventIds.WorldSourceStatFailed ||
                 eventId == RuntimeLogEventIds.WorldReadFailed ||
                 eventId == RuntimeLogEventIds.PersistenceSaveTemplateLoadFailed)
        {
            host.ReportServerStage("Startup blocked", message, 2, ServerStageCount, host.Snapshot.Fraction, failed: true);
        }
    }
}

internal sealed class StartupProgressWindow : Runnable
{
    private static readonly char[] Spinner = ['◐', '◓', '◑', '◒'];
    private readonly Label operationLabel;
    private readonly Label worldLabel;
    private readonly Label stageLabel;
    private readonly Label progressLabel;
    private readonly Label detailLabel;
    private readonly Label telemetryLabel;
    private readonly Label footerLabel;

    internal StartupProgressWindow()
    {
        Title = "TerraRuntime · Startup";
        SchemeName = "Base";

        operationLabel = CreateLabel(3, 1);
        operationLabel.Height = 2;
        worldLabel = CreateLabel(3, 4);
        stageLabel = CreateLabel(3, 6);
        progressLabel = CreateLabel(3, 8);
        detailLabel = CreateLabel(3, 10);
        telemetryLabel = CreateLabel(3, 12);
        footerLabel = new Label
        {
            X = 3,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(3),
            Text = "Terminal.Gui framebuffer · detached startup telemetry · no runtime work on UI thread",
            SchemeName = "Base"
        };

        Add(operationLabel, worldLabel, stageLabel, progressLabel, detailLabel, telemetryLabel, footerLabel);
    }

    internal void Refresh(StartupProgressSnapshot snapshot, TimeSpan elapsed, int animationFrame)
    {
        string operation = snapshot.Operation == StartupProgressOperation.WorldGeneration
            ? "WORLD GENERATION"
            : "SERVER RUNTIME · STARTUP";
        operationLabel.Text = $"TERRARUNTIME\n{operation}";
        worldLabel.Text = $"WORLD  {snapshot.World}";
        stageLabel.Text = snapshot.Failed ? $"! {snapshot.Stage}" : snapshot.Stage;
        progressLabel.Text = RenderProgressBar(snapshot.Fraction, ResolveBarWidth());
        detailLabel.Text = snapshot.Detail;

        char spinner = Spinner[Math.Abs(animationFrame) % Spinner.Length];
        telemetryLabel.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{spinner}  {snapshot.Fraction * 100d,5:F1}%   step {snapshot.StageIndex}/{snapshot.StageCount}   " +
            $"elapsed {elapsed:hh\\:mm\\:ss}   updated {snapshot.UpdatedAtUtc:HH:mm:ss} UTC");

        SetNeedsDraw();
    }

    internal static string RenderProgressBar(double fraction, int width)
    {
        int normalizedWidth = Math.Clamp(width, 12, 96);
        double normalizedFraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0d, 1d) : 0d;
        int filled = (int)Math.Round(normalizedFraction * normalizedWidth, MidpointRounding.AwayFromZero);
        filled = Math.Clamp(filled, 0, normalizedWidth);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[{new string('█', filled)}{new string('░', normalizedWidth - filled)}]  {normalizedFraction * 100d,5:F1}%");
    }

    private int ResolveBarWidth()
    {
        int viewportWidth = Viewport.Width;
        if (viewportWidth <= 0)
            viewportWidth = 80;
        return Math.Clamp(viewportWidth - 20, 20, 76);
    }

    private static Label CreateLabel(int x, int y) => new()
    {
        X = x,
        Y = y,
        Width = Dim.Fill(x),
        SchemeName = "Base"
    };
}
