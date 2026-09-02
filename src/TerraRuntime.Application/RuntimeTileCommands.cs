using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

/// <summary>
/// Connection-authenticated packet-17 state carried to the authoritative game thread. The decoded wire request
/// is intentionally preserved verbatim; gameplay authority, reach, tool power and inventory consumption must be
/// decided by ServerRuntimeState rather than by the socket thread.
/// </summary>
internal sealed record ClientTileManipulationRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaTileManipulationState State) : RuntimeCommand;
