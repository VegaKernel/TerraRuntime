using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Authoritative-loop commands for runtime-owned dropped items. Packet 21/22 wire state is mapped into
/// packet-neutral Core updates before crossing this boundary. Every client-originated command retains both the exact
/// connection/player generation and, for explicit item-slot operations, the exact active world-item generation that
/// existed when ingress admitted the frame.
/// </summary>
internal sealed record WorldItemAllocateRuntimeCommand(
    ConnectionHandle Connection,
    WorldItemDropStateUpdate State,
    TaskCompletionSource<WorldItemSnapshot?>? Completion = null) : RuntimeCommand;

internal sealed record WorldItemDropRuntimeCommand(
    ConnectionHandle Connection,
    WorldItemHandle Target,
    WorldItemDropStateUpdate State) : RuntimeCommand;

internal sealed record WorldItemRemoveRuntimeCommand(
    ConnectionHandle Connection,
    WorldItemHandle Target) : RuntimeCommand;

internal sealed record WorldItemOwnerRuntimeCommand(
    ConnectionHandle Connection,
    WorldItemHandle Target,
    WorldItemOwnerStateUpdate State) : RuntimeCommand;
