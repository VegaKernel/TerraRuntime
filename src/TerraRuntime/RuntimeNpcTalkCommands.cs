using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

internal sealed record ClientNpcTalkRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaNpcTalkState State) : RuntimeCommand;
