using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

/// <summary>
/// Internal world-control command. The control capability never crosses the operations/UI boundary;
/// it is carried only through the in-process authoritative command queue and applied on the game thread.
/// </summary>
internal sealed record SetInterestManagementRuntimeCommand(
    IInterestManagementControl Control,
    bool Enabled) : RuntimeCommand;
