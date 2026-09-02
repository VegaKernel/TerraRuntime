using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

internal sealed record ClientNpcHomeRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaNpcHomeState State) : RuntimeCommand;
