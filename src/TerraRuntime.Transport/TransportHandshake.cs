namespace TerraRuntime.Transport;

public readonly record struct TransportHandshake(
    ushort ProtocolVersion,
    TransportCapabilities Capabilities,
    Guid ProcessInstanceId,
    string Role,
    string BuildVersion);
