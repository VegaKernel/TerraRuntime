namespace TerraRuntime.Transport;

/// <summary>
/// Version/capability handshake shared by every TerraRuntime process boundary.
///
/// The same transport contract is intended for both Vega-to-server connections (including one Vega host connected
/// to multiple TerraRuntime servers) and TerraRuntime supervisor-to-sandbox-worker connections. The transport layer
/// identifies the peer process and negotiates mechanics; gameplay/world/plugin services remain higher-level protocols.
/// </summary>
/// <param name="ProtocolVersion">Transport protocol version understood by the peer.</param>
/// <param name="Capabilities">Mechanics supported by the peer for this transport session.</param>
/// <param name="ProcessInstanceId">Ephemeral identity of this process boot. A restart must use a new value.</param>
/// <param name="Role">Application-defined peer role such as a server, Vega host, sandbox supervisor or sandbox worker.</param>
/// <param name="BuildVersion">Informational build identity used for diagnostics and compatibility reporting.</param>
public readonly record struct TransportHandshake(
    ushort ProtocolVersion,
    TransportCapabilities Capabilities,
    Guid ProcessInstanceId,
    string Role,
    string BuildVersion);
