using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal sealed record NpcSpawnRuntimeCommand(
    byte Slot,
    NpcStateUpdate State,
    TaskCompletionSource<NpcSnapshot?>? Completion = null) : RuntimeCommand;

internal sealed record NpcUpdateRuntimeCommand(
    NpcHandle Npc,
    NpcStateUpdate State) : RuntimeCommand;

internal sealed record NpcDespawnRuntimeCommand(
    NpcHandle Npc,
    TaskCompletionSource<bool>? Completion = null) : RuntimeCommand;
