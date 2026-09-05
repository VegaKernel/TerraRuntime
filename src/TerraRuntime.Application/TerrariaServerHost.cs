using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.Application.Operations;

namespace TerraRuntime.Application;

public static class TerrariaServerHost
{
    /// <summary>
    /// Runs one Terraria world. The optional interest-management control is the only supported
    /// external switch for runtime visibility optimization; spatial policy remains owned by TerraRuntime.
    /// </summary>
    public static async Task<int> RunAsync(
        ServerHostOptions options,
        IInterestManagementControl? interestManagement = null,
        ILifecycle? hostLifecycle = null,
        ITerraRuntimeWorldGeneratorSource? worldGenerators = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var runtimeLogs = new RuntimeLogBuffer();
        await using var hostLog = new RuntimeHostLog(runtimeLogs);

        IInterestManagementControl runtimeInterestManagement =
            interestManagement ?? new InterestManagementControl(options.InterestManagementEnabled);
        if (options.InterestManagementEnabled)
            runtimeInterestManagement.SetEnabled(true);

        WorldStartupPreparationResult preparation = await WorldStartupPreparation.PrepareAsync(
            options,
            hostLog).ConfigureAwait(false);
        if (preparation.Status == WorldStartupPreparationStatus.RestartAfterRecovery)
        {
            await hostLog.DisposeAsync().ConfigureAwait(false);
            return await RunAsync(
                options,
                runtimeInterestManagement,
                hostLifecycle,
                worldGenerators).ConfigureAwait(false);
        }
        if (preparation.Status == WorldStartupPreparationStatus.Failed || preparation.Startup is null)
            return preparation.ExitCode;

        using var session = new ServerProcessSession(
            options,
            runtimeInterestManagement,
            hostLifecycle,
            worldGenerators,
            runtimeLogs,
            hostLog,
            preparation.Startup);
        return await session.RunAsync().ConfigureAwait(false);
    }
}
