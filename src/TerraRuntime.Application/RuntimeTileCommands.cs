using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

/// <summary>
/// Connection-authenticated packet-17 state carried to the authoritative game thread. The decoded wire request
/// is intentionally preserved verbatim; gameplay authority, reach, tool power and inventory consumption must be
/// decided by ServerRuntimeState rather than by the socket thread.
/// </summary>
internal sealed record ClientTileManipulationRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaTileManipulationState State) : RuntimeCommand;


/// <summary>Connection-authenticated packet-48 liquid proposal carried to the authoritative world thread.</summary>
internal sealed record ClientLiquidRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaLiquidState State) : RuntimeCommand;

/// <summary>Connection-authenticated packet-52 action-2 proposal for the authoritative world thread.</summary>
internal sealed record ClientTempleDoorUnlockRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaLockAndUnlockState State) : RuntimeCommand;

/// <summary>Connection-authenticated packet-19 normal-door open proposal for the authoritative world thread.</summary>
internal sealed record ClientDoorOpenRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaDoorToggleState State) : RuntimeCommand;

/// <summary>Connection-authenticated packet-19 normal-door close proposal for the authoritative world thread.</summary>
internal sealed record ClientDoorCloseRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaDoorToggleState State) : RuntimeCommand;

/// <summary>Connection-authenticated packet-19 forced tall-gate toggle proposal for the authoritative world thread.</summary>
internal sealed record ClientTallGateToggleRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaDoorToggleState State) : RuntimeCommand;
