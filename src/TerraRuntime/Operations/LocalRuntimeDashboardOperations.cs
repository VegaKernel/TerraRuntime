using System.Diagnostics;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Operations;

internal sealed class LocalRuntimeDashboardOperations : IRuntimeDashboardOperations
{
    private static readonly long MinimumTickRateSampleTicks = Math.Max(1L, Stopwatch.Frequency / 4);

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

    private long sampleTick = -1;
    private long sampleTimestamp;
    private double observedTicksPerSecond;

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
            ObservedTicksPerSecond: ObserveTickRate(loop.Tick),
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
            ActiveConnections: admission.ActiveConnections,
            AcceptedConnections: admission.AcceptedConnections,
            RejectedConnections: admission.RejectedConnections,
            CapturedAtUtc: loop.CapturedAtUtc);
    }

    private double ObserveTickRate(long currentTick)
    {
        long now = Stopwatch.GetTimestamp();

        lock (sampleSync)
        {
            if (sampleTick < 0)
            {
                sampleTick = currentTick;
                sampleTimestamp = now;
                return 0d;
            }

            long elapsed = now - sampleTimestamp;
            if (elapsed < MinimumTickRateSampleTicks)
                return observedTicksPerSecond;

            long completedTicks = currentTick - sampleTick;
            if (completedTicks >= 0)
            {
                observedTicksPerSecond = completedTicks * (double)Stopwatch.Frequency / elapsed;
            }

            sampleTick = currentTick;
            sampleTimestamp = now;
            return observedTicksPerSecond;
        }
    }
}
