using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Authoritative-loop commands for runtime-owned dropped items. Packet 21/22 wire state is mapped into
/// packet-neutral Core updates before crossing this boundary. Every client-originated command retains the exact
/// connection/player generation that admitted it so delayed work cannot mutate state after disconnect or slot reuse.
/// </summary>
internal sealed record WorldItemAllocateRuntimeCommand(
    ConnectionHandle Connection,
    WorldItemDropStateUpdate State,
    TaskCompletionSource<WorldItemSnapshot?>? Completion = null) : RuntimeCommand;

internal sealed record WorldItemDropRuntimeCommand(
    ConnectionHandle Connection,
    short Slot,
    WorldItemDropStateUpdate State) : RuntimeCommand;

internal sealed record WorldItemRemoveRuntimeCommand(
    ConnectionHandle Connection,
    short Slot) : RuntimeCommand;

internal sealed record WorldItemOwnerRuntimeCommand(
    ConnectionHandle Connection,
    short Slot,
    WorldItemOwnerStateUpdate State) : RuntimeCommand;
