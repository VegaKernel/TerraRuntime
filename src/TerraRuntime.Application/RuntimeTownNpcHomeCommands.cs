using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed record ClientNpcHomeRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaNpcHomeState State) : RuntimeCommand;
