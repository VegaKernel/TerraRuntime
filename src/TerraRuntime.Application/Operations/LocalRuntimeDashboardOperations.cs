using System.Diagnostics;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Operations;

internal sealed class LocalRuntimeDashboardOperations : IRuntimeDashboardOperations
{
    private static readonly long MinimumProcessCpuSampleTicks = Math.Max(1L, Stopwatch.Frequency / 4);

    private readonly AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand> gameLoop;
    private readonly TerrariaConnectionAdmissionGate admission;
    private readonly IInterestManagementControl interestManagement;
    private readonly string worldName;
    private readonly int worldWidthTiles;
    private readonly int worldHeightTiles;
    private readonly int port;
    private readonly int maxPlayers;
    private readonly int targetTicksPerSecond;
    private readonly object sampleSync = new();
    private readonly RuntimeTickRateObserver tickRateObserver = new();

    private long processCpuSampleTimestamp;
    private long processCpuSampleTicks = -1;
    private double processCpuPercent;

    public LocalRuntimeDashboardOperations(
        AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand> gameLoop,
        TerrariaConnectionAdmissionGate admission,
        IInterestManagementControl interestManagement,
        string worldName,
        int worldWidthTiles,
        int worldHeightTiles,
        int port,
        int maxPlayers,
        int targetTicksPerSecond)
    {
        this.gameLoop = gameLoop ?? throw new ArgumentNullException(nameof(gameLoop));
        this.admission = admission ?? throw new ArgumentNullException(nameof(admission));
        this.interestManagement = interestManagement ?? throw new ArgumentNullException(nameof(interestManagement));
        this.worldName = string.IsNullOrWhiteSpace(worldName)
            ? throw new ArgumentException("World name is required.", nameof(worldName))
            : worldName;
        ArgumentOutOfRangeException.ThrowIfLessThan(worldWidthTiles, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(worldHeightTiles, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPlayers, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetTicksPerSecond, 1);

        this.worldWidthTiles = worldWidthTiles;
        this.worldHeightTiles = worldHeightTiles;
        this.port = port;
        this.maxPlayers = maxPlayers;
        this.targetTicksPerSecond = targetTicksPerSecond;
    }

    public RuntimeDashboardSnapshot CaptureSnapshot()
    {
        GameLoopSnapshot loop = gameLoop.Snapshot;
        RuntimeLifecycleState lifecycle = gameLoop.Fault is not null
            ? RuntimeLifecycleState.Faulted
            : gameLoop.IsRunning
                ? RuntimeLifecycleState.Running
                : RuntimeLifecycleState.Stopped;

        TimeSpan processCpuTime;
        using (Process process = Process.GetCurrentProcess())
            processCpuTime = process.TotalProcessorTime;

        GCMemoryInfo gcMemory = GC.GetGCMemoryInfo();

        return new RuntimeDashboardSnapshot(
            Lifecycle: lifecycle,
            WorldName: worldName,
            WorldWidthTiles: worldWidthTiles,
            WorldHeightTiles: worldHeightTiles,
            Port: port,
            MaxPlayers: maxPlayers,
            InterestManagementEnabled: interestManagement.IsEnabled,
            Tick: loop.Tick,
            TargetTicksPerSecond: targetTicksPerSecond,
            ObservedTicksPerSecond: tickRateObserver.Observe(loop.Tick),
            LastTickMilliseconds: loop.LastTickMilliseconds,
            WorstTickMilliseconds: loop.WorstTickMilliseconds,
            CpuTimeAvailable: loop.CpuTimeAvailable,
            LastTickCpuMilliseconds: loop.LastTickCpuMilliseconds,
            WorstTickCpuMilliseconds: loop.WorstTickCpuMilliseconds,
            SlowestPhase: loop.SlowestLastPhase.ToString(),
            SlowestPhaseMilliseconds: loop.SlowestLastPhaseMilliseconds,
            MissedTickDeadlines: loop.MissedTickDeadlines,
            CommandsProcessed: loop.CommandsProcessed,
            PendingCommands: loop.PendingCommands,
            DeferredCommands: loop.DeferredCommands,
            RejectedCommands: loop.RejectedCommands,
            CommandBudgetExhaustions: loop.CommandBudgetExhaustions,
            OldestPendingCommandAgeMilliseconds: loop.OldestPendingCommandAgeMilliseconds,
            ManagedHeapBytes: GC.GetTotalMemory(forceFullCollection: false),
            TotalAllocatedBytes: GC.GetTotalAllocatedBytes(precise: false),
            WorkingSetBytes: Environment.WorkingSet,
            ProcessCpuPercent: ObserveProcessCpuPercent(processCpuTime),
            GcPauseTimePercentage: gcMemory.PauseTimePercentage,
            Gen0Collections: GC.CollectionCount(0),
            Gen1Collections: GC.CollectionCount(1),
            Gen2Collections: GC.CollectionCount(2),
            ActiveConnections: admission.ActiveConnections,
            AcceptedConnections: admission.AcceptedConnections,
            RejectedConnections: admission.RejectedConnections,
            CapturedAtUtc: loop.CapturedAtUtc);
    }

    public bool TrySetInterestManagementEnabled(bool enabled) =>
        gameLoop.TryPost(
            GameCommandSourceId.System,
            new SetInterestManagementRuntimeCommand(interestManagement, enabled));

    private double ObserveProcessCpuPercent(TimeSpan totalCpuTime)
    {
        long now = Stopwatch.GetTimestamp();
        long totalCpuTicks = totalCpuTime.Ticks;

        lock (sampleSync)
        {
            if (processCpuSampleTicks < 0)
            {
                processCpuSampleTicks = totalCpuTicks;
                processCpuSampleTimestamp = now;
                return 0d;
            }

            long elapsed = now - processCpuSampleTimestamp;
            if (elapsed < MinimumProcessCpuSampleTicks)
                return processCpuPercent;

            long cpuTicks = totalCpuTicks - processCpuSampleTicks;
            if (cpuTicks >= 0 && elapsed > 0)
            {
                double wallSeconds = elapsed / (double)Stopwatch.Frequency;
                double cpuSeconds = cpuTicks / (double)TimeSpan.TicksPerSecond;
                double capacitySeconds = wallSeconds * Math.Max(1, Environment.ProcessorCount);
                processCpuPercent = Math.Clamp(cpuSeconds / capacitySeconds * 100d, 0d, 100d);
            }

            processCpuSampleTicks = totalCpuTicks;
            processCpuSampleTimestamp = now;
            return processCpuPercent;
        }
    }
}
