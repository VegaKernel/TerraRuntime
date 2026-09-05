using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed record ClientNpcCatchRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaNpcCatchState State) : RuntimeCommand;
