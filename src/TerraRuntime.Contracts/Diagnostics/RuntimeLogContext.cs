namespace TerraRuntime.Contracts.Diagnostics;

/// <summary>
/// Optional detached correlation data. Mutable runtime entities and raw packet payloads are deliberately
/// excluded from this contract.
/// </summary>
public readonly record struct RuntimeLogContext(
    string? CorrelationId = null,
    string? WorldId = null,
    string? ConnectionId = null,
    string? PlayerHandle = null,
    string? EntityHandle = null,
    string? PacketDirection = null,
    int? PacketId = null);
