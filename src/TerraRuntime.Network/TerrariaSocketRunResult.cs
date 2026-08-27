namespace TerraRuntime.Network;

public readonly record struct TerrariaSocketRunResult(
    TerrariaPipePumpResult Inbound,
    OutboundWriterResult Outbound,
    TerrariaConnectionStopReason StopReason);
