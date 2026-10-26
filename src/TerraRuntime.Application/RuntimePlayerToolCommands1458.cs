using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

// Ordered visual progress observation. The world owner may relay it after
// bounds/current-player checks; it cannot create a hit or advance tool clocks.
internal sealed record ClientTilePickingRuntimeCommand1458(ConnectionHandle Connection,
    TerrariaTilePickingState1458 State) : RuntimeCommand;

// Source client completion observation, never a raw chest mutation command.
internal sealed record ClientToolChestUpdatesRuntimeCommand1458(ConnectionHandle Connection,
    TerrariaChestUpdatesState1458 State) : RuntimeCommand;

// Pure visual141, optionally retained as the source-qualified tool branch witness.
internal sealed record ClientLucyAxeMessageRuntimeCommand1458(ConnectionHandle Connection,
    TerrariaLucyAxeMessageState1458 State) : RuntimeCommand;

// Authenticated one-cell special hammer observation, never a raw terrain writer.
internal sealed record ClientToolTileSquareRuntimeCommand1458(ConnectionHandle Connection,
    TerraRuntime.Protocol.Multiplicity.TerrariaToolTileSquareState1458 State) : RuntimeCommand;
