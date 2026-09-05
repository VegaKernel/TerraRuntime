using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed record ClientChestOpenRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaChestOpenRequest Request) : RuntimeCommand;

internal sealed record ClientChestItemRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaChestItemState State) : RuntimeCommand;

internal sealed record ClientActiveChestRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaActiveChestState State) : RuntimeCommand;

internal sealed record ClientChestNameLookupRuntimeCommand(
    ConnectionHandle Connection,
    TerrariaChestNameLookupRequest Request) : RuntimeCommand;
