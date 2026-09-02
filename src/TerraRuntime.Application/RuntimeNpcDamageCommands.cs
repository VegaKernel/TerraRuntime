using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime;

/// <summary>Connection-owned packet-28 command. Exact generation resolution and every mutation occur on the game loop.</summary>
internal sealed record ClientNpcDamageRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaNpcDamageState State) : RuntimeCommand;
