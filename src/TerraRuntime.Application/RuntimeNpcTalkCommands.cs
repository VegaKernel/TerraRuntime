using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed record ClientNpcTalkRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaNpcTalkState State) : RuntimeCommand;
