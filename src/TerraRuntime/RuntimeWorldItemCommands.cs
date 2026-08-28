using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Authoritative-loop commands for runtime-owned dropped items. Packet 21/22 wire state is mapped into
/// packet-neutral Core updates before crossing this boundary.
/// </summary>
internal sealed record WorldItemAllocateRuntimeCommand(
    WorldItemDropStateUpdate State,
    TaskCompletionSource<WorldItemSnapshot?>? Completion = null) : RuntimeCommand;

internal sealed record WorldItemDropRuntimeCommand(
    short Slot,
    WorldItemDropStateUpdate State) : RuntimeCommand;

internal sealed record WorldItemRemoveRuntimeCommand(short Slot) : RuntimeCommand;

internal sealed record WorldItemOwnerRuntimeCommand(
    short Slot,
    WorldItemOwnerStateUpdate State) : RuntimeCommand;
