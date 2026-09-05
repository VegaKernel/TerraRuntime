using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed record ClientSignReadRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaSignReadRequest Request) : RuntimeCommand;

internal sealed record ClientSignUpdateRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaSignState State) : RuntimeCommand;
